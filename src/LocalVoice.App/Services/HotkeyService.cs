using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Threading;
using LocalVoice.Core;
using LocalVoice.App.Platform;
namespace LocalVoice.App.Services;

public sealed class HotkeyService : IDisposable
{
    private readonly HwndSource _window;
    private readonly Dispatcher _dispatcher;
    private NativeMethods.KeyboardProc? _hookProc;
    private IntPtr _hook;
    private HotkeySpec _spec;
    private bool _ptt, _suspended, _disposed;
    public event EventHandler? Pressed;
    public event EventHandler? Released;
    public event EventHandler? Escape;
    public HotkeySpec Current => _spec;
    public HotkeyService()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _window = new HwndSource(new HwndSourceParameters("LocalVoiceHotkeys")
        { ParentWindow = NativeMethods.HWND_MESSAGE, Width = 0, Height = 0 });
        _window.AddHook(WndProc);
    }
    public void Configure(HotkeySpec spec)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_suspended) { _spec = spec; return; }
        Unregister();
        if (!NativeMethods.RegisterHotKey(_window.Handle, 0x4C56, spec.Modifiers | NativeMethods.MOD_NOREPEAT, spec.VirtualKey))
        {
            var error = Marshal.GetLastWin32Error();
            if (_spec != default) NativeMethods.RegisterHotKey(_window.Handle, 0x4C56, _spec.Modifiers | NativeMethods.MOD_NOREPEAT, _spec.VirtualKey);
            throw new Win32Exception(error, error == 1409
                ? "Эта комбинация уже занята. Выберите другую, например Ctrl+Alt+D."
                : "Windows не разрешила зарегистрировать горячую клавишу.");
        }
        _spec = spec;
    }
    public void Suspend() { _suspended = true; Unregister(); }
    public void Resume()
    {
        _suspended = false;
        var spec = _spec; _spec = default;
        Configure(spec == default ? HotkeyParser.Parse("Ctrl+Alt+Space") : spec);
    }
    public void BeginSession(bool pushToTalk)
    {
        EndSession(); _ptt = pushToTalk;
        _hookProc = HookCallback;
        _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _hookProc,
            NativeMethods.GetModuleHandle(null), 0);
        // RegisterHotKey still works if a security product denies the optional release hook.
    }
    public void EndSession()
    {
        if (_hook != IntPtr.Zero) NativeMethods.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero; _hookProc = null; _ptt = false;
    }
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == 0x4C56)
        { handled = true; Pressed?.Invoke(this, EventArgs.Empty); }
        return IntPtr.Zero;
    }
    private IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var data = Marshal.PtrToStructure<NativeMethods.KeyboardEvent>(lParam);
            var message = wParam.ToInt32();
            if (data.vkCode == 0x1B && message is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN)
            {
                _dispatcher.BeginInvoke(() => Escape?.Invoke(this, EventArgs.Empty));
                return new IntPtr(1); // Escape belongs to dictation only while the session is active.
            }
            if (_ptt && message is NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP && IsChordKey(data.vkCode))
                _dispatcher.BeginInvoke(() => Released?.Invoke(this, EventArgs.Empty));
        }
        return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
    }
    private bool IsChordKey(uint vk)
    {
        if (vk == _spec.VirtualKey) return true;
        return vk switch
        {
            0x10 or 0xA0 or 0xA1 => (_spec.Modifiers & HotkeySpec.Shift) != 0,
            0x11 or 0xA2 or 0xA3 => (_spec.Modifiers & HotkeySpec.Control) != 0,
            0x12 or 0xA4 or 0xA5 => (_spec.Modifiers & HotkeySpec.Alt) != 0,
            0x5B or 0x5C => (_spec.Modifiers & HotkeySpec.Win) != 0,
            _ => false
        };
    }
    private void Unregister() { if (_spec != default) NativeMethods.UnregisterHotKey(_window.Handle, 0x4C56); }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; EndSession(); Unregister(); _window.RemoveHook(WndProc); _window.Dispose();
    }
}
