using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalVoice.Core;
using System.Windows.Input;
using LocalVoice.App.Services;
using LocalVoice.App.Platform;
namespace LocalVoice.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    public SettingsStore SettingsStore { get; }
    public ModelManager Models { get; }
    public WhisperEngine Engine { get; }
    public HistoryStore History { get; }
    public StatisticsStore Statistics { get; }
    public DictationController Controller { get; }
    public HotkeyService Hotkeys { get; }
    public MicrophoneService Microphones { get; }
    public ClipboardService Clipboard { get; }
    public TextInsertionService Insertion { get; }
    public ExportProviderRegistry ExportProviders { get; }
    public IAppLog Log { get; }
    private readonly StartupService _startup;
    private readonly ThemeService _themes;
    private readonly Func<string, Task<bool>> _confirm;
    private IReadOnlyList<TranscriptEntry> _allHistory = [];
    public SettingsViewModel Settings { get; }
    public ObservableCollection<ModelItemViewModel> ModelItems { get; } = [];
    public ObservableCollection<HistoryItemViewModel> HistoryItems { get; } = [];
    public ObservableCollection<IExportProvider> ConnectedProviders => new(ExportProviders.ConnectedRemote);
    [ObservableProperty] private string _selectedTab = "Dashboard";
    [ObservableProperty] private string _lastText = "";
    [ObservableProperty] private string _liveText = "";
    [ObservableProperty] private string _status = "Готов к диктовке";
    [ObservableProperty] private string _lastError = "";
    [ObservableProperty] private string _notice = "Ваш голос остаётся на этом компьютере.";
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private double _level;
    [ObservableProperty] private long _recognitionCount;
    [ObservableProperty] private double _totalSeconds;
    [ObservableProperty] private string _historySearch = "";
    [ObservableProperty] private string _modelStorageText = "";
    [ObservableProperty] private bool _isHistoryEnabled;
    public string Version => typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
    public bool IsBusy => Controller.State != DictationState.Idle;
    public bool IsListening => Controller.State == DictationState.Listening;
    public bool IsTesting => Controller.IsTesting;
    public bool ControllerBusy => IsBusy;
    public bool CanEdit => !IsBusy && !IsTesting;
    public bool HasText => !string.IsNullOrWhiteSpace(LastText);
    public bool HasLiveText => !string.IsNullOrWhiteSpace(LiveText);
    public bool ShowExport => true;
    public string StartLabel => Controller.State switch { DictationState.Loading => "Загружаю модель…", DictationState.Listening => "Остановить", DictationState.Finishing => "Завершаю…", _ => "Начать прослушивание" };
    public string OverlayStatus => Controller.State switch { DictationState.Loading => "Загружаю модель…", DictationState.Finishing => "Завершаю…", _ => "Слушаю…" };
    public string MicrophoneSummary => Controller.ActiveDevice ?? (SettingsStore.Current.InputDeviceId.Length == 0 ? "Системный микрофон" : "Выбранный микрофон");
    public string ModelSummary => ModelCatalog.ById[SettingsStore.Current.ModelId].Name;
    public string LanguageSummary => Settings.Language.Name;
    public string HotkeySummary => SettingsStore.Current.Hotkey;
    public string ModeHint => SettingsStore.Current.Mode == ListeningMode.PushToTalk ? "Удерживайте сочетание. Отпустите — текст готов." : "Повторное сочетание или Esc — остановить.";
    public string TotalTimeText => TimeSpan.FromSeconds(TotalSeconds).ToString(@"hh\:mm\:ss");
    public string ListenButtonHint => IsBusy ? "Микрофон включён только на время диктовки" : $"{HotkeySummary} · работает в любом приложении";
    public string PrivacyText => "Аудио только в памяти · без облака";
    public MainViewModel(SettingsStore settings, ModelManager models, WhisperEngine engine, HistoryStore history, StatisticsStore statistics,
        DictationController controller, HotkeyService hotkeys, MicrophoneService microphones, ClipboardService clipboard,
        TextInsertionService insertion, ExportProviderRegistry providers, IAppLog log, StartupService startup, ThemeService themes,
        Func<string, Task<bool>> confirm)
    {
        SettingsStore = settings; Models = models; Engine = engine; History = history; Statistics = statistics; Controller = controller; Hotkeys = hotkeys;
        Microphones = microphones; Clipboard = clipboard; Insertion = insertion; ExportProviders = providers; Log = log; _startup = startup; _themes = themes; _confirm = confirm;
        Settings = new SettingsViewModel(this);
        foreach (var model in ModelCatalog.All) ModelItems.Add(new(model, this));
        Controller.StateChanged += (_, _) => OnUi(() => { RefreshState(); if (!IsBusy) LiveText = ""; });
        Controller.Level += (_, value) => OnUi(() => Level = Math.Clamp(Math.Sqrt(value) * 2.4, 0, 1));
        Controller.LiveTextChanged += (_, text) => OnUi(() => { LiveText = text; if (IsBusy) LastText = text; });
        Controller.Error += (_, text) => OnUi(() => { LastError = text; HasError = true; Notice = text; _ = Statistics.ErrorAsync(text).ContinueWith(t => Log.Error("Statistics", t.Exception!), TaskContinuationOptions.OnlyOnFaulted); });
        Controller.Notice += (_, text) => OnUi(() => Notice = text);
        Controller.Completed += (_, entry) => OnUi(async () =>
        {
            LastText = entry.Text;
            try
            {
                await History.AddAsync(entry, SettingsStore.Current.SaveHistory, SettingsStore.Current.HistoryLimit);
                await Statistics.RecordAsync(entry.DurationSeconds);
                if (SettingsStore.Current.AutoPaste) await Insertion.InsertAsync(entry.Text, Controller.Target);
                Notice = SettingsStore.Current.AutoPaste ? "Текст готов. Вставка выполнена в исходное окно." : "Текст готов — скопируйте или вставьте в исходное окно.";
            }
            catch (Exception ex) { ReportError(ex, "Сохранение или вставка результата"); }
        });
        History.Changed += (_, _) => _ = RefreshHistoryAsync();
        Statistics.Changed += (_, _) => _ = RefreshStatisticsAsync();
        Models.Changed += (_, _) => OnUi(RefreshModels);
        SettingsStore.Current.StartWithWindows = _startup.IsEnabled();
        IsHistoryEnabled = SettingsStore.Current.SaveHistory;
        RefreshModels(); RefreshState();
    }
    public async Task InitializeAsync() { await RefreshHistoryAsync(); await RefreshStatisticsAsync(); }
    private static void OnUi(Action action) => Application.Current.Dispatcher.BeginInvoke(action);
    private static void OnUi(Func<Task> action) => Application.Current.Dispatcher.BeginInvoke(async () => { try { await action(); } catch (Exception ex) { AppServices.Current.Log.Error("UI async action", ex); } });
    partial void OnLastTextChanged(string value) { OnPropertyChanged(nameof(HasText)); }
    partial void OnLiveTextChanged(string value) { OnPropertyChanged(nameof(HasLiveText)); }
    partial void OnTotalSecondsChanged(double value) => OnPropertyChanged(nameof(TotalTimeText));
    partial void OnHistorySearchChanged(string value) => FilterHistory();
    private void RefreshState()
    {
        Status = Controller.IsTesting ? "Тест микрофона · говорят 10 секунд" : Controller.State switch
        { DictationState.Loading => "Загружаю локальную модель…", DictationState.Listening => "Слушаю… говорите естественно", DictationState.Finishing => "Обрабатываю последний фрагмент…", _ => "Готов к диктовке" };
        foreach (var name in new[] { nameof(IsBusy), nameof(IsListening), nameof(IsTesting), nameof(CanEdit), nameof(StartLabel), nameof(OverlayStatus), nameof(MicrophoneSummary), nameof(ListenButtonHint) }) OnPropertyChanged(name);
        RefreshModels();
    }
    public void RefreshModels()
    {
        foreach (var item in ModelItems) item.Refresh();
        ModelStorageText = $"{ModelDescriptor.FormatSize(Models.InstalledBytes)} на диске";
        OnPropertyChanged(nameof(ModelSummary));
    }
    [RelayCommand]
    public void Navigate(string tab) { SelectedTab = tab; if (tab == "Settings") Settings.RefreshDevices(); if (tab == "History") _ = RefreshHistoryAsync(); }
    [RelayCommand] private void RefreshModelStatuses() => RefreshModels();
    [RelayCommand] private void OpenSettings() => Navigate("Settings");
    [RelayCommand] private Task ToggleListeningAsync() => Controller.ToggleAsync();
    [RelayCommand] private Task TestMicrophoneAsync() => Controller.ToggleTestAsync();
    [RelayCommand] private Task CopyAsync() => CopyTextAsync(LastText);
    [RelayCommand] private Task PasteAsync() => PasteTextAsync(LastText);
    [RelayCommand] private void ClearText() { LastText = ""; LiveText = ""; Notice = "Поле очищено. Записи истории не изменены."; }
    [RelayCommand] private void OpenExport() { Navigate("Integrations"); }
    [RelayCommand] private async Task ExportToAsync(IExportProvider? provider)
    {
        if (provider is null || !provider.IsConfigured || !provider.SendsOverNetwork) { Navigate("Integrations"); return; }
        try { await provider.ExportAsync(LastText); Notice = $"Текст отправлен: {provider.DisplayName}."; }
        catch (Exception ex) { ReportError(ex, "Экспорт"); }
    }
    [RelayCommand] private void OpenLogs()
    {
        try { Directory.CreateDirectory(Log.DirectoryPath); Process.Start(new ProcessStartInfo("explorer.exe") { Arguments = $"\"{Log.DirectoryPath}\"", UseShellExecute = true }); }
        catch (Exception ex) { ReportError(ex, "Открытие логов"); }
    }
    [RelayCommand] private void CheckUpdates() => MessageBox.Show($"LocalVoice {Version}\n\nАвтоматическое обновление в этой локальной сборке не подключено. Приложение ничего не проверяет в сети. Новый установщик можно получить у поставщика сборки и запустить поверх текущей версии.", "Проверка обновлений", MessageBoxButton.OK, MessageBoxImage.Information);
    [RelayCommand] private void OpenLicenses()
    {
        try { Process.Start(new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.md")) { UseShellExecute = true }); }
        catch (Exception ex) { ReportError(ex, "Открытие лицензий"); }
    }
    [RelayCommand] private Task ClearHistoryAsync() => DeleteHistoryAsync(null);
    public async Task CopyTextAsync(string text)
    { try { await Clipboard.CopyAsync(text); Notice = "Скопировано в буфер обмена."; } catch (Exception ex) { ReportError(ex, "Копирование"); } }
    public async Task PasteTextAsync(string text)
    { try { await Insertion.InsertAsync(text, Controller.Target); Notice = "Вставка выполнена."; } catch (Exception ex) { ReportError(ex, "Вставка"); } }
    public async Task DeleteHistoryAsync(Guid? id)
    {
        try
        {
            if (!await _confirm(id is null ? "Удалить всю историю с диска? Это действие нельзя отменить." : "Удалить эту запись из истории?")) return;
            if (id is null) await History.ClearAsync(); else await History.DeleteAsync([id.Value]);
        }
        catch (Exception ex) { ReportError(ex, "Удаление истории"); }
    }
    private async Task RefreshHistoryAsync()
    {
        _allHistory = await History.SnapshotAsync();
        OnUi(() => { IsHistoryEnabled = SettingsStore.Current.SaveHistory; FilterHistory(); });
    }
    private void FilterHistory()
    {
        var query = HistorySearch.Trim();
        HistoryItems.Clear();
        foreach (var entry in _allHistory.Where(x => query.Length == 0 || x.Text.Contains(query, StringComparison.OrdinalIgnoreCase))) HistoryItems.Add(new(entry, this));
    }
    private async Task RefreshStatisticsAsync()
    {
        var value = await Statistics.SnapshotAsync();
        OnUi(() => { RecognitionCount = value.RecognitionCount; TotalSeconds = value.TotalSeconds; if (value.LastError is not null) { LastError = value.LastError; HasError = true; } });
    }
    public async Task DownloadModelAsync(ModelItemViewModel item)
    {
        if (item.Busy || Models.IsBusy || ControllerBusy) return;
        item.Busy = true; item.Progress = 0; item.ProgressText = $"Нужно скачать {item.Size}. Аудио никуда не отправляется."; item.Refresh();
        try
        {
            var progress = new Progress<DownloadProgress>(p => OnUi(() => { item.Progress = p.Received / (double)p.Total * 100; item.ProgressText = $"{p.Stage} · {ModelDescriptor.FormatSize(p.Received)} / {item.Size}"; item.Status = p.Stage; }));
            await Models.DownloadAsync(item.Descriptor, progress);
            Notice = $"{item.Name}: файл скачан, SHA-256 подтверждён.";
            if (!ModelItems.Any(x => x.Installed && x.Selected)) await SelectModelAsync(item);
        }
        catch (OperationCanceledException) { Notice = "Загрузка отменена. Неполный файл удалён."; }
        catch (Exception ex) { ReportError(ex, "Загрузка модели"); item.ProgressText = "Не скачана. Подробности в последнем ошибке и логах."; }
        finally { item.Busy = false; item.Refresh(); }
    }
    public async Task SelectModelAsync(ModelItemViewModel item)
    {
        try
        {
            if (IsBusy) { Notice = "Сначала остановите диктовку."; return; }
            var settings = SettingsStore.Current.Clone(); settings.ModelId = item.Id;
            await SettingsStore.SaveAsync(settings); RefreshModels(); OnPropertyChanged(nameof(ModelSummary)); Notice = $"Выбрана модель {item.Name}.";
        }
        catch (Exception ex) { ReportError(ex, "Выбор модели"); }
    }
    public async Task DeleteModelAsync(ModelItemViewModel item)
    {
        try
        {
            if (IsBusy || !await _confirm($"Удалить {item.Name} с диска? Её можно скачать снова.")) return;
            await Models.DeleteAsync(item.Descriptor, async () =>
            {
                if (Controller.State != DictationState.Idle) throw new InvalidOperationException("Сначала остановите диктовку.");
                await Engine.UnloadAsync();
            });
            RefreshModels(); Notice = "Модель удалена. История и настройки сохранены.";
        }
        catch (Exception ex) { ReportError(ex, "Удаление модели"); }
    }
    public async Task SaveSettingsAsync()
    {
        var previous = SettingsStore.Current.Clone();
        if (Settings.IsCapturingHotkey) CancelHotkeyCapture();
        var next = AppSettings.Normalize(Settings.ToSettings());
        try
        {
            if (!HotkeySpec.TryParse(next.Hotkey, out var spec)) throw new InvalidOperationException("Запишите сочетание, например Ctrl+Alt+Space.");
            Hotkeys.Configure(spec);
            try { _startup.SetEnabled(next.StartWithWindows); }
            catch { Hotkeys.Configure(LocalVoice.App.Platform.HotkeyParser.Parse(previous.Hotkey)); throw; }
            try { await SettingsStore.SaveAsync(next); }
            catch { Hotkeys.Configure(LocalVoice.App.Platform.HotkeyParser.Parse(previous.Hotkey)); _startup.SetEnabled(previous.StartWithWindows); throw; }
            _themes.Apply(next.Theme);
            OnPropertyChanged(nameof(SettingsStore));
            if (previous.SaveHistory && !next.SaveHistory) await History.ClearAsync();
            Settings.Load(); RefreshState(); OnPropertyChanged(nameof(HotkeySummary)); OnPropertyChanged(nameof(LanguageSummary)); OnPropertyChanged(nameof(ModeHint));
            Notice = "Настройки сохранены."; HasError = false;
        }
        catch (Exception ex) { Hotkeys.Resume(); Settings.Load(); ReportError(ex, "Сохранение настроек"); }
    }
    public async Task ToggleStartupAsync()
    {
        var next = SettingsStore.Current.Clone(); next.StartWithWindows = !SettingsStore.Current.StartWithWindows;
        try { _startup.SetEnabled(next.StartWithWindows); await SettingsStore.SaveAsync(next); Settings.StartWithWindows = next.StartWithWindows; }
        catch (Exception ex) { ReportError(ex, "Автозапуск"); }
        OnPropertyChanged(nameof(Settings));
    }
    public void CancelHotkeyCapture() { Settings.IsCapturingHotkey = false; Settings.Hotkey = SettingsStore.Current.Hotkey; Hotkeys.Resume(); }
    public void CaptureHotkey(HotkeySpec spec) { Settings.IsCapturingHotkey = false; Settings.Hotkey = spec.ToString(); Hotkeys.Resume(); Notice = "Сочетание записано. Нажмите «Сохранить настройки»."; }
    public void ReportError(Exception exception, string context)
    {
        Log.Error(context, exception); LastError = exception.Message; HasError = true; Notice = exception.Message;
        OnUi(async () => { try { await Statistics.ErrorAsync(exception.Message); } catch (Exception ex) { Log.Error("Statistics", ex); } });
    }
}

