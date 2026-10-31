using LocalVoice.Core;
namespace LocalVoice.App.Services;

public enum DictationState { Idle, Loading, Listening, Finishing }
public sealed class DictationController : IAsyncDisposable
{
    private readonly SettingsStore _settings;
    private readonly ModelManager _models;
    private readonly WhisperEngine _engine;
    private readonly MicrophoneService _microphones;
    private readonly IAppLog _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _testGate = new(1, 1);
    private DictationSession? _session;
    private MicrophoneCaptureSession? _test;
    private CancellationTokenSource? _startCancellation, _testCancellation;
    public DictationState State { get; private set; }
    public bool IsTesting { get; private set; }
    public string? ActiveDevice { get; private set; }
    public IntPtr Target { get; private set; }
    public event EventHandler? StateChanged;
    public event EventHandler<float>? Level;
    public event EventHandler<string>? LiveTextChanged;
    public event EventHandler<string>? Error;
    public event EventHandler<string>? Notice;
    public event EventHandler<TranscriptEntry>? Completed;
    public event EventHandler? SessionStarting;
    public event EventHandler? SessionStopping;
    public DictationController(WhisperEngine engine, ModelManager models, MicrophoneService microphones, SettingsStore settings, IAppLog log)
    { _settings = settings; _models = models; _engine = engine; _microphones = microphones; _log = log; }
    public void RememberTarget(IntPtr hwnd) { if (hwnd != IntPtr.Zero) Target = hwnd; }
    public Task ToggleAsync() => State == DictationState.Idle ? StartAsync() : StopAsync();
    public async Task StartAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (State != DictationState.Idle) return;
            if (IsTesting) await StopTestAsync().ConfigureAwait(false);
            var settings = _settings.Current.Clone();
            var model = ModelCatalog.ById[settings.ModelId];
            if (!_models.LooksInstalled(model))
            {
                Notice?.Invoke(this, "Сначала скачайте модель в разделе «Модели». Это единственная сетевая загрузка.");
                return;
            }
            var cts = new CancellationTokenSource(); _startCancellation = cts;
            SetState(DictationState.Loading); SessionStarting?.Invoke(this, EventArgs.Empty);
            try
            {
                await _engine.EnsureLoadedAsync(model, cts.Token).ConfigureAwait(false);
                var (capture, device, name) = _microphones.CreateCapture(settings.InputDeviceId);
                var microphone = new MicrophoneCaptureSession(capture, device, name);
                var session = new DictationSession(microphone, _engine, settings);
                _session = session;
                session.Level += (_, value) => Level?.Invoke(this, value);
                session.TextChanged += (_, value) => LiveTextChanged?.Invoke(this, value);
                session.Faulted += (_, ex) => { ReportError(ex, "Capture/recognition"); _ = Task.Run(StopAsync); };
                session.Start(); ActiveDevice = name; SetState(DictationState.Listening);
                _log.Info("Dictation started");
            }
            catch (OperationCanceledException) { await CleanupSessionAsync().ConfigureAwait(false); SessionStopping?.Invoke(this, EventArgs.Empty); SetState(DictationState.Idle); }
            catch (Exception ex)
            {
                await CleanupSessionAsync().ConfigureAwait(false); SessionStopping?.Invoke(this, EventArgs.Empty); SetState(DictationState.Idle);
                ReportError(ex, "Start dictation");
            }
            finally { _startCancellation = null; cts.Dispose(); }
        }
        finally { _gate.Release(); }
    }
    public async Task StopAsync()
    {
        if (State == DictationState.Loading) { CancelStart(); return; }
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (State != DictationState.Listening || _session is null) return;
            SetState(DictationState.Finishing);
            var session = _session;
            DictationResult? result = null;
            try { result = await session.FinishAsync().ConfigureAwait(false); }
            catch (Exception ex) { ReportError(ex, "Finish dictation"); }
            await CleanupSessionAsync().ConfigureAwait(false);
            SessionStopping?.Invoke(this, EventArgs.Empty); SetState(DictationState.Idle);
            if (result is { Text.Length: > 0 })
            {
                var settings = _settings.Current;
                var entry = new TranscriptEntry(Guid.NewGuid(), DateTimeOffset.UtcNow, result.Text,
                    settings.Language == "auto" ? "auto" : settings.Language, settings.ModelId, result.Seconds);
                Completed?.Invoke(this, entry);
                if (result.TimedOut) Notice?.Invoke(this, "Последний фрагмент не успел обработаться. Выберите более лёгкую модель.");
            }
            else if (result is not null) Notice?.Invoke(this, "Речь не распознана. Проверьте микрофон и уровень шума.");
            _log.Info("Dictation stopped");
        }
        finally { _gate.Release(); }
    }
    private void CancelStart()
    {
        try { _startCancellation?.Cancel(); } catch (ObjectDisposedException) { }
    }
    public async Task ToggleTestAsync()
    {
        await _testGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (IsTesting) { await StopTestCoreAsync(); return; }
            if (State != DictationState.Idle) return;
            try
            {
                var settings = _settings.Current;
                var (capture, device, name) = _microphones.CreateCapture(settings.InputDeviceId);
                var session = new MicrophoneCaptureSession(capture, device, name);
                _test = session; ActiveDevice = name;
                session.Level += (_, value) => Level?.Invoke(this, value);
                session.Faulted += (_, ex) => { ReportError(ex, "Microphone test"); _ = Task.Run(StopTestAsync); };
                session.Start(); IsTesting = true;
                var cts = new CancellationTokenSource(); _testCancellation = cts;
                _ = Task.Delay(TimeSpan.FromSeconds(10), cts.Token).ContinueWith(t =>
                {
                    if (!t.IsCanceled) _ = Task.Run(StopTestAsync);
                }, TaskScheduler.Default);
                _log.Info("Microphone test started");
            }
            catch (Exception ex) { await StopTestCoreAsync(); ReportError(ex, "Microphone test"); }
            finally { StateChanged?.Invoke(this, EventArgs.Empty); }
        }
        finally { _testGate.Release(); }
    }
    public async Task StopTestAsync()
    {
        await _testGate.WaitAsync().ConfigureAwait(false);
        try { await StopTestCoreAsync(); } finally { _testGate.Release(); }
    }
    private async Task StopTestCoreAsync()
    {
        var session = _test; _test = null;
        var cts = _testCancellation; _testCancellation = null;
        try { cts?.Cancel(); } catch (ObjectDisposedException) { }
        cts?.Dispose();
        IsTesting = false;
        if (session is not null) await session.DisposeAsync().ConfigureAwait(false);
        Level?.Invoke(this, 0); StateChanged?.Invoke(this, EventArgs.Empty);
    }
    private async Task CleanupSessionAsync()
    {
        var session = _session; _session = null;
        if (session is not null) await session.DisposeAsync().ConfigureAwait(false);
        Level?.Invoke(this, 0);
    }
    private void SetState(DictationState state) { State = state; StateChanged?.Invoke(this, EventArgs.Empty); }
    private void ReportError(Exception ex, string context)
    {
        _log.Error(context, ex);
        Error?.Invoke(this, ex.Message);
    }
    public async ValueTask DisposeAsync()
    {
        CancelStart();
        await StopAsync(); await StopTestAsync(); await CleanupSessionAsync();
        _gate.Dispose(); _testGate.Dispose();
    }
}
