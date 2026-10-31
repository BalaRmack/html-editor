namespace LocalVoice.Core;

public sealed class SettingsStore
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    public AppSettings Current { get; private set; } = new();
    public bool LoadWarning { get; private set; }
    public SettingsStore(string path) => _path = path;
    public async Task LoadAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            LoadWarning = File.Exists(_path);
            Current = AppSettings.Normalize(await AtomicJsonStore.LoadAsync(_path, () => new AppSettings(), ct).ConfigureAwait(false));
            LoadWarning &= !File.Exists(_path);
        }
        finally { _gate.Release(); }
    }
    public async Task SaveAsync(AppSettings settings, CancellationToken ct = default)
    {
        var normalized = AppSettings.Normalize(settings.Clone());
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await AtomicJsonStore.SaveAsync(_path, normalized, ct).ConfigureAwait(false);
            Current = normalized;
        }
        finally { _gate.Release(); }
    }
}
