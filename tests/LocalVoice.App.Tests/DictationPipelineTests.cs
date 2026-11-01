using System.Threading.Channels;
using LocalVoice.App.Services;
using LocalVoice.Core;
using Xunit;

namespace LocalVoice.App.Tests;

public class DictationPipelineTests
{
    private static AppSettings Settings() => new() { VadThreshold = 0.012, CpuThreads = 2 };
    private static AudioFrame Frame(float amplitude, int samples = 1600)
    {
        var pcm = new float[samples]; Array.Fill(pcm, amplitude);
        return new AudioFrame(pcm, amplitude, DateTime.UtcNow);
    }

    [Fact]
    public async Task ChunkedSpeech_ProducesFinalText()
    {
        await using var capture = new FakeCapture();
        await using var recognition = new FakeRecognition((_, _) => Task.FromResult("привет мир"));
        await using var session = new DictationSession(capture, recognition, Settings());
        var text = ""; session.TextChanged += (_, value) => text = value;
        session.Start();
        for (var i = 0; i < 20; i++) capture.Push(Frame(0.1f));
        for (var i = 0; i < 10; i++) capture.Push(Frame(0f));
        capture.Complete();
        var result = await session.FinishAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Contains("привет мир", result.Text); Assert.Contains("привет мир", text);
        Assert.True(recognition.Calls >= 1);
    }

    [Fact]
    public async Task PartialText_AppearsBeforeStop()
    {
        await using var capture = new FakeCapture();
        await using var recognition = new FakeRecognition((_, _) => Task.FromResult("промежуточный текст"));
        await using var session = new DictationSession(capture, recognition, Settings());
        var seen = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.TextChanged += (_, value) => { if (!string.IsNullOrWhiteSpace(value)) seen.TrySetResult(value); };
        session.Start();
        for (var i = 0; i < 15; i++) capture.Push(Frame(0.1f));
        var partial = await seen.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("промежуточный текст", partial);
        capture.Complete(); await session.FinishAsync().WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task SlowPartial_DoesNotQueueAnotherPartial()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var capture = new FakeCapture();
        await using var recognition = new FakeRecognition(async (_, _) => { await release.Task; return "готово"; });
        await using var session = new DictationSession(capture, recognition, Settings());
        session.Start();
        for (var i = 0; i < 50; i++) capture.Push(Frame(0.1f));
        await WaitUntil(() => recognition.Calls == 1, TimeSpan.FromSeconds(5));
        Assert.Equal(1, recognition.Calls);
        release.SetResult(); capture.Complete();
        await session.FinishAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(recognition.Calls <= 3, $"Expected no partial backlog, got {recognition.Calls} calls");
    }

    [Fact]
    public async Task Stop_FlushesLastAudioWithoutSilence()
    {
        await using var capture = new FakeCapture();
        await using var recognition = new FakeRecognition((_, _) => Task.FromResult("последняя фраза"));
        await using var session = new DictationSession(capture, recognition, Settings());
        session.Start();
        for (var i = 0; i < 8; i++) capture.Push(Frame(0.1f));
        capture.Complete();
        var result = await session.FinishAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("последняя фраза", result.Text);
    }

    [Fact]
    public async Task Silence_DoesNotInvokeRecognizer()
    {
        await using var capture = new FakeCapture();
        await using var recognition = new FakeRecognition((_, _) => Task.FromResult("галлюцинация"));
        await using var session = new DictationSession(capture, recognition, Settings());
        var statuses = new List<string>(); session.StatusChanged += (_, value) => statuses.Add(value);
        session.Start();
        for (var i = 0; i < 12; i++) capture.Push(Frame(0.001f));
        capture.Complete();
        var result = await session.FinishAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("", result.Text); Assert.Equal(0, recognition.Calls);
    }

    private static async Task WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && watch.Elapsed < timeout) await Task.Delay(20);
        Assert.True(condition(), "Condition was not met before timeout");
    }

    private sealed class FakeCapture : IAudioCaptureSession
    {
        private readonly Channel<AudioFrame> _channel = Channel.CreateBounded<AudioFrame>(512);
        public string DeviceName => "Fake microphone";
        public ChannelReader<AudioFrame> Frames => _channel.Reader;
        public bool Overflowed => false;
        public event EventHandler<float>? Level;
        public event EventHandler<Exception>? Faulted;
        public void Start() { }
        public void Push(AudioFrame frame) { _channel.Writer.TryWrite(frame); Level?.Invoke(this, frame.Rms); }
        public void Complete() => _channel.Writer.TryComplete();
        public Task StopAsync() { Complete(); return Task.CompletedTask; }
        private int _disposed;
        public ValueTask DisposeAsync() { if (Interlocked.Exchange(ref _disposed, 1) == 0) { Complete(); Faulted?.Invoke(this, new InvalidOperationException("disposed")); } return ValueTask.CompletedTask; }
    }
    private sealed class FakeRecognition(Func<float[], CancellationToken, Task<string>> recognize) : IRecognitionSession
    {
        public int Calls;
        public Task<string> TranscribeAsync(float[] pcm, CancellationToken ct) { Interlocked.Increment(ref Calls); return recognize(pcm, ct); }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
