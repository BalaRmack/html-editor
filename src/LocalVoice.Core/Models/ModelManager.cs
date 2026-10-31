using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
namespace LocalVoice.Core;

public sealed record DownloadProgress(long Received, long Total, string Stage);
public sealed class ModelManager : IDisposable
{
    private readonly AppPaths _paths;
    private readonly IAppLog _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HttpClient _http;
    private CancellationTokenSource? _download;
    public bool IsBusy { get; private set; }
    public event EventHandler? Changed;
    public ModelManager(AppPaths paths, IAppLog log, HttpClient? httpClient = null)
    {
        _paths = paths; _log = log;
        _http = httpClient ?? new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.None,
            MaxConnectionsPerServer = 2, PooledConnectionLifetime = TimeSpan.FromMinutes(2)
        }) { Timeout = Timeout.InfiniteTimeSpan };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("LocalVoice/1.0");
    }
    public string GetPath(ModelDescriptor model) => Path.Combine(_paths.ModelsDirectory, model.FileName);
    public bool LooksInstalled(ModelDescriptor model) => File.Exists(GetPath(model)) && new FileInfo(GetPath(model)).Length == model.ExpectedBytes;
    public long InstalledBytes => ModelCatalog.All.Where(LooksInstalled).Sum(x => x.ExpectedBytes);
    public void CancelDownload() => _download?.Cancel();
    public async Task VerifyAsync(ModelDescriptor model, CancellationToken ct = default)
    {
        var path = GetPath(model);
        if (!File.Exists(path) || new FileInfo(path).Length != model.ExpectedBytes)
            throw new FileNotFoundException("Файл модели отсутствует или повреждён. Скачайте его заново.", path);
        var hash = await HashFileAsync(path, ct).ConfigureAwait(false);
        if (!hash.Equals(model.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Контрольная сумма модели не совпала. Удалите модель и скачайте её заново.");
    }
    public async Task DownloadAsync(ModelDescriptor model, IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _download = linked; IsBusy = true; Changed?.Invoke(this, EventArgs.Empty);
        var target = GetPath(model); var partial = target + ".partial";
        try
        {
            if (LooksInstalled(model))
            {
                progress?.Report(new(model.ExpectedBytes, model.ExpectedBytes, "Проверка SHA-256"));
                await VerifyAsync(model, linked.Token).ConfigureAwait(false); return;
            }
            var free = new DriveInfo(Path.GetPathRoot(target)!).AvailableFreeSpace;
            if (free < model.ExpectedBytes + 256L * 1024 * 1024)
                throw new IOException("Недостаточно места. Освободите ещё как минимум 256 МБ сверх размера модели.");
            TryDelete(partial);
            var uri = ValidateDownloadUri(model.Url);
            using var response = await GetWithSafeRedirectsAsync(uri, linked.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? model.ExpectedBytes;
            if (total != model.ExpectedBytes) throw new InvalidDataException("Размер файла на сервере изменился. Загрузка отменена.");
            long received = 0;
            await using (var source = await response.Content.ReadAsStreamAsync(linked.Token).ConfigureAwait(false))
            await using (var destination = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 128, true))
            {
                var buffer = new byte[128 * 1024]; var watch = Stopwatch.StartNew();
                while (true)
                {
                    var read = await source.ReadAsync(buffer, linked.Token).ConfigureAwait(false);
                    if (read == 0) break;
                    received += read;
                    if (received > model.ExpectedBytes) throw new InvalidDataException("Сервер передал больше данных, чем ожидается.");
                    await destination.WriteAsync(buffer.AsMemory(0, read), linked.Token).ConfigureAwait(false);
                    if (watch.ElapsedMilliseconds >= 80)
                    {
                        progress?.Report(new(received, model.ExpectedBytes, "Скачивание"));
                        watch.Restart();
                    }
                }
                destination.Flush(flushToDisk: true);
            }
            if (received != model.ExpectedBytes) throw new InvalidDataException("Загрузка прервана. Попробуйте ещё раз.");
            progress?.Report(new(received, total, "Проверка SHA-256"));
            var hash = await HashFileAsync(partial, linked.Token).ConfigureAwait(false);
            if (!hash.Equals(model.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("SHA-256 не совпал. Непроверенная модель удалена.");
            File.Move(partial, target, overwrite: true);
            progress?.Report(new(total, total, "Готово"));
            _log.Info($"Model downloaded and verified: {model.Id}");
        }
        catch (OperationCanceledException) { TryDelete(partial); throw; }
        catch { TryDelete(partial); throw; }
        finally
        {
            _download = null; IsBusy = false; _gate.Release(); Changed?.Invoke(this, EventArgs.Empty);
        }
    }
    private async Task<HttpResponseMessage> GetWithSafeRedirectsAsync(Uri uri, CancellationToken ct)
    {
        for (var redirect = 0; redirect < 8; redirect++)
        {
            var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is not null)
            {
                var next = response.Headers.Location.IsAbsoluteUri ? response.Headers.Location : new Uri(uri, response.Headers.Location);
                response.Dispose(); uri = ValidateDownloadUri(next.AbsoluteUri); continue;
            }
            return response;
        }
        throw new InvalidDataException("Слишком много перенаправлений при загрузке модели.");
    }
    public static Uri ValidateDownloadUri(string value)
    {
        var uri = new Uri(value);
        if (uri.Scheme != "https" || !uri.IsDefaultPort || string.IsNullOrEmpty(uri.Host) ||
            !(uri.Host.Equals("huggingface.co", StringComparison.OrdinalIgnoreCase) ||
              uri.Host.EndsWith(".huggingface.co", StringComparison.OrdinalIgnoreCase) ||
              uri.Host.EndsWith(".hf.co", StringComparison.OrdinalIgnoreCase) ||
              uri.Host.Equals("xethub.hf.co", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Разрешены только HTTPS-адресы Hugging Face и его CDN.");
        return uri;
    }
    public async Task DeleteAsync(ModelDescriptor model, Func<Task> unload)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_download is not null) throw new InvalidOperationException("Сначала отмените загрузку.");
            await unload().ConfigureAwait(false);
            TryDelete(GetPath(model));
            _log.Info($"Model deleted: {model.Id}");
        }
        finally { _gate.Release(); Changed?.Invoke(this, EventArgs.Empty); }
    }
    public static async Task<string> HashFileAsync(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 128, true);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1024 * 128];
        while (true)
        {
            var count = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (count == 0) break;
            sha.AppendData(buffer, 0, count);
        }
        return Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
    }
    private static void TryDelete(string path) { try { File.Delete(path); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } }
    public void Dispose() { _download?.Cancel(); _http.Dispose(); _gate.Dispose(); }
}
