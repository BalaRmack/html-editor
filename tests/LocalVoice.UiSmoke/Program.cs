using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Color = System.Windows.Media.Color;
using System.Windows.Threading;
using LocalVoice.App.Services;
using LocalVoice.App.Views;
using Application = System.Windows.Application;

namespace LocalVoice.UiSmoke;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var outDir = Path.GetFullPath(args.Length > 0 ? args[0] : "artifacts/ui");
        Directory.CreateDirectory(outDir);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (var theme in new[] { "Dark", "Light" })
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/LocalVoice;component/Themes/{theme}.xaml") });
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/LocalVoice;component/Themes/Controls.xaml") });
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/LocalVoice;component/Themes/Icons.xaml") });

        AppServices? services = null;
        MainWindow? main = null;
        OverlayWindow? overlay = null;
        try
        {
            CheckTheme(app.Resources, "Dark");
            services = new AppServices();
            main = new MainWindow { DataContext = services.ViewModel };
            main.Show(); Pump();
            services.ViewModel.Navigate("Settings"); Pump();
            var combo = FindVisual<ComboBox>(main);
            if (combo is null) throw new InvalidOperationException("Settings ComboBox was not created.");
            combo.IsDropDownOpen = true; Pump();
            Save(main, Path.Combine(outDir, "settings.png"));
            var popup = combo.Template.FindName("PART_Popup", combo) as Popup;
            if (popup?.Child is FrameworkElement popupRoot) Save(popupRoot, Path.Combine(outDir, "combo-popup.png"));
            combo.IsDropDownOpen = false;

            services.ViewModel.OpenTutorialCommand.Execute(null); Pump();
            Save(main, Path.Combine(outDir, "tutorial.png"));
            services.ViewModel.Tutorial.SkipCommand.Execute(null); Pump();

            overlay = new OverlayWindow { DataContext = services.ViewModel };
            overlay.ShowNoActivate(); Pump();
            if (overlay.Width > 370 || overlay.Height > 110)
                throw new InvalidOperationException($"Overlay is not compact: {overlay.Width}x{overlay.Height}");
            Save(overlay, Path.Combine(outDir, "overlay.png"));
            overlay.ShowFailure("Тестовая ошибка: микрофон недоступен."); Pump();
            if (!overlay.IsVisible || !services.ViewModel.HasOverlayError)
                throw new InvalidOperationException("Overlay error disappeared instead of staying visible.");
            Save(overlay, Path.Combine(outDir, "overlay-error.png"));
            overlay.HideAnimated(); Pump();
            Console.WriteLine("UI SMOKE PASSED");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            File.WriteAllText(Path.Combine(outDir, "error.txt"), ex.ToString());
            return 1;
        }
        finally
        {
            overlay?.Close();
            if (main is not null) { main.AllowClose(); main.Close(); }
            services?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            app.Shutdown();
        }
    }

    private static void CheckTheme(ResourceDictionary resources, string name)
    {
        // The smoke app merges Dark then Light; check the source dictionaries directly for both palettes.
        var dict = new ResourceDictionary { Source = new Uri($"pack://application:,,,/LocalVoice;component/Themes/{name}.xaml") };
        Color C(string key) => ((SolidColorBrush)dict[key]).Color;
        Contrast(C("Text"), C("Surface"));
        Contrast(C("Text"), C("SurfaceRaised"));
        Contrast(C("Text"), C("PopupSurface"));
        Contrast(C("TextMuted"), C("Surface"));
        Contrast(C("AccentInk"), C("Accent"));
        if (name == "Dark")
        {
            var bg = C("Background");
            if (bg.R != bg.G || bg.G != bg.B) throw new InvalidOperationException("Dark background is not neutral black/gray.");
        }
    }
    private static void Contrast(Color a, Color b)
    {
        static double L(Color c)
        {
            static double Ch(double v) { v /= 255; return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }
            return 0.2126 * Ch(c.R) + 0.7152 * Ch(c.G) + 0.0722 * Ch(c.B);
        }
        var l1 = L(a); var l2 = L(b);
        var ratio = (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
        if (ratio < 4.5) throw new InvalidOperationException($"Insufficient contrast {ratio:F2}: {a} on {b}");
    }
    private static void Pump()
    {
        for (var i = 0; i < 3; i++)
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }
    }
    private static T? FindVisual<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match && (match as UIElement)?.IsVisible == true) return match;
            var result = FindVisual<T>(child);
            if (result is not null) return result;
        }
        return null;
    }
    private static void Save(Visual visual, string path)
    {
        if (visual is not FrameworkElement element) return;
        var w = Math.Max(1, (int)element.ActualWidth);
        var h = Math.Max(1, (int)element.ActualHeight);
        var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
