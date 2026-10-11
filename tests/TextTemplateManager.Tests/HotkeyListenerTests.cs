using System.Runtime.InteropServices;
using TextTemplateManager.Services.Platform;
using Xunit;

namespace TextTemplateManager.Tests;

/// <summary>A global hotkey another app already owns can't be registered; the listener has to say so,
/// otherwise Quick Paste silently never opens. Each listener gets its own hotkey messages, and disposing
/// one frees its hotkey and window. (Synchronous tests: a window belongs to the thread that made it.)</summary>
public class HotkeyListenerTests
{
    private const string Combo = "Ctrl+Shift+Alt+F9";
    private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, VK_F9 = 0x78;
    private const uint WM_HOTKEY = 0x0312;
    private const int HotkeyId = 9000;

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);

    [Fact]
    public void A_hotkey_owned_elsewhere_is_reported_and_free_ones_register()
    {
        using var listener = new HotkeyListener();
        Assert.True(listener.Register(Combo));
        Assert.True(listener.Register("None"));   // releases it again

        // Another owner (this thread, standing in for another app) takes the combination first.
        Assert.True(RegisterHotKey(IntPtr.Zero, 0xBEEF, MOD_CONTROL | MOD_SHIFT | MOD_ALT, VK_F9));
        try
        {
            Assert.False(listener.Register(Combo));
        }
        finally { UnregisterHotKey(IntPtr.Zero, 0xBEEF); }

        Assert.True(listener.Register(Combo));
        Assert.True(listener.Register(""));
    }

    [Fact]
    public void Each_listener_gets_only_its_own_hotkey_messages()
    {
        using var first = new HotkeyListener();
        using var second = new HotkeyListener();
        int firstCount = 0, secondCount = 0;
        first.HotkeyPressed += () => firstCount++;
        second.HotkeyPressed += () => secondCount++;

        SendMessage(second.Handle, WM_HOTKEY, HotkeyId, IntPtr.Zero);
        SendMessage(second.Handle, WM_HOTKEY, HotkeyId, IntPtr.Zero);
        SendMessage(first.Handle, WM_HOTKEY, HotkeyId, IntPtr.Zero);

        Assert.Equal(1, firstCount);
        Assert.Equal(2, secondCount);
    }

    [Fact]
    public void Disposing_frees_the_hotkey_and_destroys_the_window()
    {
        var listener = new HotkeyListener();
        IntPtr window = listener.Handle;
        Assert.True(listener.Register(Combo));

        listener.Dispose();
        listener.Dispose();   // twice is harmless

        Assert.False(IsWindow(window));
        using var next = new HotkeyListener();
        Assert.True(next.Register(Combo));
        Assert.True(next.Register("None"));
        Assert.Throws<ObjectDisposedException>(() => listener.Register(Combo));
    }
}
