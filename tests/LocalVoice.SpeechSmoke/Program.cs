using System.Diagnostics;
using System.Text.Json;
using LocalVoice.App.Services;
using LocalVoice.Core;
using NAudio.Wave;

// Headless native-engine smoke test. It never opens the microphone and never writes audio.
var modelId = Args(args, "--model") ?? "small-q5_1";
var audioDir = Args(args, "--audio-dir") ?? throw new ArgumentException("--audio-dir is required");
var dataDir = Args(args, "--data-dir") ?? Path.Combine(Path.GetTempPath(), "localvoice-smoke-" + Guid.NewGuid().ToString("N"));
var model = ModelCatalog.ById[modelId];
var paths = new AppPaths(dataDir);
await using var log = new FileLog(paths.LogsDirectory);
using var models = new ModelManager(paths, log);
await using var engine = new WhisperEngine(models, log);
var results = new List<object>();
try
{
    Console.WriteLine($"Downloading/verifying {model.Id} ({model.SizeText})…");
    await models.DownloadAsync(model, new Progress<DownloadProgress>(p =>
    {
        if (p.Stage == "Проверка SHA-256") Console.WriteLine("Verifying SHA-256…");
    }));
    await engine.EnsureLoadedAsync(model, CancellationToken.None);
    foreach (var (file, language, expected) in new[]
    {
        ("ru_sample.wav", "ru", new[] { "привет", "проверка", "русского" }),
        ("en_sample.wav", "en", new[] { "test", "english", "dictation" }),
        ("ru_sample.wav", "auto", new[] { "привет", "проверка" })
    })
    {
        var pcm = ReadPcm16(Path.Combine(audioDir, file));
        await using var processor = engine.CreateProcessor(language, Math.Clamp(Environment.ProcessorCount / 2, 2, 8));
        var watch = Stopwatch.StartNew();
        var text = await engine.TranscribeAsync(processor, pcm, CancellationToken.None);
        watch.Stop();
        var normalized = text.ToLowerInvariant();
        var missing = expected.Where(word => !normalized.Contains(word)).ToArray();
        Console.WriteLine($"{file} [{language}] {watch.ElapsedMilliseconds} ms: {text}");
        results.Add(new { file, language, text, milliseconds = watch.ElapsedMilliseconds, passed = missing.Length == 0 });
        if (missing.Length > 0) throw new InvalidOperationException($"Missing expected words: {string.Join(", ", missing)}");
    }
    Console.WriteLine("SPEECH SMOKE PASSED");
}
finally
{
    await engine.DisposeAsync();
    var report = Path.Combine(paths.Root, "speech-smoke.json");
    await File.WriteAllTextAsync(report, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"Report: {report}");
}
static string? Args(string[] args, string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}
static float[] ReadPcm16(string path)
{
    using var reader = new WaveFileReader(path);
    if (reader.WaveFormat.SampleRate != 16000 || reader.WaveFormat.Channels != 1 || reader.WaveFormat.BitsPerSample != 16)
        throw new InvalidOperationException("Smoke samples must be 16 kHz mono PCM16.");
    var bytes = new byte[reader.Length];
    var read = reader.Read(bytes, 0, bytes.Length);
    var samples = new float[read / 2];
    for (var i = 0; i < samples.Length; i++) samples[i] = BitConverter.ToInt16(bytes, i * 2) / 32768f;
    return samples;
}
