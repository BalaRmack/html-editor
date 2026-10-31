using System.Net;
using System.Security.Cryptography;
using LocalVoice.Core;
using Xunit;
namespace LocalVoice.Core.Tests;

public sealed class ModelTests
{
    [Fact] public void Catalog_is_pinned_multilingual_and_has_distinct_hashes()
    {
        Assert.Equal(4, ModelCatalog.All.Count);
        Assert.Equal(ModelCatalog.All.Count, ModelCatalog.All.Select(x => x.Sha256).Distinct().Count());
        foreach (var model in ModelCatalog.All)
        {
            Assert.Contains(ModelCatalog.Revision, model.Url); Assert.DoesNotContain("/resolve/main/", model.Url);
            Assert.Equal(64, model.Sha256.Length); Assert.True(model.ExpectedBytes > 1_000_000); Assert.EndsWith(".bin", model.FileName);
            Assert.Equal(model, ModelCatalog.ById[model.Id]);
        }
    }
    [Theory]
    [InlineData("http://huggingface.co/model.bin")]
    [InlineData("https://evil.example/model.bin")]
    [InlineData("https://huggingface.co.evil.example/model.bin")]
    [InlineData("https://user:password@huggingface.co/model.bin")]
    public void Unsafe_download_urls_are_rejected(string url)
    {
        // The fake test handler never contacts a network. Production uses the pinned HTTPS catalog.
        Assert.Throws<InvalidDataException>(() => ModelManager.ValidateDownloadUri(url));
    }
    [Fact] public async Task Download_verifies_hash_and_cleans_unverified_file()
    {
        using var temp = new TempDirectory(); var paths = new AppPaths(temp.Path("data")); await using var log = new FileLog(paths.LogsDirectory);
        var payload = "verified test payload"u8.ToArray(); var hash = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        var model = new ModelDescriptor("test", "Test", "Test", "test.bin", payload.Length, hash, "https://huggingface.co/localvoice-tests/test.bin");
        using var http = new HttpClient(new FakeHandler(payload)); using var manager = new ModelManager(paths, log, http);
        await manager.DownloadAsync(model); Assert.True(manager.LooksInstalled(model)); await manager.VerifyAsync(model);
        Assert.Equal(payload, await File.ReadAllBytesAsync(manager.GetPath(model))); Assert.False(File.Exists(manager.GetPath(model) + ".partial"));
        await manager.DeleteAsync(model, () => Task.CompletedTask); Assert.False(manager.LooksInstalled(model));
    }
    [Fact] public async Task Mismatched_checksum_never_becomes_an_installed_model()
    {
        using var temp = new TempDirectory(); var paths = new AppPaths(temp.Path("data")); await using var log = new FileLog(paths.LogsDirectory);
        var model = new ModelDescriptor("test", "Test", "Test", "test.bin", 4, new string('0', 64), "https://huggingface.co/localvoice-tests/test.bin");
        using var http = new HttpClient(new FakeHandler([1, 2, 3, 4])); using var manager = new ModelManager(paths, log, http);
        await Assert.ThrowsAsync<InvalidDataException>(() => manager.DownloadAsync(model));
        Assert.False(manager.LooksInstalled(model)); Assert.False(File.Exists(manager.GetPath(model) + ".partial"));
    }
    private sealed class FakeHandler(byte[] data) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = new ByteArrayContent(data); content.Headers.ContentLength = data.Length;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }
}
