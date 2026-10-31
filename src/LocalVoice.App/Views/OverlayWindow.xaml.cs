using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using LocalVoice.App.Platform;
using LocalVoice.Core;
namespace LocalVoice.App.Views;

public partial class OverlayWindow : Window
{
    private int _generation;
    public OverlayWindow() { InitializeComponent(); SourceInitialized += (_, _) => MakeNoActivate(); }
    private void MakeNoActivate()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, (style | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW) & ~NativeMethods.WS_EX_APPWINDOW);
    }
    public void ShowNoActivate()
    {
        var generation = ++_generation;
        var settings = AppServices.Current.Settings.Current;
        Opacity = 0; UpdateLayout();
        var work = SystemParameters.WorkArea;
        var width = ActualWidth > 0 ? ActualWidth : 646 * settings.OverlayScale;
        var height = ActualHeight > 0 ? ActualHeight : 225 * settings.OverlayScale;
        var position = settings.OverlayPosition.ToString();
        Left = position.EndsWith("Left") ? work.Left + 10 : position.EndsWith("Right") ? work.Right - width - 10 : work.Left + (work.Width - width) / 2;
        Top = position.StartsWith("Top") ? work.Top + 8 : work.Bottom - height - 8;
        if (!IsVisible) Show();
        MakeNoActivate();
        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
        BeginAnimation(OpacityProperty, new DoubleAnimation(settings.OverlayOpacity, TimeSpan.FromMilliseconds(130)) { EasingFunction = new CubicEase() });
        if (generation != _generation) Hide();
    }
    public async void HideAnimated()
    {
        var generation = ++_generation;
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(100)));
        await Task.Delay(105);
        if (generation == _generation && IsVisible) Hide();
    }
    private void OnExportClick(object sender, RoutedEventArgs e) => ((App)Application.Current).PrepareOverlayExport();
}
