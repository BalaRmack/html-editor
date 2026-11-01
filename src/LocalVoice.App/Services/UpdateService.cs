using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using LocalVoice.Core;
using Application = System.Windows.Application;

namespace LocalVoice.App.Services;

/// <summary>
/// In-app updater. Checks GitHub Releases, downloads the per-user installer, verifies size + SHA-256,
/// and asks the app to restart into the silent installer. No browser, no audio, no transcript data.
/// </summary>
public sealed partial class UpdateService : ObservableObject, IAsyncDisposable
{
    private readonly AppPaths _paths;
    private readonly SettingsStore _settings;
    private readonly IAppLog _log;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private readonly List<Task> _loops = [];
    private Func<bool>? _canInstallPredicate;
    private bool _installRequested, _disposed;

    [ObservableProperty] private string _statusText = "Обновления не проверялись";
    [ObservableProperty] private string _detailText = "";
    [ObservableProperty] private double _progress;
    [ObservableProperty] private bool _isChecking;
    [ObservableProperty] private bool _isDownloading;
    [ObservableProperty] private bool _isReady;
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private string _availableVersionText = "";
    [ObservableProperty] private bool _canInstall;
    public string? PendingInstallerPath { get; private set; }
    public event EventHandler<string>? InstallRequested;

    public UpdateService(AppPaths paths, SettingsStore settings, IAppLog log, HttpMessageHandler? handler = null)
    {
        _paths = paths; _settings = settings; _log = log;
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _http.Timeout = TimeSpan.FromMinutes(30);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"LocalVoice/{typeof(UpdateService).Assembly.GetName().Version?.ToString(3) ?? "0.0.0"} (+https://github.com/{UpdatePolicy.Repository})");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    public void Start(Func<bool> canInstall)
    {
        _canInstallPredicate = canInstall;
        _loops.Add(Task.Run(() => CheckLoopAsync(_cts.Token)));
        _loops.Add(Task.Run(() => InstallLoopAsync(_cts.Token)));
    }

    private async Task CheckLoopAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(15), ct);
            while (!ct.IsCancellationRequested)
            {
                await CheckAndDownloadAsync(false, ct);
                await Task.Delay(TimeSpan.FromHours(6), ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _log.Error("Update check loop", ex); }
    }
    private async Task InstallLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(15), ct);
                TryAutoInstall();
            }
        }
        catch (OperationCanceledException) { }
    }

    public void TryAutoInstall()
    {
        if (_disposed || _installRequested || !IsReady || PendingInstallerPath is null) return;
        if (!_settings.Current.AutoUpdate || !_settings.Current.AutoInstallUpdates) return;
        if (_canInstallPredicate?.Invoke() != true) return;
        RequestInstall();
    }
    public bool RequestInstall()
    {
        if (!IsReady || PendingInstallerPath is null || _installRequested) return false;
        _installRequested = true;
        InstallRequested?.Invoke(this, PendingInstallerPath);
        return true;
    }
    public void MarkInstallFailed(Exception ex)
    {
        _installRequested = false;
        _log.Error("Apply update", ex);
        Ui(() => { HasError = true; StatusText = "Не удалось установить обновление"; DetailText = ex.Message; });
    }

    public async Task<bool> CheckAndDownloadAsync(bool force, CancellationToken ct = default)
    {
        if (_disposed) return false;
        if (!force && !_settings.Current.AutoUpdate)
        {
            Ui(() => { StatusText = "Автообновление выключено"; DetailText = "Включите его в настройках или проверьте вручную."; });
            return false;
        }
        await _gate.WaitAsync(ct);
        try
        {
            Ui(() => { IsChecking = true; HasError = false; StatusText = "Проверяю обновления…"; DetailText = ""; Progress = 0; });
            using var metadataCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
            metadataCts.CancelAfter(TimeSpan.FromSeconds(30));
            var release = await FetchLatestAsync(metadataCts.Token);
            var current = typeof(UpdateService).Assembly.GetName().Version ?? new Version(0, 0, 0);
            if (!UpdatePolicy.IsNewer(release.Version, current))
            {
                Ui(() => { IsReady = false; CanInstall = false; AvailableVersionText = ""; StatusText = "У вас последняя версия"; DetailText = $"LocalVoice {current.ToString(3)}."; });
                return false;
            }

            Ui(() => { IsChecking = false; IsDownloading = true; AvailableVersionText = release.VersionText; StatusText = $"Скачиваю LocalVoice {release.VersionText}…"; DetailText = "Загрузка идёт внутри приложения."; });
            var checksumText = await DownloadStringAsync(release.Checksum, 4096, ct);
            if (!UpdatePolicy.TryReadChecksum(checksumText, release.Installer.Name, out var expectedHash))
                throw new InvalidDataException("Файл контрольной суммы обновления повреждён или не соответствует установщику.");

            var dir = Path.Combine(_paths.UpdatesDirectory, release.VersionText);
            Directory.CreateDirectory(dir);
            var finalPath = Path.Combine(dir, release.Installer.Name);
            if (!await IsValidInstallerAsync(finalPath, release.Installer.Size, expectedHash, ct))
            {
                await DownloadInstallerAsync(release.Installer, finalPath, expectedHash, ct);
            }
            CleanupOldVersions(release.VersionText);
            PendingInstallerPath = finalPath;
            _log.Info($"Update {release.VersionText} downloaded and SHA-256 verified.");
            Ui(() =>
            {
                IsDownloading = false; IsReady = true; HasError = false; Progress = 100;
                StatusText = $"Обновление {release.VersionText} готово";
                DetailText = _settings.Current.AutoInstallUpdates
                    ? "Установится автоматически, когда диктовка и загрузки свободны. Можно нажать «Установить сейчас»."
                    : "Нажмите «Установить сейчас». Приложение перезапустится само.";
            });
            TryAutoInstall();
            return true;
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested) { return false; }
        catch (Exception ex)
        {
            _log.Error("Update download", ex);
            Ui(() => { HasError = true; StatusText = "Не удалось обновить"; DetailText = Friendly(ex); });
            return false;
        }
        finally
        {
            Ui(() => { IsChecking = false; IsDownloading = false; });
            _gate.Release();
        }
    }

    private async Task<AvailableUpdate> FetchLatestAsync(CancellationToken ct)
    {
        using var response = await _http.GetAsync(UpdatePolicy.LatestReleaseApi, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            throw new HttpRequestException("GitHub временно ограничил проверки обновлений. Попробуйте позже.");
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;
        if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean()) throw new InvalidDataException("Последний релиз помечен как черновик.");
        if (root.TryGetProperty("prerelease", out var pre) && pre.GetBoolean()) throw new InvalidDataException("Последний релиз является предварительным.");
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!UpdatePolicy.TryParseStableVersion(tag, out var version)) throw new InvalidDataException($"Не удалось разобрать версию релиза: {tag}");
        var installerName = UpdatePolicy.InstallerName(version);
        var checksumName = UpdatePolicy.ChecksumName(version);
        UpdateAsset? installer = null, checksum = null;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            var item = new UpdateAsset(asset.GetProperty("id").GetInt64(), name, asset.GetProperty("size").GetInt64(), asset.GetProperty("url").GetString() ?? "");
            if (name.Equals(installerName, StringComparison.OrdinalIgnoreCase)) installer = item;
            else if (name.Equals(checksumName, StringComparison.OrdinalIgnoreCase)) checksum = item;
        }
        if (installer is null || checksum is null) throw new FileNotFoundException("В релизе нет установщика win-x64 или его SHA-256.");
        if (installer.Size <= 0 || !UpdatePolicy.IsTrustedInitialUrl(installer.DownloadUrl) || !UpdatePolicy.IsTrustedInitialUrl(checksum.DownloadUrl))
            throw new InvalidDataException("Метаданные установщика не прошли проверку.");
        var notes = root.TryGetProperty("body", out var body) ? body.GetString() : null;
        return new AvailableUpdate(version, tag, installer, checksum, notes);
    }

    private async Task<string> DownloadStringAsync(UpdateAsset asset, int maxBytes, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, asset.DownloadUrl);
        request.Headers.Accept.ParseAdd("application/octet-stream");
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > maxBytes) throw new InvalidDataException("Файл контрольной суммы слишком большой.");
        }
        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    private async Task DownloadInstallerAsync(UpdateAsset asset, string finalPath, string expectedHash, CancellationToken ct)
    {
        var partial = finalPath + ".partial";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, asset.DownloadUrl);
            request.Headers.Accept.ParseAdd("application/octet-stream");
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            await using var network = await response.Content.ReadAsStreamAsync(ct);
            await using var file = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            long received = 0; var lastProgress = -1;
            int read;
            while ((read = await network.ReadAsync(buffer, ct)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                await file.WriteAsync(buffer.AsMemory(0, read), ct);
                sha.AppendData(buffer, 0, read);
                received += read;
                if (received > asset.Size) throw new InvalidDataException("Установщик больше заявленного размера.");
                var percent = (int)(received * 100 / Math.Max(1, asset.Size));
                if (percent != lastProgress)
                {
                    lastProgress = percent;
                    Ui(() => { Progress = percent; DetailText = $"{UpdatePolicy.FormatBytes(received)} / {UpdatePolicy.FormatBytes(asset.Size)} · SHA-256 будет проверен."; });
                }
            }
            await file.FlushAsync(ct);
            if (received != asset.Size) throw new InvalidDataException("Установщик скачан не полностью.");
            var hash = Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
            if (!hash.Equals(expectedHash, StringComparison.Ordinal)) throw new InvalidDataException("SHA-256 установщика не совпал. Файл удалён.");
            file.Close();
            File.Move(partial, finalPath, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(partial)) File.Delete(partial); } catch (IOException) { }
            throw;
        }
    }

    private static async Task<bool> IsValidInstallerAsync(string path, long size, string expectedHash, CancellationToken ct)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length != size) return false;
            await using var stream = File.OpenRead(path);
            using var sha = SHA256.Create();
            var hash = Convert.ToHexString(await sha.ComputeHashAsync(stream, ct)).ToLowerInvariant();
            return hash.Equals(expectedHash, StringComparison.Ordinal);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private void CleanupOldVersions(string currentVersion)
    {
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(_paths.UpdatesDirectory))
            {
                if (string.Equals(Path.GetFileName(dir), currentVersion, StringComparison.OrdinalIgnoreCase)) continue;
                try { Directory.Delete(dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
        catch (IOException) { }
    }

    private static string Friendly(Exception ex) => ex switch
    {
        HttpRequestException http => http.Message,
        TaskCanceledException => "Время ожидания истекло. Проверьте подключение и повторите.",
        _ => ex.Message
    };
    partial void OnIsCheckingChanged(bool value) => RefreshCanInstall();
    partial void OnIsDownloadingChanged(bool value) => RefreshCanInstall();
    partial void OnIsReadyChanged(bool value) => RefreshCanInstall();
    private void RefreshCanInstall() => Ui(() => CanInstall = IsReady && !IsChecking && !IsDownloading && PendingInstallerPath is not null);
    private static void Ui(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(action);
    }
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true; _cts.Cancel();
        try { await Task.WhenAll(_loops).WaitAsync(TimeSpan.FromSeconds(3)); } catch (Exception) { }
        _http.Dispose(); _cts.Dispose(); _gate.Dispose();
    }
}
