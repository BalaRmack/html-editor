using LocalVoice.Core;
using Xunit;
namespace LocalVoice.Core.Tests;

public sealed class ConfigurationTests
{
    [Theory]
    [InlineData("Ctrl+Alt+Space", "Ctrl+Alt+Space")]
    [InlineData("alt + control + d", "Ctrl+Alt+D")]
    [InlineData("Ctrl+Shift+F12", "Ctrl+Shift+F12")]
    [InlineData("Win+Alt+R", "Alt+Win+R")]
    public void Hotkey_round_trips(string input, string expected)
    {
        Assert.True(HotkeySpec.TryParse(input, out var key));
        Assert.Equal(expected, key.ToString());
        Assert.True(HotkeySpec.TryParse(key.ToString(), out var again));
        Assert.Equal(key, again);
    }
    [Theory]
    [InlineData("")]
    [InlineData("Space")]
    [InlineData("Shift+A")]
    [InlineData("Ctrl+Ctrl+A")]
    [InlineData("Ctrl+A+B")]
    [InlineData("Ctrl+Escape")]
    [InlineData("Ctrl+F25")]
    [InlineData("Ctrl+ğ")]
    public void Unsafe_or_unknown_hotkeys_are_rejected(string input) => Assert.False(HotkeySpec.TryParse(input, out _));
    [Fact] public void Defaults_are_local_and_opt_out()
    {
        var s = new AppSettings();
        Assert.Equal("small-q5_1", s.ModelId); Assert.Equal("auto", s.Language);
        Assert.False(s.AutoPaste); Assert.False(s.StartWithWindows); Assert.Equal(ListeningMode.Toggle, s.Mode);
    }
    [Fact] public void Normalization_clamps_without_changing_unrelated_values()
    {
        var s = AppSettings.Normalize(new() { OverlayOpacity = 20, OverlayScale = -1, CpuThreads = 999, VadThreshold = double.NaN, ModelId = "missing", Language = "RU", Hotkey = "bad" });
        Assert.Equal(1, s.OverlayOpacity); Assert.Equal(.8, s.OverlayScale); Assert.Equal("ru", s.Language);
        Assert.Equal(.012, s.VadThreshold); Assert.Equal("small-q5_1", s.ModelId); Assert.Equal("Ctrl+Alt+Space", s.Hotkey);
        Assert.True(s.CpuThreads <= Math.Min(16, Environment.ProcessorCount));
    }
    [Fact] public async Task Settings_survive_restart_and_clone_is_independent()
    {
        using var temp = new TempDirectory(); var store = new SettingsStore(temp.Path("settings.json"));
        var settings = new AppSettings { Language = "ru", AutoPaste = true, Hotkey = "Ctrl+Alt+D" };
        await store.SaveAsync(settings); settings.Language = "en";
        var next = new SettingsStore(temp.Path("settings.json")); await next.LoadAsync();
        Assert.Equal("ru", next.Current.Language); Assert.Equal("Ctrl+Alt+D", next.Current.Hotkey); Assert.True(next.Current.AutoPaste);
    }
    [Fact] public async Task Corrupt_settings_fall_back_to_previous_snapshot()
    {
        using var temp = new TempDirectory(); var path = temp.Path("settings.json"); var store = new SettingsStore(path);
        await store.SaveAsync(new AppSettings { Language = "ru" });
        await store.SaveAsync(new AppSettings { Language = "en" });
        await File.WriteAllTextAsync(path, "{ broken json");
        var next = new SettingsStore(path); await next.LoadAsync(); Assert.Equal("ru", next.Current.Language);
    }
}
public sealed class TempDirectory : IDisposable
{
    private readonly string _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "localvoice-tests-" + Guid.NewGuid().ToString("N"));
    public TempDirectory() => Directory.CreateDirectory(_root);
    public string Path(string name) => System.IO.Path.Combine(_root, name);
    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }
}

public sealed class UiSettingsTests
{
    [Fact] public void New_defaults_use_black_theme_and_show_tutorial_once()
    {
        var s = new AppSettings();
        Assert.Equal(AppTheme.Dark, s.Theme); Assert.False(s.TutorialCompleted); Assert.Equal(3, s.SchemaVersion);
    }
    [Fact] public void Legacy_system_theme_moves_to_black_but_explicit_light_is_preserved()
    {
        var legacy = AppSettings.Normalize(new AppSettings { SchemaVersion = 1, Theme = AppTheme.System });
        Assert.Equal(AppTheme.Dark, legacy.Theme);
        var light = AppSettings.Normalize(new AppSettings { SchemaVersion = 1, Theme = AppTheme.Light });
        Assert.Equal(AppTheme.Light, light.Theme);
    }
    [Fact] public void Tutorial_completion_survives_clone_and_save()
    {
        var s = new AppSettings { TutorialCompleted = true };
        Assert.True(s.Clone().TutorialCompleted);
        Assert.True(AppSettings.Normalize(s).TutorialCompleted);
    }
}

public sealed class UpdatePolicyTests
{
    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("v2.0", "2.0.0")]
    public void Stable_versions_parse(string tag, string expected)
    {
        Assert.True(UpdatePolicy.TryParseStableVersion(tag, out var version));
        Assert.Equal(expected, version.ToString(3));
    }
    [Theory]
    [InlineData("v1.2.3-beta.1")]
    [InlineData("nightly")]
    [InlineData("")]
    public void Prerelease_or_invalid_tags_are_rejected(string tag) => Assert.False(UpdatePolicy.TryParseStableVersion(tag, out _));

    [Fact] public void Only_newer_versions_are_offered()
    {
        Assert.True(UpdatePolicy.IsNewer(new Version(1, 2, 1), new Version(1, 2, 0)));
        Assert.False(UpdatePolicy.IsNewer(new Version(1, 2, 0), new Version(1, 2, 0)));
        Assert.False(UpdatePolicy.IsNewer(new Version(1, 1, 9), new Version(1, 2, 0)));
    }
    [Fact] public void Checksum_must_match_expected_asset_and_be_64_hex()
    {
        var hash = new string('a', 64);
        Assert.True(UpdatePolicy.TryReadChecksum($"{hash}  LocalVoice-1.2.0-win-x64-setup.exe\n", "LocalVoice-1.2.0-win-x64-setup.exe", out var parsed));
        Assert.Equal(hash, parsed);
        Assert.True(UpdatePolicy.TryReadChecksum($"{hash} *LocalVoice-1.2.0-win-x64-setup.exe", "LocalVoice-1.2.0-win-x64-setup.exe", out _));
        Assert.False(UpdatePolicy.TryReadChecksum($"{hash}  other.exe", "LocalVoice-1.2.0-win-x64-setup.exe", out _));
        Assert.False(UpdatePolicy.TryReadChecksum("nothash  LocalVoice-1.2.0-win-x64-setup.exe", "LocalVoice-1.2.0-win-x64-setup.exe", out _));
        Assert.False(UpdatePolicy.TryReadChecksum($"{hash}  LocalVoice-1.2.0-win-x64-setup.exe\nextra", "LocalVoice-1.2.0-win-x64-setup.exe", out _));
    }
    [Fact] public void Update_urls_must_be_https_github()
    {
        Assert.True(UpdatePolicy.IsTrustedInitialUrl("https://api.github.com/repos/BalaRmack/html-editor/releases/assets/1"));
        Assert.True(UpdatePolicy.IsTrustedInitialUrl("https://github.com/BalaRmack/html-editor/releases/download/v1.2.0/file.exe"));
        Assert.False(UpdatePolicy.IsTrustedInitialUrl("http://api.github.com/x"));
        Assert.False(UpdatePolicy.IsTrustedInitialUrl("https://evil.example/x"));
    }
    [Fact] public void Auto_update_defaults_on_and_survives_clone()
    {
        var s = new AppSettings();
        Assert.True(s.AutoUpdate); Assert.True(s.AutoInstallUpdates); Assert.Equal(3, s.SchemaVersion);
        var clone = s.Clone(); clone.AutoUpdate = false;
        Assert.True(s.AutoUpdate); Assert.False(clone.AutoUpdate);
        Assert.True(AppSettings.Normalize(s).AutoUpdate);
    }
}
