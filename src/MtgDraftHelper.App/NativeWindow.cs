using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using MtgDraftHelper.Core.Settings;

namespace MtgDraftHelper.App;

/// <summary>Win32 funkce pro overlay: průchozí klikání, neaktivující okno, globální zkratky.</summary>
internal static class NativeWindow
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x20;
    private const int WsExNoActivate = 0x08000000;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    /// <summary>Kliknutí na overlay nebere fokus hře.</summary>
    public static void SetNoActivate(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        SetWindowLong(hwnd, GwlExStyle, GetWindowLong(hwnd, GwlExStyle) | WsExNoActivate);
    }

    /// <summary>Kliknutí projdou overlayem do okna pod ním (hry).</summary>
    public static void SetClickThrough(Window window, bool enabled)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var style = GetWindowLong(hwnd, GwlExStyle);
        style = enabled ? style | WsExTransparent : style & ~WsExTransparent;
        SetWindowLong(hwnd, GwlExStyle, style);
    }
}

/// <summary>Globální klávesové zkratky (fungují, i když má fokus Arena).</summary>
internal sealed class HotkeyManager : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModNoRepeat = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    private readonly IntPtr _hwnd;
    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _actions = [];
    private int _nextId = 0xB000;

    public HotkeyManager(Window window)
    {
        _hwnd = new WindowInteropHelper(window).EnsureHandle();
        _source = HwndSource.FromHwnd(_hwnd);
        _source.AddHook(WndProc);
    }

    /// <summary>Zaregistruje zkratku. Vrací false, pokud je neplatná nebo ji už používá jiná aplikace.</summary>
    public bool Register(string text, Action action)
    {
        if (!Hotkey.TryParse(text, out var hotkey) || ToKey(hotkey.Key) is not { } key)
            return false;

        var id = _nextId++;
        var vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        if (!RegisterHotKey(_hwnd, id, (uint)hotkey.Modifiers | ModNoRepeat, vk))
            return false;
        _actions[id] = action;
        return true;
    }

    public void UnregisterAll()
    {
        foreach (var id in _actions.Keys)
            UnregisterHotKey(_hwnd, id);
        _actions.Clear();
    }

    public static Key? ToKey(string name)
    {
        if (name.Length == 1 && char.IsDigit(name[0]))
            name = "D" + name; // číslice nad písmeny
        return Enum.TryParse<Key>(name, ignoreCase: true, out var key) && key != Key.None ? key : null;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && _actions.TryGetValue(wParam.ToInt32(), out var action))
        {
            action();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterAll();
        _source.RemoveHook(WndProc);
    }
}
