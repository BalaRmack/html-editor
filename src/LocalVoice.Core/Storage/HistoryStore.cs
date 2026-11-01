namespace LocalVoice.Core;

public sealed record TranscriptEntry(Guid Id, DateTimeOffset CreatedAt, string Text, string Language, string ModelId, double DurationSeconds);
public sealed class HistoryStore
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private List<TranscriptEntry> _items = [];
    public event EventHandler? Changed;
    public HistoryStore(string path) => _path = path;
    public async Task LoadAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var data = await AtomicJsonStore.LoadAsync(_path, () => new HistoryDocument(), ct).ConfigureAwait(false);
            _items = (data.Items ?? []).Where(x => x is not null && !string.IsNullOrWhiteSpace(x.Text))
                .OrderByDescending(x => x.CreatedAt).ToList();
        }
        finally { _gate.Release(); }
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public async Task<IReadOnlyList<TranscriptEntry>> SnapshotAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { return _items.ToArray(); } finally { _gate.Release(); }
    }
    public async Task AddAsync(TranscriptEntry entry, bool enabled, int limit)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (enabled) _items.Insert(0, entry);
            _items = _items.Take(enabled ? Math.Clamp(limit, 20, 1000) : 0).ToList();
            await AtomicJsonStore.SaveAsync(_path, new HistoryDocument { Items = _items }).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public async Task DeleteAsync(IEnumerable<Guid> ids)
    {
        var set = ids.ToHashSet();
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _items.RemoveAll(x => set.Contains(x.Id));
            await AtomicJsonStore.SaveAsync(_path, new HistoryDocument { Items = _items }).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public async Task ClearAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { _items.Clear(); await AtomicJsonStore.SaveAsync(_path, new HistoryDocument { Items = _items }).ConfigureAwait(false); }
        finally { _gate.Release(); }
        Changed?.Invoke(this, EventArgs.Empty);
    }
    private sealed class HistoryDocument { public int SchemaVersion { get; set; } = 1; public List<TranscriptEntry>? Items { get; set; } }
}
