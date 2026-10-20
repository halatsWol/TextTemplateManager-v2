using System;
using System.Threading;
using System.Threading.Tasks;

namespace TextTemplateManager.Services.Updates;

public enum UpdateStage
{
    /// <summary>Not a Store install (dev run, sideloaded build): nothing to update from.</summary>
    Unavailable,
    /// <summary>InAppUpdates = 0.</summary>
    DisabledByPolicy,
    UpToDate,
    Checking,
    /// <summary>A newer version exists but isn't downloaded: waiting for the user's consent, or only a notice.</summary>
    Available,
    Downloading,
    /// <summary>Downloaded and staged: Restart to update switches to it.</summary>
    Ready,
    Installing,
    Failed,
    /// <summary>The switch failed <see cref="UpdateAttempt.MaxAttempts"/> times: only the manual steps are left.</summary>
    ManualStepsNeeded,
}

/// <param name="CanDownload">Download offers the user a download (the Store's consent dialog).</param>
/// <param name="StoreBlocked">Automatic Store updates are off on this PC (policy or the user's Store setting).</param>
public sealed record UpdateStatus(UpdateStage Stage, Version? Version = null, double Progress = 0, string? Message = null,
    bool CanDownload = false, bool StoreBlocked = false, DateTime? LastChecked = null)
{
    public bool IsReady => Stage == UpdateStage.Ready;
}

/// <summary>What a start makes of the previous switch attempt.</summary>
public sealed record StartupUpdateResult(AttemptOutcome Outcome, UpdateAttempt? Attempt);

/// <summary>The in-app updater: checks the source, downloads the update while the app keeps running, and switches to
/// it when asked (Restart to update) or when the app sits unused (idle install). No UI here: the app shows
/// <see cref="Status"/> and calls in. <see cref="Changed"/> may be raised on any thread.</summary>
public sealed class UpdateService : IDisposable
{
    public static readonly TimeSpan FirstCheckDelay = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    public const string UpdatedArgument = "--updated";
    /// <summary>After an idle install; the window opens with What's new like after <see cref="UpdatedArgument"/>.</summary>
    public const string UpdatedIdleArgument = "--updated-idle";

    private readonly IUpdateSource? _source;
    private readonly IPackageSwitcher? _switcher;
    private readonly UpdateLog _log;
    private readonly string _attemptPath;
    private readonly Func<UpdatePolicy> _readPolicy;
    private readonly SemaphoreSlim _busy = new(1, 1);
    private Timer? _timer;
    private UpdateOffer? _offer;
    private CancellationTokenSource? _downloadCancel;
    private DateTime? _lastChecked;

    /// <param name="source">Null when there is nothing to update from (dev run, sideloaded build).</param>
    public UpdateService(IUpdateSource? source, IPackageSwitcher? switcher, UpdateLog log, string attemptPath,
        Func<UpdatePolicy>? readPolicy = null)
    {
        _source = switcher == null ? null : source;
        _switcher = switcher;
        _log = log;
        _attemptPath = attemptPath;
        _readPolicy = readPolicy ?? UpdatePolicy.Read;
        Policy = _readPolicy();
        Status = _source == null ? new UpdateStatus(UpdateStage.Unavailable)
            : Policy.Mode == InAppUpdatesMode.Off ? new UpdateStatus(UpdateStage.DisabledByPolicy)
            : new UpdateStatus(UpdateStage.UpToDate);
    }

    public UpdatePolicy Policy { get; private set; }

    /// <summary>The user's "Check for and download updates automatically". Off: no check or download on its own, no idle
    /// install and no install on quit; Check for updates and Restart to update still work. On while automatic Store
    /// updates are off by policy: downloads around the Store policy (winget) where the administrator allows it.</summary>
    public bool CheckAutomatically { get; set; } = true;

    /// <summary><see cref="CheckAutomatically"/> unless the AutomaticUpdates policy decides.</summary>
    public bool ChecksAutomatically => Policy.AutomaticUpdatesForced ?? CheckAutomatically;

    private enum DownloadRoute { None, Silent, DespitePolicy, Consent }

    private DownloadRoute RouteFor(bool userInitiated, UpdateOffer offer) =>
        Policy.Mode != InAppUpdatesMode.Full ? DownloadRoute.None
        : _source!.CanDownloadSilently && !Policy.StoreBlocksAutomaticUpdates ? DownloadRoute.Silent
        : Policy.UpdatesDespiteStorePolicy && ChecksAutomatically ? DownloadRoute.DespitePolicy
        : userInitiated && AvailableStatus(offer).CanDownload ? DownloadRoute.Consent
        : DownloadRoute.None;
    public UpdateStatus Status { get; private set; }
    public event Action<UpdateStatus>? Changed;
    public UpdateLog Log => _log;

    /// <summary>The source's name for the log, or null without one.</summary>
    public string? SourceName => _source?.Name;

    /// <summary>Whether idle install applies: the policy decides when it sets IdleInstall, else the user's setting (and
    /// only with automatic updates on).</summary>
    public bool IdleInstallAllowed(bool userSetting) =>
        Policy.Mode == InAppUpdatesMode.Full && (Policy.IdleInstallForced ?? (userSetting && ChecksAutomatically));

    /// <summary>Quitting with a downloaded update installs it (on the way out) only with automatic updates on.</summary>
    public bool InstallOnQuit => Policy.Mode == InAppUpdatesMode.Full && ChecksAutomatically;

    /// <summary>Reads the policy again (an administrator may have changed it since the last check) and says whether a
    /// switch is still allowed; when it isn't, the status follows the policy: no Restart to update any more.</summary>
    public bool RefreshPolicyForSwitch()
    {
        Policy = _readPolicy();
        if (Policy.Mode == InAppUpdatesMode.Full) return true;
        if (Policy.Mode == InAppUpdatesMode.Off) Set(new UpdateStatus(UpdateStage.DisabledByPolicy));
        else if (Status.Stage is UpdateStage.Ready or UpdateStage.ManualStepsNeeded && Status.Version is { } v)
            Set(AvailableStatus(new UpdateOffer(v)));
        _log.Write($"switch refused: InAppUpdates policy is {Policy.Mode}");
        return false;
    }

    private void Set(UpdateStatus status)
    {
        Status = status with { LastChecked = _lastChecked };
        Changed?.Invoke(Status);
    }

    /// <summary>Reads what the previous run's switch left behind; call once at startup.</summary>
    public StartupUpdateResult EvaluateStartup()
    {
        var attempt = UpdateAttempt.Load(_attemptPath);
        if (attempt == null || _switcher == null) return new(AttemptOutcome.Unknown, null);
        var outcome = attempt.Evaluate(_switcher.RunningVersion);
        _log.Write($"start {_switcher.RunningVersion}: switch to {attempt.TargetVersion} (try {attempt.Attempts}) {outcome}");
        if (outcome is AttemptOutcome.Succeeded or AttemptOutcome.Unknown) UpdateAttempt.Clear(_attemptPath);
        return new(outcome, attempt);
    }

    /// <summary>Checks after <see cref="FirstCheckDelay"/> and then every <see cref="CheckInterval"/>.</summary>
    public void Start()
    {
        if (_source == null || Policy.Mode == InAppUpdatesMode.Off) return;
        _timer = new Timer(_ => _ = CheckAsync(userInitiated: false), null, FirstCheckDelay, CheckInterval);
    }

    /// <summary>Asks the source for a newer version; downloads it right away when allowed without asking.</summary>
    public async Task CheckAsync(bool userInitiated)
    {
        if (_source == null || _switcher == null) return;
        Policy = _readPolicy();
        if (!userInitiated && !ChecksAutomatically) return;
        if (Policy.Mode == InAppUpdatesMode.Off) { Set(new UpdateStatus(UpdateStage.DisabledByPolicy)); return; }
        if (!await _busy.WaitAsync(0)) return;   // a check, download or switch is running
        try
        {
            if (!userInitiated && Status.Stage is UpdateStage.Ready or UpdateStage.ManualStepsNeeded) return;
            var before = Status;
            if (userInitiated || before.Stage is UpdateStage.UpToDate or UpdateStage.Failed) Set(new UpdateStatus(UpdateStage.Checking));
            UpdateOffer? offer;
            try { offer = await _source.CheckAsync(CancellationToken.None); }
            catch (Exception ex)
            {
                _log.Write($"check ({_source.Name}) failed: {ex.Message}");
                Set(userInitiated ? new UpdateStatus(UpdateStage.Failed, Message: "Couldn't check for updates: " + ex.Message)
                    : before.Stage == UpdateStage.Checking ? new UpdateStatus(UpdateStage.UpToDate) : before);
                return;
            }
            _lastChecked = DateTime.Now;
            if (offer == null || offer.Version <= _switcher.RunningVersion)
            {
                _offer = null;
                Set(new UpdateStatus(UpdateStage.UpToDate));
                return;
            }
            bool newOffer = _offer?.Version != offer.Version;
            _offer = offer;
            if (newOffer) _log.Write($"check ({_source.Name}): {offer.Version} available, running {_switcher.RunningVersion}");

            var gaveUp = UpdateAttempt.Load(_attemptPath);
            if (gaveUp != null && gaveUp.TargetVersion == offer.Version.ToString() && gaveUp.Attempts >= UpdateAttempt.MaxAttempts)
            {
                Set(new UpdateStatus(UpdateStage.ManualStepsNeeded, offer.Version));
                return;
            }
            // Notice only (InAppUpdates = 1): an update Windows already staged is still only a notice, never Restart to update.
            if (_switcher.IsStaged(offer.Version) && Policy.Mode == InAppUpdatesMode.Full)
            {
                if (newOffer) _log.Write($"{offer.Version} is already staged");
                Set(new UpdateStatus(UpdateStage.Ready, offer.Version));
                return;
            }
            Set(AvailableStatus(offer));
        }
        finally { _busy.Release(); }

        if (Status.Stage == UpdateStage.Available && _offer is { } available && RouteFor(false, available) != DownloadRoute.None)
            await DownloadAsync(IntPtr.Zero, userInitiated: false);
    }

    private UpdateStatus AvailableStatus(UpdateOffer offer)
    {
        bool storeBlocked = Policy.StoreBlocksAutomaticUpdates || !_source!.CanDownloadSilently;
        bool canDownload = Policy.Mode == InAppUpdatesMode.Full && (!Policy.StoreBlocksAutomaticUpdates || Policy.AllowDespiteStorePolicy);
        return new UpdateStatus(UpdateStage.Available, offer.Version, CanDownload: canDownload, StoreBlocked: storeBlocked);
    }

    /// <summary>Downloads the offered version. Silently when the source allows it; otherwise, when the user clicked
    /// Download, through the source's consent dialog over <paramref name="ownerWindow"/>.</summary>
    public async Task DownloadAsync(IntPtr ownerWindow, bool userInitiated)
    {
        if (_source == null || _switcher == null || _offer is not { } offer) return;
        if (Policy.Mode != InAppUpdatesMode.Full) return;
        if (!await _busy.WaitAsync(0)) return;
        try
        {
            var route = RouteFor(userInitiated, offer);
            if (route == DownloadRoute.None) return;
            _downloadCancel = new CancellationTokenSource();
            Set(new UpdateStatus(UpdateStage.Downloading, offer.Version));
            var progress = new Progress(p => Set(new UpdateStatus(UpdateStage.Downloading, offer.Version, Math.Clamp(p, 0, 1))));
            _log.Write($"download {offer.Version} ({route switch { DownloadRoute.Silent => "silent", DownloadRoute.DespitePolicy => "despite the Store policy", _ => "with consent" }})");
            DownloadResult result;
            try
            {
                if (route == DownloadRoute.DespitePolicy)
                {
                    // winget may go on to install, which closes the app: then Windows starts it again.
                    try { _switcher.RegisterRestart(UpdatedArgument); } catch { }
                }
                result = route switch
                {
                    DownloadRoute.Silent => await _source.DownloadAsync(offer, progress, _downloadCancel.Token),
                    DownloadRoute.DespitePolicy => await _source.DownloadDespitePolicyAsync(offer, progress, _downloadCancel.Token),
                    _ => await _source.RequestDownloadAsync(offer, ownerWindow, progress, _downloadCancel.Token),
                };
            }
            catch (OperationCanceledException) { result = new DownloadResult(DownloadOutcome.Canceled); }
            catch (Exception ex) { result = new DownloadResult(DownloadOutcome.Failed, ex.Message); }
            _log.Write($"download {offer.Version}: {result.Outcome} {result.Detail}");

            if (result.Outcome == DownloadOutcome.Completed && _switcher.IsStaged(offer.Version))
                Set(new UpdateStatus(UpdateStage.Ready, offer.Version));
            else if (result.Outcome == DownloadOutcome.Completed)
                // The source says done but nothing is staged (the Store may still be finishing): look again at the next check.
                Set(AvailableStatus(offer) with { Message = "The download finished, but the update isn't ready yet. It's checked again later." });
            else if (result.Outcome == DownloadOutcome.Canceled)
                Set(AvailableStatus(offer));
            else if (result.Outcome == DownloadOutcome.Blocked)
                Set(AvailableStatus(offer) with { StoreBlocked = true });
            else
                Set(new UpdateStatus(UpdateStage.Failed, offer.Version, Message: "The update couldn't be downloaded: " + (result.Detail ?? "unknown error")));
        }
        finally
        {
            _downloadCancel = null;
            _busy.Release();
        }
    }

    public void CancelDownload() => _downloadCancel?.Cancel();

    /// <summary>Switches to the staged version. The app must have saved everything before: a successful switch ends
    /// the process (Windows closes it and, with <paramref name="restart"/>, starts the new version). Returns the reason
    /// when it didn't happen. Without <paramref name="restart"/> (quitting with an update ready) the new version starts
    /// with the next launch.</summary>
    public async Task<string?> SwitchAsync(bool idle, bool restart = true)
    {
        if (_switcher == null || Status.Stage != UpdateStage.Ready || Status.Version is not { } target) return "No update is ready.";
        if (!RefreshPolicyForSwitch()) return "Updates on this PC are managed by your organization.";
        if (!await _busy.WaitAsync(0)) return "An update step is already running.";
        try
        {
            if (!_switcher.IsStaged(target))
            {
                _log.Write($"switch {target}: not staged any more");
                _offer = null;
                Set(new UpdateStatus(UpdateStage.UpToDate));
                return "The downloaded update is gone (Windows may have removed it). It's downloaded again at the next check.";
            }
            var previous = UpdateAttempt.Load(_attemptPath);
            int tries = previous?.TargetVersion == target.ToString() ? previous.Attempts + 1 : 1;
            new UpdateAttempt(target.ToString(), _switcher.RunningVersion.ToString(), idle, DateTime.Now, tries).Save(_attemptPath);
            _log.Write($"switch to {target} ({(!restart ? "on exit" : idle ? "idle" : "user")}, try {tries})");
            Set(new UpdateStatus(UpdateStage.Installing, target));

            if (restart)
            {
                _switcher.RegisterRestart(idle ? UpdatedIdleArgument : UpdatedArgument);
                // Windows' own restart came 10-110 s after the switch on the test VM: the watchdog mustn't race it.
                try { _switcher.StartWatchdog(TimeSpan.FromSeconds(120), _log.Path); }
                catch (Exception ex) { _log.Write($"watchdog not set: {ex.Message}"); }
            }
            else _switcher.UnregisterRestart();
            string? problem = await _switcher.SwitchAsync();

            // Still running: Windows didn't switch.
            _log.Write($"switch to {target} failed: {problem}");
            if (tries >= UpdateAttempt.MaxAttempts)
                Set(new UpdateStatus(UpdateStage.ManualStepsNeeded, target, Message: problem));
            else
                Set(new UpdateStatus(UpdateStage.Ready, target, Message: "The last try to install it didn't work: " + problem));
            return problem ?? "Windows didn't switch to the new version.";
        }
        finally { _busy.Release(); }
    }

    /// <summary>A previous run's switch failed: show the update as ready again, or the manual steps after too many tries.</summary>
    public void ReportFailedSwitch(UpdateAttempt attempt)
    {
        if (!Version.TryParse(attempt.TargetVersion, out var target)) return;
        _offer = new UpdateOffer(target);
        if (attempt.Attempts >= UpdateAttempt.MaxAttempts) Set(new UpdateStatus(UpdateStage.ManualStepsNeeded, target));
        else if (_switcher?.IsStaged(target) == true && Policy.Mode == InAppUpdatesMode.Full)
            Set(new UpdateStatus(UpdateStage.Ready, target, Message: "The last try to install it didn't work."));
        else Set(AvailableStatus(_offer) with { Message = "The last try to install it didn't work." });
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _downloadCancel?.Cancel();
    }

    private sealed class Progress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
