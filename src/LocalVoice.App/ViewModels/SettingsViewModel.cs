using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalVoice.Core;
using LocalVoice.App.Services;
namespace LocalVoice.App.ViewModels;

public sealed record NamedOption(string Id, string Name);
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    [ObservableProperty] private string _hotkey = "";
    [ObservableProperty] private bool _isCapturingHotkey;
    [ObservableProperty] private NamedOption _mode;
    [ObservableProperty] private NamedOption _language;
    [ObservableProperty] private NamedOption _theme;
    [ObservableProperty] private NamedOption _overlayPosition;
    [ObservableProperty] private MicrophoneDevice _microphone;
    [ObservableProperty] private double _overlayScale;
    [ObservableProperty] private double _overlayOpacity;
    [ObservableProperty] private double _vadThreshold;
    [ObservableProperty] private int _cpuThreads;
    [ObservableProperty] private bool _autoPaste;
    [ObservableProperty] private bool _startWithWindows;
    [ObservableProperty] private bool _saveHistory;
    [ObservableProperty] private bool _autoUpdate;
    public IReadOnlyList<NamedOption> Modes { get; } = [new("Toggle", "Нажал — слушает / нажал — стоп"), new("PushToTalk", "Удерживать клавиши — говорить")] ;
    public IReadOnlyList<NamedOption> Languages { get; } = [new("auto", "Авто · русский / английский"), new("ru", "Русский"), new("en", "English"), new("de", "Deutsch"), new("fr", "Français"), new("es", "Español"), new("uk", "Українська"), new("zh", "中文"), new("ja", "日本語")];
    public IReadOnlyList<NamedOption> Themes { get; } = [new("System", "Как в Windows"), new("Dark", "Чёрная"), new("Light", "Светлая")];
    public IReadOnlyList<NamedOption> Positions { get; } = [new("BottomCenter", "Внизу по центру"), new("BottomRight", "Внизу справа"), new("BottomLeft", "Внизу слева"), new("TopCenter", "Вверху по центру"), new("TopRight", "Вверху справа"), new("TopLeft", "Вверху слева")];
    public ObservableCollection<MicrophoneDevice> Microphones { get; } = [];
    public int MaxThreads { get; } = Math.Min(16, Environment.ProcessorCount);
    public SettingsViewModel(MainViewModel main) { _main = main; _mode = Modes[0]; _language = Languages[0]; _theme = Themes[0]; _overlayPosition = Positions[0]; _microphone = new("", "Системный по умолчанию"); Load(); }
    public void Load()
    {
        var s = _main.SettingsStore.Current;
        Hotkey = s.Hotkey; Mode = Modes.First(x => x.Id == s.Mode.ToString()); Language = Languages.FirstOrDefault(x => x.Id == s.Language) ?? Languages[0];
        Theme = Themes.First(x => x.Id == s.Theme.ToString()); OverlayPosition = Positions.First(x => x.Id == s.OverlayPosition.ToString());
        OverlayScale = s.OverlayScale; OverlayOpacity = s.OverlayOpacity; VadThreshold = s.VadThreshold; CpuThreads = s.CpuThreads;
        AutoPaste = s.AutoPaste; StartWithWindows = s.StartWithWindows; SaveHistory = s.SaveHistory;
        AutoUpdate = s.AutoUpdate;
        RefreshDevices();
    }
    public AppSettings ToSettings()
    {
        var s = _main.SettingsStore.Current.Clone();
        s.Hotkey = Hotkey; s.Mode = Enum.Parse<ListeningMode>(Mode.Id); s.Language = Language.Id; s.Theme = Enum.Parse<AppTheme>(Theme.Id);
        s.OverlayPosition = Enum.Parse<Core.OverlayPosition>(OverlayPosition.Id); s.OverlayScale = OverlayScale; s.OverlayOpacity = OverlayOpacity;
        s.VadThreshold = VadThreshold; s.CpuThreads = CpuThreads; s.AutoPaste = AutoPaste; s.StartWithWindows = StartWithWindows;
        s.SaveHistory = SaveHistory; s.AutoUpdate = AutoUpdate; s.InputDeviceId = Microphone.Id;
        return s;
    }
    [RelayCommand] public void RefreshDevices()
    {
        var selected = _main.SettingsStore.Current.InputDeviceId;
        Microphones.Clear(); Microphones.Add(new("", "Системный по умолчанию"));
        try { foreach (var device in _main.Microphones.GetDevices()) Microphones.Add(device); }
        catch (Exception ex) { _main.ReportError(ex, "Перечисление микрофонов"); }
        Microphone = Microphones.FirstOrDefault(x => x.Id == selected) ?? Microphones[0];
    }
    [RelayCommand] private void CaptureHotkey()
    {
        if (IsCapturingHotkey) { _main.CancelHotkeyCapture(); return; }
        _main.Hotkeys.Suspend(); IsCapturingHotkey = true; Hotkey = "Нажмите сочетание клавишей…";
    }
    [RelayCommand] private Task SaveAsync() => _main.SaveSettingsAsync();
    [RelayCommand] private void ResetOverlay() { OverlayPosition = Positions[0]; OverlayScale = 1; OverlayOpacity = .96; }
}
