using System.Diagnostics;
using LocalVoice.Core;
namespace LocalVoice.App.Services;

public sealed record DictationResult(string Text, double Seconds, bool TimedOut);

/// <summary>
/// Streams microphone frames through VAD into a single recognizer. Partials are opportunistic:
/// only one may be in flight, and finals always win. This keeps slow/NoAvx CPUs from drowning
/// in repeated partial inference while still flushing the last utterance on stop.
/// </summary>
public sealed class DictationSession : IAsyncDisposable
{
    private readonly IAudioCaptureSession _capture;
    private readonly IRecognitionSession _recognition;
    private readonly AppSettings _settings;
    private readonly RecognitionQueue _queue = new();
    private readonly TranscriptBuffer _buffer = new();
    private readonly CancellationTokenSource _inferenceCancellation = new();
    private readonly Stopwatch _elapsed = new();
    private readonly object _bufferGate = new();
    private Task? _reader, _worker;
    private Exception? _error;
    private int _utterance, _overflow, _partialInFlight, _processing;
    private long _frames, _finalJobs, _partialJobs, _emptyResults, _inferenceMs;
    private float _peakRms;
    private string _status = "Слушаю…";
    private TimeSpan _lastVoiced = TimeSpan.Zero;
    private bool _heardSpeech;

    public event EventHandler<float>? Level;
    public event EventHandler<string>? TextChanged;
    public event EventHandler<bool>? SpeechChanged;
    public event EventHandler<string>? StatusChanged;
    public event EventHandler<Exception>? Faulted;
    public string DiagnosticSummary =>
        $"frames={_frames}; peakRms={_peakRms:F4}; heardSpeech={_heardSpeech}; finals={_finalJobs}; partials={_partialJobs}; empty={_emptyResults}; inferenceMs={_inferenceMs}";

    public DictationSession(IAudioCaptureSession capture, IRecognitionSession recognition, AppSettings settings)
    { _capture = capture; _recognition = recognition; _settings = settings; }

    public void Start()
    {
        _capture.Level += OnLevel;
        _capture.Faulted += OnCaptureFault;
        _capture.Start(); _elapsed.Start();
        _reader = Task.Run(() => ReadAudioAsync(_settings.VadThreshold));
        _worker = Task.Run(WorkerAsync);
    }
    private void OnLevel(object? sender, float level)
    {
        Interlocked.Increment(ref _frames);
        if (level > _peakRms) _peakRms = level;
        Level?.Invoke(this, level);
    }
    private void OnCaptureFault(object? sender, Exception ex) => SetError(ex);
    private void SetError(Exception ex)
    {
        if (_error is null) Interlocked.CompareExchange(ref _error, ex, null);
        Faulted?.Invoke(this, ex);
    }
    private void SetStatus(string status)
    {
        if (_status == status) return;
        _status = status; StatusChanged?.Invoke(this, status);
    }
    private void Publish() { lock (_bufferGate) TextChanged?.Invoke(this, _buffer.DisplayText); }

    private async Task WorkerAsync()
    {
        try
        {
            while (await _queue.TakeAsync(_inferenceCancellation.Token).ConfigureAwait(false) is { } job)
            {
                Interlocked.Increment(ref _processing);
                SetStatus(job.Final ? "Обрабатываю фразу…" : "Распознаю…");
                var watch = Stopwatch.StartNew();
                string text;
                try { text = await _recognition.TranscribeAsync(job.Audio, _inferenceCancellation.Token).ConfigureAwait(false); }
                finally
                {
                    watch.Stop(); Interlocked.Add(ref _inferenceMs, watch.ElapsedMilliseconds);
                    Interlocked.Decrement(ref _processing);
                    if (!job.Final) Interlocked.Exchange(ref _partialInFlight, 0);
                }
                if (job.Final) Interlocked.Increment(ref _finalJobs); else Interlocked.Increment(ref _partialJobs);
                if (string.IsNullOrWhiteSpace(text)) Interlocked.Increment(ref _emptyResults);
                lock (_bufferGate) _buffer.Accept(job.Utterance, text, job.Final);
                Publish();
                SetStatus(string.IsNullOrWhiteSpace(text) ? (_heardSpeech ? "Слушаю…" : "Нет речи — проверьте микрофон") : "Текст готов");
            }
        }
        catch (OperationCanceledException) when (_inferenceCancellation.IsCancellationRequested) { }
        catch (Exception ex) { SetError(ex); }
    }

    private async Task ReadAudioAsync(double threshold)
    {
        var vad = new UtteranceAccumulator(threshold);
        try
        {
            await foreach (var frame in _capture.Frames.ReadAllAsync().ConfigureAwait(false))
            {
                if (_elapsed.Elapsed >= TimeSpan.FromMinutes(2)) { Volatile.Write(ref _overflow, 1); await _capture.StopAsync().ConfigureAwait(false); break; }
                var update = vad.Accept(frame);
                if (frame.Rms >= threshold * 0.6) _lastVoiced = _elapsed.Elapsed;
                if (update.Started)
                {
                    _heardSpeech = true; SpeechChanged?.Invoke(this, true);
                    if (Volatile.Read(ref _processing) == 0) SetStatus("Слышу речь…");
                }
                if (update.FinalDue)
                {
                    var audio = vad.TakeFinal(force: vad.BufferedSeconds >= 12);
                    if (audio.Length > 0 && !_queue.TryAdd(new(_utterance++, audio, true)))
                        SetError(new InvalidOperationException("Очередь распознавания заполнена. Выберите более лёгкую модель."));
                    SpeechChanged?.Invoke(this, false);
                }
                else if (update.PartialDue)
                {
                    // Never queue a second partial while one is still being recognized.
                    if (Volatile.Read(ref _processing) == 0 && Interlocked.CompareExchange(ref _partialInFlight, 1, 0) == 0)
                    {
                        if (!_queue.TryAdd(new(_utterance, vad.Snapshot(), false))) Interlocked.Exchange(ref _partialInFlight, 0);
                    }
                }
                if (!_heardSpeech && _elapsed.Elapsed - _lastVoiced > TimeSpan.FromSeconds(8) && Volatile.Read(ref _processing) == 0)
                    SetStatus("Нет речи — проверьте микрофон");
            }
            if (_capture.Overflowed || Volatile.Read(ref _overflow) != 0)
                SetError(new InvalidOperationException(Volatile.Read(ref _overflow) != 0
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
        _capture.Level -= OnLevel; _capture.Faulted -= OnCaptureFault;
        try { await _capture.StopAsync().ConfigureAwait(false); } catch { }
        _inferenceCancellation.Cancel(); _queue.Complete();
        if (_reader is not null) try { await _reader.ConfigureAwait(false); } catch { }
        if (_worker is not null) try { await _worker.ConfigureAwait(false); } catch { }
        await _recognition.DisposeAsync().ConfigureAwait(false);
        await _capture.DisposeAsync().ConfigureAwait(false);
        _queue.Dispose(); _inferenceCancellation.Dispose();
    }
}
