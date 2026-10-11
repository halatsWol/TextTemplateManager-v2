using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Windows.System;

namespace TextTemplateManager.Services.Platform;

/// <summary>The global hotkey, received by a message-only window. Create and dispose it on the same thread
/// (a window belongs to the thread that created it).</summary>
public class HotkeyListener : IDisposable
{
    private IntPtr _hwnd;
    private const int HOTKEY_ID = 9000;

    // Win32 Constants
    private const int WM_HOTKEY = 0x0312;
    private const int ERROR_CLASS_ALREADY_EXISTS = 1410;
    private const string WindowClassName = "TTM_HotkeyMessageWindow";

    // The window class is registered once, with this one procedure; it finds the listener by window
    // handle. Static, so the delegate lives as long as the class and no listener's window can call a
    // collected or another listener's procedure.
    private static readonly WndProc s_wndProc = StaticWndProc;
    private static readonly ConcurrentDictionary<IntPtr, HotkeyListener> s_listeners = new();
    private static readonly Lazy<bool> s_classRegistered = new(RegisterWindowClass);

    public event Action? HotkeyPressed;

    /// <summary>The message window; exposed for tests.</summary>
    internal IntPtr Handle => _hwnd;

    public HotkeyListener()
    {
        _ = s_classRegistered.Value;
        // HWND_MESSAGE (-3) makes it a message-only window
        _hwnd = CreateWindowEx(0, WindowClassName, "TTM_Internal", 0, 0, 0, 0, 0, (IntPtr)(-3), IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create the hotkey window.");
        s_listeners[_hwnd] = this;
    }

    private static bool RegisterWindowClass()
    {
        var wc = new WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = s_wndProc,
            lpszClassName = WindowClassName,
        };
        if (RegisterClassEx(ref wc) == 0 && Marshal.GetLastWin32Error() is int error && error != ERROR_CLASS_ALREADY_EXISTS)
            throw new Win32Exception(error, "Could not register the hotkey window class.");
        return true;
    }

    private static IntPtr StaticWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_HOTKEY && (int)wParam == HOTKEY_ID && s_listeners.TryGetValue(hWnd, out var listener))
        {
            listener.HotkeyPressed?.Invoke();
            return IntPtr.Zero;
        }
        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    /// <summary>Registers the hotkey, replacing the previous one. False when Windows refuses it, usually
    /// because another app already owns the combination. No hotkey ("None"/empty) counts as success.</summary>
    public bool Register(string hotkeyStr)
    {
        ObjectDisposedException.ThrowIf(_hwnd == IntPtr.Zero, this);
        UnregisterHotKey(_hwnd, HOTKEY_ID);

        uint modifiers = 0;
        uint vk = 0;

        var parts = hotkeyStr.Split('+');
        foreach (var part in parts)
        {
            switch (part.Trim())
            {
                case "Ctrl": modifiers |= 0x0002; break;
                case "Alt": modifiers |= 0x0001; break;
                case "Shift": modifiers |= 0x0004; break;
                case "Win": modifiers |= 0x0008; break;
                default:
                    if (Enum.TryParse<VirtualKey>(part, out var key))
                        vk = (uint)key;
                    break;
            }
        }

        return vk == 0 || RegisterHotKey(_hwnd, HOTKEY_ID, modifiers, vk);
    }

    #region Win32 Imports
    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx([In] ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public int cbSize;
        public int style;
        public WndProc lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    #endregion

    public void Dispose()
    {
        if (_hwnd == IntPtr.Zero) return;
        UnregisterHotKey(_hwnd, HOTKEY_ID);
        s_listeners.TryRemove(_hwnd, out _);
        DestroyWindow(_hwnd);
        _hwnd = IntPtr.Zero;
    }
}
