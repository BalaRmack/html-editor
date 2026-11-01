using System.Threading.Channels;
using LocalVoice.Core;
namespace LocalVoice.App.Services;

public interface IAudioCaptureSession : IAsyncDisposable
{
    string DeviceName { get; }
    ChannelReader<AudioFrame> Frames { get; }
    bool Overflowed { get; }
    event EventHandler<float>? Level;
    event EventHandler<Exception>? Faulted;
    void Start();
    Task StopAsync();
}

public interface IRecognitionSession : IAsyncDisposable
{
    Task<string> TranscribeAsync(float[] pcm, CancellationToken ct);
}
