using TextTemplateManager.Data;
using TextTemplateManager.Models;
using Xunit;

namespace TextTemplateManager.Tests;

/// <summary>When the automatic backups happen and how many stay, driven by a fake clock and explicit passes
/// instead of the 30-second timer.</summary>
public class BackupSchedulerTests
{
    private readonly StorageService _storage = new(TestEnvironment.NewScratchDir());
    private readonly BackupService _backups;
    private readonly BackupScheduler _scheduler;
    private DateTime _now = new(2026, 10, 12, 9, 0, 0);

    public BackupSchedulerTests()
    {
        _backups = new BackupService(_storage, () => _now);
        _scheduler = new BackupScheduler(_backups, _storage, () => _now, quietMs: 0);
    }

    // Ids derived from the title, so saving the same title twice writes identical files.
    private static Guid StableId(string seed) =>
        new(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(seed)));

    private Task Save(string title) => _storage.SaveAsync(_storage.GetDataPath(), new Folder
    {
        Id = StableId("Root"),
        Title = "Root",
        LastChange = "20261012090000",
        Children = { new Template { Id = StableId(title), Title = title, Content = $"<p>{title}</p>", LastChange = "20261012090000" } },
    });

    private async Task At(TimeSpan offset)
    {
        _now = new DateTime(2026, 10, 12, 9, 0, 0) + offset;
        await _scheduler.TickAsync();
    }

    private string[] Names(BackupTier tier) =>
        _backups.List().Where(b => b.Tier == tier).Select(b => Path.GetFileName(b.Path)).ToArray();

    [Fact]
    public async Task Startup_makes_a_daily_and_a_two_hourly_but_no_recent_backup()
    {
        await Save("Alpha");

        await At(TimeSpan.Zero);

        Assert.Equal(new[] { "ttm-backup-daily-20261012-090000.zip" }, Names(BackupTier.Daily));
        Assert.Equal(new[] { "ttm-backup-2h-20261012-090000.zip" }, Names(BackupTier.TwoHourly));
        Assert.Empty(Names(BackupTier.Recent));
    }

    [Fact]
    public async Task A_recent_backup_comes_two_minutes_after_the_last_change()
    {
        await Save("Alpha");
        await At(TimeSpan.Zero);

        await Save("Beta");
        await At(TimeSpan.FromMinutes(1));         // a pass sees the change
        await Save("Gamma");
        await At(TimeSpan.FromMinutes(1.5));       // and the next one restarts the two minutes
        await At(TimeSpan.FromMinutes(3));
        Assert.Empty(Names(BackupTier.Recent));

        await At(TimeSpan.FromMinutes(3.5));
        Assert.Equal(new[] { "ttm-backup-recent-20261012-090330.zip" }, Names(BackupTier.Recent));
        await At(TimeSpan.FromMinutes(10));        // nothing new since
        Assert.Single(Names(BackupTier.Recent));
    }

    [Fact]
    public async Task A_save_that_changed_nothing_makes_no_new_recent_backup()
    {
        await Save("Alpha");
        await At(TimeSpan.Zero);
        await Save("Beta");
        await At(TimeSpan.FromMinutes(1));
        await At(TimeSpan.FromMinutes(4));
        Assert.Single(Names(BackupTier.Recent));

        await Save("Beta");                        // same content, still a completed save
        await At(TimeSpan.FromMinutes(5));
        await At(TimeSpan.FromMinutes(8));

        Assert.Single(Names(BackupTier.Recent));
    }

    [Fact]
    public async Task Three_recent_backups_are_kept_and_damaged_ones_dont_count()
    {
        await Save("v0");
        await At(TimeSpan.Zero);
        Directory.CreateDirectory(_storage.BackupDirectory);
        string damaged = Path.Combine(_storage.BackupDirectory, "ttm-backup-recent-20261012-085959.zip");
        await File.WriteAllTextAsync(damaged, "not a zip");

        for (int i = 1; i <= 4; i++)
        {
            await Save($"v{i}");
            await At(TimeSpan.FromMinutes(10 * i));
            await At(TimeSpan.FromMinutes(10 * i + 3));
        }

        Assert.Equal(new[]
        {
            "ttm-backup-recent-20261012-094300.zip", "ttm-backup-recent-20261012-093300.zip",
            "ttm-backup-recent-20261012-092300.zip", "ttm-backup-recent-20261012-085959.zip",
        }, Names(BackupTier.Recent));
    }

    [Fact]
    public async Task Two_hourly_backups_follow_the_interval_and_skip_when_nothing_changed()
    {
        await Save("Alpha");
        await At(TimeSpan.Zero);

        await At(TimeSpan.FromHours(2));           // due, but nothing changed
        Assert.Single(Names(BackupTier.TwoHourly));

        await Save("Beta");
        await At(TimeSpan.FromHours(3.9));         // checked at 2 h, so the next is due at 4 h
        Assert.Single(Names(BackupTier.TwoHourly));
        await At(TimeSpan.FromHours(4));
        Assert.Equal(2, Names(BackupTier.TwoHourly).Length);
    }

    [Fact]
    public async Task The_next_daily_comes_with_the_first_change_after_midnight()
    {
        await Save("Alpha");
        await At(TimeSpan.Zero);

        await At(TimeSpan.FromHours(16));          // next day, 01:00, no change
        Assert.Single(Names(BackupTier.Daily));

        await Save("Beta");
        await At(TimeSpan.FromHours(16.5));
        Assert.Equal(new[] { "ttm-backup-daily-20261013-013000.zip", "ttm-backup-daily-20261012-090000.zip" }, Names(BackupTier.Daily));
    }

    [Fact]
    public async Task A_restart_on_the_same_day_makes_no_second_daily()
    {
        await Save("Alpha");
        await At(TimeSpan.Zero);

        var restarted = new BackupScheduler(_backups, _storage, () => _now, quietMs: 0);
        _now = _now.AddHours(1);
        await restarted.TickAsync();

        Assert.Single(Names(BackupTier.Daily));
        Assert.Single(Names(BackupTier.TwoHourly));
    }

    [Fact]
    public async Task Exit_backs_up_changes_without_waiting_the_two_minutes()
    {
        await Save("Alpha");
        await At(TimeSpan.Zero);
        await Save("Beta");
        _now = _now.AddSeconds(5);

        await _scheduler.FlushAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(new[] { "ttm-backup-recent-20261012-090005.zip" }, Names(BackupTier.Recent));
    }

    [Fact]
    public async Task A_stopped_scheduler_makes_no_more_backups()
    {
        await Save("Alpha");
        await At(TimeSpan.Zero);
        await Save("Beta");
        await At(TimeSpan.FromMinutes(1));

        await _scheduler.StopAsync();
        await At(TimeSpan.FromHours(5));

        Assert.Empty(Names(BackupTier.Recent));
        Assert.Single(Names(BackupTier.TwoHourly));
    }

    [Fact]
    public async Task Half_written_backups_from_a_crash_go_at_startup()
    {
        Directory.CreateDirectory(_storage.BackupDirectory);
        string partial = Path.Combine(_storage.BackupDirectory, "ttm-backup-recent-20261011-120000.zip.partial");
        await File.WriteAllTextAsync(partial, "");

        await At(TimeSpan.Zero);

        Assert.False(File.Exists(partial));
    }
}
