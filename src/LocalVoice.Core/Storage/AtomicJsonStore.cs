using System.Text.Json;
using System.Text.Json.Serialization;

namespace LocalVoice.Core;

public static class AtomicJsonStore
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
        NumberHandling = JsonNumberHandling.Strict
    };
    public static async Task<T> LoadAsync<T>(string path, Func<T> fallback, CancellationToken ct = default) where T : class
        => (await LoadWithStatusAsync(path, fallback, ct).ConfigureAwait(false)).Value;
    public static async Task<(T Value, bool UsedBackup, bool HadExistingFile)> LoadWithStatusAsync<T>(string path, Func<T> fallback, CancellationToken ct = default) where T : class
    {
        var hadExistingFile = File.Exists(path) || File.Exists(path + ".bak");
        foreach (var candidate in new[] { path, path + ".bak" })
        {
            if (!File.Exists(candidate)) continue;
            try
            {
                await using var stream = File.OpenRead(candidate);
                var value = await JsonSerializer.DeserializeAsync<T>(stream, Options, ct).ConfigureAwait(false);
                if (value is not null) return (value, candidate.EndsWith(".bak", StringComparison.Ordinal), hadExistingFile);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { }
        }
        return (fallback(), false, hadExistingFile);
    }
    public static async Task SaveAsync<T>(string path, T value, CancellationToken ct = default)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16384, true))
            {
                await JsonSerializer.SerializeAsync(stream, value, Options, ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(path)) File.Replace(temp, path, path + ".bak", ignoreMetadataErrors: true);
            else File.Move(temp, path);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { }
        }
    }
}
