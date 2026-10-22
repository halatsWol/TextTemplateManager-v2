using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Services.Store;

namespace TextTemplateManager.Services.Updates;

/// <summary>Updates from the Microsoft Store (StoreContext). Downloading stages the new version without installing it,
/// so the app keeps running; the switch is <see cref="PackageSwitcher"/>'s.
/// The Store never names the new version: an update's Package is the installed one. Whether it's downloaded comes from
/// the Store's queue item for the app, which says PausedReadyToInstall once it's staged (seen on 2.2.0 to 2.2.1, 2026-10-21).
/// A version staged outside the queue (Windows Update) is found by <see cref="IPackageSwitcher.FindStaged"/>.</summary>
public sealed class StoreUpdateSource : IUpdateSource
{
    public static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan QueuePollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PausedTimeout = TimeSpan.FromMinutes(2);

    private readonly StoreContext _store = StoreContext.GetDefault();
    private IReadOnlyList<StorePackageUpdate> _updates = [];
    private bool _downloaded;

    public string Name => "Store";

    public string DisplayName => "Microsoft Store";

    public bool CanDownloadSilently
    {
        get
        {
            try { return _store.CanSilentlyDownloadStorePackageUpdates; }
            catch { return false; }
        }
    }

    public DateTime? RefusedUntil { get; private set; }

    public async Task<UpdateOffer?> CheckAsync(CancellationToken cancel)
    {
        // Read the queue first: the update check puts the queue item back to Paused, though the package stays staged.
        if (await QueueStateAsync() == StoreQueueItemExtendedState.PausedReadyToInstall) _downloaded = true;
        // The Store logs a refusal at the start of the check; a little slack for the clock.
        var started = DateTime.UtcNow.AddSeconds(-5);
        _updates = await _store.GetAppAndOptionalStorePackageUpdatesAsync().AsTask(cancel);
        RefusedUntil = null;
        if (_updates.Count == 0)
        {
            _downloaded = false;
            RefusedUntil = StoreScanLog.FindRefusal(Windows.ApplicationModel.Package.Current.Id.FamilyName, started, DateTime.UtcNow);
        }
        return _updates.Count > 0 || _downloaded ? new UpdateOffer(null) : null;
    }

    public async Task<bool> IsDownloadedAsync(UpdateOffer offer) =>
        _downloaded || await QueueStateAsync() == StoreQueueItemExtendedState.PausedReadyToInstall;

    public async Task<DownloadResult> DownloadAsync(UpdateOffer offer, IProgress<double> progress, CancellationToken cancel)
    {
        if (_updates.Count == 0) return new DownloadResult(DownloadOutcome.Failed, "no update from the Store");
        var result = await _store.TrySilentDownloadStorePackageUpdatesAsync(_updates).AsTask(cancel, Report(progress));
        return result.OverallState == StorePackageUpdateState.Completed ? await WaitForQueueAsync(progress, cancel) : Map(result);
    }

    public async Task<DownloadResult> RequestDownloadAsync(UpdateOffer offer, IntPtr ownerWindow, IProgress<double> progress, CancellationToken cancel)
    {
        if (_updates.Count == 0) return new DownloadResult(DownloadOutcome.Failed, "no update from the Store");
        if (ownerWindow != IntPtr.Zero) WinRT.Interop.InitializeWithWindow.Initialize(_store, ownerWindow);
        var result = await _store.RequestDownloadStorePackageUpdatesAsync(_updates).AsTask(cancel, Report(progress));
        return result.OverallState == StorePackageUpdateState.Completed ? await WaitForQueueAsync(progress, cancel) : Map(result);
    }

    private async Task<StoreQueueItemStatus?> QueueStatusAsync()
    {
        try
        {
            var items = await _store.GetAssociatedStoreQueueItemsAsync();
            return (items.FirstOrDefault(i => i.InstallKind == StoreQueueItemKind.Update) ?? items.FirstOrDefault())?.GetCurrentStatus();
        }
        catch { return null; }
    }

    private async Task<StoreQueueItemExtendedState?> QueueStateAsync() => (await QueueStatusAsync())?.PackageInstallExtendedState;

    /// <summary>The download call reports Completed as soon as the Store has queued the download (after about a second,
    /// with nothing downloaded yet): the queue item tells when it's really staged.</summary>
    private async Task<DownloadResult> WaitForQueueAsync(IProgress<double> progress, CancellationToken cancel)
    {
        var until = DateTime.UtcNow + DownloadTimeout;
        DateTime? pausedSince = null;
        while (DateTime.UtcNow < until)
        {
            var status = await QueueStatusAsync();
            // With automatic Store updates off the item can sit in Paused (BlockedOnUser) for good, seen 2026-10-21.
            if (status?.PackageInstallExtendedState == StoreQueueItemExtendedState.Paused)
            {
                pausedSince ??= DateTime.UtcNow;
                if (DateTime.UtcNow - pausedSince > PausedTimeout)
                    return new DownloadResult(DownloadOutcome.Completed, "the Store's queue item stays paused");
            }
            else pausedSince = null;
            switch (status?.PackageInstallExtendedState)
            {
                case StoreQueueItemExtendedState.PausedReadyToInstall or StoreQueueItemExtendedState.PausedPackagesInUse:
                    _downloaded = true;
                    return DownloadResult.Done;
                case StoreQueueItemExtendedState.Completed:
                    return DownloadResult.Done;
                case StoreQueueItemExtendedState.Canceled:
                    return new DownloadResult(DownloadOutcome.Canceled);
                case StoreQueueItemExtendedState.Error:
                    return status!.ExtendedError is { } error
                        ? new DownloadResult(DownloadOutcome.Failed, "queue item error " + UpdateError.Describe(error), UpdateError.Code(error))
                        : new DownloadResult(DownloadOutcome.Failed, "queue item error without details", "Error");
                case StoreQueueItemExtendedState.PausedLowBattery:
                    return new DownloadResult(DownloadOutcome.Failed, "battery too low");
                case StoreQueueItemExtendedState.PausedWiFiRequired or StoreQueueItemExtendedState.PausedWiFiRecommended:
                    return new DownloadResult(DownloadOutcome.Failed, "waiting for a Wi-Fi or unmetered connection");
                case null:
                    return new DownloadResult(DownloadOutcome.Completed, "the Store has no queue item for the update");
            }
            if (status!.UpdateStatus.PackageDownloadProgress is var p and > 0) progress.Report(p);
            await Task.Delay(QueuePollInterval, cancel);
        }
        return new DownloadResult(DownloadOutcome.Failed, $"the Store didn't finish the download in {DownloadTimeout.TotalMinutes:0} minutes");
    }

    private static IProgress<StorePackageUpdateStatus> Report(IProgress<double> progress) =>
        new Progress<StorePackageUpdateStatus>(s => progress.Report(s.PackageDownloadProgress));

    private static DownloadResult Map(StorePackageUpdateResult result) => result.OverallState switch
    {
        StorePackageUpdateState.Completed => DownloadResult.Done,
        StorePackageUpdateState.Canceled => new DownloadResult(DownloadOutcome.Canceled),
        StorePackageUpdateState.ErrorWiFiRequired or StorePackageUpdateState.ErrorWiFiRecommended =>
            new DownloadResult(DownloadOutcome.Failed, "waiting for a Wi-Fi or unmetered connection"),
        StorePackageUpdateState.ErrorLowBattery => new DownloadResult(DownloadOutcome.Failed, "battery too low"),
        _ => new DownloadResult(DownloadOutcome.Failed, $"download call ended in state {result.OverallState}", result.OverallState.ToString()),
    };
}
