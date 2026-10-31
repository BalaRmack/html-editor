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
