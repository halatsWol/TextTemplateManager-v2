using System.IO.Compression;
using TextTemplateManager.Data;
using TextTemplateManager.Models;
using Xunit;

namespace TextTemplateManager.Tests;

/// <summary>A backup is only ever a complete, checked zip: the app's files plus copies of the linked sync
/// files, described by a manifest. Anything that doesn't load is refused or recorded, never half-saved.</summary>
public class BackupServiceTests
{
    private readonly string _dir = TestEnvironment.NewScratchDir();
    private readonly StorageService _storage;
    private DateTime _now = new(2026, 10, 12, 9, 30, 15);
    private readonly BackupService _backups;

    public BackupServiceTests()
    {
        _storage = new StorageService(_dir);
        _backups = new BackupService(_storage, () => _now);
    }

    private static Folder Tree(string title) => new()
    {
        Title = "Root",
        LastChange = "20261012090000",
        Children = { new Template { Title = title, Content = $"<p>{title}</p>", LastChange = "20261012090000" } },
    };

    private Task<BackupResult> Create(BackupTier tier = BackupTier.Recent) =>
        _backups.CreateAsync(tier, BackupSource.Live(_storage), quietMs: 0);

    private async Task<SyncSource> LinkSyncFile(string name, bool write = true)
    {
        string path = Path.Combine(_dir, "shared", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (write) await _storage.SaveSharedAsync(path, Tree(name));
        var source = new SyncSource { Name = name, Path = path };
        var sync = File.Exists(_storage.GetSyncSettingsPath()) ? await _storage.LoadSyncSettingsAsync() : new SyncSettings();
        sync.Sources.Add(source);
        await _storage.SaveSyncSettingsAsync(sync);
        return source;
    }

    [Fact]
    public async Task A_backup_holds_the_app_files_and_the_sync_files_and_checks_out()
    {
        await _storage.SaveAsync(_storage.GetDataPath(), Tree("Alpha"));
        await _storage.SaveSettingsAsync(new AppSettings { PasteWindowHotkey = "Ctrl+Alt+Q" });
        var team = await LinkSyncFile("team.ttmdata");

        var result = await Create(BackupTier.Daily);

        Assert.Equal(BackupOutcome.Created, result.Outcome);
        Assert.Equal("ttm-backup-daily-20261012-093015.zip", Path.GetFileName(result.Path));
        Assert.Equal(_storage.BackupDirectory, Path.GetDirectoryName(result.Path));
        Assert.Null(BackupService.Verify(result.Path!, out var manifest));
        Assert.Equal(BackupTier.Daily, manifest!.Tier);
        Assert.Equal(_now.ToUniversalTime(), manifest.CreatedUtc);

        using var zip = ZipFile.OpenRead(result.Path!);
        Assert.Equal(new[] { "data.ttmdata", "settings.ttmsettings", "sync.ttmsettings", $"sync/{team.Id:N}.ttmdata", "manifest.json" },
            zip.Entries.Select(e => e.FullName));
        var syncEntry = manifest.Files.Single(f => f.Kind == BackupFileKind.SyncFile);
        Assert.Equal(team.Path, syncEntry.OriginalPath);
        Assert.Equal(team.Id, syncEntry.SourceId);
        using var copy = new StreamReader(zip.GetEntry(syncEntry.Name)!.Open());
        Assert.Equal(await File.ReadAllTextAsync(team.Path), await copy.ReadToEndAsync());
    }

    [Fact]
    public async Task A_sync_file_that_cant_be_read_is_recorded_and_the_rest_is_kept()
    {
        await _storage.SaveAsync(_storage.GetDataPath(), Tree("Alpha"));
        var missing = await LinkSyncFile("gone.ttmdata", write: false);
        var broken = await LinkSyncFile("broken.ttmdata", write: false);
        await File.WriteAllTextAsync(broken.Path, "{ not json");

        var result = await Create();

        Assert.Equal(BackupOutcome.Created, result.Outcome);
        Assert.Null(BackupService.Verify(result.Path!, out var manifest));
        var skipped = manifest!.Files.Where(f => f.Kind == BackupFileKind.SyncFile).ToList();
        Assert.All(skipped, f => Assert.False(f.Included));
        Assert.Equal(new[] { "missing", "not a valid sync file" }, skipped.Select(f => f.Problem));
        Assert.Equal(new[] { missing.Path, broken.Path }, skipped.Select(f => f.OriginalPath));
    }

    [Fact]
    public async Task No_data_file_means_no_backup()
    {
        Assert.Equal(BackupOutcome.NothingToBackUp, (await Create()).Outcome);
        Assert.Empty(_backups.List());
    }

    [Theory]
    [InlineData(BackupTier.Recent)]
    [InlineData(BackupTier.TwoHourly)]
    [InlineData(BackupTier.Daily)]
    public async Task An_empty_state_gets_no_automatic_backup(BackupTier tier)
    {
        await _storage.SaveAsync(_storage.GetDataPath(), new Folder { Title = "Root", LastChange = "20261012090000" });
        await _storage.SaveSettingsAsync(new AppSettings { TutorialOfferDone = true });

        Assert.Equal(BackupOutcome.NothingToBackUp, (await Create(tier)).Outcome);
        Assert.Empty(_backups.List());
    }

    [Fact]
    public async Task An_empty_tree_with_a_sync_source_or_an_export_is_still_backed_up()
    {
        await _storage.SaveAsync(_storage.GetDataPath(), new Folder { Title = "Root", LastChange = "20261012090000" });
        Assert.Equal(BackupOutcome.Created, (await Create(BackupTier.Exported)).Outcome);

        await LinkSyncFile("team.ttmdata");
        Assert.Equal(BackupOutcome.Created, (await Create(BackupTier.Recent)).Outcome);
    }

    [Fact]
    public async Task An_app_file_that_doesnt_load_stops_the_backup_and_leaves_nothing_behind()
    {
        await _storage.SaveAsync(_storage.GetDataPath(), Tree("Alpha"));
        await File.WriteAllTextAsync(_storage.GetSettingsPath(), "{ broken");

        var result = await Create();

        Assert.Equal(BackupOutcome.SourceUnreadable, result.Outcome);
        Assert.False(Directory.Exists(_storage.BackupDirectory) && Directory.EnumerateFileSystemEntries(_storage.BackupDirectory).Any());
    }

    [Fact]
    public async Task A_backup_waits_for_the_files_to_go_quiet()
    {
        await _storage.SaveAsync(_storage.GetDataPath(), Tree("Alpha"));   // just written

        var result = await _backups.CreateAsync(BackupTier.Recent, BackupSource.Live(_storage),
            quietMs: 60_000, quietTimeout: TimeSpan.FromMilliseconds(300));

        Assert.Equal(BackupOutcome.NotQuiet, result.Outcome);
    }

    [Fact]
    public async Task A_v1_backup_is_made_from_another_folder()
    {
        string v1 = TestEnvironment.NewScratchDir();
        await new StorageService(v1).SaveAsync(Path.Combine(v1, "data.ttmdata"), Tree("Old"));
        await File.WriteAllTextAsync(Path.Combine(v1, "settings.ttmsettings"), "﻿{\"PasteWindowHotkey\": \"Shift+Alt+Y\"}");

        var result = await _backups.CreateAsync(BackupTier.V1, BackupSource.Folder(v1), quietMs: 0);

        Assert.Equal(BackupOutcome.Created, result.Outcome);
        Assert.StartsWith("ttm-backup-v1-", Path.GetFileName(result.Path));
        Assert.Null(BackupService.Verify(result.Path!, out var manifest));
        Assert.Equal(new[] { BackupFileKind.Data, BackupFileKind.Settings }, manifest!.Files.Select(f => f.Kind));
    }

    [Fact]
    public async Task The_v1_backup_is_made_once_and_only_when_there_is_1x_data()
    {
        Assert.Equal(BackupOutcome.NothingToBackUp, (await _backups.EnsureV1BackupAsync()).Outcome);
        Assert.False(_backups.HasLegacyData);

        Directory.CreateDirectory(_storage.LegacyDirectory);
        await new StorageService(_storage.LegacyDirectory).SaveAsync(Path.Combine(_storage.LegacyDirectory, "data.ttmdata"), Tree("Old"));
        string before = File.ReadAllText(Path.Combine(_storage.LegacyDirectory, "data.ttmdata"));
        Assert.True(_backups.HasLegacyData);

        var first = await _backups.EnsureV1BackupAsync();
        _now = _now.AddDays(1);
        var second = await _backups.EnsureV1BackupAsync();

        Assert.Equal(BackupOutcome.Created, first.Outcome);
        Assert.Equal(BackupOutcome.Unchanged, second.Outcome);
        Assert.Equal(new[] { BackupTier.V1 }, _backups.List().Select(b => b.Tier));
        Assert.Equal(before, File.ReadAllText(Path.Combine(_storage.LegacyDirectory, "data.ttmdata")));   // only read

        File.WriteAllText(first.Path!, "damaged");   // a damaged one doesn't count
        Assert.Equal(BackupOutcome.Created, (await _backups.EnsureV1BackupAsync()).Outcome);
    }

    [Fact]
    public void The_first_start_offers_the_1x_data_and_the_newest_backup_that_can_be_restored()
    {
        BackupInfo Info(BackupTier tier, string? problem = null) => new($"{tier}{problem}.zip", tier, _now, null, problem);
        var damagedNewest = Info(BackupTier.Recent, "no manifest");
        var daily = Info(BackupTier.Daily);
        var v1 = Info(BackupTier.V1);

        Assert.Equal(new FirstStartOffer(v1, daily), FirstStartOffer.From(new[] { damagedNewest, v1, daily }));
        Assert.Equal(new FirstStartOffer(null, daily), FirstStartOffer.From(new[] { daily }));
        Assert.Equal(new FirstStartOffer(v1, null), FirstStartOffer.From(new[] { v1, damagedNewest }));
        Assert.False(FirstStartOffer.From(new[] { damagedNewest, Info(BackupTier.V1, "data.ttmdata is damaged") }).HasChoice);
        Assert.False(FirstStartOffer.From(Array.Empty<BackupInfo>()).HasChoice);
    }

    [Fact]
    public async Task Damage_is_found_and_listed_backups_are_newest_first()
    {
        await _storage.SaveAsync(_storage.GetDataPath(), Tree("Alpha"));
        string older = (await Create(BackupTier.Daily)).Path!;
        _now = _now.AddHours(3);
        string newer = (await Create(BackupTier.TwoHourly)).Path!;
        _now = _now.AddHours(1);
        string tampered = (await Create()).Path!;
        using (var zip = ZipFile.Open(tampered, ZipArchiveMode.Update))
        {
            zip.GetEntry("data.ttmdata")!.Delete();
            using var w = new StreamWriter(zip.CreateEntry("data.ttmdata").Open());
            w.Write("{\"Title\":\"Root\",\"Children\":[]}");
        }
        string noManifest = Path.Combine(_storage.BackupDirectory, "ttm-backup-recent-20200101-000000.zip");
        using (var zip = ZipFile.Open(noManifest, ZipArchiveMode.Create)) zip.CreateEntry("data.ttmdata");
        File.WriteAllText(Path.Combine(_storage.BackupDirectory, "ttm-backup-recent-20991231-000000.zip.partial"), "");
        File.WriteAllText(Path.Combine(_storage.BackupDirectory, "holiday-photos.zip"), "");

        var list = _backups.List();

        Assert.Equal(new[] { tampered, newer, older, noManifest }, list.Select(b => b.Path));
        Assert.Equal(new[] { BackupTier.Recent, BackupTier.TwoHourly, BackupTier.Daily, BackupTier.Recent }, list.Select(b => b.Tier));
        Assert.Equal(new[] { "data.ttmdata is damaged", null, null, "no manifest" }, list.Select(b => b.Problem));
        Assert.Equal(new DateTime(2026, 10, 12, 12, 30, 15), list[1].Created);
    }

    [Fact]
    public async Task An_exported_backup_goes_where_it_was_asked_and_replaces_a_file_there()
    {
        await _storage.SaveAsync(_storage.GetDataPath(), Tree("Alpha"));
        string target = Path.Combine(TestEnvironment.NewScratchDir(), "elsewhere", "my backup.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await File.WriteAllTextAsync(target, "old file the save dialog agreed to replace");

        var result = await _backups.CreateAsync(BackupTier.Exported, BackupSource.Live(_storage), quietMs: 0, targetPath: target);

        Assert.Equal(BackupOutcome.Created, result.Outcome);
        Assert.Equal(target, result.Path);
        Assert.Null(BackupService.Verify(target, out var manifest));
        Assert.Equal(BackupTier.Exported, manifest!.Tier);
        Assert.False(File.Exists(target + ".partial"));
        Assert.Empty(_backups.List());   // not one of the automatic backups
    }

    [Fact]
    public async Task A_backups_contents_say_which_parts_it_can_restore()
    {
        await _storage.SaveAsync(_storage.GetDataPath(), Tree("Alpha"));
        string plain = (await Create()).Path!;

        await _storage.SaveSettingsAsync(new AppSettings());
        await LinkSyncFile("team.ttmdata");
        var data = Tree("Alpha");
        data.Children.Add(new Folder { Title = "Team", SyncId = Guid.Empty });   // a synced folder's cached copy
        await _storage.SaveAsync(_storage.GetDataPath(), data);
        _now = _now.AddMinutes(1);
        string full = (await Create()).Path!;

        Assert.Equal(new BackupContents(Local: true, Settings: false, SyncSettings: false, SyncedFolders: false), BackupService.ReadContents(plain));
        Assert.Equal(RestoreParts.All, BackupService.ReadContents(full)!.Available);
    }

    [Fact]
    public async Task Half_written_backups_are_cleared_away()
    {
        Directory.CreateDirectory(_storage.BackupDirectory);
        string partial = Path.Combine(_storage.BackupDirectory, "ttm-backup-recent-20261012-093015.zip.partial");
        string other = Path.Combine(_storage.BackupDirectory, "notes.partial");
        await File.WriteAllTextAsync(partial, "");
        await File.WriteAllTextAsync(other, "");

        _backups.DeleteStalePartials();

        Assert.False(File.Exists(partial));
        Assert.True(File.Exists(other));
    }
}
