using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using System.Windows.Interop;
using LocalVoice.App.Platform;
using LocalVoice.App.Services;
using LocalVoice.App.ViewModels;
using LocalVoice.Core;
namespace LocalVoice.App.Views;

public partial class MainWindow : Window
{
    private bool _allowClose;
    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var dark = AppServices.Current.Themes.IsDark ? 1 : 0;
            NativeMethods.DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
            var rounded = 2; NativeMethods.DwmSetWindowAttribute(hwnd, 33, ref rounded, sizeof(int));
        };
        AppServices.Current.Themes.Changed += OnThemeChanged;
        Activated += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var dark = AppServices.Current.Themes.IsDark ? 1 : 0;
            NativeMethods.DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
        };
    }
    public void AllowClose() { _allowClose = true; }
    private void OnThemeChanged(object? sender, EventArgs e)
    {
        if (!IsLoaded) return;
        var hwnd = new WindowInteropHelper(this).Handle; var dark = AppServices.Current.Themes.IsDark ? 1 : 0;
        NativeMethods.DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) { AppServices.Current.Themes.Changed -= OnThemeChanged; return; }
        e.Cancel = true;
        (DataContext as MainViewModel)?.CancelHotkeyCapture();
        Hide();
    }
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var model = DataContext as MainViewModel;
        if (model is null || !model.Settings.IsCapturingHotkey) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        e.Handled = true;
        if (key == Key.Escape) { model.CancelHotkeyCapture(); return; }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.None) return;
        var modifiers = Keyboard.Modifiers;
        uint flags = 0;
        if (modifiers.HasFlag(ModifierKeys.Control)) flags |= HotkeySpec.Control;
        if (modifiers.HasFlag(ModifierKeys.Alt)) flags |= HotkeySpec.Alt;
        if (modifiers.HasFlag(ModifierKeys.Shift)) flags |= HotkeySpec.Shift;
        if (modifiers.HasFlag(ModifierKeys.Windows)) flags |= HotkeySpec.Win;
        var name = key.ToString();
        var specText = string.Join('+', new[] { (flags & HotkeySpec.Control) != 0 ? "Ctrl" : null, (flags & HotkeySpec.Alt) != 0 ? "Alt" : null, (flags & HotkeySpec.Shift) != 0 ? "Shift" : null, (flags & HotkeySpec.Win) != 0 ? "Win" : null, name }.Where(x => x is not null));
        if (HotkeySpec.TryParse(specText, out var spec)) model.CaptureHotkey(spec);
        else model.Notice = "Используйте Ctrl, Alt или Win вместе с буквой, цифрой, F-клавишей или Space.";
    }
}
