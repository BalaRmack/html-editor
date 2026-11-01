using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
namespace LocalVoice.App.Services;

/// <summary>Short, cheap UI transitions. Honours the Windows "animate controls" accessibility setting.</summary>
public static class UiMotion
{
    public static bool Enabled => SystemParameters.ClientAreaAnimation;
    public static Duration Fast => new(TimeSpan.FromMilliseconds(Enabled ? 120 : 0));
    public static Duration Normal => new(TimeSpan.FromMilliseconds(Enabled ? 180 : 0));

    public static void Enter(FrameworkElement element, double offsetY = 8)
    {
        if (!Enabled) { element.Opacity = 1; element.RenderTransform = Transform.Identity; return; }
        var move = new TranslateTransform(0, offsetY);
        element.RenderTransform = move;
        element.Opacity = 1;
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, Normal) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(offsetY, 0, Normal) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    public static void Transition(FrameworkElement element)
    {
        if (!Enabled) return;
        var move = new TranslateTransform(0, 6);
        element.RenderTransform = move;
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.55, 1, Fast) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(6, 0, Fast) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }
}
