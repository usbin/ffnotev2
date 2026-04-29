using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ffnotev2.Services;

public static class HotkeyModifiers
{
    public const uint Alt = 0x0001;
    public const uint Control = 0x0002;
    public const uint Shift = 0x0004;
    public const uint NoRepeat = 0x4000;
}

public class HotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private HwndSource? _source;
    private readonly Dictionary<int, Action> _hotkeys = [];
    private int _nextId = 9000;

    public void Initialize(Window window)
    {
        var helper = new WindowInteropHelper(window);
        _source = HwndSource.FromHwnd(helper.Handle);
        _source.AddHook(HwndHook);
    }

    public void Register(uint modifiers, uint vk, Action callback)
    {
        int id = _nextId++;
        if (RegisterHotKey(_source!.Handle, id, modifiers | HotkeyModifiers.NoRepeat, vk))
            _hotkeys[id] = callback;
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && _hotkeys.TryGetValue(wParam.ToInt32(), out var action))
        {
            action();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_source == null) return;
        foreach (var id in _hotkeys.Keys)
            UnregisterHotKey(_source.Handle, id);
        _source.RemoveHook(HwndHook);
    }
}
