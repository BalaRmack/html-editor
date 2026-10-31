namespace LocalVoice.App.Services;

public sealed record RecognitionJob(int Utterance, float[] Audio, bool Final);
public sealed class RecognitionQueue : IDisposable
{
    private readonly Queue<RecognitionJob> _finals = new();
    private RecognitionJob? _partial;
    private bool _completed;
    private readonly SemaphoreSlim _available = new(0);
    private readonly object _gate = new();
    public bool TryAdd(RecognitionJob job)
    {
        lock (_gate)
        {
            if (_completed) return false;
            if (job.Final)
            {
                if (_finals.Count >= 16) return false;
                if (_partial?.Utterance == job.Utterance) _partial = null;
                _finals.Enqueue(job);
            }
            else
            {
                if (_partial is not null || _finals.Any(x => x.Utterance == job.Utterance)) return false;
                _partial = job;
            }
        }
        _available.Release(); return true;
    }
    public void Complete()
    {
        lock (_gate) { _completed = true; _partial = null; }
        _available.Release();
    }
    public async Task<RecognitionJob?> TakeAsync(CancellationToken ct)
    {
        while (true)
        {
            await _available.WaitAsync(ct).ConfigureAwait(false);
            lock (_gate)
            {
                if (_finals.TryDequeue(out var final)) return final;
                if (_partial is not null) { var partial = _partial; _partial = null; return partial; }
                if (_completed) return null;
            }
        }
    }
    public void Dispose() => _available.Dispose();
}
