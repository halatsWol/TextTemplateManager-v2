using System;
using System.Runtime.InteropServices;

namespace TextTemplateManager.Services.Updates;

/// <summary>What the app looks like when idle install considers switching to a ready update.</summary>
public readonly record struct IdleState(
    bool UpdateReady,
    bool Allowed,
    bool WindowVisible,
    bool QuickPasteVisible,
    bool UnsavedChanges,
    bool Busy,
    TimeSpan UserIdle,
    TimeSpan AppUptime);

/// <summary>Idle install: switch to a ready update only while nobody uses the app (window and Quick Paste closed,
/// nothing unsaved, no dialog or tour) and the PC has had no input for a while.</summary>
public static class IdleInstall
{
    public static readonly TimeSpan MinUserIdle = TimeSpan.FromMinutes(10);

    /// <summary>RegisterApplicationRestart only takes effect after the process ran this long.</summary>
    public static readonly TimeSpan MinUptime = TimeSpan.FromMinutes(2);

    public static readonly TimeSpan CheckEvery = TimeSpan.FromMinutes(1);

    public static bool ShouldInstall(IdleState s) =>
        s.UpdateReady && s.Allowed && !s.WindowVisible && !s.QuickPasteVisible && !s.UnsavedChanges && !s.Busy
        && s.UserIdle >= MinUserIdle && s.AppUptime >= MinUptime;

    /// <summary>Time since the last keyboard or mouse input in this session.</summary>
    public static TimeSpan UserIdleTime()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info)) return TimeSpan.Zero;
        return TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.dwTime));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
}
