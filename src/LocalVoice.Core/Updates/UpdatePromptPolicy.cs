namespace LocalVoice.Core;

/// <summary>Pure rules for the interactive update prompt. Download/install are always user decisions.</summary>
public static class UpdatePromptPolicy
{
    public static readonly TimeSpan PostponeDuration = TimeSpan.FromHours(24);

    public static bool ShouldPrompt(Version candidate, Version current, string? skippedVersion,
        DateTimeOffset? postponedUntilUtc, DateTimeOffset nowUtc, bool manual = false)
    {
        if (!UpdatePolicy.IsNewer(candidate, current)) return false;
        // A manual "Check updates" action intentionally bypasses postpone/skip for this version.
        if (manual) return true;
        if (NormalizeSkippedVersion(skippedVersion) is { Length: > 0 } skipped &&
            UpdatePolicy.TryParseStableVersion(skipped, out var skippedParsed) && skippedParsed == candidate)
            return false;
        if (postponedUntilUtc is { } until && until.ToUniversalTime() > nowUtc) return false;
        return true;
    }

    public static DateTimeOffset PostponeUntil(DateTimeOffset nowUtc) => nowUtc.Add(PostponeDuration);

    public static string NormalizeSkippedVersion(string? value) =>
        UpdatePolicy.TryParseStableVersion(value, out var version) ? version.ToString(3) : "";
}
