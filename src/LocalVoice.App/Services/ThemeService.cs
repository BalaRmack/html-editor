using System.Windows;
using Microsoft.Win32;
using LocalVoice.Core;
namespace LocalVoice.App.Services;

public sealed class ThemeService : IDisposable
{
    private AppTheme _preference;
    public bool IsDark { get; private set; } = true;
    public event EventHandler? Changed;
    public ThemeService() => SystemEvents.UserPreferenceChanged += OnPreferenceChanged;
    public void Apply(AppTheme theme)
    {
        _preference = theme;
        var dark = theme == AppTheme.Dark || (theme == AppTheme.System && ReadSystemDark());
        IsDark = dark;
        var dictionary = new ResourceDictionary { Source = new Uri($"Themes/{(dark ? "Dark" : "Light")}.xaml", UriKind.Relative) };
        var merged = Application.Current.Resources.MergedDictionaries;
        for (var i = merged.Count - 1; i >= 0; i--)
            if (merged[i].Source?.OriginalString.Contains("/Dark.xaml") == true || merged[i].Source?.OriginalString.Contains("/Light.xaml") == true)
                merged.RemoveAt(i);
        merged.Add(dictionary);
        Changed?.Invoke(this, EventArgs.Empty);
    }
    private static bool ReadSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is not int value || value == 0;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException) { return true; }
    }
    private void OnPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General && _preference == AppTheme.System)
            Application.Current.Dispatcher.BeginInvoke(() => Apply(_preference));
    }
    public void Dispose() => SystemEvents.UserPreferenceChanged -= OnPreferenceChanged;
}
