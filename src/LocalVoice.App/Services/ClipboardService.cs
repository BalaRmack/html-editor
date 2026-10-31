using System.Runtime.InteropServices;
using System.Windows;
namespace LocalVoice.App.Services;

public sealed class ClipboardService
{
    public async Task CopyAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("Пока нечего копировать.");
        await Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            for (var attempt = 0; ; attempt++)
            {
                try { Clipboard.SetText(text); return; }
                catch (COMException) when (attempt < 8) { await Task.Delay(50); }
                catch (COMException ex) { throw new InvalidOperationException("Буфер обмена занят другим приложением. Попробуйте ещё раз.", ex); }
            }
        }).Task.Unwrap();
    }
}
