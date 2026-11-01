using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using System.Windows.Threading;
namespace LocalVoice.App.Controls;

/// <summary>Mirrored envelope of recent microphone RMS. No random "fake speech" animation and no idle timer.</summary>
public sealed class WaveformView : FrameworkElement
{
    private const int BarCount = 44;
    private readonly float[] _history = new float[BarCount];
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private float _target, _smooth;
    private int _tick;

    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(nameof(Level), typeof(double), typeof(WaveformView),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender, OnLevelChanged));
    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(nameof(IsActive), typeof(bool), typeof(WaveformView),
        new FrameworkPropertyMetadata(false, OnActiveChanged));
    public static readonly DependencyProperty BarBrushProperty = DependencyProperty.Register(nameof(BarBrush), typeof(Brush), typeof(WaveformView),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Level { get => (double)GetValue(LevelProperty); set => SetValue(LevelProperty, value); }
    public bool IsActive { get => (bool)GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }
    public Brush BarBrush { get => (Brush)GetValue(BarBrushProperty); set => SetValue(BarBrushProperty, value); }

    public WaveformView()
    {
        IsHitTestVisible = false;
        _timer.Tick += (_, _) => Tick();
        Loaded += (_, _) => UpdateTimer();
        Unloaded += (_, _) => _timer.Stop();
        IsVisibleChanged += (_, _) => UpdateTimer();
    }
    private static void OnLevelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((WaveformView)d)._target = (float)Math.Clamp((double)e.NewValue, 0, 1);
    private static void OnActiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (WaveformView)d;
        if (!(bool)e.NewValue) { Array.Clear(view._history); view._smooth = view._target = 0; view.InvalidateVisual(); }
        view.UpdateTimer();
    }
    private void UpdateTimer()
    {
        if (IsActive && IsVisible) { if (!_timer.IsEnabled) _timer.Start(); }
        else _timer.Stop();
    }
    private void Tick()
    {
        _smooth += (_target - _smooth) * 0.45f;
        if (++_tick % 2 == 0)
        {
            Array.Copy(_history, 1, _history, 0, BarCount - 1);
            _history[BarCount - 1] = _smooth;
        }
        InvalidateVisual();
    }
    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth; var h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        var brush = BarBrush ?? Brushes.White;
        var gap = w / BarCount;
        var barWidth = Math.Min(3.0, gap * 0.62);
        for (var i = 0; i < BarCount; i++)
        {
            var taper = 0.35 + 0.65 * Math.Sin(Math.PI * (i + 0.5) / BarCount);
            var value = IsActive ? _history[i] : 0;
            var barHeight = 2.0 + value * (h - 3.0) * taper;
            var x = i * gap + (gap - barWidth) / 2;
            var y = (h - barHeight) / 2;
            dc.DrawRoundedRectangle(brush, null, new Rect(x, y, barWidth, barHeight), barWidth / 2, barWidth / 2);
        }
    }
}
