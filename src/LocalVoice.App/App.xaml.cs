using System.IO;
using System.Security.Principal;
using System.Threading;
using System.Windows;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using LocalVoice.App.Platform;
using LocalVoice.App.Services;
using LocalVoice.App.Views;
using LocalVoice.Core;
namespace LocalVoice.App;

public partial class App : Application
{
    private Mutex? _mutex;
    private EventWaitHandle? _activate;
    private RegisteredWaitHandle? _wait;
    private AppServices? _services;
    private MainWindow? _main;
    private OverlayWindow? _overlay;
    private TrayService? _tray;
    private bool _exiting, _initialized;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _mutex = new Mutex(true, @"Local\LocalVoice.Instance", out var first);
        if (!first)
        {
            try { using var existing = EventWaitHandle.OpenExisting(@"Local\LocalVoice.Activate"); existing.Set(); } catch (WaitHandleCannotBeOpenedException) { }
            Shutdown(); return;
        }
        _activate = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\LocalVoice.Activate");
        _wait = ThreadPool.RegisterWaitForSingleObject(_activate, (_, _) => Dispatcher.BeginInvoke(() => ShowMain("Dashboard")), null, -1, false);
        DispatcherUnhandledException += (_, args) =>
        {
            _services?.Log.Error("UI exception", args.Exception);
            if (_services is not null) _services.ViewModel.LastError = args.Exception.Message;
            args.Handled = !_exiting;
            if (!_exiting) MessageBox.Show("Не удалось выполнить действие. Подробности — в папке логов. Приложение продолжает работать.", "LocalVoice", MessageBoxButton.OK, MessageBoxImage.Warning);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => _services?.Log.Error("Fatal domain exception", args.ExceptionObject as Exception ?? new Exception("Unknown fatal error"));
        TaskScheduler.UnobservedTaskException += (_, args) => { _services?.Log.Error("Background task", args.Exception); args.SetObserved(); };
        SessionEnding += (_, _) => ExitApplication();
        try
        {
            _services = new AppServices(); await _services.InitializeAsync(CancellationToken.None); _initialized = true;
            _overlay = new OverlayWindow { DataContext = _services.ViewModel };
            _tray = new TrayService(_services.ViewModel, () => ShowMain("Dashboard"), () => ShowMain("Settings"), () => ShowMain("History"), ExitApplication);
            _services.Controller.SessionStarting += (_, _) => Dispatcher.BeginInvoke(() => { _overlay?.ShowNoActivate(); _services.Hotkeys.BeginSession(_services.Settings.Current.Mode == ListeningMode.PushToTalk); });
            _services.Controller.SessionStopping += (_, _) => Dispatcher.BeginInvoke(() => { _services.Hotkeys.EndSession(); _overlay?.HideAnimated(); });
            _services.Hotkeys.Pressed += (_, _) => HotkeyPressed();
            _services.Hotkeys.Released += (_, _) => { if (_services.Settings.Current.Mode == ListeningMode.PushToTalk) Fire(_services.Controller.StopAsync()); };
            _services.Hotkeys.Escape += (_, _) => { if (_services.Controller.State != DictationState.Idle) Fire(_services.Controller.StopAsync()); };
            if (!e.Args.Contains("--minimized", StringComparer.OrdinalIgnoreCase)) ShowMain("Dashboard");
            else if (!_services.Models.LooksInstalled(ModelCatalog.ById[_services.Settings.Current.ModelId])) _tray.ShowMessage("Откройте LocalVoice в трее и скачайте первую модель.");
            _services.Log.Info("Application ready; no network requests or model preload.");
        }
        catch (Exception ex)
        {
            _services?.Log.Error("Startup", ex);
            MessageBox.Show($"LocalVoice не удалось запустить.\n\n{ex.Message}\n\nПопробуйте переустановить приложение или проверить права на папку %LOCALAPPDATA%\\LocalVoice.", "LocalVoice", MessageBoxButton.OK, MessageBoxImage.Error);
            ExitApplication();
        }
    }
    private void HotkeyPressed()
    {
        if (_services is null) return;
        if (_services.Controller.State == DictationState.Idle)
        {
            var foreground = NativeMethods.GetForegroundWindow();
            if (foreground != IntPtr.Zero && foreground != (_main is null ? IntPtr.Zero : new WindowInteropHelper(_main).Handle))
                _services.Controller.RememberTarget(foreground);
            if (_services.Settings.Current.Mode == ListeningMode.PushToTalk && _services.Controller.IsTesting) Fire(_services.Controller.StopTestAsync());
        }
        Fire(_services.Controller.ToggleAsync());
    }
    private async void Fire(Task task) { try { await task; } catch (Exception ex) { _services?.ViewModel.ReportError(ex, "Действие"); } }
    public void ShowMain(string tab)
    {
        if (!_initialized || _services is null || _exiting) return;
        if (_main is null)
        {
            _main = new MainWindow { DataContext = _services.ViewModel };
            _main.Closed += (_, _) => _main = null;
        }
        _services.ViewModel.Navigate(tab);
        if (!_main.IsVisible) _main.Show();
        if (_main.WindowState == WindowState.Minimized) _main.WindowState = WindowState.Normal;
        _main.Activate();
    }
    public void PrepareOverlayExport()
    {
        if (_services is null) return;
        var providers = _services.ExportProviders.ConnectedRemote.ToArray();
        if (providers.Length == 1 && !string.IsNullOrWhiteSpace(_services.ViewModel.LastText))
            _services.ViewModel.ExportToCommand.Execute(providers[0]);
        else ShowMain("Integrations");
    }
    private void ExitApplication()
    {
        if (_exiting) return;
        _exiting = true;
        _tray?.Dispose(); _overlay?.Close(); _main?.AllowClose(); _main?.Close();
        Fire(ShutdownAsync());
    }
    private async Task ShutdownAsync()
    {
        try { if (_services is not null) await _services.DisposeAsync(); }
        catch (Exception ex) { _services?.Log.Error("Shutdown", ex); }
        finally
        {
            _wait?.Unregister(null); _activate?.Dispose();
            try { _mutex?.ReleaseMutex(); } catch (ApplicationException) { }
            _mutex?.Dispose(); Dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
        }
    }
}
