using System.Drawing;
using System.Windows;
using Application = System.Windows.Application;
using LocalVoice.App.ViewModels;
using WinForms = System.Windows.Forms;
namespace LocalVoice.App.Services;

public sealed class TrayService : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;
    private readonly WinForms.ToolStripMenuItem _listen;
    private readonly WinForms.ToolStripMenuItem _startup;
    private readonly MainViewModel _model;
    private readonly Action _open, _settings, _history, _exit;
    public TrayService(MainViewModel model, Action open, Action settings, Action history, Action exit)
    {
        _model = model; _open = open; _settings = settings; _history = history; _exit = exit;
        var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/LocalVoice.ico")).Stream;
        using (stream) _icon = new WinForms.NotifyIcon { Icon = new Icon(stream), Text = "LocalVoice — локальная диктовка", Visible = true };
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Открыть LocalVoice", null, (_, _) => _open());
        _listen = new WinForms.ToolStripMenuItem("Начать прослушивание", null, (_, _) => Fire(model.ToggleListeningCommand.ExecuteAsync(null)));
        menu.Items.Add(_listen);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Настройки", null, (_, _) => _settings());
        menu.Items.Add("История", null, (_, _) => _history());
        _startup = new WinForms.ToolStripMenuItem("Автозапуск", null, (_, _) => Fire(model.ToggleStartupAsync()));
        _startup.CheckOnClick = true; menu.Items.Add(_startup);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => _exit());
        _icon.ContextMenuStrip = menu;
        _icon.MouseClick += (_, e) => { if (e.Button == WinForms.MouseButtons.Left) _open(); };
        model.Controller.StateChanged += OnState;
        menu.Opening += (_, _) => { _startup.Checked = model.SettingsStore.Current.StartWithWindows; };
        OnState(null, EventArgs.Empty);
    }
    private void OnState(object? sender, EventArgs e) => Application.Current.Dispatcher.BeginInvoke(() =>
    {
        _listen.Text = _model.IsBusy ? "Остановить прослушивание" : "Начать прослушивание";
        _listen.Enabled = _model.Controller.State is DictationState.Idle or DictationState.Listening;
    });
    private async void Fire(Task task) { try { await task; } catch (Exception ex) { _model.ReportError(ex, "Команда трея"); } }
    public void ShowMessage(string text) => _icon.ShowBalloonTip(3500, "LocalVoice", text, WinForms.ToolTipIcon.Info);
    public void Dispose() { _model.Controller.StateChanged -= OnState; _icon.Visible = false; _icon.Icon?.Dispose(); _icon.Dispose(); }
}
