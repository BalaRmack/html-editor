namespace LocalVoice.Core;

/// <summary>Ordered finals plus replaceable partials; stale partials never overwrite a final.</summary>
public sealed class TranscriptBuffer
{
    private readonly Dictionary<int, string> _finals = [];
    private readonly Dictionary<int, string> _partials = [];
    public void Accept(int utterance, string text, bool final)
    {
        text = TranscriptText.Clean(text);
        if (_finals.ContainsKey(utterance)) return;
        if (final) { _partials.Remove(utterance); if (text.Length > 0) _finals[utterance] = text; else _finals[utterance] = ""; }
        else if (text.Length > 0) _partials[utterance] = text;
    }
    public string DisplayText
    {
        get
        {
            var text = ""; var next = 0;
            while (_finals.TryGetValue(next, out var final)) { text = TranscriptText.Join(text, final); next++; }
            if (_partials.TryGetValue(next, out var partial)) text = TranscriptText.Join(text, partial);
            return text;
        }
    }
    public string FinalText
    {
        get
        {
            var text = "";
            foreach (var item in _finals.OrderBy(x => x.Key)) text = TranscriptText.Join(text, item.Value);
            return text;
        }
    }
}
