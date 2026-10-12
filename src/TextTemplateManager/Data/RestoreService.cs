using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace TextTemplateManager.Data;

/// <summary>What a restore takes from the backup.</summary>
[Flags]
public enum RestoreParts
{
    /// <summary>The local templates and folders (data.ttmdata without the synced folders).</summary>
    Local = 1,
    Settings = 2,
    /// <summary>The list of sync sources (sync.ttmsettings).</summary>
    SyncSettings = 4,
    /// <summary>The synced folders as cached in data.ttmdata: what a source shows while its file is gone.</summary>
    SyncedFolders = 8,
    Sync = SyncSettings | SyncedFolders,
    All = Local | Settings | Sync,
}

public sealed record RestoreResult(bool Succeeded, string? Problem = null);

/// <summary>Journal of a restore in progress: what each target was before (null = it didn't exist).</summary>
public sealed class RestoreJournal
{
    public List<RestoreJournalItem> Items { get; set; } = new();
}

public sealed class RestoreJournalItem
{
    public string Target { get; set; } = "";
    public string? Previous { get; set; }
}

/// <summary>Puts a backup's files back into the data folder: all of them or nothing. Each file is unpacked
/// and checked first, the current files are copied aside with a journal, then each is swapped in; a failure,
/// or a crash found at the next start, puts the previous files back. Linked sync files are never written: the
/// backup's copies of them stay in the zip as a fallback to extract by hand.</summary>
public sealed class RestoreService
{
    private readonly StorageService _storage;

    public RestoreService(StorageService storage) => _storage = storage;

    private string WorkDirectory => Path.Combine(_storage.DataDirectory, "restore-work");
    private string JournalPath => Path.Combine(WorkDirectory, "journal.json");

    /// <summary>Restores <paramref name="parts"/> of the backup at <paramref name="backupPath"/>. Nothing the
    /// running app holds may be saved afterwards (<see cref="StorageService.SuspendWritesAsync"/>); the app
    /// restarts or is still starting.</summary>
    public async Task<RestoreResult> RestoreAsync(string backupPath, RestoreParts parts)
    {
        RecoverInterrupted();
        string? problem = BackupService.Verify(backupPath, out var manifest);
        if (problem != null) return new(false, $"The backup can't be used: {problem}.");

        string staging = Path.Combine(WorkDirectory, "new");
        string previous = Path.Combine(WorkDirectory, "old");
        try
        {
            // Unpack and check every file before anything live is touched.
            var plan = new List<(byte[] Content, string Target)>();
            using (var zip = ZipFile.OpenRead(backupPath))
            {
                byte[]? Take(BackupFileKind kind)
                {
                    var entry = manifest!.Files.FirstOrDefault(f => f.Kind == kind && f.Included);
                    return entry == null ? null : ReadChecked(zip, entry);
                }

                bool local = parts.HasFlag(RestoreParts.Local), synced = parts.HasFlag(RestoreParts.SyncedFolders);
                if ((local || synced) && Take(BackupFileKind.Data) is { } data)
                    plan.Add((local && synced ? data : MixData(data, takeLocal: local), _storage.GetDataPath()));
                if (parts.HasFlag(RestoreParts.Settings) && Take(BackupFileKind.Settings) is { } settings)
                    plan.Add((settings, _storage.GetSettingsPath()));
                if (parts.HasFlag(RestoreParts.SyncSettings) && Take(BackupFileKind.SyncSettings) is { } sync)
                    plan.Add((sync, _storage.GetSyncSettingsPath()));
            }
            if (plan.Count == 0) return new(false, "The backup holds none of the chosen parts.");

            if (Directory.Exists(WorkDirectory)) Directory.Delete(WorkDirectory, recursive: true);
            Directory.CreateDirectory(staging);
            Directory.CreateDirectory(previous);
            for (int i = 0; i < plan.Count; i++)
                await WriteFlushedAsync(Path.Combine(staging, i.ToString()), plan[i].Content);

            // The current files go aside first; the journal is what a crash during the swap is undone from.
            var journal = new RestoreJournal();
            for (int i = 0; i < plan.Count; i++)
            {
                string target = plan[i].Target;
                string? kept = null;
                if (File.Exists(target))
                {
                    kept = Path.Combine(previous, i.ToString());
                    File.Copy(target, kept);
                }
                journal.Items.Add(new RestoreJournalItem { Target = target, Previous = kept });
            }
            await WriteFlushedAsync(JournalPath, StorageService.SerializeJournal(journal));

            try
            {
                for (int i = 0; i < plan.Count; i++)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(plan[i].Target)!);
                    File.Move(Path.Combine(staging, i.ToString()), plan[i].Target, overwrite: true);
                }
            }
            catch (Exception ex)
            {
                RollBack(journal);
                return new(false, $"Restoring failed, nothing was changed: {ex.Message}");
            }

            Directory.Delete(WorkDirectory, recursive: true);
            return new(true);
        }
        catch (Exception ex)
        {
            if (!File.Exists(JournalPath)) TryDeleteWork();
            return new(false, $"Restoring failed, nothing was changed: {ex.Message}");
        }
    }

    private static byte[] ReadChecked(ZipArchive zip, BackupEntry entry)
    {
        byte[] content;
        using (var stream = zip.GetEntry(entry.Name)!.Open())
        using (var buffer = new MemoryStream())
        {
            stream.CopyTo(buffer);
            content = buffer.ToArray();
        }
        if (content.LongLength != entry.Size
            || !string.Equals(Convert.ToHexString(SHA256.HashData(content)), entry.Sha256, StringComparison.OrdinalIgnoreCase)
            || !StorageService.Parses(entry.Kind, content))
            throw new InvalidDataException($"{entry.Name} is damaged");
        return content;
    }

    // One half of the data file from the backup, the other from the current file: the local items, or the
    // synced folders (top-level folders that carry a SyncId).
    private byte[] MixData(byte[] backup, bool takeLocal)
    {
        var fromBackup = StorageService.ParseTree(backup)!;
        string currentPath = _storage.GetDataPath();
        var current = File.Exists(currentPath)
            ? StorageService.ParseTree(File.ReadAllBytes(currentPath)) ?? throw new InvalidDataException("the current data file doesn't load")
            : new Models.Folder { Id = Guid.Empty, Title = "Root" };

        var localSource = takeLocal ? fromBackup : current;
        var syncedSource = takeLocal ? current : fromBackup;
        var mixed = new Models.Folder { Id = localSource.Id, Title = localSource.Title, LastChange = localSource.LastChange };
        foreach (var synced in syncedSource.Children.Where(c => c is Models.Folder && c.SyncId != null)) mixed.Children.Add(synced);
        foreach (var item in localSource.Children.Where(c => !(c is Models.Folder && c.SyncId != null))) mixed.Children.Add(item);
        return StorageService.SerializeTree(mixed);
    }

    /// <summary>At startup: a restore cut off by a crash or power loss is undone, so the files are never a mix.</summary>
    public void RecoverInterrupted()
    {
        if (!Directory.Exists(WorkDirectory)) return;
        var journal = File.Exists(JournalPath) ? StorageService.ParseJournal(File.ReadAllBytes(JournalPath)) : null;
        if (journal != null) RollBack(journal);
        else TryDeleteWork();   // stopped before the swap: nothing live was touched
    }

    // Every item is put back even if one fails (e.g. a file still locked, which then was never replaced either).
    // The journal is only dropped once all of them are back; otherwise the next start tries again.
    private void RollBack(RestoreJournal journal)
    {
        bool complete = true;
        foreach (var item in journal.Items)
        {
            try
            {
                if (item.Previous != null)
                {
                    if (!SameContent(item.Previous, item.Target)) File.Copy(item.Previous, item.Target, overwrite: true);
                }
                else if (File.Exists(item.Target)) File.Delete(item.Target);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { complete = false; }
        }
        if (complete) TryDeleteWork();
    }

    private static bool SameContent(string a, string b) =>
        File.Exists(b) && File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));

    private void TryDeleteWork()
    {
        try { Directory.Delete(WorkDirectory, recursive: true); } catch { }
    }

    private static async Task WriteFlushedAsync(string path, byte[] content)
    {
        await using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        await fs.WriteAsync(content);
        fs.Flush(flushToDisk: true);
    }
}
