using LocalVoice.Core;
namespace LocalVoice.App.Services;

public enum DictationState { Idle, Loading, Listening, Finishing }
public sealed class DictationController : IAsyncDisposable
{
    private readonly WhisperEngine _engine;
    private readonly ModelManager _models;
    private readonly MicrophoneService _microphones;
    private readonly SettingsStore _settings;
    private readonly IAppLog _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DictationSession? _session;
    private CancellationTokenSource? _startCancellation;
    private MicrophoneCaptureSession? _test;
    private CancellationTokenSource? _testCancellation;
    private IntPtr _dictationTarget;
    public DictationState State { get; private set; }
    public bool IsTesting { get; private set; }
    public string? ActiveDevice { get; private set; }
    public event EventHandler? StateChanged;
    public event EventHandler<float>? Level;
    public event EventHandler<string>? TextChanged;
    public event EventHandler<string>? LiveTextChanged;
    public event EventHandler<TranscriptEntry>? Completed;
    public event EventHandler<string>? Error;
    public event EventHandler<string>? Notice;
    public event EventHandler? SessionStarting;
    public event EventHandler? SessionStopping;
    public DictationController(WhisperEngine engine, ModelManager models, MicrophoneService microphones, SettingsStore settings, IAppLog log)
    { _engine = engine; _models = models; _microphones = microphones; _settings = settings; _log = log; }
    public void RememberTarget(IntPtr hwnd) { if (hwnd != IntPtr.Zero) _dictationTarget = hwnd; }
    public IntPtr Target => _dictationTarget;
    public async Task ToggleAsync()
    {
        if (State != DictationState.Idle) { await StopAsync().ConfigureAwait(false); return; }
        await StartAsync().ConfigureAwait(false);
    }
    private void SetState(DictationState state) { State = state; StateChanged?.Invoke(this, EventArgs.Empty); }
    private void ReportError(Exception ex, string context)
    { _log.Error(context, ex); Error?.Invoke(this, ex is UnauthorizedAccessException ? "Нет доступа к файлу или микрофону. Проверьте разрешения Windows и выберите устройство заново." : ex.Message); }
    private async Task StartAsync()
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
            using var cts = new CancellationTokenSource(); _startCancellation = cts;
            SetState(DictationState.Loading); SessionStarting?.Invoke(this, EventArgs.Empty);
            try
            {
                await _engine.EnsureLoadedAsync(model, cts.Token).ConfigureAwait(false);
                var (capture, device, name) = _microphones.CreateCapture(settings.InputDeviceId);
                _session = new MicrophoneCaptureSession(capture, device, name);
                var session = new DictationSession(_session, _engine, settings);
                session.Level += (_, value) => Level?.Invoke(this, value);
                session.TextChanged += (_, value) => LiveTextChanged?.Invoke(this, value);
                session.Faulted += (_, ex) => ReportError(ex, "Capture/recognition");
                session.Start(); ActiveDevice = name; SetState(DictationState.Listening);
                _log.Info("Dictation started");
            }
            catch (OperationCanceledException) { await CleanupSessionAsync().ConfigureAwait(false); SessionStopping?.Invoke(this, EventArgs.Empty); SetState(DictationState.Idle); }
            catch (Exception ex)
            {
                await CleanupSessionAsync().ConfigureAwait(false); SessionStopping?.Invoke(this, EventArgs.Empty); SetState(DictationState.Idle);
                ReportError(ex, "Start dictation");
            }
        }
        finally { _startCancellation = null; _gate.Release(); }
    }
    public async Task StopAsync()
    {
        _startCancellation?.Cancel();
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_session is null) { SessionStopping?.Invoke(this, EventArgs.Empty); SetState(DictationState.Idle); return; }
            SetState(DictationState.Finishing); SessionStopping?.Invoke(this, EventArgs.Empty);
            var session = _session;
            DictationResult result;
            try { result = await session.FinishAsync().ConfigureAwait(false); }
            finally { await CleanupSessionAsync().ConfigureAwait(false); Level?.Invoke(this, 0); SetState(DictationState.Idle); }
            if (result.TimedOut) Notice?.Invoke(this, "Обработка последнего фрагмента заняла слишком много времени. Сохранён подтверждённый текст; выберите модель легче.");
            if (!string.IsNullOrWhiteSpace(result.Text))
            {
                var entry = new TranscriptEntry(Guid.NewGuid(), DateTimeOffset.Now, result.Text, _settings.Current.Language, _settings.Current.ModelId, result.Seconds);
                TextChanged?.Invoke(this, entry.Text); Completed?.Invoke(this, entry);
            }
            else Notice?.Invoke(this, "Речь не найдена. Проверьте микрофон и попробуйте говорить чуть громче.");
            _log.Info($"Dictation finished: {result.Seconds:F1}s, chars={result.Text.Length}");
        }
        catch (Exception ex) { ReportError(ex, "Finish dictation"); }
        finally { _gate.Release(); }
    }
    private async Task CleanupSessionAsync()
    {
        if (_session is not null) { await _session.DisposeAsync().ConfigureAwait(false); _session = null; }
    }
    public async Task ToggleTestAsync()
    {
        if (IsTesting) { await StopTestAsync().ConfigureAwait(false); return; }
        if (State != DictationState.Idle) { Notice?.Invoke(this, "Сначала остановите диктовку."); return; }
        using var cts = new CancellationTokenSource(); _testCancellation = cts;
        try
        {
            var (capture, device, name) = _microphones.CreateCapture(_settings.Current.InputDeviceId);
            _test = new MicrophoneCaptureSession(capture, device, name);
            _test.Level += (_, value) => Level?.Invoke(this, value);
            _test.Faulted += (_, ex) => { ReportError(ex, "Microphone test"); _ = StopTestAsync(); };
            _test.Start(); ActiveDevice = name; IsTesting = true; StateChanged?.Invoke(this, EventArgs.Empty);
            _ = Task.Run(async () => { try { await Task.Delay(TimeSpan.FromSeconds(10), cts.Token).ConfigureAwait(false); await StopTestAsync().ConfigureAwait(false); } catch (OperationCanceledException) { } });
        }
        catch (Exception ex) { if (_test is not null) await _test.DisposeAsync().ConfigureAwait(false); _test = null; ReportError(ex, "Microphone test"); }
    }
    public async Task StopTestAsync()
    {
        _testCancellation?.Cancel();
        if (_test is not null) await _test.DisposeAsync().ConfigureAwait(false);
        _test = null; IsTesting = false; Level?.Invoke(this, 0); StateChanged?.Invoke(this, EventArgs.Empty);
    }
    public async ValueTask DisposeAsync()
    {
        _startCancellation?.Cancel(); await StopTestAsync().ConfigureAwait(false);
        await _gate.WaitAsync().ConfigureAwait(false);
        try { await CleanupSessionAsync().ConfigureAwait(false); } finally { _gate.Release(); _gate.Dispose(); }
    }
}
