using LocalVoice.Core;
using Whisper.net;
using Whisper.net.LibraryLoader;
namespace LocalVoice.App.Services;

public enum CpuRuntimePreference { Automatic, NoAvx }

public sealed class WhisperEngine : IAsyncDisposable
{
    private readonly ModelManager _models;
    private readonly IAppLog _log;
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private WhisperFactory? _factory;
    private string? _loadedId;
    private bool _disposed;
    private readonly CpuRuntimePreference _cpuRuntime;
    public string? LoadedModelId => _loadedId;
    public string? LoadedRuntime => RuntimeOptions.LoadedLibrary?.ToString();
    public WhisperEngine(ModelManager models, IAppLog log, CpuRuntimePreference cpuRuntime = CpuRuntimePreference.Automatic) { _models = models; _log = log; _cpuRuntime = cpuRuntime; }
    public async Task EnsureLoadedAsync(ModelDescriptor model, CancellationToken ct)
    {
        await _loadGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_factory is not null && _loadedId == model.Id) return;
            await _models.VerifyAsync(model, ct).ConfigureAwait(false);
            var path = _models.GetPath(model);
            var factory = await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                // Automatic: fast AVX2 CPU runtime when supported, otherwise the bundled NoAvx runtime.
                // No GPU/cloud runtime is ever selected. The forced mode is used only by CI smoke tests.
                RuntimeOptions.RuntimeLibraryOrder = _cpuRuntime == CpuRuntimePreference.NoAvx
                    ? [RuntimeLibrary.CpuNoAvx]
                    : [RuntimeLibrary.Cpu, RuntimeLibrary.CpuNoAvx];
                try { return WhisperFactory.FromPath(path, new WhisperFactoryOptions { UseGpu = false }); }
                catch (Exception ex) when (ex is PlatformNotSupportedException or DllNotFoundException or FileNotFoundException)
                { throw new InvalidOperationException("Не удалось запустить локальный движок Whisper. Установите последнюю версию LocalVoice: в ней есть совместимый CPU-runtime без AVX. Подробности в логах.", ex); }
            }, ct).ConfigureAwait(false);
            _factory?.Dispose(); _factory = factory; _loadedId = model.Id;
            _log.Info($"Local CPU model loaded: {model.Id}; runtime={LoadedRuntime ?? "unknown"}");
        }
        finally { _loadGate.Release(); }
    }
    public WhisperProcessor CreateProcessor(string language, int threads)
    {
        if (_factory is null) throw new InvalidOperationException("Модель ещё не загружена.");
        return _factory.CreateBuilder()
            .WithLanguage(language == "auto" ? "auto" : language)
            .WithThreads(Math.Clamp(threads, 1, 16))
            .WithNoContext().WithNoSpeechThreshold(0.6f)
            .WithTemperature(0).WithTemperatureInc(0)
            .WithProbabilities().WithMaxSegmentLength(240)
            .WithGreedySamplingStrategy().ParentBuilder.Build();
    }
    public async Task<string> TranscribeAsync(WhisperProcessor processor, float[] pcm, CancellationToken ct)
    {
        var text = "";
        await foreach (var segment in processor.ProcessAsync(pcm.AsMemory(), ct).ConfigureAwait(false))
        {
            if (segment.NoSpeechProbability > 0.6f) continue;
            var clean = TranscriptText.Clean(segment.Text);
            if (clean.Length > 0) text = TranscriptText.Join(text, clean);
        }
        return text;
    }
    public async Task UnloadAsync()
    {
        await _loadGate.WaitAsync().ConfigureAwait(false);
        try { _factory?.Dispose(); _factory = null; _loadedId = null; }
        finally { _loadGate.Release(); }
    }
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true; await UnloadAsync().ConfigureAwait(false); _loadGate.Dispose();
    }
}
