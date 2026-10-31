using System.Text.RegularExpressions;
namespace LocalVoice.Core;

public static partial class TranscriptText
{
    public static string Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        text = text.ReplaceLineEndings(" ").Trim();
        if (Hallucination().IsMatch(text)) return "";
        return Spaces().Replace(text, " ").Trim();
    }
    public static string Join(string previous, string next)
    {
        previous = Clean(previous); next = Clean(next);
        if (previous.Length == 0) return next;
        if (next.Length == 0) return previous;
        var a = previous.Split(' '); var b = next.Split(' ');
        var max = Math.Min(12, Math.Min(a.Length, b.Length));
        for (var count = max; count > 0; count--)
        {
            if (a.TakeLast(count).Select(Normalize).SequenceEqual(b.Take(count).Select(Normalize)))
                return previous + " " + string.Join(' ', b.Skip(count));
        }
        return previous + " " + next;
    }
    private static string Normalize(string word) => WordNoise().Replace(word.ToLowerInvariant(), "");
    [GeneratedRegex(@"^\s*(\[?(blank[_ ]audio|silence|music|музыка|аплодисменты|субтитры.*|спасибо за просмотр|thanks for watching)\]?|[.!?…]+)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Hallucination();
    [GeneratedRegex(@"\s+")] private static partial Regex Spaces();
    [GeneratedRegex(@"[^\p{L}\p{N}]")] private static partial Regex WordNoise();
}
