namespace LocalVoice.Core;

public sealed class UsageStatistics
{
    public long RecognitionCount { get; set; }
    public double TotalSeconds { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset? LastRecognitionAt { get; set; }
}
public sealed class StatisticsStore
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private UsageStatistics _value = new();
    public event EventHandler? Changed;
    public StatisticsStore(string path) => _path = path;
    public async Task LoadAsync()
    {
        _value = await AtomicJsonStore.LoadAsync(_path, () => new UsageStatistics()).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public async Task<UsageStatistics> SnapshotAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { return new() { RecognitionCount = _value.RecognitionCount, TotalSeconds = _value.TotalSeconds, LastError = _value.LastError, LastRecognitionAt = _value.LastRecognitionAt }; }
        finally { _gate.Release(); }
    }
    public async Task RecordAsync(double duration)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _value.RecognitionCount++;
            _value.TotalSeconds += Math.Max(0, duration);
            _value.LastRecognitionAt = DateTimeOffset.UtcNow;
            await AtomicJsonStore.SaveAsync(_path, _value).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public async Task ErrorAsync(string message)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _value.LastError = message.Length > 400 ? message[..400] : message;
            await AtomicJsonStore.SaveAsync(_path, _value).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
