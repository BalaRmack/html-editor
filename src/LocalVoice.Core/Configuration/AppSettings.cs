namespace LocalVoice.Core;

public enum ListeningMode { Toggle, PushToTalk }
public enum AppTheme { System, Dark, Light }
public enum OverlayPosition { BottomCenter, BottomRight, BottomLeft, TopCenter, TopRight, TopLeft }

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;
    public string Hotkey { get; set; } = "Ctrl+Alt+Space";
    public ListeningMode Mode { get; set; } = ListeningMode.Toggle;
    public string Language { get; set; } = "auto";
    public string ModelId { get; set; } = "small-q5_1";
    public string InputDeviceId { get; set; } = "";
    public AppTheme Theme { get; set; } = AppTheme.System;
    public OverlayPosition OverlayPosition { get; set; } = OverlayPosition.BottomCenter;
    public double OverlayScale { get; set; } = 1.0;
    public double OverlayOpacity { get; set; } = 0.96;
    public bool AutoPaste { get; set; }
    public bool StartWithWindows { get; set; }
    public bool SaveHistory { get; set; } = true;
    public int HistoryLimit { get; set; } = 200;
    public double VadThreshold { get; set; } = 0.012;
    public int CpuThreads { get; set; } = Math.Clamp(Environment.ProcessorCount / 2, 2, 8);

    public AppSettings Clone() => (AppSettings)MemberwiseClone();
    public static AppSettings Normalize(AppSettings? source)
    {
        var s = source ?? new AppSettings();
        s.SchemaVersion = 1;
        if (!HotkeySpec.TryParse(s.Hotkey, out var hotkey)) s.Hotkey = "Ctrl+Alt+Space";
        else s.Hotkey = hotkey.ToString();
        if (!ModelCatalog.ById.ContainsKey(s.ModelId)) s.ModelId = "small-q5_1";
        s.Language = s.Language is null ? "auto" : s.Language.Trim().ToLowerInvariant();
        if (s.Language != "auto" && (s.Language.Length != 2 || !s.Language.All(char.IsAsciiLetter))) s.Language = "auto";
        s.InputDeviceId ??= "";
        if (!Enum.IsDefined(s.Mode)) s.Mode = ListeningMode.Toggle;
        if (!Enum.IsDefined(s.Theme)) s.Theme = AppTheme.System;
        if (!Enum.IsDefined(s.OverlayPosition)) s.OverlayPosition = OverlayPosition.BottomCenter;
        s.OverlayScale = Clamp(s.OverlayScale, 0.8, 1.3, 1.0);
        s.OverlayOpacity = Clamp(s.OverlayOpacity, 0.65, 1.0, 0.96);
        s.VadThreshold = Clamp(s.VadThreshold, 0.003, 0.08, 0.012);
        s.HistoryLimit = Math.Clamp(s.HistoryLimit, 20, 1000);
        s.CpuThreads = Math.Clamp(s.CpuThreads, 1, Math.Min(16, Environment.ProcessorCount));
        return s;
    }
    private static double Clamp(double value, double min, double max, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}

public readonly record struct HotkeySpec(uint Modifiers, uint VirtualKey)
{
    public const uint Alt = 1, Control = 2, Shift = 4, Win = 8;
    public static bool TryParse(string? text, out HotkeySpec result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        uint modifiers = 0, key = 0;
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in parts)
        {
            if (!seen.Add(raw)) return false;
            switch (raw.ToUpperInvariant())
            {
                case "CTRL" or "CONTROL": modifiers |= Control; continue;
                case "ALT": modifiers |= Alt; continue;
                case "SHIFT": modifiers |= Shift; continue;
                case "WIN" or "WINDOWS": modifiers |= Win; continue;
            }
            if (key != 0) return false;
            key = raw.ToUpperInvariant() switch
            {
                "SPACE" => 0x20, "TAB" => 0x09, "ENTER" => 0x0D,
                "INSERT" => 0x2D, "DELETE" => 0x2E, "HOME" => 0x24, "END" => 0x23,
                "PAGEUP" => 0x21, "PAGEDOWN" => 0x22,
                "LEFT" => 0x25, "UP" => 0x26, "RIGHT" => 0x27, "DOWN" => 0x28,
                "PAUSE" => 0x13, "CAPSLOCK" => 0x14,
                _ => 0
            };
            if (key == 0 && raw.Length == 1 && char.IsAsciiLetterOrDigit(raw[0])) key = char.ToUpperInvariant(raw[0]);
            if (key == 0 && raw.Length is 2 or 3 && raw[0] is 'F' or 'f' &&
                int.TryParse(raw[1..], out var fn) && fn is >= 1 and <= 24) key = (uint)(0x6F + fn);
            if (key is >= 0x70 and <= 0x87 && raw.Length is 2 or 3) continue;
            if (key == 0) return false;
        }
        if (key == 0 || modifiers == 0 || modifiers == Shift) return false;
        result = new HotkeySpec(modifiers, key);
        return true;
    }
    public override string ToString()
    {
        var parts = new List<string>();
        if ((Modifiers & Control) != 0) parts.Add("Ctrl");
        if ((Modifiers & Alt) != 0) parts.Add("Alt");
        if ((Modifiers & Shift) != 0) parts.Add("Shift");
        if ((Modifiers & Win) != 0) parts.Add("Win");
        parts.Add(VirtualKey switch
        {
            0x20 => "Space", 0x09 => "Tab", 0x0D => "Enter", 0x2D => "Insert", 0x2E => "Delete",
            0x24 => "Home", 0x23 => "End", 0x21 => "PageUp", 0x22 => "PageDown",
            0x25 => "Left", 0x26 => "Up", 0x27 => "Right", 0x28 => "Down",
            0x13 => "Pause", 0x14 => "CapsLock",
            >= 0x70 and <= 0x87 => $"F{VirtualKey - 0x6F}",
            _ => ((char)VirtualKey).ToString()
        });
        return string.Join('+', parts);
    }
}
