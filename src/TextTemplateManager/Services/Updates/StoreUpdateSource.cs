using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Services.Store;

namespace TextTemplateManager.Services.Updates;

/// <summary>Updates from the Microsoft Store (StoreContext). Downloading stages the new version without installing it,
/// so the app keeps running; the switch is <see cref="PackageSwitcher"/>'s.</summary>
public sealed class StoreUpdateSource : IUpdateSource
{
    private readonly StoreContext _store = StoreContext.GetDefault();
    private IReadOnlyList<StorePackageUpdate> _updates = [];

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
        _updates = await _store.GetAppAndOptionalStorePackageUpdatesAsync().AsTask(cancel);
        var newest = _updates.Select(u => PackageSwitcher.ToVersion(u.Package.Id.Version)).DefaultIfEmpty().Max();
        return newest == null ? null : new UpdateOffer(newest);
    }

    public async Task<DownloadResult> DownloadAsync(UpdateOffer offer, IProgress<double> progress, CancellationToken cancel)
    {
        if (_updates.Count == 0) return new DownloadResult(DownloadOutcome.Failed, "no update from the Store");
        var result = await _store.TrySilentDownloadStorePackageUpdatesAsync(_updates).AsTask(cancel, Report(progress));
        return Map(result);
    }

    public async Task<DownloadResult> RequestDownloadAsync(UpdateOffer offer, IntPtr ownerWindow, IProgress<double> progress, CancellationToken cancel)
    {
        if (_updates.Count == 0) return new DownloadResult(DownloadOutcome.Failed, "no update from the Store");
        if (ownerWindow != IntPtr.Zero) WinRT.Interop.InitializeWithWindow.Initialize(_store, ownerWindow);
        var result = await _store.RequestDownloadStorePackageUpdatesAsync(_updates).AsTask(cancel, Report(progress));
        return Map(result);
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
