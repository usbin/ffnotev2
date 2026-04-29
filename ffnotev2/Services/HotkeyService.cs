using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ffnotev2.Services;

[Flags]
public enum HotkeyModifiers : uint
{
    None = 0x0000,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Win = 0x0008,
    NoRepeat = 0x4000
}

public partial class HotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly Dictionary<int, Action> _callbacks = new();
    private HwndSource? _source;
    private IntPtr _hwnd;
    private int _nextId = 1;

    /// <summary>OnSourceInitialized 시점에서만 호출 가능.</summary>
    public void Initialize(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var helper = new WindowInteropHelper(window);
        _hwnd = helper.Handle;
        if (_hwnd == IntPtr.Zero)
            throw new InvalidOperationException("HotkeyService.Initialize는 OnSourceInitialized 이후에만 호출하세요.");

        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(HwndHook);
    }

    public bool Register(HotkeyModifiers modifiers, uint virtualKey, Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (_hwnd == IntPtr.Zero)
            throw new InvalidOperationException("Initialize()를 먼저 호출하세요.");

        var id = _nextId++;
        if (!RegisterHotKey(_hwnd, id, (uint)modifiers, virtualKey))
            return false;
        _callbacks[id] = callback;
        return true;
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && _callbacks.TryGetValue(wParam.ToInt32(), out var action))
        {
            action();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void UnregisterAll()
    {
        if (_hwnd != IntPtr.Zero)
        {
            foreach (var id in _callbacks.Keys)
                UnregisterHotKey(_hwnd, id);
        }
        _callbacks.Clear();
        _nextId = 1;
    }

    public void Dispose()
    {
        UnregisterAll();
        _source?.RemoveHook(HwndHook);
        _source = null;
        GC.SuppressFinalize(this);
    }
}
