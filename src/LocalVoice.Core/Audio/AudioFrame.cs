namespace LocalVoice.Core;

public readonly record struct AudioFrame(float[] Samples, float Rms)
{
    public const int SampleRate = 16000;
    public double Seconds => Samples.Length / (double)SampleRate;
    public static float ComputeRms(ReadOnlySpan<float> samples)
    {
        double sum = 0;
        foreach (var sample in samples) sum += sample * sample;
        return samples.Length == 0 ? 0 : (float)Math.Sqrt(sum / samples.Length);
    }
}

/// <summary>Energy VAD with a noise floor, pre-roll and hang-over. Audio stays in RAM.</summary>
public sealed class UtteranceAccumulator
{
    private readonly Queue<AudioFrame> _preRoll = new();
    private readonly List<float> _audio = [];
    private double _preRollSeconds, _silenceSeconds, _voicedSeconds, _sincePartial;
    private double _noiseFloor = 0.003;
    private bool _active;
    public bool IsSpeaking { get; private set; }
    public double BufferedSeconds => _audio.Count / (double)AudioFrame.SampleRate;
    public double Threshold { get; }
    public UtteranceAccumulator(double threshold = 0.012) => Threshold = Math.Clamp(threshold, 0.003, 0.08);
    public UtteranceUpdate Accept(AudioFrame frame)
    {
        var duration = frame.Seconds;
        var gate = Math.Max(Threshold, _noiseFloor * 3);
        var voiced = frame.Rms >= gate;
        if (!_active)
        {
            if (frame.Rms < gate) _noiseFloor = Math.Min(0.03, _noiseFloor * 0.97 + frame.Rms * 0.03);
            _preRoll.Enqueue(frame); _preRollSeconds += duration;
            while (_preRollSeconds > 0.30 && _preRoll.Count > 1) _preRollSeconds -= _preRoll.Dequeue().Seconds;
            if (!voiced) { IsSpeaking = false; return default; }
            _active = true; IsSpeaking = true;
            while (_preRoll.TryDequeue(out var previous))
            {
                Append(previous);
                if (previous.Rms > Threshold * 0.6) _voicedSeconds += previous.Seconds;
            }
            _preRollSeconds = 0; _sincePartial = _preRoll.Sum(x => x.Seconds);
            return new(true, false, false);
        }
        Append(frame);
        _silenceSeconds = voiced ? 0 : _silenceSeconds + duration;
        if (frame.Rms > Threshold * 0.6) _voicedSeconds += duration;
        _sincePartial += duration;
        var final = (_silenceSeconds >= 0.65 && _voicedSeconds >= 0.18) || BufferedSeconds >= 12;
        var partial = !final && _sincePartial >= 0.8 && BufferedSeconds >= 0.7;
        if (partial) _sincePartial = 0;
        return new(false, partial, final);
    }
    private void Append(AudioFrame frame) => _audio.AddRange(frame.Samples);
    public float[] Snapshot() => _audio.ToArray();
    public float[] TakeFinal(bool force = false)
    {
        var result = _voicedSeconds >= (force ? 0.10 : 0.18) ? Snapshot() : [];
        var overlap = force ? Array.Empty<float>() : _audio.Skip(Math.Max(0, _audio.Count - 3200)).ToArray();
        _audio.Clear(); _silenceSeconds = _voicedSeconds = _sincePartial = 0; _active = false; IsSpeaking = false;
        _preRoll.Clear(); _preRollSeconds = 0;
        if (overlap.Length > 0) { _preRoll.Enqueue(new AudioFrame(overlap, AudioFrame.ComputeRms(overlap))); _preRollSeconds = 0.2; }
        return result;
    }
}
public readonly record struct UtteranceUpdate(bool Started, bool PartialDue, bool FinalDue);
