using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace TextTemplateManager.Helpers
{
    public static class WindowHelper
    {
        public static IntPtr TargetWindowHandle { get; set; }

        public static void CaptureTargetContext() => TargetWindowHandle = GetForegroundWindow();

        public static IntPtr GetTargetWindow() => TargetWindowHandle;

        #region Win32 API Imports

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsZoomed(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern uint GetDpiForWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr SetActiveWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        // --- Restored for App.xaml.cs ---
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll")]
        public static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        #endregion

        #region Constants & Delegates

        public const int GWL_WNDPROC = -4;
        public delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        public const int SW_MAXIMIZE = 3;
        public const int SW_RESTORE = 9;
        public const int HWND_TOPMOST = -1;
        public const int HWND_NOTOPMOST = -2;
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_SHOWWINDOW = 0x0040;

        #endregion

        #region INPUT Structs
        [StructLayout(LayoutKind.Sequential)]
        internal struct INPUT { public uint type; public InputUnion u; }

        // MOUSEINPUT is the union's largest member and so sets sizeof(INPUT); without it SendInput
        // rejects the size and sends nothing.
        [StructLayout(LayoutKind.Explicit)]
        internal struct InputUnion { [FieldOffset(0)] public KEYBDINPUT ki; [FieldOffset(0)] public MOUSEINPUT mi; }

        [StructLayout(LayoutKind.Sequential)]
        internal struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
        #endregion

        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        public const ushort VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_LWIN = 0x5B, VK_RWIN = 0x5C, VK_V = 0x56;

        internal static INPUT[] KeyInputs((ushort Vk, bool Up)[] keys) =>
            Array.ConvertAll(keys, k => new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new InputUnion { ki = new KEYBDINPUT { wVk = k.Vk, dwFlags = k.Up ? KEYEVENTF_KEYUP : 0 } },
            });

        /// <summary>Sends the key presses and releases in one SendInput call, so no other input can land
        /// between them. False when Windows took fewer than all of them (e.g. blocked by UIPI).</summary>
        public static bool SendKeys(params (ushort Vk, bool Up)[] keys)
        {
            var inputs = KeyInputs(keys);
            uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
            if (sent != inputs.Length)
                System.Diagnostics.Debug.WriteLine($"[Input] SendInput sent {sent} of {inputs.Length}, error {Marshal.GetLastWin32Error()}");
            return sent == inputs.Length;
        }

        /// <summary>Releases Ctrl, Alt, Shift and both Windows keys.</summary>
        public static void ResetModifiers() =>
            SendKeys((VK_CONTROL, true), (VK_MENU, true), (VK_SHIFT, true), (VK_LWIN, true), (VK_RWIN, true));

        // ---- Minimum window size (WM_GETMINMAXINFO subclass) ----
        // WinUI 3 has no min-size API, so we subclass the top-level HWND and clamp the min
        // track size. Sizes are given in DIPs and scaled by the window's DPI. A single shared
        // hook services every subclassed window; per-window state (original proc + min dims)
        // is looked up by HWND so multiple windows can each have their own minimum.

        private const uint WM_GETMINMAXINFO = 0x0024;
        private const uint WM_SYSCHAR = 0x0106;
        private const uint WM_QUERYENDSESSION = 0x0011;
        private const uint WM_ENDSESSION = 0x0016;
        private static WndProcDelegate? _subclassHook;   // shared hook, kept alive to avoid GC
        private static readonly Dictionary<IntPtr, WindowHook> _hookedWindows = new();

        private sealed class WindowHook
        {
            public IntPtr Orig;
            public int MinW;
            public int MinH;
            public bool SuppressAltBeep;
            public Action? QueryEndSession;
            public Action? EndSession;
        }

        /// <summary>Windows sign-out/shutdown: <paramref name="queryEndSession"/> runs when Windows announces
        /// it (the session may still be cancelled), <paramref name="endSession"/> when it is final; the
        /// process is ended as soon as that one returns.</summary>
        public static void OnSessionEnd(IntPtr hWnd, Action queryEndSession, Action endSession)
        {
            var hook = EnsureHook(hWnd);
            hook.QueryEndSession = queryEndSession;
            hook.EndSession = endSession;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public int ptX, ptY;
        }

        [DllImport("user32.dll")] private static extern bool PeekMessage(out MSG msg, IntPtr hWnd, uint min, uint max, uint remove);
        [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG msg);
        [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref MSG msg);

        /// <summary>Keeps this thread's messages flowing until <paramref name="done"/> or the timeout. Used inside
        /// WM_ENDSESSION, where the UI thread must not return yet but the awaited saves still need it.</summary>
        public static void PumpMessagesUntil(Func<bool> done, TimeSpan timeout)
        {
            const uint PM_REMOVE = 0x0001;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (!done() && sw.Elapsed < timeout)
            {
                if (PeekMessage(out var msg, IntPtr.Zero, 0, 0, PM_REMOVE))
                {
                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);
                }
                else System.Threading.Thread.Sleep(10);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public int reservedX, reservedY;
            public int maxSizeX, maxSizeY;
            public int maxPositionX, maxPositionY;
            public int minTrackX, minTrackY;
            public int maxTrackX, maxTrackY;
        }

        private static WindowHook EnsureHook(IntPtr hWnd)
        {
            if (_hookedWindows.TryGetValue(hWnd, out var existing)) return existing;
            _subclassHook ??= WindowSubclassProc;
            IntPtr hookPtr = Marshal.GetFunctionPointerForDelegate(_subclassHook);
            IntPtr orig = SetWindowLongPtr(hWnd, GWL_WNDPROC, hookPtr);
            var hook = new WindowHook { Orig = orig };
            _hookedWindows[hWnd] = hook;
            return hook;
        }

        /// <summary>Enforces a minimum window size (in DIPs) by subclassing the window proc.</summary>
        public static void SetWindowMinSize(IntPtr hWnd, int minWidthDip, int minHeightDip)
        {
            var hook = EnsureHook(hWnd);
            hook.MinW = minWidthDip;
            hook.MinH = minHeightDip;
        }

        /// <summary>Swallows the "ding" Windows plays for an Alt+key that matches no menu mnemonic
        /// (WM_SYSCHAR). Enable only for windows with no menu bar, or Alt-mnemonics break.</summary>
        public static void SuppressAltMenuBeep(IntPtr hWnd, bool suppress = true)
        {
            EnsureHook(hWnd).SuppressAltBeep = suppress;
        }

        /// <summary>Finds the WinUI content-input child window (which actually receives keyboard
        /// WM_SYSCHAR — the top-level frame does not) and suppresses its Alt+key beep there.
        /// Returns the child HWND, or Zero if the content island isn't realized yet.</summary>
        public static IntPtr SuppressAltMenuBeepOnChild(IntPtr topHwnd)
        {
            IntPtr child = FindDescendantByClass(topHwnd, "InputSite");
            if (child != IntPtr.Zero) SuppressAltMenuBeep(child);
            return child;
        }

        private static IntPtr FindDescendantByClass(IntPtr parent, string classContains)
        {
            IntPtr result = IntPtr.Zero;
            EnumChildProc cb = (h, l) =>
            {
                var sb = new StringBuilder(256);
                GetClassName(h, sb, sb.Capacity);
                if (sb.ToString().IndexOf(classContains, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    result = h;
                    return false; // found it; stop enumerating
                }
                return true;
            };
            EnumChildWindows(parent, cb, IntPtr.Zero);
            return result;
        }

        private delegate bool EnumChildProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr hWndParent, EnumChildProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        /// <summary>Restores a window's original proc and drops its subclass. Call when the window
        /// closes so a recycled HWND doesn't keep a stale subclass.</summary>
        public static void RemoveWindowHook(IntPtr hWnd)
        {
            if (_hookedWindows.TryGetValue(hWnd, out var hook))
            {
                SetWindowLongPtr(hWnd, GWL_WNDPROC, hook.Orig); // harmless if the HWND is already gone
                _hookedWindows.Remove(hWnd);
            }
        }

        private static IntPtr WindowSubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (!_hookedWindows.TryGetValue(hWnd, out var hook))
                return DefWindowProc(hWnd, msg, wParam, lParam); // defensive; should never happen

            if (msg == WM_GETMINMAXINFO && lParam != IntPtr.Zero && (hook.MinW > 0 || hook.MinH > 0))
            {
                double scale = GetDpiForWindow(hWnd) / 96.0;
                if (scale <= 0) scale = 1.0;
                var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
                if (hook.MinW > 0) mmi.minTrackX = (int)(hook.MinW * scale);
                if (hook.MinH > 0) mmi.minTrackY = (int)(hook.MinH * scale);
                Marshal.StructureToPtr(mmi, lParam, false);
                return IntPtr.Zero;
            }

            // Eat the Alt+key menu-mnemonic beep for opted-in (menu-less) windows.
            if (msg == WM_SYSCHAR && hook.SuppressAltBeep)
                return IntPtr.Zero;

            if (msg == WM_QUERYENDSESSION && hook.QueryEndSession != null)
            {
                hook.QueryEndSession();
                return (IntPtr)1;   // don't block the sign-out
            }
            if (msg == WM_ENDSESSION && wParam != IntPtr.Zero && hook.EndSession != null)
            {
                hook.EndSession();
                return IntPtr.Zero;
            }

            return CallWindowProc(hook.Orig, hWnd, msg, wParam, lParam);
        }

        [DllImport("user32.dll")]
        private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        public static bool ForceWindowToFront(IntPtr hWnd)
        {
            // Only un-minimize. Do NOT SW_RESTORE unconditionally — that un-maximizes a
            // maximized target window (e.g. a maximized Firefox drops to a floating size).
            if (IsIconic(hWnd))
                ShowWindow(hWnd, SW_RESTORE);
            SetWindowPos(hWnd, (IntPtr)HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
            SetWindowPos(hWnd, (IntPtr)HWND_NOTOPMOST, 0, 0, 0, 0, SWP_SHOWWINDOW | SWP_NOMOVE | SWP_NOSIZE);
            bool result = SetForegroundWindow(hWnd);
            SetActiveWindow(hWnd);
            return result;
        }
    }
}