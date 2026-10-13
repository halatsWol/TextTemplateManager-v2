using System.IO.Compression;
using TextTemplateManager.Data;
using TextTemplateManager.Models;
using Xunit;

namespace TextTemplateManager.Tests;

/// <summary>A restore puts back all chosen files or none, never a mix, and never overwrites a linked sync file
/// that still exists.</summary>
public class RestoreServiceTests
{
    private readonly string _dir = TestEnvironment.NewScratchDir();
    private readonly string _shared = TestEnvironment.NewScratchDir();
    private readonly StorageService _storage;
    private readonly BackupService _backups;
    private readonly RestoreService _restore;

    public RestoreServiceTests()
    {
        _storage = new StorageService(_dir);
        _backups = new BackupService(_storage);
        _restore = new RestoreService(_storage);
    }

    private static Folder Tree(string title) => new()
    {
        Title = "Root",
        LastChange = "20261012090000",
        Children = { new Template { Title = title, Content = $"<p>{title}</p>", LastChange = "20261012090000" } },
    };

    private readonly SyncSource _kept = new() { Name = "Kept" };
    private readonly SyncSource _lost = new() { Name = "Lost" };

    // State A, backed up: data "A", hotkey A, two linked sync files.
    private async Task<string> BackUpStateA()
    {
        _kept.Path = Path.Combine(_shared, "kept.ttmdata");
        _lost.Path = Path.Combine(_shared, "lost.ttmdata");
        await _storage.SaveAsync(_storage.GetDataPath(), Tree("A"));
        await _storage.SaveSettingsAsync(new AppSettings { PasteWindowHotkey = "Ctrl+Alt+A" });
        await _storage.SaveSharedAsync(_kept.Path, Tree("Kept A"));
        await _storage.SaveSharedAsync(_lost.Path, Tree("Lost A"));
        await _storage.SaveSyncSettingsAsync(new SyncSettings { Sources = { _kept, _lost } });
        var result = await _backups.CreateAsync(BackupTier.Recent, BackupSource.Live(_storage), quietMs: 0);
        Assert.Equal(BackupOutcome.Created, result.Outcome);
        return result.Path!;
    }

    // State B: everything changed, one sync file gone.
    private async Task ChangeToStateB()
    {
        await _storage.SaveAsync(_storage.GetDataPath(), Tree("B"));
        await _storage.SaveSettingsAsync(new AppSettings { PasteWindowHotkey = "Ctrl+Alt+B" });
        await _storage.SaveSharedAsync(_kept.Path, Tree("Kept B"));
        File.Delete(_lost.Path);
        await _storage.SaveSyncSettingsAsync(new SyncSettings());
    }

    private string Read(string path) => File.ReadAllText(path);

    [Fact]
    public async Task Restoring_everything_brings_back_the_app_files_and_never_writes_a_sync_file()
    {
        string backup = await BackUpStateA();
        string dataA = Read(_storage.GetDataPath()), settingsA = Read(_storage.GetSettingsPath()), syncA = Read(_storage.GetSyncSettingsPath());
        await ChangeToStateB();

        var result = await _restore.RestoreAsync(backup, RestoreParts.All);

        Assert.True(result.Succeeded, result.Problem);
        Assert.Equal(dataA, Read(_storage.GetDataPath()));
        Assert.Equal(settingsA, Read(_storage.GetSettingsPath()));
        Assert.Equal(syncA, Read(_storage.GetSyncSettingsPath()));
        Assert.Contains("Kept B", Read(_kept.Path));   // linked files keep what they have
        Assert.False(File.Exists(_lost.Path));         // a gone one shows from the cache in the local data
        Assert.Equal(new[] { "Backups" }, Directory.GetDirectories(_dir).Select(Path.GetFileName));   // no work or copy folders left
    }

    // A data file as the app keeps it: a synced folder's cached copy (a top-level folder with a SyncId) and local items.
    private static readonly Guid TeamId = Guid.NewGuid();
    private static Folder DataWith(string synced, string local) => new()
    {
        Title = "Root",
        LastChange = "20261012090000",
        Children =
        {
            new Folder { Id = TeamId, Title = "Team", SyncId = Guid.Empty, LastChange = "20261012090000",
                Children = { new Template { Title = synced, Content = "<p>s</p>", LastChange = "20261012090000" } } },
            new Template { Title = local, Content = "<p>l</p>", LastChange = "20261012090000" },
        },
    };

    private async Task<string> BackUpData(Folder data)
    {
        await _storage.SaveAsync(_storage.GetDataPath(), data);
        await _storage.SaveSyncSettingsAsync(new SyncSettings { Sources = { new SyncSource { Id = TeamId, Name = "Team A" } } });
        return (await _backups.CreateAsync(BackupTier.Recent, BackupSource.Live(_storage), quietMs: 0)).Path!;
    }

    private async Task<string[]> Titles()
    {
        var tree = await StorageService.LoadRootAsync(_storage.GetDataPath());
        return tree!.Children.Select(c => c is Folder f ? $"{f.Title}/{f.Children[0].Title}" : c.Title).ToArray();
    }

    [Fact]
    public async Task Restoring_only_the_synced_folders_keeps_the_current_local_items()
    {
        string backup = await BackUpData(DataWith("Synced A", "Local A"));
        await _storage.SaveAsync(_storage.GetDataPath(), DataWith("Synced B", "Local B"));

        var result = await _restore.RestoreAsync(backup, RestoreParts.SyncedFolders);

        Assert.True(result.Succeeded, result.Problem);
        Assert.Equal(new[] { "Team/Synced A", "Local B" }, await Titles());
    }

    [Fact]
    public async Task Restoring_only_the_local_items_keeps_the_current_synced_folders()
    {
        string backup = await BackUpData(DataWith("Synced A", "Local A"));
        await _storage.SaveAsync(_storage.GetDataPath(), DataWith("Synced B", "Local B"));

        var result = await _restore.RestoreAsync(backup, RestoreParts.Local);

        Assert.True(result.Succeeded, result.Problem);
        Assert.Equal(new[] { "Team/Synced B", "Local A" }, await Titles());
    }

    [Fact]
    public async Task Restoring_only_the_sync_settings_leaves_the_data_file_alone()
    {
        string backup = await BackUpData(DataWith("Synced A", "Local A"));
        string syncA = Read(_storage.GetSyncSettingsPath());
        await _storage.SaveAsync(_storage.GetDataPath(), DataWith("Synced B", "Local B"));
        string dataB = Read(_storage.GetDataPath());
        await _storage.SaveSyncSettingsAsync(new SyncSettings());

        var result = await _restore.RestoreAsync(backup, RestoreParts.SyncSettings);

        Assert.True(result.Succeeded, result.Problem);
        Assert.Equal(syncA, Read(_storage.GetSyncSettingsPath()));
        Assert.Equal(dataB, Read(_storage.GetDataPath()));
    }

    [Fact]
    public async Task Restoring_only_the_local_templates_leaves_settings_and_sync_alone()
    {
        string backup = await BackUpStateA();
        string dataA = Read(_storage.GetDataPath());
        await ChangeToStateB();
        string settingsB = Read(_storage.GetSettingsPath()), syncB = Read(_storage.GetSyncSettingsPath());

        var result = await _restore.RestoreAsync(backup, RestoreParts.Local);

        Assert.True(result.Succeeded, result.Problem);
        Assert.Equal(dataA, Read(_storage.GetDataPath()));
        Assert.Equal(settingsB, Read(_storage.GetSettingsPath()));
        Assert.Equal(syncB, Read(_storage.GetSyncSettingsPath()));
    }

    [Fact]
    public async Task A_damaged_backup_is_refused_and_nothing_changes()
    {
        string backup = await BackUpStateA();
        await ChangeToStateB();
        string dataB = Read(_storage.GetDataPath());
        using (var zip = ZipFile.Open(backup, ZipArchiveMode.Update))
        {
            zip.GetEntry("settings.ttmsettings")!.Delete();
            using var w = new StreamWriter(zip.CreateEntry("settings.ttmsettings").Open());
            w.Write("{}");
        }

        var result = await _restore.RestoreAsync(backup, RestoreParts.All);

        Assert.False(result.Succeeded);
        Assert.Contains("settings.ttmsettings is damaged", result.Problem);
        Assert.Equal(dataB, Read(_storage.GetDataPath()));
    }

    [Fact]
    public async Task A_file_that_cant_be_replaced_puts_everything_back()
    {
        string backup = await BackUpStateA();
        await ChangeToStateB();
        string dataB = Read(_storage.GetDataPath()), settingsB = Read(_storage.GetSettingsPath());

        RestoreResult result;
        // Readable (so it can be copied aside) but not replaceable: data is swapped first, then this fails.
        using (new FileStream(_storage.GetSettingsPath(), FileMode.Open, FileAccess.Read, FileShare.Read))
            result = await _restore.RestoreAsync(backup, RestoreParts.All);

        Assert.False(result.Succeeded);
        Assert.Equal(dataB, Read(_storage.GetDataPath()));
        Assert.Equal(settingsB, Read(_storage.GetSettingsPath()));
        Assert.False(Directory.Exists(Path.Combine(_dir, "restore-work")));
    }

    [Fact]
    public async Task A_restore_cut_off_by_a_crash_is_undone_at_the_next_start()
    {
        await _storage.SaveAsync(_storage.GetDataPath(), Tree("Before"));
        string before = Read(_storage.GetDataPath());
        string work = Path.Combine(_dir, "restore-work");
        Directory.CreateDirectory(Path.Combine(work, "old"));
        File.Copy(_storage.GetDataPath(), Path.Combine(work, "old", "0"));
        string recovered = _storage.GetSettingsPath();   // didn't exist before the restore
        await File.WriteAllBytesAsync(Path.Combine(work, "journal.json"), StorageService.SerializeJournal(new RestoreJournal
        {
            Items =
            {
                new RestoreJournalItem { Target = _storage.GetDataPath(), Previous = Path.Combine(work, "old", "0") },
                new RestoreJournalItem { Target = recovered, Previous = null },
            },
        }));
        await _storage.SaveAsync(_storage.GetDataPath(), Tree("Half restored"));   // the swap got this far
        Directory.CreateDirectory(Path.GetDirectoryName(recovered)!);
        await File.WriteAllTextAsync(recovered, "{}");

        _restore.RecoverInterrupted();

        Assert.Equal(before, Read(_storage.GetDataPath()));
        Assert.False(File.Exists(recovered));
        Assert.False(Directory.Exists(work));
    }

    [Fact]
    public async Task A_file_that_cant_be_put_back_yet_keeps_the_journal_for_the_next_start()
    {
        await _storage.SaveAsync(_storage.GetDataPath(), Tree("Before"));
        string before = Read(_storage.GetDataPath());
        string work = Path.Combine(_dir, "restore-work");
        Directory.CreateDirectory(Path.Combine(work, "old"));
        File.Copy(_storage.GetDataPath(), Path.Combine(work, "old", "0"));
        await File.WriteAllBytesAsync(Path.Combine(work, "journal.json"), StorageService.SerializeJournal(new RestoreJournal
        {
            Items = { new RestoreJournalItem { Target = _storage.GetDataPath(), Previous = Path.Combine(work, "old", "0") } },
        }));
        await _storage.SaveAsync(_storage.GetDataPath(), Tree("Half restored"));

        using (new FileStream(_storage.GetDataPath(), FileMode.Open, FileAccess.Read, FileShare.Read))
            _restore.RecoverInterrupted();
        Assert.True(File.Exists(Path.Combine(work, "journal.json")));

        _restore.RecoverInterrupted();
        Assert.Equal(before, Read(_storage.GetDataPath()));
        Assert.False(Directory.Exists(work));
    }

    private static string UnusedDrive() =>
        "QRSTUVWXYZJKLMNOP".Select(c => $"{c}:\\").First(root => !Directory.Exists(root));

    [Fact]
    public void A_sync_files_location_says_whether_it_can_be_recreated()
    {
        string file = Path.Combine(_shared, "here.ttmdata");
        File.WriteAllText(file, "{}");

        Assert.Null(RestoreService.LocationProblem(file));
        Assert.Equal(SyncLocationProblem.FileMissing, RestoreService.LocationProblem(Path.Combine(_shared, "gone.ttmdata")));
        Assert.Equal(SyncLocationProblem.FolderMissing, RestoreService.LocationProblem(Path.Combine(_shared, "a", "b", "gone.ttmdata")));
        Assert.Equal(SyncLocationProblem.Unreachable, RestoreService.LocationProblem(UnusedDrive() + @"Team\team.ttmdata"));
        Assert.Equal(SyncLocationProblem.Unreachable, RestoreService.LocationProblem(Path.Combine(file, "below a file.ttmdata")));
        Assert.Equal(SyncLocationProblem.Unreachable, RestoreService.LocationProblem(Path.Combine(_shared, "a<b", "x.ttmdata")));
        Assert.Equal(SyncLocationProblem.Unreachable, RestoreService.LocationProblem(@"relative\x.ttmdata"));
    }

    [Fact]
    public async Task Missing_sync_files_of_a_backup_are_listed_with_whether_it_has_a_copy()
    {
        string backup = await BackUpStateA();
        File.Delete(_lost.Path);
        var never = new SyncSource { Name = "Never", Path = Path.Combine(_shared, "never", "there.ttmdata") };
        await _storage.SaveSyncSettingsAsync(new SyncSettings { Sources = { _kept, _lost, never } });
        string withNever = (await _backups.CreateAsync(BackupTier.Daily, BackupSource.Live(_storage), quietMs: 0)).Path!;

        Assert.Equal(new[] { new SyncLocationIssue(_lost.Id, "Lost", _lost.Path, SyncLocationProblem.FileMissing, HasCopy: true) },
            RestoreService.FindSyncLocationIssues(backup));
        Assert.Equal(new[] { (_lost.Id, false), (never.Id, false) },
            RestoreService.FindSyncLocationIssues(withNever).Select(i => (i.SourceId, i.HasCopy)));   // missing when backed up too
    }

    [Fact]
    public async Task A_placed_sync_file_is_written_from_the_backup_and_the_restored_settings_point_at_it()
    {
        string backup = await BackUpStateA();
        string lostA = Read(_lost.Path);
        await ChangeToStateB();
        string moved = Path.Combine(_shared, "new", "folder", "lost.ttmdata");

        var result = await _restore.RestoreAsync(backup, RestoreParts.SyncSettings,
            new[] { new SyncFilePlacement(_lost.Id, moved, WriteCopy: true) });

        Assert.True(result.Succeeded, result.Problem);
        Assert.Equal(lostA, Read(moved));
        var sync = await _storage.LoadSyncSettingsAsync();
        Assert.Equal(new[] { _kept.Path, moved }, sync.Sources.Select(s => s.Path));
        Assert.Contains("Kept B", Read(_kept.Path));
    }

    [Fact]
    public async Task Without_a_copy_of_the_file_the_synced_folders_cached_copy_is_written()
    {
        string teamPath = Path.Combine(_shared, "team.ttmdata");   // never written: the backup has only the cached folder
        await _storage.SaveAsync(_storage.GetDataPath(), DataWith("Synced A", "Local A"));
        await _storage.SaveSyncSettingsAsync(new SyncSettings { Sources = { new SyncSource { Id = TeamId, Name = "Team", Path = teamPath } } });
        string backup = (await _backups.CreateAsync(BackupTier.Recent, BackupSource.Live(_storage), quietMs: 0)).Path!;
        Assert.True(RestoreService.FindSyncLocationIssues(backup).Single().HasCopy);

        var result = await _restore.RestoreAsync(backup, RestoreParts.SyncSettings, new[] { new SyncFilePlacement(TeamId, teamPath, WriteCopy: true) });

        Assert.True(result.Succeeded, result.Problem);
        var file = await StorageService.LoadRootAsync(teamPath);
        Assert.Equal(Guid.Empty, file!.Id);
        Assert.Equal("Synced A", Assert.Single(file.Children).Title);
    }

    [Fact]
    public async Task A_linked_file_is_kept_as_it_is()
    {
        string backup = await BackUpStateA();
        string other = Path.Combine(_shared, "other.ttmdata");
        await _storage.SaveSharedAsync(other, Tree("Other"));
        string otherBefore = Read(other);

        var result = await _restore.RestoreAsync(backup, RestoreParts.SyncSettings, new[] { new SyncFilePlacement(_lost.Id, other, WriteCopy: false) });

        Assert.True(result.Succeeded, result.Problem);
        Assert.Equal(otherBefore, Read(other));
        Assert.Equal(other, (await _storage.LoadSyncSettingsAsync()).Sources.Single(s => s.Id == _lost.Id).Path);
    }

    [Fact]
    public async Task A_failed_restore_takes_a_placed_sync_file_back_out()
    {
        string backup = await BackUpStateA();
        await ChangeToStateB();
        string dataB = Read(_storage.GetDataPath()), syncB = Read(_storage.GetSyncSettingsPath());

        RestoreResult result;
        // The sync settings are swapped last, after the placed file.
        using (new FileStream(_storage.GetSyncSettingsPath(), FileMode.Open, FileAccess.Read, FileShare.Read))
            result = await _restore.RestoreAsync(backup, RestoreParts.All, new[] { new SyncFilePlacement(_lost.Id, _lost.Path, WriteCopy: true) });

        Assert.False(result.Succeeded);
        Assert.False(File.Exists(_lost.Path));
        Assert.Equal(dataB, Read(_storage.GetDataPath()));
        Assert.Equal(syncB, Read(_storage.GetSyncSettingsPath()));
    }

    // A backup with the Team folder ("Synced A") linked to team.ttmdata, which isn't written.
    private async Task<(string Backup, string TeamPath)> BackUpTeam()
    {
        string teamPath = Path.Combine(_shared, "team.ttmdata");
        await _storage.SaveAsync(_storage.GetDataPath(), DataWith("Synced A", "Local A"));
        await _storage.SaveSyncSettingsAsync(new SyncSettings { Sources = { new SyncSource { Id = TeamId, Name = "Team A", Path = teamPath } } });
        return ((await _backups.CreateAsync(BackupTier.Recent, BackupSource.Live(_storage), quietMs: 0)).Path!, teamPath);
    }

    [Fact]
    public async Task Synced_folders_without_sync_settings_are_merged_into_the_file_like_an_import()
    {
        var (backup, teamPath) = await BackUpTeam();
        string syncBefore = Read(_storage.GetSyncSettingsPath());
        await _storage.SaveSharedAsync(teamPath, new Folder
        {
            Id = Guid.Empty, Title = "Root", LastChange = "20200101000000",
            Children =
            {
                new Template { Title = "Synced A", Content = "<p>old</p>", LastChange = "20200101000000" },
                new Template { Title = "Other", Content = "<p>o</p>", LastChange = "20200101000000" },
            },
        });

        var target = Assert.Single(_restore.FindSyncedFolderTargets(backup));
        Assert.Equal(new SyncedFolderTarget(TeamId, "Team A", teamPath, null, Templates: 1, Conflicts: 1, AddsSource: false), target);

        var result = await _restore.RestoreAsync(backup, RestoreParts.SyncedFolders,
            new[] { new SyncFilePlacement(TeamId, teamPath, WriteCopy: true, MergeAs: ImportConflicts.OverwriteOlder) });

        Assert.True(result.Succeeded, result.Problem);
        var file = await StorageService.LoadRootAsync(teamPath);
        Assert.Equal(new[] { "Synced A:<p>s</p>", "Other:<p>o</p>" }, file!.Children.Cast<Template>().Select(t => $"{t.Title}:{t.Content}"));
        Assert.Equal(syncBefore, Read(_storage.GetSyncSettingsPath()));   // already linked there
    }

    [Fact]
    public async Task Merging_with_skip_keeps_the_files_templates()
    {
        var (backup, teamPath) = await BackUpTeam();
        await _storage.SaveSharedAsync(teamPath, new Folder
        {
            Id = Guid.Empty, Title = "Root", LastChange = "20200101000000",
            Children = { new Template { Title = "Synced A", Content = "<p>old</p>", LastChange = "20200101000000" } },
        });

        var result = await _restore.RestoreAsync(backup, RestoreParts.SyncedFolders,
            new[] { new SyncFilePlacement(TeamId, teamPath, WriteCopy: true, MergeAs: ImportConflicts.Skip) });

        Assert.True(result.Succeeded, result.Problem);
        Assert.Equal("<p>old</p>", ((Template)(await StorageService.LoadRootAsync(teamPath))!.Children.Single()).Content);
    }

    [Fact]
    public async Task A_synced_folder_whose_source_is_gone_links_the_backups_source_again()
    {
        var (backup, teamPath) = await BackUpTeam();
        await _storage.SaveSyncSettingsAsync(new SyncSettings());

        var target = Assert.Single(_restore.FindSyncedFolderTargets(backup));
        Assert.Equal((SyncLocationProblem?)SyncLocationProblem.FileMissing, target.Problem);
        Assert.True(target.AddsSource);

        var result = await _restore.RestoreAsync(backup, RestoreParts.SyncedFolders, new[] { new SyncFilePlacement(TeamId, teamPath, WriteCopy: true) });

        Assert.True(result.Succeeded, result.Problem);
        Assert.Equal("Synced A", (await StorageService.LoadRootAsync(teamPath))!.Children.Single().Title);
        var source = Assert.Single((await _storage.LoadSyncSettingsAsync()).Sources);
        Assert.Equal((TeamId, "Team A", teamPath), (source.Id, source.Name, source.Path));
    }

    [Fact]
    public async Task A_synced_folder_target_that_cant_be_used_says_why()
    {
        var (backup, teamPath) = await BackUpTeam();
        await File.WriteAllTextAsync(teamPath, "{ not a sync file");
        Assert.Equal((SyncLocationProblem?)SyncLocationProblem.Unreadable, _restore.FindSyncedFolderTargets(backup).Single().Problem);

        await _storage.SaveAsync(_storage.GetDataPath(), DataWith("Synced A", "Local A"));
        await _storage.SaveSyncSettingsAsync(new SyncSettings());
        File.Delete(_storage.GetSyncSettingsPath());
        string noSources = (await _backups.CreateAsync(BackupTier.Daily, BackupSource.Live(_storage), quietMs: 0)).Path!;
        var unknown = _restore.FindSyncedFolderTargets(noSources).Single();
        Assert.Equal(("Team", (string?)null, (SyncLocationProblem?)SyncLocationProblem.Unreachable), (unknown.Name, unknown.Path, unknown.Problem));
    }

    [Fact]
    public async Task Settings_from_1x_only_replace_the_fields_1x_had()
    {
        Directory.CreateDirectory(_storage.LegacyDirectory);
        await new StorageService(_storage.LegacyDirectory).SaveAsync(Path.Combine(_storage.LegacyDirectory, "data.ttmdata"), Tree("Old"));
        await File.WriteAllTextAsync(Path.Combine(_storage.LegacyDirectory, "settings.ttmsettings"),
            "﻿{\"AutoCheckUpdates\":false,\"PasteWindowHotkey\":\"Ctrl+Alt+V\",\"BrowserConnectorPort\":47700}");
        string v1 = (await _backups.EnsureV1BackupAsync()).Path!;
        await _storage.SaveSettingsAsync(new AppSettings { StartHidden = false, PasteWindowHotkey = "Ctrl+Alt+Q", LegacyRemovalOffered = true });

        var result = await _restore.RestoreAsync(v1, RestoreParts.Settings);

        Assert.True(result.Succeeded, result.Problem);
        var settings = await _storage.LoadSettingsAsync();
        Assert.Equal(("Ctrl+Alt+V", 47700), (settings.PasteWindowHotkey, settings.BrowserConnectorPort));
        Assert.Equal((false, true), (settings.StartHidden, settings.LegacyRemovalOffered));
        Assert.DoesNotContain("AutoCheckUpdates", Read(_storage.GetSettingsPath()));
    }

    [Fact]
    public async Task After_writes_are_suspended_nothing_is_saved()
    {
        await _storage.SaveAsync(_storage.GetDataPath(), Tree("A"));
        string before = Read(_storage.GetDataPath());

        await _storage.SuspendWritesAsync();
        await _storage.SaveAsync(_storage.GetDataPath(), Tree("B"));
        await _storage.SaveSettingsAsync(new AppSettings());

        Assert.Equal(before, Read(_storage.GetDataPath()));
        Assert.False(File.Exists(_storage.GetSettingsPath()));
    }
}
