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
/// the Store's queue item for the app, which says PausedReadyToInstall once it's staged (seen on 2.2.0 → 2.2.1, 2026-10-21).</summary>
public sealed class StoreUpdateSource : IUpdateSource
{
    public static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan QueuePollInterval = TimeSpan.FromSeconds(2);

    private readonly StoreContext _store = StoreContext.GetDefault();
    private IReadOnlyList<StorePackageUpdate> _updates = [];
    private bool _downloaded;

    public string Name => "Store";

    public bool CanDownloadSilently
    {
        get
        {
            try { return _store.CanSilentlyDownloadStorePackageUpdates; }
            catch { return false; }
        }
    }

    public async Task<UpdateOffer?> CheckAsync(CancellationToken cancel)
    {
        // Read the queue first: the update check puts the queue item back to Paused, though the package stays staged.
        if (await QueueStateAsync() == StoreQueueItemExtendedState.PausedReadyToInstall) _downloaded = true;
        _updates = await _store.GetAppAndOptionalStorePackageUpdatesAsync().AsTask(cancel);
        if (_updates.Count == 0) _downloaded = false;
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
        while (DateTime.UtcNow < until)
        {
            var status = await QueueStatusAsync();
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
                    return new DownloadResult(DownloadOutcome.Failed, $"Store: 0x{status!.ExtendedError?.HResult ?? 0:X8}");
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

    /// <summary>winget's msstore source: a user-started install through the Store's install service, which the "no
    /// automatic updates" policy doesn't block, and needs no Microsoft account. It downloads and stages the update; its
    /// install step fails while the app runs (the switch is then the app's own), or, if it closes the app, Windows
    /// restarts it. Not tested against a real Store release yet.</summary>
    public async Task<DownloadResult> DownloadDespitePolicyAsync(UpdateOffer offer, IProgress<double> progress, CancellationToken cancel)
    {
        string alias = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WindowsApps\winget.exe");
        var start = new System.Diagnostics.ProcessStartInfo(System.IO.File.Exists(alias) ? alias : "winget.exe",
            "upgrade --id 9NMBSZLLLN6V --source msstore --accept-source-agreements --accept-package-agreements --silent --disable-interactivity")
        { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        System.Diagnostics.Process? process;
        try { process = System.Diagnostics.Process.Start(start); }
        catch (Exception ex) { return new DownloadResult(DownloadOutcome.Failed, "winget isn't available: " + ex.Message); }
        if (process == null) return new DownloadResult(DownloadOutcome.Failed, "winget didn't start");
        using (process)
        {
            progress.Report(0);   // winget's own progress isn't read: indeterminate
            var output = process.StandardOutput.ReadToEndAsync(cancel);
            _ = process.StandardError.ReadToEndAsync(cancel);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            timeout.CancelAfter(TimeSpan.FromMinutes(30));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                if (cancel.IsCancellationRequested) throw;
                return new DownloadResult(DownloadOutcome.Failed, "winget took longer than 30 minutes");
            }
            string last = (await output).Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).LastOrDefault(l => l.Length > 0) ?? "";
            // The app checks afterwards whether the update is staged; winget's exit code says little while the app runs.
            if (await QueueStateAsync() is StoreQueueItemExtendedState.PausedReadyToInstall or StoreQueueItemExtendedState.PausedPackagesInUse)
                _downloaded = true;
            return new DownloadResult(DownloadOutcome.Completed, $"winget exit 0x{process.ExitCode:X8}: {last}");
        }
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
        _ => new DownloadResult(DownloadOutcome.Failed, $"Store: {result.OverallState}"),
    };
}
