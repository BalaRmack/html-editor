using System.Threading.Channels;
namespace LocalVoice.Core;

public interface IAppLog { void Info(string message); void Error(string context, Exception exception); string DirectoryPath { get; } }
public sealed class FileLog : IAppLog, IAsyncDisposable
{
    private readonly Channel<string> _channel = Channel.CreateBounded<string>(new BoundedChannelOptions(512) { FullMode = BoundedChannelFullMode.DropWrite });
    private readonly Task _writer;
    private readonly CancellationTokenSource _cts = new();
    public string DirectoryPath { get; }
    public FileLog(string directory)
    {
        DirectoryPath = directory;
        Directory.CreateDirectory(directory);
        _writer = WriteLoopAsync();
    }
    public void Info(string message) => Write("INFO", message);
    public void Error(string context, Exception exception) => Write("ERROR", $"{context}: {exception.GetType().Name}: {Sanitize(exception.Message)}");
    private void Write(string level, string message) => _channel.Writer.TryWrite($"{DateTimeOffset.UtcNow:O} [{level}] {Sanitize(message)}");
    private static string Sanitize(string text) => text.ReplaceLineEndings(" ")[..Math.Min(text.ReplaceLineEndings(" ").Length, 1600)];
    private async Task WriteLoopAsync()
    {
        try
        {
            await foreach (var line in _channel.Reader.ReadAllAsync(_cts.Token))
            {
                var path = Path.Combine(DirectoryPath, $"localvoice-{DateTime.UtcNow:yyyyMMdd}.log");
                if (File.Exists(path) && new FileInfo(path).Length > 2_000_000) continue;
                await File.AppendAllTextAsync(path, line + Environment.NewLine, _cts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    public async ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();
        try { await _writer.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); } catch (TimeoutException) { _cts.Cancel(); }
        _cts.Dispose();
    }
}
