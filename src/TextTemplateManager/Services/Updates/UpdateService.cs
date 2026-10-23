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
    private readonly UpdateCheckHistory _checks;
    private DateTime? _refusedUntil;

    /// <summary>The clock for the check history (UTC); tests set their own.</summary>
    internal Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

    /// <param name="source">Null when there is nothing to update from (dev run, sideloaded build).</param>
    public UpdateService(IUpdateSource? source, IPackageSwitcher? switcher, UpdateLog log, string attemptPath,
        Func<UpdatePolicy>? readPolicy = null)
    {
        _source = switcher == null ? null : source;
        _switcher = switcher;
        _log = log;
        _attemptPath = attemptPath;
        _checks = new UpdateCheckHistory(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(attemptPath) ?? "", "update-checks.txt"));
        _lastChecked = _checks.Last?.ToLocalTime();
        _readPolicy = readPolicy ?? UpdatePolicy.Read;
        Policy = _readPolicy();
        Status = _source == null ? new UpdateStatus(UpdateStage.Unavailable)
            : Policy.Mode == InAppUpdatesMode.Off ? new UpdateStatus(UpdateStage.DisabledByPolicy)
            : new UpdateStatus(UpdateStage.UpToDate, LastChecked: _lastChecked);
    }

    public UpdatePolicy Policy { get; private set; }

    /// <summary>The user's "Check for and download updates automatically". Off: no check or download on its own, no idle
    /// install and no install on quit; Check for updates and Restart to update still work. While automatic Store
    /// updates are off, on means downloading through the Store's request route on its own (where the administrator
    /// allows it), as the user's Download would.</summary>
    public bool CheckAutomatically { get; set; } = true;

    /// <summary><see cref="CheckAutomatically"/> unless the AutomaticUpdates policy decides.</summary>
    public bool ChecksAutomatically => Policy.AutomaticUpdatesForced ?? CheckAutomatically;

    /// <summary>The window a Store dialog belongs to when a download starts on its own; the app sets it.</summary>
    public Func<IntPtr> OwnerWindow { get; set; } = () => IntPtr.Zero;

    /// <summary>Added when the Store reports a download done that doesn't get staged: on 2026-10-21 Windows Update staged
    /// the update on a PC with automatic Store updates off and without the Store app.</summary>
    public const string WindowsUpdateHint = "Windows Update usually brings it on its own; Restart to update appears as soon as it's downloaded.";

    private enum DownloadRoute { None, Silent, Consent }

    // Under the Store policy the request route resumed the Store's waiting download without a dialog and without the
    // Store app (2026-10-22, AutoDownload = 4), so automatic updates use it too.
    private DownloadRoute RouteFor(bool userInitiated, UpdateOffer offer) =>
        Policy.Mode != InAppUpdatesMode.Full ? DownloadRoute.None
        : _source!.CanDownloadSilently && !Policy.StoreBlocksAutomaticUpdates ? DownloadRoute.Silent
        : (userInitiated || ChecksAutomatically) && AvailableStatus(offer).CanDownload ? DownloadRoute.Consent
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
        else if (Status.Stage is UpdateStage.Ready or UpdateStage.ManualStepsNeeded)
            Set(AvailableStatus(_offer ?? new UpdateOffer(Status.Version)));
        _log.Write($"switch refused: InAppUpdates policy is {Policy.Mode}");
        return false;
    }

    /// <summary>The earliest time (UTC) the Store answers a check: TTM's own check history, or a refusal the Store logged.</summary>
    private DateTime? NextCheckAllowed(DateTime utcNow)
    {
        var next = _checks.NextAllowed(utcNow);
        if (_refusedUntil is { } refused && refused > utcNow && (next == null || refused > next)) next = refused;
        return next;
    }

    /// <summary>A time for people: the clock time today, with the date on another day.</summary>
    private string At(DateTime utc)
    {
        var local = utc.ToLocalTime();
        return local.Date == UtcNow().ToLocalTime().Date ? local.ToString("t") : local.ToString("g");
    }

    /// <summary>For the log: the version, or that the source didn't name it.</summary>
    private static string Describe(Version? version) => version?.ToString() ?? "a newer version";

    private async Task<bool> IsStagedAsync(UpdateOffer offer) =>
        offer.Version is { } version ? _switcher!.IsStaged(version) : await _source!.IsDownloadedAsync(offer);

    private void Set(UpdateStatus status)
    {
        if (status.Stage == UpdateStage.Ready && Status.Stage != UpdateStage.Ready) _log.Write($"ready: {Describe(status.Version)}");
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

    /// <summary>Checks every <see cref="CheckInterval"/>, counted from the last check (also of an earlier run), at the
    /// earliest <see cref="FirstCheckDelay"/> after the start: restarting the app doesn't use up the Store's checks.</summary>
    public void Start()
    {
        if (_source == null || Policy.Mode == InAppUpdatesMode.Off) return;
        _timer = new Timer(_ => _ = CheckAsync(userInitiated: false), null, FirstCheckDue(), CheckInterval);
    }

    internal TimeSpan FirstCheckDue()
    {
        if (_checks.Last is not { } last) return FirstCheckDelay;
        var due = last + CheckInterval - UtcNow();
        return due > FirstCheckDelay ? due : FirstCheckDelay;
    }

    /// <summary>Asks the source for a newer version and downloads it right away: on its own when the source allows it,
    /// through the source's request route over <paramref name="ownerWindow"/> (or <see cref="OwnerWindow"/>) otherwise.
    /// A version Windows already staged is found first, without asking the source; an update already known is
    /// downloaded without a new check.</summary>
    public async Task CheckAsync(bool userInitiated, IntPtr ownerWindow = default)
    {
        if (_source == null || _switcher == null) return;
        Policy = _readPolicy();
        if (!userInitiated && !ChecksAutomatically) return;
        if (Policy.Mode == InAppUpdatesMode.Off) { Set(new UpdateStatus(UpdateStage.DisabledByPolicy)); return; }
        if (!await _busy.WaitAsync(0)) return;   // a check, download or switch is running
        bool download;
        try { download = await CheckCoreAsync(userInitiated); }
        finally { _busy.Release(); }
        if (download) await DownloadCoreAsync(ownerWindow, userInitiated);
    }

    /// <summary>The check itself; true when the update it found (or already knew) should be downloaded now.</summary>
    private async Task<bool> CheckCoreAsync(bool userInitiated)
    {
        var source = _source!;
        var switcher = _switcher!;
        if (!userInitiated && Status.Stage is UpdateStage.Ready or UpdateStage.ManualStepsNeeded) return false;
        var before = Status;
        if (Policy.Mode == InAppUpdatesMode.Full && await Task.Run(switcher.FindStaged) is { } staged)
        {
            bool known = _offer?.Version == staged && before.Stage == UpdateStage.Ready;
            _offer = new UpdateOffer(staged);
            if (!known) _log.Write($"{staged} is staged, running {switcher.RunningVersion}");
            var gaveUpOn = UpdateAttempt.Load(_attemptPath);
            Set(gaveUpOn != null && gaveUpOn.IsFor(staged, switcher.RunningVersion) && gaveUpOn.Attempts >= UpdateAttempt.MaxAttempts
                ? new UpdateStatus(UpdateStage.ManualStepsNeeded, staged)
                : new UpdateStatus(UpdateStage.Ready, staged));
            return false;
        }
        // An update found earlier (its download not started, refused or failed) needs no new check to be downloaded.
        if (userInitiated && _offer is { } found && source.HasUpdate && before.Stage is UpdateStage.Available or UpdateStage.Failed
            && RouteFor(true, found) != DownloadRoute.None)
            return true;
        // Inside the Store's limits a check only repeats its last answer: skip it, and tell the user when it was theirs.
        if (NextCheckAllowed(UtcNow()) is { } next)
        {
            if (userInitiated)
            {
                string when = $"The Microsoft Store allows another check at {At(next)} or later.";
                Set(before.Stage is UpdateStage.UpToDate or UpdateStage.Checking or UpdateStage.Failed
                    ? new UpdateStatus(UpdateStage.UpToDate, Message: $"Up to date (last checked at {_lastChecked:t}). {when}")
                    : before with { Message = when });
            }
            return false;
        }
        if (userInitiated || before.Stage is UpdateStage.UpToDate or UpdateStage.Failed) Set(new UpdateStatus(UpdateStage.Checking));
        UpdateOffer? offer;
        try { offer = await source.CheckAsync(CancellationToken.None); }
        catch (Exception ex)
        {
            _log.Write($"check ({source.Name}) failed: {UpdateError.Describe(ex)}");
            Set(userInitiated ? new UpdateStatus(UpdateStage.Failed, Message: $"Couldn't check for updates ({source.DisplayName}, error {UpdateError.Code(ex)}).")
                : before.Stage == UpdateStage.Checking ? new UpdateStatus(UpdateStage.UpToDate) : before);
            return false;
        }
        if (offer == null && source.RefusedUntil is { } refusedUntil)
        {
            // The Store didn't check at all (its limits, counting every caller with the app's identity).
            _refusedUntil = refusedUntil;
            _log.Write($"check ({source.Name}{(userInitiated ? ", asked" : "")}): refused by the Store's check limits, next from {refusedUntil.ToLocalTime():g}");
            _offer = null;
            Set(new UpdateStatus(UpdateStage.UpToDate, Message: "The Microsoft Store skipped this check because it limits how often an app may check. " +
                $"Try again at {At(refusedUntil)} or later."));
            return false;
        }
        _checks.Record(UtcNow());
        _lastChecked = DateTime.Now;
        if (offer == null || offer.Version is { } offered && offered <= switcher.RunningVersion)
        {
            _log.Write($"check ({source.Name}{(userInitiated ? ", asked" : "")}): no update, running {switcher.RunningVersion}");
            _offer = null;
            Set(new UpdateStatus(UpdateStage.UpToDate));
            return false;
        }
        bool newOffer = _offer == null || _offer.Version != offer.Version;
        _offer = offer;
        // Every check, also one that finds the same update again: an unnamed offer is never "new" (2026-10-23).
        _log.Write($"check ({source.Name}{(userInitiated ? ", asked" : "")}): {Describe(offer.Version)} available, running {switcher.RunningVersion}");

        var gaveUp = UpdateAttempt.Load(_attemptPath);
        if (gaveUp != null && gaveUp.IsFor(offer.Version, switcher.RunningVersion) && gaveUp.Attempts >= UpdateAttempt.MaxAttempts)
        {
            Set(new UpdateStatus(UpdateStage.ManualStepsNeeded, offer.Version));
            return false;
        }
        // Notice only (InAppUpdates = 1): an update Windows already staged is still only a notice, never Restart to update.
        if (Policy.Mode == InAppUpdatesMode.Full && await IsStagedAsync(offer))
        {
            if (newOffer) _log.Write($"{Describe(offer.Version)} is already staged");
            Set(new UpdateStatus(UpdateStage.Ready, offer.Version));
            return false;
        }
        Set(AvailableStatus(offer));
        return RouteFor(userInitiated, offer) != DownloadRoute.None;
    }

    private UpdateStatus AvailableStatus(UpdateOffer offer)
    {
        bool storeBlocked = Policy.StoreBlocksAutomaticUpdates || !_source!.CanDownloadSilently;
        bool canDownload = Policy.Mode == InAppUpdatesMode.Full && (!Policy.StoreBlocksAutomaticUpdates || Policy.AllowDespiteStorePolicy);
        return new UpdateStatus(UpdateStage.Available, offer.Version, CanDownload: canDownload, StoreBlocked: storeBlocked);
    }

    /// <summary>Downloads the offered version. Silently when the source allows it; otherwise through the source's request
    /// route (the Store's consent dialog, if it shows one) over <paramref name="ownerWindow"/>. An offer this process
    /// didn't find itself (the failed switch before the restart) is checked first: the source has nothing to download yet.</summary>
    public async Task DownloadAsync(IntPtr ownerWindow, bool userInitiated)
    {
        if (_source is { HasUpdate: false } && _offer != null) await CheckAsync(userInitiated, ownerWindow);
        else await DownloadCoreAsync(ownerWindow, userInitiated);
    }

    private async Task DownloadCoreAsync(IntPtr ownerWindow, bool userInitiated)
    {
        if (_source == null || _switcher == null || _offer is not { } offer) return;
        if (Policy.Mode != InAppUpdatesMode.Full) return;
        if (ownerWindow == IntPtr.Zero) ownerWindow = OwnerWindow();
        if (!await _busy.WaitAsync(0)) return;
        try
        {
            var route = RouteFor(userInitiated, offer);
            if (route == DownloadRoute.None) return;
            _downloadCancel = new CancellationTokenSource();
            Set(new UpdateStatus(UpdateStage.Downloading, offer.Version));
            var progress = new Progress(p => Set(new UpdateStatus(UpdateStage.Downloading, offer.Version, Math.Clamp(p, 0, 1))));
            _log.Write($"download {Describe(offer.Version)} ({(route == DownloadRoute.Silent ? "silent" : "with consent")})");
            DownloadResult result;
            try
            {
                result = route == DownloadRoute.Silent
                    ? await _source.DownloadAsync(offer, progress, _downloadCancel.Token)
                    : await _source.RequestDownloadAsync(offer, ownerWindow, progress, _downloadCancel.Token);
            }
            catch (OperationCanceledException) { result = new DownloadResult(DownloadOutcome.Canceled); }
            catch (Exception ex) { result = DownloadResult.From(ex); }
            _log.Write($"download {Describe(offer.Version)}: {result.Outcome}{(result.Detail is { } detail ? " " + detail : "")}");

            if (result.Outcome == DownloadOutcome.Completed && await IsStagedAsync(offer))
                Set(new UpdateStatus(UpdateStage.Ready, offer.Version));
            else if (result.Outcome == DownloadOutcome.Completed && await Task.Run(_switcher.FindStaged) is { } staged)
            {
                _offer = new UpdateOffer(staged);
                _log.Write($"{staged} is staged");
                Set(new UpdateStatus(UpdateStage.Ready, staged));
            }
            else if (result.Outcome == DownloadOutcome.Canceled)
                Set(AvailableStatus(offer));
            else if (result.Outcome == DownloadOutcome.Completed && route == DownloadRoute.Consent && AvailableStatus(offer).StoreBlocked)
                // The Store's queue item can stay paused under the policy: Windows Update may still bring it.
                Set(AvailableStatus(offer) with { Message = "The Microsoft Store hasn't downloaded it yet. " + WindowsUpdateHint });
            else if (result.Outcome == DownloadOutcome.Completed)
                // The source says done but nothing is staged (the Store may still be finishing): look again at the next check.
                Set(AvailableStatus(offer) with { Message = "The download finished, but Windows hasn't prepared the update yet. The app checks again later." });
            else if (result.Outcome == DownloadOutcome.Blocked)
                Set(AvailableStatus(offer) with { StoreBlocked = true });
            else
                Set(new UpdateStatus(UpdateStage.Failed, offer.Version, Message: result.Code is { } code
                    ? $"The update couldn't be downloaded ({_source.DisplayName}, error {code})."
                    : $"The update couldn't be downloaded: {result.Detail ?? "unknown error"}."));
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
        if (_switcher == null || _source == null || Status.Stage != UpdateStage.Ready) return "No update is ready.";
        Version? target = Status.Version;
        if (!RefreshPolicyForSwitch()) return "Your organization manages updates on this PC.";
        if (!await _busy.WaitAsync(0)) return "An update step is already running.";
        try
        {
            if (!await IsStagedAsync(_offer ?? new UpdateOffer(target)))
            {
                _log.Write($"switch to {Describe(target)}: not staged any more");
                _offer = null;
                Set(new UpdateStatus(UpdateStage.UpToDate));
                return "The downloaded update is no longer there (Windows may have removed it). The app downloads it again at the next check.";
            }
            var previous = UpdateAttempt.Load(_attemptPath);
            int tries = previous?.IsFor(target, _switcher.RunningVersion) == true ? previous.Attempts + 1 : 1;
            new UpdateAttempt(target?.ToString() ?? "", _switcher.RunningVersion.ToString(), idle, DateTime.Now, tries).Save(_attemptPath);
            _log.Write($"switch to {Describe(target)} ({(!restart ? "on exit" : idle ? "idle" : "user")}, try {tries})");
            Set(new UpdateStatus(UpdateStage.Installing, target));

            if (restart)
            {
                _switcher.RegisterRestart(idle ? UpdatedIdleArgument : UpdatedArgument);
                // Windows' own restart came 10-110 s after the switch on the test VM: the watchdog mustn't race it.
                try { _switcher.StartWatchdog(TimeSpan.FromSeconds(120), _log.Path); }
                catch (Exception ex) { _log.Write($"watchdog not set: {ex.Message}"); }
            }
            else _switcher.UnregisterRestart();
            var failure = await _switcher.SwitchAsync();

            // Still running: Windows didn't switch.
            _log.Write($"switch to {Describe(target)} failed: {failure?.Detail ?? "no reason"}");
            string problem = failure?.Code is { } code
                ? $"Windows didn't switch to the new version (error {code})."
                : "Windows didn't switch to the new version.";
            if (tries >= UpdateAttempt.MaxAttempts)
                Set(new UpdateStatus(UpdateStage.ManualStepsNeeded, target, Message: problem));
            else
                Set(new UpdateStatus(UpdateStage.Ready, target, Message: "The last attempt to install it didn't work. " + problem));
            return problem;
        }
        finally { _busy.Release(); }
    }

    /// <summary>A previous run's switch failed: show the update as ready again, or the manual steps after too many tries.</summary>
    public void ReportFailedSwitch(UpdateAttempt attempt)
    {
        Version? target = Version.TryParse(attempt.TargetVersion, out var named) ? named : null;
        _offer = new UpdateOffer(target);
        if (attempt.Attempts >= UpdateAttempt.MaxAttempts) Set(new UpdateStatus(UpdateStage.ManualStepsNeeded, target));
        // Without a version the next check finds out whether it's still staged.
        else if (target != null && _switcher?.IsStaged(target) == true && Policy.Mode == InAppUpdatesMode.Full)
            Set(new UpdateStatus(UpdateStage.Ready, target, Message: "The last attempt to install it didn't work."));
        else Set(AvailableStatus(_offer) with { Message = "The last attempt to install it didn't work." });
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
