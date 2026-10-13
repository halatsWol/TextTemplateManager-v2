using System;
using System.Threading.Tasks;
using Microsoft.Win32;
using TextTemplateManager.Models;

namespace TextTemplateManager.Services.Platform;

/// <summary>How 1.x started with Windows: its Run value, switched off in Windows Settings › Apps › Startup (or by
/// its own toggle) through the StartupApproved record, and started hidden when the Run command carries --hidden.</summary>
public readonly record struct LegacyAutostartState(bool On, bool Hidden);

public static class LegacyAutostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName = "TextTemplateManager";

    /// <summary>The 1.x state from the registry (read only), or null when 1.x has no Run value: never set up, or
    /// already uninstalled, so nothing is known.</summary>
    public static LegacyAutostartState? Read()
    {
        try
        {
            using var run = Registry.CurrentUser.OpenSubKey(RunKey);
            using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey);
            return Parse(run?.GetValue(ValueName) as string, approved?.GetValue(ValueName) as byte[]);
        }
        catch { return null; }
    }

    // A record whose first byte has the low bit set (0x03) is switched off; no record counts as on.
    internal static LegacyAutostartState? Parse(string? runCommand, byte[]? approved) =>
        runCommand == null ? null : new(
            On: approved is not { Length: >= 1 } || (approved[0] & 1) == 0,
            Hidden: runCommand.Contains("--hidden", StringComparison.OrdinalIgnoreCase));

    /// <summary>Takes over the 1.x choice: start with Windows on or off, and start hidden. Nothing changes when
    /// nothing is known.</summary>
    public static async Task CarryOverAsync(LegacyAutostartState? legacy, IAppEnvironment environment, AppSettings settings)
    {
        if (legacy is not { } state) return;
        settings.StartHidden = state.Hidden;
        await environment.SetAutostartAsync(state.On);
    }
}
