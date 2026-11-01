using System.Buffers.Binary;
using System.Threading.Channels;
using LocalVoice.Core;
using NAudio.Wave;
using NAudio.CoreAudioApi;
namespace LocalVoice.App.Services;

public sealed class MicrophoneCaptureSession : IAudioCaptureSession
{
    private readonly WasapiCapture _capture;
    private readonly MMDevice _owner;
    private readonly CancellationTokenSource _cts = new();
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _overflow, _disposed;
    public string DeviceName { get; }
    public ChannelReader<AudioFrame> Frames => _frames.Reader;
    private readonly Channel<AudioFrame> _frames = Channel.CreateBounded<AudioFrame>(new BoundedChannelOptions(256)
    { FullMode = BoundedChannelFullMode.Wait, SingleReader = true, SingleWriter = true });
    public event EventHandler<float>? Level;
    public event EventHandler<Exception>? Faulted;
    public bool Overflowed => Volatile.Read(ref _overflow) != 0;
    public MicrophoneCaptureSession(WasapiCapture capture, MMDevice owner, string name)
    {
        _capture = capture; DeviceName = name;
        // WasapiCapture does not own the MMDevice; keep it alive until capture is disposed.
        _owner = owner;
        _capture.DataAvailable += OnData;
        _capture.RecordingStopped += OnStopped;
    }
    public void Start()
    {
        try { _capture.StartRecording(); }
        catch { _stopped.TrySetResult(); throw; }
    }
    private void OnData(object? sender, WaveInEventArgs args)
    {
        if (_cts.IsCancellationRequested || args.BytesRecorded == 0) return;
        try
        {
            var format = _capture.WaveFormat;
            if (format.Encoding != WaveFormatEncoding.Pcm || format.BitsPerSample != 16 || format.SampleRate != 16000 || format.Channels != 1)
                throw new NotSupportedException("Windows не предоставила ожидаемый формат PCM 16 кГц. Выберите другой микрофон.");
            var samples = new float[args.BytesRecorded / 2];
            for (var i = 0; i < samples.Length; i++)
                samples[i] = BinaryPrimitives.ReadInt16LittleEndian(args.Buffer.AsSpan(i * 2, 2)) / 32768f;
            var frame = new AudioFrame(samples, AudioFrame.ComputeRms(samples));
            if (!_frames.Writer.TryWrite(frame))
            {
                Interlocked.Exchange(ref _overflow, 1);
                Faulted?.Invoke(this, new InvalidOperationException("Процессор не успевает. Выберите модель Base или закройте тяжёлые приложения."));
                _ = StopAsync(); return;
            }
            Level?.Invoke(this, frame.Rms);
        }
        catch (Exception ex) { Faulted?.Invoke(this, ex); _ = StopAsync(); }
    }
    private void OnStopped(object? sender, StoppedEventArgs args)
    {
        _frames.Writer.TryComplete();
        if (args.Exception is not null) Faulted?.Invoke(this, args.Exception);
        _stopped.TrySetResult();
    }
    public async Task StopAsync()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        if (_cts.IsCancellationRequested) { await _stopped.Task.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false); return; }
        _cts.Cancel();
        try { _capture.StopRecording(); } catch { _stopped.TrySetResult(); }
        try { await _stopped.Task.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false); }
        catch (TimeoutException) { _frames.Writer.TryComplete(); }
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _cts.Cancel();
        try { _capture.StopRecording(); } catch { _stopped.TrySetResult(); }
        try { await _stopped.Task.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false); }
        catch (TimeoutException) { _frames.Writer.TryComplete(); }
        _capture.DataAvailable -= OnData; _capture.RecordingStopped -= OnStopped;
        _capture.Dispose(); _owner.Dispose(); _cts.Dispose();
    }
}
