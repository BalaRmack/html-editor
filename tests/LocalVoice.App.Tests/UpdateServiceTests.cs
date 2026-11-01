using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LocalVoice.App.Services;
using LocalVoice.Core;
using Xunit;

namespace LocalVoice.App.Tests;

public sealed class UpdateServiceTests
{
    [Fact]
    public async Task BackgroundCheck_OnlyReadsMetadata_AndOffersPrompt()
    {
        using var temp = new TempPath();
        var handler = new FakeGitHubHandler();
        var settings = new SettingsStore(temp.SettingsPath); await settings.LoadAsync();
        await using var service = new UpdateService(temp.Paths, settings, new NullLog(), handler);
        var offers = 0; service.UpdateOffered += (_, offer) => { offers++; Assert.False(offer.Manual); };
        Assert.True(await service.CheckForUpdatesAsync(false));
        Assert.Equal(1, offers);
        Assert.Equal(1, handler.MetadataRequests);
        Assert.Equal(0, handler.AssetRequests);
        Assert.True(service.HasAvailableUpdate);
        Assert.False(service.IsReady);
    }

    [Fact]
    public async Task Postpone_PersistsAndSuppressesUntilManualCheck()
    {
        using var temp = new TempPath();
        var handler = new FakeGitHubHandler();
        var settings = new SettingsStore(temp.SettingsPath); await settings.LoadAsync();
        await using var service = new UpdateService(temp.Paths, settings, new NullLog(), handler);
        var offers = 0; service.UpdateOffered += (_, _) => offers++;
        await service.CheckForUpdatesAsync(false);
        await service.PostponeUpdateCommand.ExecuteAsync(null);
        Assert.NotNull(settings.Current.UpdatePostponedUntilUtc);
        await service.CheckForUpdatesAsync(false);
        Assert.Equal(1, offers);
        await service.CheckForUpdatesAsync(true);
        Assert.Equal(2, offers);
    }

    [Fact]
    public async Task Skip_PersistsAndOnlySuppressesThatVersion()
    {
        using var temp = new TempPath();
        var handler = new FakeGitHubHandler();
        var settings = new SettingsStore(temp.SettingsPath); await settings.LoadAsync();
        await using var service = new UpdateService(temp.Paths, settings, new NullLog(), handler);
        var offers = 0; service.UpdateOffered += (_, _) => offers++;
        await service.CheckForUpdatesAsync(false);
        await service.SkipUpdateCommand.ExecuteAsync(null);
        Assert.Equal(FakeGitHubHandler.VersionText, settings.Current.SkippedUpdateVersion);
        await service.CheckForUpdatesAsync(false);
        Assert.Equal(1, offers);
        Assert.False(UpdatePromptPolicy.ShouldPrompt(new Version(1, 4, 0), new Version(1, 3, 0), settings.Current.SkippedUpdateVersion, null, DateTimeOffset.UtcNow));
        Assert.True(UpdatePromptPolicy.ShouldPrompt(new Version(1, 4, 1), new Version(1, 3, 0), settings.Current.SkippedUpdateVersion, null, DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task DownloadCommand_DownloadsAndVerifiesInstaller()
    {
        using var temp = new TempPath();
        var handler = new FakeGitHubHandler();
        var settings = new SettingsStore(temp.SettingsPath); await settings.LoadAsync();
        await using var service = new UpdateService(temp.Paths, settings, new NullLog(), handler);
        await service.CheckForUpdatesAsync(false);
        Assert.Equal(0, handler.AssetRequests);
        await service.DownloadUpdateCommand.ExecuteAsync(null);
        Assert.True(service.IsReady);
        Assert.Equal(2, handler.AssetRequests);
        Assert.NotNull(service.PendingInstallerPath);
        Assert.True(File.Exists(service.PendingInstallerPath));
        Assert.Equal(FakeGitHubHandler.InstallerBytes.Length, new FileInfo(service.PendingInstallerPath!).Length);
    }

    [Fact]
    public async Task InstallWaitsForExplicitCommandAndIdlePredicate()
    {
        using var temp = new TempPath();
        var handler = new FakeGitHubHandler();
        var settings = new SettingsStore(temp.SettingsPath); await settings.LoadAsync();
        await using var service = new UpdateService(temp.Paths, settings, new NullLog(), handler);
        var installRequests = 0; service.InstallRequested += (_, _) => installRequests++;
        await service.CheckForUpdatesAsync(false);
        await service.DownloadUpdateCommand.ExecuteAsync(null);
        Assert.Equal(0, installRequests);
        service.SetInstallPredicate(() => false);
        Assert.False(service.RequestInstall());
        Assert.Equal(0, installRequests);
        service.SetInstallPredicate(() => true);
        Assert.True(service.RequestInstall());
        Assert.Equal(1, installRequests);
    }

    private sealed class TempPath : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "localvoice-update-tests-" + Guid.NewGuid().ToString("N"));
        public TempPath() { Directory.CreateDirectory(_root); Paths = new AppPaths(_root); }
        public AppPaths Paths { get; }
        public string SettingsPath => Paths.SettingsFile;
        public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }
    }
    private sealed class NullLog : IAppLog
    {
        public string DirectoryPath => Path.GetTempPath();
        public void Info(string message) { }
        public void Error(string context, Exception exception) { }
    }
    private sealed class FakeGitHubHandler : HttpMessageHandler
    {
        public const string VersionText = "1.4.0";
        public static readonly byte[] InstallerBytes = Encoding.UTF8.GetBytes("fake verified installer payload");
        public int MetadataRequests, AssetRequests;
        private static string InstallerName => UpdatePolicy.InstallerName(new Version(1, 4, 0));
        private static string ChecksumName => UpdatePolicy.ChecksumName(new Version(1, 4, 0));
        private static string Hash => Convert.ToHexString(SHA256.HashData(InstallerBytes)).ToLowerInvariant();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            if (url == UpdatePolicy.LatestReleaseApi)
            {
                MetadataRequests++;
                var json = JsonSerializer.Serialize(new
                {
                    tag_name = "v" + VersionText,
                    draft = false,
                    prerelease = false,
                    body = "Test release notes",
                    assets = new object[]
                    {
                        new { id = 1, name = InstallerName, size = InstallerBytes.Length, url = "https://api.github.com/repos/BalaRmack/html-editor/releases/assets/1" },
                        new { id = 2, name = ChecksumName, size = 100, url = "https://api.github.com/repos/BalaRmack/html-editor/releases/assets/2" }
                    }
                });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
            }
            AssetRequests++;
            if (url.EndsWith("/assets/2", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"{Hash}  {InstallerName}\n", Encoding.UTF8, "text/plain") });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(InstallerBytes) });
        }
    }
}
