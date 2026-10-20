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
        public bool Silent = true;
        public DownloadResult Result = DownloadResult.Done;
        public FakeSwitcher? StagesInto;
        public int Downloads, ConsentDownloads, DespiteDownloads;
        public Exception? CheckThrows;

        public string Name => "fake";
        public bool CanDownloadSilently => Silent;

        public Task<UpdateOffer?> CheckAsync(CancellationToken cancel) =>
            CheckThrows != null ? Task.FromException<UpdateOffer?>(CheckThrows)
            : Task.FromResult(Offer == null ? null : new UpdateOffer(Offer));

        public Task<DownloadResult> DownloadAsync(UpdateOffer offer, IProgress<double> progress, CancellationToken cancel)
        {
            Downloads++;
            progress.Report(0.5);
            if (Result.Outcome == DownloadOutcome.Completed) StagesInto?.Staged.Add(offer.Version);
            return Task.FromResult(Result);
        }

        public Task<DownloadResult> RequestDownloadAsync(UpdateOffer offer, IntPtr ownerWindow, IProgress<double> progress, CancellationToken cancel)
        {
            ConsentDownloads++;
            if (Result.Outcome == DownloadOutcome.Completed) StagesInto?.Staged.Add(offer.Version);
            return Task.FromResult(Result);
        }

        public Task<DownloadResult> DownloadDespitePolicyAsync(UpdateOffer offer, IProgress<double> progress, CancellationToken cancel)
        {
            DespiteDownloads++;
            if (Result.Outcome == DownloadOutcome.Completed) StagesInto?.Staged.Add(offer.Version);
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeSwitcher : IPackageSwitcher
    {
        public readonly HashSet<Version> Staged = [];
        public readonly List<string> Calls = [];
        public string? SwitchProblem = "0x80073D02";

        public Version RunningVersion { get; set; } = Running;
        public bool IsStaged(Version version) => Staged.Contains(version);
        public void RegisterRestart(string arguments) => Calls.Add("restart " + arguments);
        public void UnregisterRestart() => Calls.Add("no restart");
        public void StartWatchdog(TimeSpan after, string logPath) => Calls.Add("watchdog");
        public Task<string?> SwitchAsync() { Calls.Add("switch"); return Task.FromResult(SwitchProblem); }
    }

    private static (UpdateService Service, FakeSource Source, FakeSwitcher Switcher, string Dir) Create(
        UpdatePolicy? policy = null, bool withSource = true)
    {
        string dir = TestEnvironment.NewScratchDir();
        var switcher = new FakeSwitcher();
        var source = new FakeSource { StagesInto = switcher };
        var service = new UpdateService(withSource ? source : null, switcher, new UpdateLog(Path.Combine(dir, "update.log")),
            Path.Combine(dir, "update-attempt.json"), () => policy ?? UpdatePolicy.Default);
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
    public async Task Store_policy_off_with_automatic_updates_off_downloads_through_the_consent_dialog()
    {
        var (service, source, _, _) = Create(UpdatePolicy.FromValues(null, null, null, 4));
        source.Silent = false;
        service.CheckAutomatically = false;

        await service.CheckAsync(userInitiated: true);
        Assert.Equal((UpdateStage.Available, true, true), (service.Status.Stage, service.Status.CanDownload, service.Status.StoreBlocked));
        Assert.Equal(0, source.Downloads);

        await service.DownloadAsync(IntPtr.Zero, userInitiated: true);
        Assert.Equal((0, 1, 0), (source.Downloads, source.ConsentDownloads, source.DespiteDownloads));
        Assert.Equal(UpdateStage.Ready, service.Status.Stage);
    }

    [Fact]
    public async Task Store_policy_off_without_the_admins_permission_offers_no_download()
    {
        var (service, source, _, _) = Create(UpdatePolicy.FromValues(null, 0, null, 4));

        await service.CheckAsync(userInitiated: false);
        await service.DownloadAsync(IntPtr.Zero, userInitiated: true);

        Assert.Equal((UpdateStage.Available, false), (service.Status.Stage, service.Status.CanDownload));
        Assert.Equal(0, source.Downloads + source.ConsentDownloads);
    }

    [Fact]
    public async Task Store_policy_off_with_automatic_updates_on_downloads_on_its_own_and_arms_the_restart()
    {
        var (service, source, switcher, _) = Create(UpdatePolicy.FromValues(null, null, null, 4));
        source.Silent = false;

        await service.CheckAsync(userInitiated: false);

        Assert.Equal((0, 0, 1), (source.Downloads, source.ConsentDownloads, source.DespiteDownloads));
        Assert.Equal(UpdateStage.Ready, service.Status.Stage);
        Assert.Contains("restart " + UpdateService.UpdatedArgument, switcher.Calls);
    }

    [Fact]
    public async Task Updating_despite_the_store_policy_needs_the_admins_permission()
    {
        var (service, source, _, _) = Create(UpdatePolicy.FromValues(null, 0, null, 4));
        Assert.False(service.Policy.UpdatesDespiteStorePolicy);

        await service.CheckAsync(userInitiated: false);

        Assert.Equal(0, source.Downloads + source.ConsentDownloads + source.DespiteDownloads);
        Assert.Equal(UpdateStage.Available, service.Status.Stage);
    }

    [Fact]
    public async Task Without_the_store_policy_automatic_updates_download_through_the_store()
    {
        var (service, source, _, _) = Create();

        await service.CheckAsync(userInitiated: false);

        Assert.Equal((1, 0), (source.Downloads, source.DespiteDownloads));
        Assert.False(service.Policy.UpdatesDespiteStorePolicy);
    }

    [Fact]
    public async Task Store_policy_off_with_automatic_updates_off_downloads_nothing_on_its_own()
    {
        var (service, source, _, _) = Create(UpdatePolicy.FromValues(null, null, null, 4));
        source.Silent = false;
        service.CheckAutomatically = false;

        await service.CheckAsync(userInitiated: false);

        Assert.Equal(0, source.Downloads + source.ConsentDownloads + source.DespiteDownloads);
    }

    [Fact]
    public async Task Forced_automatic_updates_go_around_the_store_policy_too()
    {
        var (service, source, _, _) = Create(UpdatePolicy.FromValues(null, null, null, 4, automaticUpdates: 1));
        source.Silent = false;
        service.CheckAutomatically = false;

        await service.CheckAsync(userInitiated: false);

        Assert.Equal(1, source.DespiteDownloads);
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

        Assert.Contains("gone", problem);
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
