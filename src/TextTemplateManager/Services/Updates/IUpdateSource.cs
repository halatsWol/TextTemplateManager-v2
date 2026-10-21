using System;
using System.Threading;
using System.Threading.Tasks;

namespace TextTemplateManager.Services.Updates;

/// <summary>A newer version the source offers. <see cref="Version"/> is null when the source only knows that there is
/// one: the Store's update check describes the installed package, not the new one.</summary>
public sealed record UpdateOffer(Version? Version);

public enum DownloadOutcome { Completed, Canceled, Blocked, Failed }

public sealed record DownloadResult(DownloadOutcome Outcome, string? Detail = null)
{
    public static DownloadResult Done { get; } = new(DownloadOutcome.Completed);
}

/// <summary>Where updates come from: the Microsoft Store, or for tests a folder of packages
/// (<see cref="FileUpdateSource"/>). Downloading only stages the new version; switching to it is
/// <see cref="IPackageSwitcher"/>'s job.</summary>
public interface IUpdateSource
{
    /// <summary>A short name for the update log.</summary>
    string Name { get; }

    /// <summary>The newest version offered above the running one, or null when up to date.</summary>
    Task<UpdateOffer?> CheckAsync(CancellationToken cancel);

    /// <summary>Set by <see cref="CheckAsync"/> when it got nothing because the source refused to check (the Store's
    /// check limits): the earliest time (UTC) it checks again. Null when it checked, or when that isn't known.</summary>
    DateTime? RefusedUntil { get; }

    /// <summary>Whether an offer without a version is downloaded and staged (offers with one are checked by
    /// <see cref="IPackageSwitcher.IsStaged"/>).</summary>
    Task<bool> IsDownloadedAsync(UpdateOffer offer);

    /// <summary>False when the source can't download without the user's consent (for the Store: automatic
    /// updates turned off by policy or by the user).</summary>
    bool CanDownloadSilently { get; }

    /// <summary>Downloads and stages <paramref name="offer"/> without asking; progress 0..1.</summary>
    Task<DownloadResult> DownloadAsync(UpdateOffer offer, IProgress<double> progress, CancellationToken cancel);

    /// <summary>Downloads after the user agreed in the source's own dialog (the Store's consent dialog), shown over
    /// the window <paramref name="ownerWindow"/>; progress 0..1.</summary>
    Task<DownloadResult> RequestDownloadAsync(UpdateOffer offer, IntPtr ownerWindow, IProgress<double> progress, CancellationToken cancel);
}

/// <summary>Switches the installed app to a staged version.</summary>
public interface IPackageSwitcher
{
    /// <summary>The running package's version.</summary>
    Version RunningVersion { get; }

    /// <summary>True when <paramref name="version"/> of this app is staged on the machine (downloaded, not yet
    /// switched to).</summary>
    bool IsStaged(Version version);

    /// <summary>The newest version above the running one that is staged on the machine, whoever staged it (the Store,
    /// Windows Update); null when there is none.</summary>
    Version? FindStaged();

    /// <summary>Asks Windows to restart the app with <paramref name="arguments"/> after it closes it for the switch.</summary>
    void RegisterRestart(string arguments);

    /// <summary>Takes back <see cref="RegisterRestart"/> (quitting with an update: no restart wanted).</summary>
    void UnregisterRestart();

    /// <summary>Starts a small process outside the app that starts it again through the shell if it isn't running
    /// <paramref name="after"/> the switch (in case Windows doesn't restart it). The shell start also applies a
    /// switch that is still waiting. It notes what it did in the update log at <paramref name="logPath"/>.</summary>
    void StartWatchdog(TimeSpan after, string logPath);

    /// <summary>Registers the staged version; Windows closes the app for it. Returns only when the switch didn't
    /// happen (with the reason), as a running app is ended by a successful one.</summary>
    Task<string?> SwitchAsync();
}
