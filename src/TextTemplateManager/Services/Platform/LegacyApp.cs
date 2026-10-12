using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace TextTemplateManager.Services.Platform;

/// <summary>A running TTM 1.x: it would hold the Quick Paste shortcut and the connector port. Told apart by
/// its install path, never by the process name alone.</summary>
public static class LegacyApp
{
    /// <summary>Where the 1.x installer puts the app (per user).</summary>
    public static string InstallRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Marflow Software");

    internal static bool IsUnder(string? exePath, string root)
    {
        if (string.IsNullOrEmpty(exePath)) return false;
        string full, dir;
        try { full = Path.GetFullPath(exePath); dir = Path.GetFullPath(root).TrimEnd('\\') + '\\'; }
        catch { return false; }
        return full.StartsWith(dir, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Closes every running 1.x of this user: first the way Windows ends it at sign-out (1.9.1 and
    /// later save and exit then), killed if it is still running after <paramref name="grace"/>. Returns how
    /// many were found.</summary>
    public static async Task<int> CloseRunningAsync(TimeSpan grace)
    {
        int found = 0;
        foreach (var process in Process.GetProcessesByName("ttm"))
        {
            using (process)
            {
                string? path;
                try { path = process.MainModule?.FileName; }
                catch { continue; }   // not ours to read (other user, elevated)
                if (process.Id == Environment.ProcessId || !IsUnder(path, InstallRoot)) continue;

                found++;
                await Task.Run(() => Close(process, grace));
            }
        }
        return found;
    }

    private static void Close(Process process, TimeSpan grace)
    {
        try
        {
            foreach (var hWnd in TopLevelWindows(process.Id))
            {
                SendMessageTimeout(hWnd, WM_QUERYENDSESSION, IntPtr.Zero, (IntPtr)ENDSESSION_CLOSEAPP, SMTO_ABORTIFHUNG, 2000, out _);
                SendMessageTimeout(hWnd, WM_ENDSESSION, (IntPtr)1, (IntPtr)ENDSESSION_CLOSEAPP, SMTO_ABORTIFHUNG, (uint)grace.TotalMilliseconds, out _);
            }
            if (process.WaitForExit(grace)) return;
            process.Kill();
            process.WaitForExit(TimeSpan.FromSeconds(3));
        }
        catch { /* already gone, or not ours to end */ }
    }

    private static List<IntPtr> TopLevelWindows(int processId)
    {
        var windows = new List<IntPtr>();
        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == processId) windows.Add(hWnd);
            return true;
        }, IntPtr.Zero);
        return windows;
    }

    private const uint WM_QUERYENDSESSION = 0x0011;
    private const uint WM_ENDSESSION = 0x0016;
    private const uint ENDSESSION_CLOSEAPP = 0x00000001;
    private const uint SMTO_ABORTIFHUNG = 0x0002;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
}
