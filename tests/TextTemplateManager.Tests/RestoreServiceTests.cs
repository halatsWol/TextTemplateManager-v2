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

    [Fact]
    public async Task Restoring_sync_brings_the_local_data_with_it()
    {
        string backup = await BackUpStateA();
        string dataA = Read(_storage.GetDataPath()), syncA = Read(_storage.GetSyncSettingsPath());
        await ChangeToStateB();
        string settingsB = Read(_storage.GetSettingsPath());

        var result = await _restore.RestoreAsync(backup, RestoreParts.Sync);

        Assert.True(result.Succeeded, result.Problem);
        Assert.Equal(syncA, Read(_storage.GetSyncSettingsPath()));
        Assert.Equal(dataA, Read(_storage.GetDataPath()));
        Assert.Equal(settingsB, Read(_storage.GetSettingsPath()));
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
