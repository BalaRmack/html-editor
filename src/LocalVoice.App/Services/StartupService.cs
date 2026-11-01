using Microsoft.Win32;
namespace LocalVoice.App.Services;

public sealed class StartupService
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "LocalVoice";
    public static string ExpectedValue => $"\"{Environment.ProcessPath}\" --minimized";
    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return string.Equals(key?.GetValue(ValueName) as string, ExpectedValue, StringComparison.OrdinalIgnoreCase);
    }
    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true)
            ?? throw new InvalidOperationException("Не удалось открыть автозагрузку текущего пользователя.");
        if (enabled) key.SetValue(ValueName, ExpectedValue, RegistryValueKind.String);
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
