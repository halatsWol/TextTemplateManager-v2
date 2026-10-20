using System;
using Microsoft.Win32;

namespace TextTemplateManager.Services.Updates;

/// <summary>What TTM's own update policy allows (HKLM\SOFTWARE\Policies\Marflow Software\TextTemplateManager,
/// DWORD InAppUpdates).</summary>
public enum InAppUpdatesMode
{
    /// <summary>0: TTM never checks for or offers updates.</summary>
    Off,
    /// <summary>1: TTM tells about an available update but doesn't download or install it.</summary>
    NoticeOnly,
    /// <summary>2 or not set: notice, download, Restart to update and idle install as the user set them.</summary>
    Full,
}

/// <summary>The policies that decide what the in-app updater may do: TTM's own keys and the Store's
/// "Turn off Automatic Download and Install of updates".</summary>
public sealed record UpdatePolicy(
    InAppUpdatesMode Mode,
    bool AllowDespiteStorePolicy,
    bool StoreBlocksAutomaticUpdates,
    bool? IdleInstallForced)
{
    public const string PolicyKey = @"SOFTWARE\Policies\Marflow Software\TextTemplateManager";
    public const string StorePolicyKey = @"SOFTWARE\Policies\Microsoft\WindowsStore";

    public static UpdatePolicy Default { get; } = new(InAppUpdatesMode.Full, true, false, null);

    /// <summary>The Store's automatic updates are off by policy, and the administrator lets the user keep TTM updating
    /// anyway (Settings ▸ General ▸ Updates).</summary>
    public bool OptInDespiteStorePolicyOffered => Mode == InAppUpdatesMode.Full && StoreBlocksAutomaticUpdates && AllowDespiteStorePolicy;

    /// <summary>True when an administrator set any of TTM's own update keys.</summary>
    public bool SetByOrganization => Mode != InAppUpdatesMode.Full || !AllowDespiteStorePolicy || IdleInstallForced != null;

    /// <summary>The policy from raw registry values; null = value not set.</summary>
    public static UpdatePolicy FromValues(int? inAppUpdates, int? allowDespiteStorePolicy, int? idleInstall, int? storeAutoDownload) => new(
        inAppUpdates switch { 0 => InAppUpdatesMode.Off, 1 => InAppUpdatesMode.NoticeOnly, _ => InAppUpdatesMode.Full },
        allowDespiteStorePolicy != 0,
        storeAutoDownload == 4,
        idleInstall switch { 0 => false, 1 => true, _ => null });

    /// <summary>Reads the policy from HKLM; anything unreadable counts as not set.</summary>
    public static UpdatePolicy Read()
    {
        try
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var ttm = hklm.OpenSubKey(PolicyKey);
            using var store = hklm.OpenSubKey(StorePolicyKey);
            return FromValues(Dword(ttm, "InAppUpdates"), Dword(ttm, "AllowUpdateDespiteStorePolicy"),
                Dword(ttm, "IdleInstall"), Dword(store, "AutoDownload"));
        }
        catch { return Default; }
    }

    private static int? Dword(RegistryKey? key, string name) => key?.GetValue(name) is int value ? value : null;
}
