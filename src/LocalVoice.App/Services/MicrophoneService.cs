using NAudio.CoreAudioApi;
using NAudio.Wave;
using LocalVoice.Core;
namespace LocalVoice.App.Services;

public sealed record MicrophoneDevice(string Id, string Name);
public sealed class MicrophoneService
{
    public IReadOnlyList<MicrophoneDevice> GetDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        return enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
            .Using(x => x.Select(d => new MicrophoneDevice(d.ID, d.FriendlyName)).ToArray());
    }
    public (WasapiCapture Capture, MMDevice Device, string Name) CreateCapture(string deviceId)
    {
        var enumerator = new MMDeviceEnumerator();
        MMDevice? device = null;
        try
        {
            device = string.IsNullOrEmpty(deviceId)
                ? enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console)
                : enumerator.GetDevice(deviceId);
            if (device.State != DeviceState.Active) throw new InvalidOperationException("Микрофон отключён. Выберите другое устройство.");
            var name = device.FriendlyName;
            var capture = new WasapiCapture(device, true, 100) { WaveFormat = new WaveFormat(16000, 16, 1), ShareMode = AudioClientShareMode.Shared };
            return (capture, device, name);
        }
        catch { device?.Dispose(); device = null; throw; }
        finally { enumerator.Dispose(); }
    }
}
internal static class DisposableExtensions
{
    public static TResult Using<T, TResult>(this IEnumerable<T> source, Func<IEnumerable<T>, TResult> map) where T : IDisposable
    {
        var items = source.ToArray();
        try { return map(items); } finally { foreach (var item in items) item.Dispose(); }
    }
}
