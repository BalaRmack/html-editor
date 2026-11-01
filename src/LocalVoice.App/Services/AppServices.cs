using System.Windows;
using MessageBox = System.Windows.MessageBox;
using LocalVoice.Core;
using LocalVoice.App.ViewModels;
using System.Windows.Input;
namespace LocalVoice.App.Services;

public sealed class AppServices : IAsyncDisposable
{
    public static AppServices Current { get; private set; } = null!;
    public AppPaths Paths { get; }
    public FileLog Log { get; }
    public SettingsStore Settings { get; }
    public HistoryStore History { get; }
    public StatisticsStore Statistics { get; }
    public ModelManager Models { get; }
    public WhisperEngine Engine { get; }
    public MicrophoneService Microphones { get; } = new();
    public DictationController Controller { get; }
    public HotkeyService Hotkeys { get; }
    public ThemeService Themes { get; }
    public StartupService Startup { get; } = new();
    public ClipboardService Clipboard { get; } = new();
    public TextInsertionService Insertion { get; } = new();
    public ExportProviderRegistry ExportProviders { get; } = new();
    public UpdateService Updates { get; }
    public MainViewModel ViewModel { get; }
    public AppServices()
    {
        Current = this; Paths = new AppPaths(); Log = new FileLog(Paths.LogsDirectory);
        Settings = new SettingsStore(Paths.SettingsFile); History = new HistoryStore(Paths.HistoryFile); Statistics = new StatisticsStore(Paths.StatisticsFile);
        Models = new ModelManager(Paths, Log); Engine = new WhisperEngine(Models, Log);
        Updates = new UpdateService(Paths, Settings, Log);
        Controller = new DictationController(Engine, Models, Microphones, Settings, Log);
        Hotkeys = new HotkeyService(); Themes = new ThemeService();
        ViewModel = new MainViewModel(Settings, Models, Engine, History, Statistics, Controller, Hotkeys, Microphones, Clipboard, Insertion,
            ExportProviders, Log, Startup, Themes, Updates, ConfirmAsync);
    }
    private static Task<bool> ConfirmAsync(string text) => Task.FromResult(
        MessageBox.Show(text, "LocalVoice", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes);
    public async Task InitializeAsync(CancellationToken ct)
    {
        await Settings.LoadAsync(ct); await History.LoadAsync(ct); await Statistics.LoadAsync();
        Settings.Current.StartWithWindows = Startup.IsEnabled();
        Themes.Apply(Settings.Current.Theme);
        try { Hotkeys.Configure(LocalVoice.App.Platform.HotkeyParser.Parse(Settings.Current.Hotkey)); }
        catch (Exception ex) { ViewModel.ReportError(ex, "Горячая клавиша"); }
        ViewModel.Settings.Load(); ViewModel.RefreshModels(); await ViewModel.InitializeAsync();
        if (Settings.LoadWarning) ViewModel.Notice = "Основные настройки были повреждены; восстановлена резервная копия. Проверьте раздел «Настройки».";
        Updates.Start(() => ViewModel.CanEdit && !Models.IsBusy && !ModelItems.Any(x => x.Busy) && !ViewModel.Tutorial.IsOpen && !ViewModel.Settings.IsCapturingHotkey);
    }
    public async ValueTask DisposeAsync()
    {
        Hotkeys.Dispose(); await Updates.DisposeAsync(); await Controller.DisposeAsync(); await Engine.DisposeAsync(); Models.Dispose(); Themes.Dispose(); await Log.DisposeAsync();
    }
}
