using TextTemplateManager.Services.Updates;
using Xunit;

namespace TextTemplateManager.Tests;

/// <summary>The in-app updater over a stand-in Store and package switch: it downloads a newer version while the app runs,
/// offers Restart to update once it's staged, follows the update policies, and keeps track of switches across restarts.</summary>
public class UpdateServiceTests
{
    private static readonly Version Running = new(2, 1, 0, 0), Newer = new(2, 2, 0, 0);

    private sealed class FakeSource : IUpdateSource
    {
        public Version? Offer = Newer;
        /// <summary>False: offers without a version, as the Store does; "downloaded" is then the source's own word.</summary>
        public bool NamesVersion = true;
        public bool DownloadedUnnamed, ConfirmsDownload = true;
        public bool Silent = true;
        public DownloadResult Result = DownloadResult.Done;
        public FakeSwitcher? StagesInto;
        public int Downloads, ConsentDownloads;
        public Exception? CheckThrows;

        public string Name => "fake";
        public string DisplayName => "Fake Store";
        public bool CanDownloadSilently => Silent;

        public Task<UpdateOffer?> CheckAsync(CancellationToken cancel) =>
            CheckThrows != null ? Task.FromException<UpdateOffer?>(CheckThrows)
            : Task.FromResult(Offer == null ? null : new UpdateOffer(NamesVersion ? Offer : null));

        public Task<bool> IsDownloadedAsync(UpdateOffer offer) => Task.FromResult(DownloadedUnnamed);

        public DateTime? RefusedUntil { get; set; }

        private void Stage(UpdateOffer offer)
        {
            if (Result.Outcome != DownloadOutcome.Completed) return;
            if (offer.Version is { } version) StagesInto?.Staged.Add(version);
            else if (ConfirmsDownload) DownloadedUnnamed = true;
        }

        public Task<DownloadResult> DownloadAsync(UpdateOffer offer, IProgress<double> progress, CancellationToken cancel)
        {
            Downloads++;
            progress.Report(0.5);
            Stage(offer);
            return Task.FromResult(Result);
        }

        public Task<DownloadResult> RequestDownloadAsync(UpdateOffer offer, IntPtr ownerWindow, IProgress<double> progress, CancellationToken cancel)
        {
            ConsentDownloads++;
            Stage(offer);
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeSwitcher : IPackageSwitcher
    {
        public readonly HashSet<Version> Staged = [];
        public readonly List<string> Calls = [];
        public SwitchFailure? SwitchProblem = new("0x80073D02", "0x80073D02 error 0x80073D02: Unable to install because the following apps need to be closed");

        public Version RunningVersion { get; set; } = Running;
        public bool IsStaged(Version version) => Staged.Contains(version);
        /// <summary>What the AppX log names, staged by someone else (Windows Update).</summary>
        public Version? StagedElsewhere;
        public Version? FindStaged() => StagedElsewhere;
        public void RegisterRestart(string arguments) => Calls.Add("restart " + arguments);
        public void UnregisterRestart() => Calls.Add("no restart");
        public void StartWatchdog(TimeSpan after, string logPath) => Calls.Add("watchdog");
        public Task<SwitchFailure?> SwitchAsync() { Calls.Add("switch"); return Task.FromResult(SwitchProblem); }
    }

    private static (UpdateService Service, FakeSource Source, FakeSwitcher Switcher, string Dir) Create(
        UpdatePolicy? policy = null, bool withSource = true)
    {
        string dir = TestEnvironment.NewScratchDir();
        var switcher = new FakeSwitcher();
        var source = new FakeSource { StagesInto = switcher };
        var service = new UpdateService(withSource ? source : null, switcher, new UpdateLog(Path.Combine(dir, "update.log")),
            Path.Combine(dir, "update-attempt.json"), () => policy ?? UpdatePolicy.Default);
        // An hour per reading: tests that check more than once stay outside the Store's check limits.
        var clock = new DateTime(2026, 10, 21, 0, 0, 0, DateTimeKind.Utc);
        service.UtcNow = () => clock = clock.AddHours(1);
        return (service, source, switcher, dir);
    }

    [Fact]
    public async Task A_newer_version_is_downloaded_in_the_background_and_then_ready()
    {
        var (service, source, _, _) = Create();
        var stages = new List<UpdateStage>();
        service.Changed += s => stages.Add(s.Stage);

        await service.CheckAsync(userInitiated: false);

        Assert.Equal(1, source.Downloads);
        Assert.Equal(UpdateStage.Ready, service.Status.Stage);
        Assert.Equal(Newer, service.Status.Version);
        Assert.Contains(UpdateStage.Downloading, stages);
    }

    [Fact]
    public async Task Up_to_date_when_nothing_newer_is_offered()
    {
        var (service, source, _, _) = Create();
        source.Offer = Running;

        await service.CheckAsync(userInitiated: true);

        Assert.Equal(UpdateStage.UpToDate, service.Status.Stage);
        Assert.NotNull(service.Status.LastChecked);
        Assert.Equal(0, source.Downloads);
    }

    [Fact]
    public async Task An_update_windows_already_staged_is_ready_without_a_download()
    {
        var (service, source, switcher, _) = Create();
        switcher.Staged.Add(Newer);

        await service.CheckAsync(userInitiated: false);

        Assert.Equal(UpdateStage.Ready, service.Status.Stage);
        Assert.Equal(0, source.Downloads);
    }

    [Fact]
    public async Task An_update_the_source_doesnt_name_is_downloaded_and_ready()
    {
        var (service, source, _, _) = Create();
        source.NamesVersion = false;

        await service.CheckAsync(userInitiated: false);

        Assert.Equal((1, UpdateStage.Ready), (source.Downloads, service.Status.Stage));
        Assert.Null(service.Status.Version);
    }

    [Fact]
    public async Task An_unnamed_update_already_downloaded_is_ready_without_a_download()
    {
        var (service, source, _, _) = Create();
        source.NamesVersion = false;
        source.DownloadedUnnamed = true;

        await service.CheckAsync(userInitiated: false);

        Assert.Equal((0, UpdateStage.Ready), (source.Downloads, service.Status.Stage));
    }

    [Fact]
    public async Task An_unnamed_download_the_source_doesnt_confirm_stays_available()
    {
        var (service, source, _, _) = Create();
        source.NamesVersion = false;
        source.ConfirmsDownload = false;   // the Store said done, its queue doesn't say staged

        await service.CheckAsync(userInitiated: false);

        Assert.Equal(UpdateStage.Available, service.Status.Stage);
        Assert.Contains("hasn't prepared the update yet", service.Status.Message);
    }

    [Fact]
    public async Task A_switch_to_an_unnamed_version_counts_once_a_newer_version_runs()
    {
        var (service, source, switcher, dir) = Create();
        source.NamesVersion = false;
        await service.CheckAsync(userInitiated: false);

        await service.SwitchAsync(idle: false);
        var attempt = UpdateAttempt.Load(Path.Combine(dir, "update-attempt.json"))!;
        Assert.Equal(("", Running.ToString()), (attempt.TargetVersion, attempt.FromVersion));

        Assert.Equal(AttemptOutcome.Failed, attempt.Evaluate(Running));
        Assert.Equal(AttemptOutcome.Succeeded, attempt.Evaluate(Newer));
        Assert.Contains("switch", switcher.Calls);
    }

    [Fact]
    public async Task Two_failed_switches_to_an_unnamed_version_leave_the_manual_steps()
    {
        var (service, source, _, _) = Create();
        source.NamesVersion = false;
        await service.CheckAsync(userInitiated: false);

        await service.SwitchAsync(idle: false);
        await service.SwitchAsync(idle: false);

        Assert.Equal(UpdateStage.ManualStepsNeeded, service.Status.Stage);
    }

    [Fact]
    public async Task An_unnamed_download_that_isnt_staged_isnt_switched_to()
    {
        var (service, source, switcher, _) = Create();
        source.NamesVersion = false;
        await service.CheckAsync(userInitiated: false);
        source.DownloadedUnnamed = false;   // Windows removed it meanwhile

        string? problem = await service.SwitchAsync(idle: false);

        Assert.Contains("no longer there", problem);
        Assert.DoesNotContain("switch", switcher.Calls);
    }

    [Fact]
    public async Task A_check_within_30_minutes_of_the_last_isnt_sent_and_says_when_the_next_is_possible()
    {
        var (service, source, _, _) = Create();
        source.Offer = Running;
        var now = new DateTime(2026, 10, 21, 8, 0, 0, DateTimeKind.Utc);
        service.UtcNow = () => now;
        await service.CheckAsync(userInitiated: true);
        source.Offer = Newer;
        now = now.AddMinutes(10);

        await service.CheckAsync(userInitiated: true);

        Assert.Equal(UpdateStage.UpToDate, service.Status.Stage);
        Assert.Contains("allows another check at", service.Status.Message);
        Assert.Equal(0, source.Downloads);   // the newer version wasn't asked for

        now = now.AddMinutes(25);
        await service.CheckAsync(userInitiated: true);
        Assert.Equal(UpdateStage.Ready, service.Status.Stage);
    }

    [Fact]
    public async Task The_eleventh_check_in_24_hours_waits_until_the_oldest_ages_out()
    {
        var (service, source, _, _) = Create();
        source.Offer = Running;
        var now = new DateTime(2026, 10, 21, 0, 0, 0, DateTimeKind.Utc);
        service.UtcNow = () => now;
        for (int i = 0; i < 10; i++) { await service.CheckAsync(userInitiated: true); now = now.AddMinutes(31); }
        source.Offer = Newer;

        await service.CheckAsync(userInitiated: false);
        Assert.Equal(0, source.Downloads);

        now = new DateTime(2026, 10, 22, 0, 1, 0, DateTimeKind.Utc);
        await service.CheckAsync(userInitiated: false);
        Assert.Equal(1, source.Downloads);
    }

    [Fact]
    public async Task Check_times_survive_a_restart_and_set_the_first_check()
    {
        var (service, source, switcher, dir) = Create();
        source.Offer = Running;
        var now = new DateTime(2026, 10, 21, 8, 0, 0, DateTimeKind.Utc);
        service.UtcNow = () => now;
        await service.CheckAsync(userInitiated: false);

        var restarted = new UpdateService(source, switcher, new UpdateLog(Path.Combine(dir, "update.log")),
            Path.Combine(dir, "update-attempt.json"), () => UpdatePolicy.Default) { UtcNow = () => now.AddHours(1) };

        Assert.Equal(UpdateService.CheckInterval - TimeSpan.FromHours(1), restarted.FirstCheckDue());
        Assert.Equal(now.ToLocalTime(), restarted.Status.LastChecked);
    }

    [Fact]
    public async Task A_check_the_store_refused_says_so_and_when_it_checks_again()
    {
        var (service, source, _, dir) = Create();
        var now = new DateTime(2026, 10, 21, 8, 0, 0, DateTimeKind.Utc);
        service.UtcNow = () => now;
        source.Offer = null;
        source.RefusedUntil = now.AddHours(11);

        await service.CheckAsync(userInitiated: true);

        Assert.Equal(UpdateStage.UpToDate, service.Status.Stage);
        Assert.Contains("skipped this check", service.Status.Message);
        Assert.Null(service.Status.LastChecked);   // nothing was really checked
        Assert.Contains("refused by the Store's check limits", await File.ReadAllTextAsync(Path.Combine(dir, "update.log")));

        // Until then TTM doesn't ask again and says when it can.
        source.RefusedUntil = null;
        source.Offer = Newer;
        now = now.AddHours(1);
        await service.CheckAsync(userInitiated: true);
        Assert.Equal(0, source.Downloads);
        Assert.Contains("allows another check at", service.Status.Message);

        now = now.AddHours(11);
        await service.CheckAsync(userInitiated: true);
        Assert.Equal(1, source.Downloads);
    }

    [Fact]
    public void The_stores_logged_refusal_is_read_with_its_scan_times()
    {
        const string pfn = "MarflowSoftware.TextTemplateManager_eb1r5ey0kd4ny";
        // As logged on the work PC on 2026-10-21 (11 scans in 24 hours).
        string interval = "Scan not allowed inside interval for MarflowSoftware.TextTemplateManager_eb1r5ey0kd4ny. Current last scan time cache: " +
            "2026-10-20T19:23:36+02:00;2026-10-20T21:42:19+02:00;2026-10-20T21:46:14+02:00;2026-10-20T21:53:56+02:00;2026-10-20T21:56:50+02:00;" +
            "2026-10-20T22:02:35+02:00;2026-10-20T22:27:34+02:00;2026-10-20T22:30:03+02:00;2026-10-20T22:52:40+02:00;2026-10-21T04:30:02+02:00;" +
            "2026-10-21T09:07:49+02:00 Error: Unknown HResult Error code: 0xefffffff Function: OnlineScanAllowedInInterval";
        var scans = StoreScanLog.ParseRefusal(interval, pfn);
        Assert.NotNull(scans);
        Assert.Equal(11, scans!.Count);
        Assert.Equal(new DateTime(2026, 10, 20, 17, 23, 36, DateTimeKind.Utc), scans[0]);

        // At 18:49 local the ten newest scans in 24 hours block until the second oldest ages out (21:42 local).
        var next = UpdateCheckHistory.NextAllowed(scans, new DateTime(2026, 10, 21, 16, 49, 0, DateTimeKind.Utc));
        Assert.Equal(new DateTime(2026, 10, 21, 19, 42, 19, DateTimeKind.Utc), next);

        // As logged on the home PC on 2026-10-20 (a check inside 30 minutes).
        string cooldown = "Online scan not allowed due to cooldown period for MarflowSoftware.TextTemplateManager_eb1r5ey0kd4ny. Current last scan time cache: " +
            "2026-10-20T19:09:24+02:00;2026-10-20T19:15:15+02:00;2026-10-20T21:26:40+02:00;2026-10-20T22:53:50+02:00 Error: Unknown HResult";
        Assert.Equal(4, StoreScanLog.ParseRefusal(cooldown, pfn)!.Count);

        Assert.Null(StoreScanLog.ParseRefusal(interval, "Other.App_123"));
        Assert.Null(StoreScanLog.ParseRefusal("Scan allowed inside interval for " + pfn + ". Current last scan time cache: 2026-10-20T19:09:24+02:00", pfn));
        Assert.Null(StoreScanLog.ParseRefusal("Some reworded message for " + pfn, pfn));
    }

    [Fact]
    public async Task An_empty_answer_is_logged()
    {
        var (service, source, _, dir) = Create();
        source.Offer = null;

        await service.CheckAsync(userInitiated: true);

        Assert.Contains("no update", await File.ReadAllTextAsync(Path.Combine(dir, "update.log")));
    }

    [Fact]
    public async Task Without_a_source_nothing_is_checked()
    {
        var (service, source, _, _) = Create(withSource: false);

        await service.CheckAsync(userInitiated: true);

        Assert.Equal(UpdateStage.Unavailable, service.Status.Stage);
        Assert.Equal(0, source.Downloads);
    }

    [Fact]
    public async Task Policy_off_disables_the_updater()
    {
        var (service, source, _, _) = Create(UpdatePolicy.FromValues(0, null, null, null));

        await service.CheckAsync(userInitiated: true);

        Assert.Equal(UpdateStage.DisabledByPolicy, service.Status.Stage);
        Assert.Equal(0, source.Downloads);
    }

    [Fact]
    public async Task Notice_only_policy_tells_about_the_update_but_never_downloads()
    {
        var (service, source, _, _) = Create(UpdatePolicy.FromValues(1, null, null, null));

        await service.CheckAsync(userInitiated: false);
        await service.DownloadAsync(IntPtr.Zero, userInitiated: true);

        Assert.Equal(UpdateStage.Available, service.Status.Stage);
        Assert.False(service.Status.CanDownload);
        Assert.Equal(0, source.Downloads + source.ConsentDownloads);
    }

    [Fact]
    public async Task Store_policy_off_check_for_updates_downloads_through_the_consent_dialog()
    {
        var (service, source, _, _) = Create(UpdatePolicy.FromValues(null, null, null, 4));
        source.Silent = false;

        await service.CheckAsync(userInitiated: true);

        Assert.Equal((0, 1), (source.Downloads, source.ConsentDownloads));
        Assert.Equal(UpdateStage.Ready, service.Status.Stage);
    }

    [Fact]
    public async Task Store_policy_off_download_on_the_notice_uses_the_consent_dialog()
    {
        var (service, source, _, _) = Create(UpdatePolicy.FromValues(null, null, null, 4));
        source.Silent = false;

        await service.CheckAsync(userInitiated: false);
        Assert.Equal((UpdateStage.Available, true, true), (service.Status.Stage, service.Status.CanDownload, service.Status.StoreBlocked));
        Assert.Equal(0, source.Downloads + source.ConsentDownloads);

        await service.DownloadAsync(IntPtr.Zero, userInitiated: true);
        Assert.Equal((0, 1), (source.Downloads, source.ConsentDownloads));
        Assert.Equal(UpdateStage.Ready, service.Status.Stage);
    }

    [Fact]
    public async Task Store_policy_off_a_failed_consent_download_points_to_windows_update()
    {
        var (service, source, _, _) = Create(UpdatePolicy.FromValues(null, null, null, 4));
        source.Silent = false;
        source.Result = new DownloadResult(DownloadOutcome.Failed, "no Store app");

        await service.CheckAsync(userInitiated: true);

        Assert.Equal((UpdateStage.Available, true), (service.Status.Stage, service.Status.CanDownload));
        Assert.Contains(UpdateService.WindowsUpdateHint, service.Status.Message);
    }

    [Fact]
    public async Task Store_policy_off_a_consent_download_that_stages_nothing_points_to_windows_update()
    {
        var (service, source, _, _) = Create(UpdatePolicy.FromValues(null, null, null, 4));
        source.Silent = false;
        source.NamesVersion = false;
        source.ConfirmsDownload = false;

        await service.CheckAsync(userInitiated: true);

        Assert.Equal(UpdateStage.Available, service.Status.Stage);
        Assert.Contains(UpdateService.WindowsUpdateHint, service.Status.Message);
    }

    [Fact]
    public async Task Store_policy_off_without_the_admins_permission_offers_no_download()
    {
        var (service, source, _, _) = Create(UpdatePolicy.FromValues(null, 0, null, 4));
        Assert.False(service.Policy.UpdatesDespiteStorePolicy);

        await service.CheckAsync(userInitiated: true);
        await service.DownloadAsync(IntPtr.Zero, userInitiated: true);

        Assert.Equal((UpdateStage.Available, false), (service.Status.Stage, service.Status.CanDownload));
        Assert.Equal(0, source.Downloads + source.ConsentDownloads);
    }

    [Fact]
    public async Task Store_policy_off_automatic_checks_download_nothing_on_their_own()
    {
        var (service, source, _, _) = Create(UpdatePolicy.FromValues(null, null, null, 4, automaticUpdates: 1));
        source.Silent = false;

        await service.CheckAsync(userInitiated: false);

        Assert.Equal(0, source.Downloads + source.ConsentDownloads);
        Assert.Equal(UpdateStage.Available, service.Status.Stage);
    }

    [Fact]
    public async Task Without_the_store_policy_automatic_updates_download_through_the_store()
    {
        var (service, source, _, _) = Create();

        await service.CheckAsync(userInitiated: false);

        Assert.Equal((1, 0), (source.Downloads, source.ConsentDownloads));
        Assert.False(service.Policy.UpdatesDespiteStorePolicy);
    }

    [Fact]
    public async Task A_version_windows_update_staged_is_ready_without_asking_the_store()
    {
        var (service, source, switcher, dir) = Create(UpdatePolicy.FromValues(null, null, null, 4));
        source.Silent = false;
        source.CheckThrows = new InvalidOperationException("the Store shouldn't be asked");
        switcher.StagedElsewhere = Newer;

        await service.CheckAsync(userInitiated: false);

        Assert.Equal((UpdateStage.Ready, Newer), (service.Status.Stage, service.Status.Version));
        Assert.Equal(0, source.Downloads + source.ConsentDownloads);
        Assert.Contains($"{Newer} is staged", await File.ReadAllTextAsync(Path.Combine(dir, "update.log")));
    }

    [Fact]
    public async Task A_staged_version_is_only_a_notice_under_the_notice_only_policy()
    {
        var (service, _, switcher, _) = Create(UpdatePolicy.FromValues(1, null, null, null));
        switcher.StagedElsewhere = Newer;

        await service.CheckAsync(userInitiated: true);

        Assert.Equal(UpdateStage.Available, service.Status.Stage);
    }

    [Fact]
    public async Task A_version_staged_while_the_consent_download_ran_is_ready()
    {
        var (service, source, switcher, _) = Create(UpdatePolicy.FromValues(null, null, null, 4));
        source.Silent = false;
        source.NamesVersion = false;
        source.ConfirmsDownload = false;
        service.Changed += s => { if (s.Stage == UpdateStage.Downloading) switcher.StagedElsewhere = Newer; };

        await service.CheckAsync(userInitiated: true);

        Assert.Equal((UpdateStage.Ready, Newer), (service.Status.Stage, service.Status.Version));
    }

    [Theory]
    [InlineData("Deployment Stage operation with target volume C: on Package MarflowSoftware.TextTemplateManager_2.3.2.0_x64__eb1r5ey0kd4ny from:  (370fa352ec3f09d456d2ce925e8de69925e90076)  finished successfully.", "2.3.2.0")]
    [InlineData("Deployment Stage operation with target volume C: on Package MarflowSoftware.TextTemplateManager_2.3.2.0_neutral_~_eb1r5ey0kd4ny from:  (370fa352ec3f09d456d2ce925e8de69925e90076)  finished successfully.", null)]
    [InlineData("Deployment Register operation with target volume C: on Package MarflowSoftware.TextTemplateManager_2.3.2.0_x64__eb1r5ey0kd4ny from:  (AppxBundleManifest.xml)  finished successfully.", null)]
    [InlineData("Deployment DeStage operation with target volume C: on Package MarflowSoftware.TextTemplateManager_2.3.0.0_x64__eb1r5ey0kd4ny from:   finished successfully.", null)]
    [InlineData("Deployment Stage operation with target volume C: on Package Other.App_9.0.0.0_x64__eb1r5ey0kd4ny from:  (abc)  finished successfully.", null)]
    [InlineData("Deployment Stage operation on package MarflowSoftware.TextTemplateManager_2.3.2.0_x64__eb1r5ey0kd4ny has been de-queued and is running for user SID S-1-5-18.", null)]
    public void Staged_versions_are_read_from_the_appx_log_text(string message, string? expected)
    {
        var version = AppxStageLog.ParseStaged(message, "MarflowSoftware.TextTemplateManager", "eb1r5ey0kd4ny");

        Assert.Equal(expected == null ? null : Version.Parse(expected), version);
    }

    [Fact]
    public async Task With_automatic_updates_off_only_a_check_the_user_asks_for_runs()
    {
        var (service, source, _, _) = Create();
        service.CheckAutomatically = false;

        await service.CheckAsync(userInitiated: false);
        Assert.Equal((UpdateStage.UpToDate, 0), (service.Status.Stage, source.Downloads));
        Assert.Null(service.Status.LastChecked);

        await service.CheckAsync(userInitiated: true);
        Assert.Equal((UpdateStage.Ready, 1), (service.Status.Stage, source.Downloads));
    }

    [Fact]
    public void With_automatic_updates_off_there_is_no_idle_install_or_install_on_quit_unless_the_policy_forces_idle_install()
    {
        var (service, _, _, _) = Create();
        service.CheckAutomatically = false;
        Assert.False(service.IdleInstallAllowed(userSetting: true));
        Assert.False(service.InstallOnQuit);

        var (forced, _, _, _) = Create(UpdatePolicy.FromValues(null, null, 1, null));
        forced.CheckAutomatically = false;
        Assert.True(forced.IdleInstallAllowed(userSetting: false));

        service.CheckAutomatically = true;
        Assert.True(service.InstallOnQuit);
    }

    [Fact]
    public async Task Notice_only_keeps_an_update_windows_already_staged_a_notice()
    {
        var (service, source, switcher, _) = Create(UpdatePolicy.FromValues(1, null, null, null));
        switcher.Staged.Add(Newer);

        await service.CheckAsync(userInitiated: false);

        Assert.Equal((UpdateStage.Available, false), (service.Status.Stage, service.Status.CanDownload));
        Assert.Equal(0, source.Downloads);
    }

    [Fact]
    public async Task A_policy_set_after_the_download_stops_the_switch()
    {
        var policy = UpdatePolicy.Default;
        string dir = TestEnvironment.NewScratchDir();
        var switcher = new FakeSwitcher();
        var service = new UpdateService(new FakeSource { StagesInto = switcher }, switcher, new UpdateLog(Path.Combine(dir, "update.log")),
            Path.Combine(dir, "update-attempt.json"), () => policy);
        await service.CheckAsync(userInitiated: false);
        Assert.Equal(UpdateStage.Ready, service.Status.Stage);

        policy = UpdatePolicy.FromValues(1, null, null, null);
        string? problem = await service.SwitchAsync(idle: false);
        Assert.Contains("organization", problem);
        Assert.DoesNotContain("switch", switcher.Calls);
        Assert.Equal(UpdateStage.Available, service.Status.Stage);

        policy = UpdatePolicy.FromValues(0, null, null, null);
        Assert.False(service.RefreshPolicyForSwitch());
        Assert.Equal(UpdateStage.DisabledByPolicy, service.Status.Stage);
    }

    [Fact]
    public async Task The_automatic_updates_policy_overrides_the_users_switch()
    {
        var (forcedOn, onSource, _, _) = Create(UpdatePolicy.FromValues(null, null, null, null, automaticUpdates: 1));
        forcedOn.CheckAutomatically = false;
        await forcedOn.CheckAsync(userInitiated: false);
        Assert.Equal((UpdateStage.Ready, 1), (forcedOn.Status.Stage, onSource.Downloads));
        Assert.True(forcedOn.InstallOnQuit);

        var (forcedOff, offSource, _, _) = Create(UpdatePolicy.FromValues(null, null, null, null, automaticUpdates: 0));
        await forcedOff.CheckAsync(userInitiated: false);
        Assert.Equal((UpdateStage.UpToDate, 0), (forcedOff.Status.Stage, offSource.Downloads));
        Assert.False(forcedOff.InstallOnQuit);
        await forcedOff.CheckAsync(userInitiated: true);
        Assert.Equal(UpdateStage.Ready, forcedOff.Status.Stage);
    }

    [Fact]
    public async Task A_failed_download_is_reported_and_a_check_can_try_again()
    {
        var (service, source, _, _) = Create();
        source.Result = new DownloadResult(DownloadOutcome.Failed, "no network");

        await service.CheckAsync(userInitiated: false);
        Assert.Equal(UpdateStage.Failed, service.Status.Stage);
        Assert.Contains("no network", service.Status.Message);

        source.Result = DownloadResult.Done;
        await service.CheckAsync(userInitiated: true);
        Assert.Equal(UpdateStage.Ready, service.Status.Stage);
    }

    [Fact]
    public async Task A_download_error_shows_its_code_and_source_and_logs_the_original_message()
    {
        var (service, source, _, dir) = Create();
        var error = new System.Runtime.InteropServices.COMException("The server returned a fault.", unchecked((int)0x80244007));
        source.Result = DownloadResult.From(error);

        await service.CheckAsync(userInitiated: true);

        Assert.Equal(UpdateStage.Failed, service.Status.Stage);
        Assert.Equal("The update couldn't be downloaded (Fake Store, error 0x80244007).", service.Status.Message);
        string log = await File.ReadAllTextAsync(Path.Combine(dir, "update.log"));
        Assert.Contains("Failed 0x80244007 COMException: The server returned a fault.", log);
    }

    [Fact]
    public async Task A_check_error_shows_its_code_and_source_and_logs_the_original_message()
    {
        var (service, source, _, dir) = Create();
        source.CheckThrows = new System.Runtime.InteropServices.COMException("The RPC server is unavailable.", unchecked((int)0x800706BA));

        await service.CheckAsync(userInitiated: true);

        Assert.Equal("Couldn't check for updates (Fake Store, error 0x800706BA).", service.Status.Message);
        Assert.Contains("failed: 0x800706BA COMException: The RPC server is unavailable.", await File.ReadAllTextAsync(Path.Combine(dir, "update.log")));
    }

    [Fact]
    public async Task A_failed_switch_shows_the_code_and_logs_windows_text()
    {
        var (service, _, _, dir) = Create();
        await service.CheckAsync(userInitiated: false);

        string? problem = await service.SwitchAsync(idle: false);

        Assert.Equal("Windows didn't switch to the new version (error 0x80073D02).", problem);
        Assert.Contains("didn't work. Windows didn't switch", service.Status.Message);
        Assert.Contains("Unable to install because the following apps need to be closed", await File.ReadAllTextAsync(Path.Combine(dir, "update.log")));
    }

    [Fact]
    public async Task A_check_that_throws_in_the_background_leaves_the_status_alone()
    {
        var (service, source, _, _) = Create();
        source.CheckThrows = new InvalidOperationException("offline");

        await service.CheckAsync(userInitiated: false);
        Assert.Equal(UpdateStage.UpToDate, service.Status.Stage);

        await service.CheckAsync(userInitiated: true);
        Assert.Equal(UpdateStage.Failed, service.Status.Stage);
    }

    [Fact]
    public async Task Restart_to_update_records_the_attempt_registers_the_restart_and_switches()
    {
        var (service, _, switcher, dir) = Create();
        await service.CheckAsync(userInitiated: false);

        string? problem = await service.SwitchAsync(idle: false);

        Assert.Equal(["restart " + UpdateService.UpdatedArgument, "watchdog", "switch"], switcher.Calls);
        Assert.NotNull(problem);   // the fake switch fails like a running app would, so the call returns
        var attempt = UpdateAttempt.Load(Path.Combine(dir, "update-attempt.json"));
        Assert.Equal((Newer.ToString(), Running.ToString(), false, 1), (attempt!.TargetVersion, attempt.FromVersion, attempt.Idle, attempt.Attempts));
        Assert.Equal(UpdateStage.Ready, service.Status.Stage);
    }

    [Fact]
    public async Task The_idle_install_restarts_with_its_own_argument_and_quitting_switches_without_a_restart()
    {
        var (service, _, switcher, _) = Create();
        await service.CheckAsync(userInitiated: false);

        await service.SwitchAsync(idle: true);
        Assert.Contains("restart " + UpdateService.UpdatedIdleArgument, switcher.Calls);

        switcher.Calls.Clear();
        await service.SwitchAsync(idle: false, restart: false);
        Assert.Equal(["no restart", "switch"], switcher.Calls);
    }

    [Fact]
    public async Task After_two_failed_switches_only_the_manual_steps_are_left()
    {
        var (service, _, _, _) = Create();
        await service.CheckAsync(userInitiated: false);

        await service.SwitchAsync(idle: false);
        Assert.Equal(UpdateStage.Ready, service.Status.Stage);
        await service.SwitchAsync(idle: false);
        Assert.Equal(UpdateStage.ManualStepsNeeded, service.Status.Stage);
    }

    [Fact]
    public async Task A_staged_update_windows_removed_meanwhile_is_downloaded_again()
    {
        var (service, _, switcher, _) = Create();
        await service.CheckAsync(userInitiated: false);
        switcher.Staged.Clear();

        string? problem = await service.SwitchAsync(idle: false);

        Assert.Contains("no longer there", problem);
        Assert.DoesNotContain("switch", switcher.Calls);
        Assert.Equal(UpdateStage.UpToDate, service.Status.Stage);
    }

    [Fact]
    public async Task The_next_start_tells_whether_the_switch_worked()
    {
        var (service, _, switcher, dir) = Create();
        await service.CheckAsync(userInitiated: false);
        await service.SwitchAsync(idle: true);

        // Restarted on the new version.
        switcher.RunningVersion = Newer;
        var after = new UpdateService(new FakeSource(), switcher, new UpdateLog(Path.Combine(dir, "update.log")),
            Path.Combine(dir, "update-attempt.json"), () => UpdatePolicy.Default);
        var result = after.EvaluateStartup();

        Assert.Equal(AttemptOutcome.Succeeded, result.Outcome);
        Assert.True(result.Attempt!.Idle);
        Assert.False(File.Exists(Path.Combine(dir, "update-attempt.json")));
        Assert.Contains("Succeeded", File.ReadAllText(Path.Combine(dir, "update.log")));
    }

    [Fact]
    public async Task A_start_on_the_old_version_reports_the_failed_switch_as_ready_again()
    {
        var (service, _, switcher, dir) = Create();
        await service.CheckAsync(userInitiated: false);
        await service.SwitchAsync(idle: false);

        var after = new UpdateService(new FakeSource(), switcher, new UpdateLog(Path.Combine(dir, "update.log")),
            Path.Combine(dir, "update-attempt.json"), () => UpdatePolicy.Default);
        var result = after.EvaluateStartup();
        after.ReportFailedSwitch(result.Attempt!);

        Assert.Equal(AttemptOutcome.Failed, result.Outcome);
        Assert.Equal(UpdateStage.Ready, after.Status.Stage);
    }

    [Theory]
    [InlineData(null, null, null, null, InAppUpdatesMode.Full, true, false, null)]
    [InlineData(0, null, null, null, InAppUpdatesMode.Off, true, false, null)]
    [InlineData(1, 0, 1, 4, InAppUpdatesMode.NoticeOnly, false, true, true)]
    [InlineData(2, 1, 0, 2, InAppUpdatesMode.Full, true, false, false)]
    [InlineData(7, null, 5, null, InAppUpdatesMode.Full, true, false, null)]
    public void Policy_values_are_read_like_the_admin_guide_says(int? inApp, int? allowDespite, int? idle, int? storeAutoDownload,
        InAppUpdatesMode mode, bool allow, bool storeBlocks, bool? idleForced)
    {
        var policy = UpdatePolicy.FromValues(inApp, allowDespite, idle, storeAutoDownload);

        Assert.Equal((mode, allow, storeBlocks, idleForced),
            (policy.Mode, policy.AllowDespiteStorePolicy, policy.StoreBlocksAutomaticUpdates, policy.IdleInstallForced));
    }

    [Fact]
    public void Idle_install_follows_the_users_setting_unless_the_policy_decides()
    {
        var (service, _, _, _) = Create();
        Assert.True(service.IdleInstallAllowed(userSetting: true));
        Assert.False(service.IdleInstallAllowed(userSetting: false));

        var (forcedOn, _, _, _) = Create(UpdatePolicy.FromValues(null, null, 1, null));
        Assert.True(forcedOn.IdleInstallAllowed(userSetting: false));

        var (noticeOnly, _, _, _) = Create(UpdatePolicy.FromValues(1, null, 1, null));
        Assert.False(noticeOnly.IdleInstallAllowed(userSetting: true));
    }

    [Fact]
    public void Idle_install_waits_until_nobody_uses_the_app()
    {
        var idle = new IdleState(UpdateReady: true, Allowed: true, WindowVisible: false, QuickPasteVisible: false,
            UnsavedChanges: false, Busy: false, UserIdle: TimeSpan.FromMinutes(11), AppUptime: TimeSpan.FromMinutes(30));
        Assert.True(IdleInstall.ShouldInstall(idle));

        Assert.False(IdleInstall.ShouldInstall(idle with { WindowVisible = true }));
        Assert.False(IdleInstall.ShouldInstall(idle with { QuickPasteVisible = true }));
        Assert.False(IdleInstall.ShouldInstall(idle with { UnsavedChanges = true }));
        Assert.False(IdleInstall.ShouldInstall(idle with { Busy = true }));
        Assert.False(IdleInstall.ShouldInstall(idle with { UserIdle = TimeSpan.FromMinutes(5) }));
        Assert.False(IdleInstall.ShouldInstall(idle with { AppUptime = TimeSpan.FromSeconds(30) }));
        Assert.False(IdleInstall.ShouldInstall(idle with { Allowed = false }));
        Assert.False(IdleInstall.ShouldInstall(idle with { UpdateReady = false }));
    }

    [Fact]
    public void The_update_log_keeps_its_newer_half_when_it_grows_too_big()
    {
        string path = Path.Combine(TestEnvironment.NewScratchDir(), "update.log");
        File.WriteAllText(path, string.Concat(Enumerable.Range(0, 20000).Select(i => $"old line {i:D6}\n")));
        var log = new UpdateLog(path);

        log.Write("newest");

        string text = File.ReadAllText(path);
        Assert.True(new FileInfo(path).Length < UpdateLog.MaxBytes);
        Assert.DoesNotContain("old line 000001", text);
        Assert.Contains("old line 019999", text);
        Assert.EndsWith("newest" + Environment.NewLine, text);
    }
}
