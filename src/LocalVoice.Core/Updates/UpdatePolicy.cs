using System.Globalization;
namespace LocalVoice.Core;

public sealed record UpdateAsset(long Id, string Name, long Size, string DownloadUrl);
public sealed record AvailableUpdate(Version Version, string Tag, UpdateAsset Installer, UpdateAsset Checksum, string? Notes)
{
    public string VersionText => Version.ToString(3);
}

/// <summary>Pure update policy helpers. Network and installer execution live in the WPF app.</summary>
public static class UpdatePolicy
{
    public const string Repository = "BalaRmack/html-editor";
    public const string LatestReleaseApi = "https://api.github.com/repos/" + Repository + "/releases/latest";

    public static bool TryParseStableVersion(string? tag, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(tag)) return false;
        var text = tag.Trim();
        if (text.StartsWith('v') || text.StartsWith('V')) text = text[1..];
        if (text.Contains('-') || text.Contains('+')) return false; // stable channel only
        if (!Version.TryParse(text, out var parsed) || parsed is null) return false;
        version = new Version(parsed.Major, Math.Max(0, parsed.Minor), Math.Max(0, parsed.Build));
        return true;
    }

    public static bool IsNewer(Version candidate, Version current) => candidate > current;

    public static string InstallerName(Version version) => $"LocalVoice-{version.ToString(3)}-win-x64-setup.exe";
    public static string ChecksumName(Version version) => InstallerName(version) + ".sha256";

    /// <summary>Accepts only a single "&lt;64-hex&gt;  &lt;filename&gt;" line for the expected asset.</summary>
    public static bool TryReadChecksum(string? content, string expectedFileName, out string sha256)
    {
        sha256 = "";
        if (string.IsNullOrWhiteSpace(content)) return false;
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length != 1) return false;
        var parts = lines[0].Split(new[] { ' ', '\t' }, 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2) return false;
        var hash = parts[0].Trim().ToLowerInvariant();
        var name = parts[1].TrimStart('*').Trim();
        if (hash.Length != 64 || !hash.All(Uri.IsHexDigit)) return false;
        if (!string.Equals(name, expectedFileName, StringComparison.OrdinalIgnoreCase)) return false;
        sha256 = hash;
        return true;
    }

    public static bool IsTrustedInitialUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
        (uri.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.Equals("objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase));

    public static string FormatBytes(long bytes) => bytes >= 1L << 20
        ? string.Create(CultureInfo.InvariantCulture, $"{bytes / (double)(1L << 20):0.0} МБ")
        : string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024.0:0} КБ");
}
