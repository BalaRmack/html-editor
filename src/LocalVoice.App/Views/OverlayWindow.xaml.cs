using System.ComponentModel;
using System.Windows;
using Application = System.Windows.Application;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using LocalVoice.App.Platform;
using LocalVoice.App.Services;
using LocalVoice.App.ViewModels;
namespace LocalVoice.App.Views;

public partial class OverlayWindow : Window
{
    private const double BaseWidth = 352;
    private const double CapsuleHeight = 96;
    private const double ExpandedHeight = 260;
    private int _generation;
    private MainViewModel? _model;

    public OverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => MakeNoActivate();
        DataContextChanged += (_, _) =>
        {
            if (_model is not null) _model.PropertyChanged -= OnModelChanged;
            _model = DataContext as MainViewModel;
            if (_model is not null) _model.PropertyChanged += OnModelChanged;
        };
    }
    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsOverlayExpanded) && IsVisible) ResizeExpanded();
    }
    private void MakeNoActivate()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, (style | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW) & ~NativeMethods.WS_EX_APPWINDOW);
    }
    public void ShowNoActivate() => Present();
    public void ShowFailure(string message)
    {
        if (_model is not null) { _model.OverlayError = message; _model.IsOverlayExpanded = true; }
        Present();
    }
    private void Present()
    {
        var generation = ++_generation;
        var settings = AppServices.Current.Settings.Current;
        var scale = settings.OverlayScale;
        Width = BaseWidth * scale;
        Height = ((_model?.IsOverlayExpanded == true) ? ExpandedHeight : CapsuleHeight) * scale;
        Position(settings.OverlayPosition, Height);
        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        if (!IsVisible) Show();
        MakeNoActivate();
        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, settings.OverlayOpacity, UiMotion.Normal) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        UiMotion.Enter(Root, 8);
        if (generation != _generation) Hide();
    }
    private void ResizeExpanded()
    {
        var settings = AppServices.Current.Settings.Current;
        var target = ((_model?.IsOverlayExpanded == true) ? ExpandedHeight : CapsuleHeight) * settings.OverlayScale;
        Position(settings.OverlayPosition, target);
        BeginAnimation(HeightProperty, new DoubleAnimation(target, UiMotion.Fast) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }
    private void Position(Core.OverlayPosition position, double height)
    {
        var work = SystemParameters.WorkArea;
        var name = position.ToString();
        Left = name.EndsWith("Left") ? work.Left + 8 : name.EndsWith("Right") ? work.Right - Width - 8 : work.Left + (work.Width - Width) / 2;
        Top = name.StartsWith("Top") ? work.Top + 8 : work.Bottom - height - 8;
        Left = Math.Clamp(Left, work.Left, Math.Max(work.Left, work.Right - Width));
        Top = Math.Clamp(Top, work.Top, Math.Max(work.Top, work.Bottom - height));
    }
    public async void HideAnimated()
    {
        var generation = ++_generation;
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, UiMotion.Fast));
        await Task.Delay(UiMotion.Enabled ? 130 : 0);
        if (generation == _generation && IsVisible) Hide();
    }
    private void OnExportClick(object sender, RoutedEventArgs e) => ((App)Application.Current).PrepareOverlayExport();
}
