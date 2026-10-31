using System.ComponentModel;
using System.Runtime.InteropServices;
using LocalVoice.App.Platform;
namespace LocalVoice.App.Services;

public sealed class TextInsertionService
{
    public IntPtr LastExternalWindow { get; set; }
    public async Task InsertAsync(string text, IntPtr target, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(text)) throw new InvalidOperationException("Пока нечего вставлять.");
        if (target == IntPtr.Zero || !NativeMethods.IsWindow(target))
            throw new InvalidOperationException("Сначала начните диктовку из нужного приложения: LocalVoice запомнит его поле ввода.");
        if (NativeMethods.GetForegroundWindow() != target)
        {
            NativeMethods.SetForegroundWindow(target);
            await Task.Delay(160, ct).ConfigureAwait(false);
        }
        if (NativeMethods.GetForegroundWindow() != target)
            throw new InvalidOperationException("Не удалось вернуть фокус. Скопируйте текст и вставьте его вручную.");
        await Task.Run(() =>
        {
            foreach (var chunk in text.Chunk(40))
            {
                ct.ThrowIfCancellationRequested();
                if (NativeMethods.GetForegroundWindow() != target) throw new InvalidOperationException("Фокус изменился — вставка остановлена.");
                var inputs = new List<NativeMethods.INPUT>();
                foreach (var character in chunk)
                {
                    if (character == '\r') continue;
                    if (character == '\n')
                    {
                        inputs.Add(Key(0x0D, 0, 0)); inputs.Add(Key(0x0D, 0, 2));
                    }
                    else
                    {
                        inputs.Add(Key(0, character, 4)); inputs.Add(Key(0, character, 4 | 2));
                    }
                }
                var sent = NativeMethods.SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<NativeMethods.INPUT>());
                if (sent != inputs.Count) throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows заблокировала вставку. Приложение с правами администратора недоступно обычному приложению.");
            }
        }, ct).ConfigureAwait(false);
    }
    private static NativeMethods.INPUT Key(ushort virtualKey, ushort scan, uint flags) => new()
    { type = 1, data = new NativeMethods.InputUnion { keyboard = new() { wVk = virtualKey, wScan = scan, dwFlags = flags } } };
}
