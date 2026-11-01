using LocalVoice.Core;
using Xunit;
namespace LocalVoice.Core.Tests;

public sealed class StorageTests
{
    [Fact] public async Task History_search_data_round_trips_and_deletes()
    {
        using var temp = new TempDirectory(); var path = temp.Path("history.json"); var store = new HistoryStore(path); await store.LoadAsync();
        var a = new TranscriptEntry(Guid.NewGuid(), DateTimeOffset.UtcNow, "Русский текст", "ru", "small-q5_1", 2);
        var b = a with { Id = Guid.NewGuid(), Text = "English text", Language = "en" };
        await store.AddAsync(a, true, 20); await store.AddAsync(b, true, 20);
        var restarted = new HistoryStore(path); await restarted.LoadAsync();
        Assert.Equal(2, (await restarted.SnapshotAsync()).Count);
        await restarted.DeleteAsync([a.Id]); Assert.Equal("English text", (await restarted.SnapshotAsync()).Single().Text);
        await restarted.ClearAsync(); Assert.Empty(await restarted.SnapshotAsync());
        Assert.DoesNotContain("Русский текст", await File.ReadAllTextAsync(path));
    }
    [Fact] public async Task Disabling_history_removes_disk_content()
    {
        using var temp = new TempDirectory(); var path = temp.Path("history.json"); var store = new HistoryStore(path);
        await store.AddAsync(new(Guid.NewGuid(), DateTimeOffset.UtcNow, "Private words", "en", "small-q5_1", 1), true, 20);
        await store.AddAsync(new(Guid.NewGuid(), DateTimeOffset.UtcNow, "Never saved", "en", "small-q5_1", 1), false, 20);
        Assert.Empty(await store.SnapshotAsync()); Assert.DoesNotContain("Private words", await File.ReadAllTextAsync(path));
    }
    [Fact] public async Task History_limit_is_enforced()
    {
        using var temp = new TempDirectory(); var store = new HistoryStore(temp.Path("history.json"));
        for (var i = 0; i < 25; i++) await store.AddAsync(new(Guid.NewGuid(), DateTimeOffset.UtcNow.AddSeconds(i), $"Phrase {i}", "en", "small-q5_1", 1), true, 20);
        var items = await store.SnapshotAsync(); Assert.Equal(20, items.Count); Assert.Equal("Phrase 24", items[0].Text);
    }
    [Fact] public async Task Statistics_accumulate_and_error_does_not_reset_count()
    {
        using var temp = new TempDirectory(); var path = temp.Path("stats.json"); var store = new StatisticsStore(path);
        await store.RecordAsync(3.5); await store.ErrorAsync("Test error");
        var next = new StatisticsStore(path); await next.LoadAsync(); var s = await next.SnapshotAsync();
        Assert.Equal(1, s.RecognitionCount); Assert.Equal(3.5, s.TotalSeconds); Assert.Equal("Test error", s.LastError);
    }
    [Fact] public void Export_registry_has_no_remote_providers_by_default()
    {
        var registry = new ExportProviderRegistry(); Assert.Empty(registry.Providers); Assert.Empty(registry.ConnectedRemote);
        registry.Register(new FakeProvider()); Assert.Single(registry.Providers); Assert.Empty(registry.ConnectedRemote);
        Assert.Throws<InvalidOperationException>(() => registry.Register(new FakeProvider()));
    }
    private sealed class FakeProvider : IExportProvider
    {
        public string Id => "fake"; public string DisplayName => "Fake"; public bool SendsOverNetwork => true; public bool IsConfigured => false;
        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task ExportAsync(string text, CancellationToken ct = default) => throw new InvalidOperationException("Must not send");
        public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;
    }
}
