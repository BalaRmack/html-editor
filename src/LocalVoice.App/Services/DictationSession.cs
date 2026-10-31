using System.Diagnostics;
using LocalVoice.Core;
namespace LocalVoice.App.Services;

public sealed record DictationResult(string Text, double Seconds, bool TimedOut);
public sealed class DictationSession : IAsyncDisposable
{
    private readonly MicrophoneCaptureSession _capture;
    private readonly WhisperEngine _engine;
    private readonly AppSettings _settings;
    private readonly RecognitionQueue _queue = new();
    private readonly TranscriptBuffer _buffer = new();
    private readonly CancellationTokenSource _inferenceCancellation = new();
    private readonly Stopwatch _elapsed = new();
    private readonly object _bufferGate = new();
    private Task? _reader, _worker;
    private Exception? _error;
    private int _utterance;
    private bool _overflow;
    public event EventHandler<float>? Level;
    public event EventHandler<string>? TextChanged;
    public event EventHandler<bool>? SpeechChanged;
    public event EventHandler<Exception>? Faulted;
    public DictationSession(MicrophoneCaptureSession capture, WhisperEngine engine, AppSettings settings)
    { _capture = capture; _engine = engine; _settings = settings; }
    public void Start()
    {
        _capture.Level += (_, level) => Level?.Invoke(this, level);
        _capture.Faulted += OnCaptureFault;
        _capture.Start(); _elapsed.Start();
        Whisper.net.WhisperProcessor processor;
        try { processor = _engine.CreateProcessor(_settings.Language, _settings.CpuThreads); }
        catch { _ = _capture.StopAsync(); throw; }
        _reader = Task.Run(() => ReadAudioAsync(_settings.VadThreshold));
        _worker = Task.Run(async () =>
        {
            try
            {
                while (await _queue.TakeAsync(_inferenceCancellation.Token).ConfigureAwait(false) is { } job)
                {
                    var text = await _engine.TranscribeAsync(processor, job.Audio, _inferenceCancellation.Token).ConfigureAwait(false);
                    lock (_bufferGate) _buffer.Accept(job.Utterance, text, job.Final);
                    Publish();
                }
            }
            catch (OperationCanceledException) when (_inferenceCancellation.IsCancellationRequested) { }
            catch (Exception ex) { SetError(ex); }
            finally { await processor.DisposeAsync().ConfigureAwait(false); }
        });
    }
    private void OnCaptureFault(object? sender, Exception ex) => SetError(ex);
    private void SetError(Exception ex)
    {
        if (_error is null) Interlocked.CompareExchange(ref _error, ex, null);
        Faulted?.Invoke(this, ex);
    }
    private void Publish() { lock (_bufferGate) TextChanged?.Invoke(this, _buffer.DisplayText); }
    private async Task ReadAudioAsync(double threshold)
    {
        var vad = new UtteranceAccumulator(threshold);
        try
        {
            await foreach (var frame in _capture.Frames.ReadAllAsync().ConfigureAwait(false))
            {
                if (_elapsed.Elapsed >= TimeSpan.FromMinutes(2)) { Volatile.Write(ref _overflow, true); await _capture.StopAsync().ConfigureAwait(false); break; }
                var update = vad.Accept(frame);
                if (update.Started) SpeechChanged?.Invoke(this, true);
                if (update.FinalDue)
                {
                    var audio = vad.TakeFinal(force: vad.BufferedSeconds >= 12);
                    if (audio.Length > 0 && !_queue.TryAdd(new(_utterance++, audio, true))) SetError(new InvalidOperationException("Очередь распознавания заполнена. Выберите более лёгкую модель."));
                    SpeechChanged?.Invoke(this, false);
                }
                else if (update.PartialDue) _queue.TryAdd(new(_utterance, vad.Snapshot(), false));
            }
            if (_capture.Overflowed || Volatile.Read(ref _overflow))
                SetError(new InvalidOperationException(Volatile.Read(ref _overflow)
                    ? "Достигнут лимит сессии 2 минуты. Нажмите горячую клавишу, чтобы продолжить."
                    : "Процессор не успевает. Выберите Base или закройте тяжёлые приложения."));
            var last = vad.TakeFinal(force: true);
            if (last.Length > 0) _queue.TryAdd(new(_utterance, last, true));
        }
        catch (Exception ex) { SetError(ex); }
        finally { _queue.Complete(); }
    }
    public async Task<DictationResult> FinishAsync()
    {
        await _capture.StopAsync().ConfigureAwait(false);
        _elapsed.Stop();
        if (_reader is not null) await _reader.ConfigureAwait(false);
        if (_worker is not null)
        {
            var finished = await Task.WhenAny(_worker, Task.Delay(TimeSpan.FromSeconds(30))).ConfigureAwait(false);
            if (finished != _worker) _inferenceCancellation.Cancel();
            try { await _worker.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        string text; bool timedOut;
        lock (_bufferGate) { text = _buffer.FinalText; timedOut = _buffer.DisplayText != _buffer.FinalText; }
        return new(text, _elapsed.Elapsed.TotalSeconds, timedOut);
    }
    public async ValueTask DisposeAsync()
    {
        _capture.Faulted -= OnCaptureFault;
        _inferenceCancellation.Cancel(); _queue.Complete();
        if (_reader is not null) try { await _reader.ConfigureAwait(false); } catch { }
        if (_worker is not null) try { await _worker.ConfigureAwait(false); } catch { }
        await _capture.DisposeAsync().ConfigureAwait(false);
        _queue.Dispose(); _inferenceCancellation.Dispose();
    }
}
