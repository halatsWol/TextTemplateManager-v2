using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using TextTemplateManager.Models;

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

public enum SyncLocationProblem
{
    /// <summary>The folder is there, the file isn't.</summary>
    FileMissing,
    /// <summary>The folder is missing too, but it can be created.</summary>
    FolderMissing,
    /// <summary>The path can't exist on this PC (no such drive or share, not a valid path).</summary>
    Unreachable,
    /// <summary>A file is there but doesn't load as a sync file.</summary>
    Unreadable,
}

/// <summary>A source in a backup's sync settings whose file isn't where it points. <paramref name="HasCopy"/>: the
/// backup holds its contents (the file itself, or the synced folder's cached copy).</summary>
public sealed record SyncLocationIssue(Guid SourceId, string Name, string Path, SyncLocationProblem Problem, bool HasCopy);

/// <summary>An active source in a backup's sync settings whose file exists and differs from the backup's copy of it:
/// the restore asks whether to overwrite it.</summary>
public sealed record ChangedSyncFile(Guid SourceId, string Name, string Path, DateTime FileChanged);

/// <summary>Points a source of the restored sync settings at <paramref name="Path"/>, writing the backup's copy of its
/// file there when <paramref name="WriteCopy"/> (otherwise the file there is linked as it is). Restoring only the synced
/// folders, the copy is the synced folder of the backup, merged into the file there when <paramref name="MergeAs"/>
/// is set, and the source is linked in the current sync settings.</summary>
public sealed record SyncFilePlacement(Guid SourceId, string Path, bool WriteCopy, ImportConflicts? MergeAs = null);

/// <summary>Where a synced folder of the backup goes when it is restored without the sync settings: the file of the
/// current source with its id, else of the backup's source (<paramref name="AddsSource"/>: linked again on restoring).
/// <paramref name="Path"/> is null when no source is known; <paramref name="Conflicts"/> counts the templates that
/// already exist in the file.</summary>
public sealed record SyncedFolderTarget(Guid SourceId, string Name, string? Path, SyncLocationProblem? Problem,
    int Templates, int Conflicts, bool AddsSource);

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
/// or a crash found at the next start, puts the previous files back. Linked sync files are only written where
/// the user placed a missing one or chose to overwrite an existing one (<see cref="SyncFilePlacement"/>), as part of
/// the same all-or-nothing swap; an existing file that is kept stays the only source of its folder.</summary>
public sealed class RestoreService
{
    private readonly StorageService _storage;

    public RestoreService(StorageService storage) => _storage = storage;

    private string WorkDirectory => Path.Combine(_storage.DataDirectory, "restore-work");
    private string JournalPath => Path.Combine(WorkDirectory, "journal.json");

    /// <summary>Restores <paramref name="parts"/> of the backup at <paramref name="backupPath"/>. Nothing the
    /// running app holds may be saved afterwards (<see cref="StorageService.SuspendWritesAsync"/>); the app
    /// restarts or is still starting.</summary>
    public async Task<RestoreResult> RestoreAsync(string backupPath, RestoreParts parts, IReadOnlyCollection<SyncFilePlacement>? placements = null)
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
                byte[]? data = local || synced ? Take(BackupFileKind.Data) : null;
                byte[]? sync = parts.HasFlag(RestoreParts.SyncSettings) ? Take(BackupFileKind.SyncSettings) : null;

                var placedFiles = new List<(byte[] Content, string Target)>();
                var keptFiles = new HashSet<Guid>();
                if (sync != null)
                {
                    var syncSettings = StorageService.ParseSyncSettings(sync)!;
                    foreach (var placement in placements ?? Array.Empty<SyncFilePlacement>())
                    {
                        if (syncSettings.Sources.FirstOrDefault(s => s.Id == placement.SourceId) is not { } source) continue;
                        if (placement.WriteCopy)
                            placedFiles.Add((SyncFileCopy(zip, manifest!, source.Id) ?? throw new InvalidDataException($"the backup holds no copy of {source.Name}"),
                                placement.Path));
                        source.Path = placement.Path;
                    }
                    if (placements is { Count: > 0 }) sync = StorageService.SerializeSyncSettings(syncSettings);

                    // A linked file that exists and isn't overwritten stays the truth for its folder: the backup's cached
                    // copy of the folder is left out, so the next load builds it from the file alone.
                    foreach (var source in syncSettings.Sources.Where(s => s.IsActive && !string.IsNullOrWhiteSpace(s.Path) && File.Exists(s.Path)))
                        if (placements?.Any(p => p.SourceId == source.Id && p.WriteCopy) != true) keptFiles.Add(source.Id);
                }

                if (data != null)
                {
                    byte[] restored = local && synced ? data : MixData(data, takeLocal: local);
                    plan.Add((synced && keptFiles.Count > 0 ? WithoutCachedFolders(restored, keptFiles) : restored, _storage.GetDataPath()));
                }
                if (parts.HasFlag(RestoreParts.Settings) && Take(BackupFileKind.Settings) is { } settings)
                    plan.Add((manifest!.Tier == BackupTier.V1 ? OverCurrentSettings(settings) : KeepGreetingState(settings), _storage.GetSettingsPath()));
                if (sync != null)
                {
                    plan.AddRange(placedFiles);
                    plan.Add((sync, _storage.GetSyncSettingsPath()));   // last, after the files it points at
                }
                else if (synced && data != null && placements is { Count: > 0 })
                    PlaceSyncedFolders(plan, data, Take(BackupFileKind.SyncSettings), placements);
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

    // Synced folders restored without the sync settings: each one written (or merged) into its file, and its source
    // linked in the current sync settings, taken from the backup's when it isn't there.
    private void PlaceSyncedFolders(List<(byte[] Content, string Target)> plan, byte[] data, byte[]? backupSync,
        IReadOnlyCollection<SyncFilePlacement> placements)
    {
        var folders = CachedSyncedFolders(StorageService.ParseTree(data)!);
        var backupSources = backupSync == null ? new() : StorageService.ParseSyncSettings(backupSync)!.Sources.ToList();
        string settingsPath = _storage.GetSyncSettingsPath();
        byte[]? before = File.Exists(settingsPath) ? File.ReadAllBytes(settingsPath) : null;
        var current = before == null ? new Models.SyncSettings()
            : StorageService.ParseSyncSettings(before) ?? throw new InvalidDataException("the current sync settings don't load");

        foreach (var placement in placements)
        {
            if (folders.FirstOrDefault(f => f.Id == placement.SourceId) is not { } folder) continue;
            if (placement.WriteCopy)
            {
                var file = DataNode.BuildFileTree(folder);
                if (placement.MergeAs is { } how)
                {
                    var existing = ReadSyncFile(placement.Path) ?? throw new InvalidDataException($"{Path.GetFileName(placement.Path)} isn't a valid sync file");
                    MergeFileTree(existing, file, how, dryRun: false);
                    file = existing;
                }
                plan.Add((StorageService.SerializeTree(file), placement.Path));
            }
            if (current.Sources.FirstOrDefault(s => s.Id == folder.Id) is not { } source)
            {
                source = backupSources.FirstOrDefault(s => s.Id == folder.Id) ?? new Models.SyncSource { Id = folder.Id, Name = folder.Title };
                current.Sources.Add(source);
            }
            source.Path = placement.Path;
        }

        byte[] after = StorageService.SerializeSyncSettings(current);
        if (before == null || !before.AsSpan().SequenceEqual(after)) plan.Add((after, settingsPath));
    }

    /// <summary>For restoring the synced folders without the sync settings: where each synced folder of the backup
    /// would go, and what is there now.</summary>
    public IReadOnlyList<SyncedFolderTarget> FindSyncedFolderTargets(string backupPath)
    {
        var targets = new List<SyncedFolderTarget>();
        if (BackupService.Verify(backupPath, out var manifest) != null) return targets;
        using var zip = ZipFile.OpenRead(backupPath);
        if (manifest!.Files.FirstOrDefault(f => f.Kind == BackupFileKind.Data && f.Included) is not { } dataEntry) return targets;
        var backupSources = manifest.Files.FirstOrDefault(f => f.Kind == BackupFileKind.SyncSettings && f.Included) is { } syncEntry
            ? StorageService.ParseSyncSettings(ReadChecked(zip, syncEntry))!.Sources.ToList() : new();
        string settingsPath = _storage.GetSyncSettingsPath();
        var currentSources = File.Exists(settingsPath)
            ? StorageService.ParseSyncSettings(File.ReadAllBytes(settingsPath))?.Sources.ToList() ?? new() : new();

        foreach (var folder in CachedSyncedFolders(StorageService.ParseTree(ReadChecked(zip, dataEntry))!))
        {
            var source = currentSources.FirstOrDefault(s => s.Id == folder.Id);
            bool adds = source == null;
            source ??= backupSources.FirstOrDefault(s => s.Id == folder.Id);
            string? path = string.IsNullOrWhiteSpace(source?.Path) ? null : source.Path;
            var problem = path == null ? SyncLocationProblem.Unreachable : LocationProblem(path);
            int conflicts = 0;
            if (problem == null)
            {
                if (ReadSyncFile(path!) is { } existing) conflicts = MergeFileTree(existing, DataNode.BuildFileTree(folder), ImportConflicts.Skip, dryRun: true).Conflicts;
                else problem = SyncLocationProblem.Unreadable;
            }
            targets.Add(new(folder.Id, source?.Name ?? folder.Title, path, problem,
                folder.Children.Sum(c => c.SelfAndDescendants().OfType<Models.Template>().Count()), conflicts, adds));
        }
        return targets;
    }

    private static List<Models.Folder> CachedSyncedFolders(Models.Folder data) =>
        data.Children.OfType<Models.Folder>().Where(f => f.SyncId != null).ToList();

    private static Models.Folder? ReadSyncFile(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var buffer = new MemoryStream();
            fs.CopyTo(buffer);
            return StorageService.ParseTree(buffer.ToArray());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Merges the items of a sync file tree into another the way a direct import does: folders matched by
    /// id or title are merged, templates that exist are handled as <paramref name="conflicts"/> says, the rest is
    /// added (with new ids where the file already uses theirs).</summary>
    internal static ImportSummary MergeFileTree(Models.Folder into, Models.Folder from, ImportConflicts conflicts, bool dryRun)
    {
        var taken = into.SelfAndDescendants().Select(i => i.Id).ToHashSet();
        return MergeFileItems(into.Children, into.Id, from.Children, conflicts, dryRun, taken);
    }

    private static ImportSummary MergeFileItems(IList<Models.BaseItem> into, Guid parentId, IEnumerable<Models.BaseItem> items,
        ImportConflicts conflicts, bool dryRun, HashSet<Guid> taken)
    {
        int added = 0, overwritten = 0, skipped = 0, met = 0;
        foreach (var item in items.ToList())
        {
            var sameKind = into.Where(c => c.GetType() == item.GetType()).ToList();
            var match = sameKind.FirstOrDefault(c => c.Id == item.Id)
                ?? sameKind.FirstOrDefault(c => string.Equals(c.Title, item.Title, StringComparison.OrdinalIgnoreCase));

            if (match is Models.Folder folder)
            {
                var inner = MergeFileItems(folder.Children, folder.Id, item.Children, conflicts, dryRun, taken);
                (added, overwritten, skipped, met) = (added + inner.Added, overwritten + inner.Overwritten, skipped + inner.Skipped, met + inner.Conflicts);
            }
            else if (match is Models.Template current && item is Models.Template restored)
            {
                met++;
                bool overwrite = conflicts == ImportConflicts.OverwriteAll
                    || (conflicts == ImportConflicts.OverwriteOlder && string.CompareOrdinal(restored.LastChange, current.LastChange) > 0);
                if (!overwrite) { skipped++; continue; }
                overwritten++;
                if (dryRun) continue;
                current.Title = restored.Title;
                current.Content = restored.Content;
                current.TagsCsv = restored.TagsCsv;
                current.DefaultPasteMode = restored.DefaultPasteMode;
                current.SingleKeyShortcut = restored.SingleKeyShortcut;
                current.MultiKeyShortcut = restored.MultiKeyShortcut;
                current.LastChange = restored.LastChange;
            }
            else
            {
                added += item.SelfAndDescendants().OfType<Models.Template>().Count();
                if (dryRun) continue;
                Adopt(item, parentId, taken);
                into.Add(item);
            }
        }
        return new ImportSummary(added, overwritten, skipped, met);
    }

    private static void Adopt(Models.BaseItem item, Guid parentId, HashSet<Guid> taken)
    {
        item.ParentId = parentId;
        if (!taken.Add(item.Id))
        {
            item.Id = Guid.NewGuid();
            taken.Add(item.Id);
        }
        foreach (var child in item.Children) Adopt(child, item.Id, taken);
    }

    // A 1.x settings file has only the fields 1.x knew: the others keep their current values (start hidden as carried
    // over from 1.x, ...), and fields only 1.x had (its updater's) are dropped.
    private byte[] OverCurrentSettings(byte[] v1)
    {
        string path = _storage.GetSettingsPath();
        var merged = (File.Exists(path) ? StorageService.ParseObject(File.ReadAllBytes(path)) : null) ?? new System.Text.Json.Nodes.JsonObject();
        foreach (var (name, value) in StorageService.ParseObject(v1) ?? throw new InvalidDataException("the 1.x settings don't load"))
            merged[name] = value?.DeepClone();
        var settings = StorageService.ParseSettings(System.Text.Encoding.UTF8.GetBytes(merged.ToJsonString()))
            ?? throw new InvalidDataException("the 1.x settings don't load");
        return StorageService.SerializeSettings(settings);
    }

    // The tour offer and What's new belong to this installation: a restored backup doesn't bring them back.
    private byte[] KeepGreetingState(byte[] backup)
    {
        string path = _storage.GetSettingsPath();
        if (!File.Exists(path) || StorageService.ParseSettings(File.ReadAllBytes(path)) is not { } current) return backup;
        var settings = StorageService.ParseSettings(backup) ?? throw new InvalidDataException("the backup's settings don't load");
        settings.TutorialOfferDone = current.TutorialOfferDone;
        settings.LastRunVersion = current.LastRunVersion;
        return StorageService.SerializeSettings(settings);
    }

    // A source's file as the backup has it: the copy of the file itself, else its synced folder's cached copy.
    private static byte[]? SyncFileCopy(ZipArchive zip, BackupManifest manifest, Guid sourceId)
    {
        if (manifest.Files.FirstOrDefault(f => f.Kind == BackupFileKind.SyncFile && f.Included && f.SourceId == sourceId) is { } file)
            return ReadChecked(zip, file);
        if (manifest.Files.FirstOrDefault(f => f.Kind == BackupFileKind.Data && f.Included) is not { } data) return null;
        var cached = StorageService.ParseTree(ReadChecked(zip, data))!.Children
            .FirstOrDefault(c => c is Models.Folder && c.SyncId != null && c.Id == sourceId) as Models.Folder;
        return cached == null ? null : StorageService.SerializeTree(DataNode.BuildFileTree(cached));
    }

    /// <summary>The sources in the backup's sync settings whose file isn't where they point on this PC, each with
    /// what is wrong there; empty when the backup has no sync settings or can't be read.</summary>
    public static IReadOnlyList<SyncLocationIssue> FindSyncLocationIssues(string backupPath)
    {
        var issues = new List<SyncLocationIssue>();
        if (BackupService.Verify(backupPath, out var manifest) != null) return issues;
        using var zip = ZipFile.OpenRead(backupPath);
        if (manifest!.Files.FirstOrDefault(f => f.Kind == BackupFileKind.SyncSettings && f.Included) is not { } entry) return issues;
        foreach (var source in StorageService.ParseSyncSettings(ReadChecked(zip, entry))!.Sources)
        {
            if (string.IsNullOrWhiteSpace(source.Path) || LocationProblem(source.Path) is not { } problem) continue;
            issues.Add(new(source.Id, source.Name, source.Path, problem, SyncFileCopy(zip, manifest, source.Id) != null));
        }
        return issues;
    }

    /// <summary>The active sources in the backup's sync settings whose file exists and holds something else than the
    /// backup's copy of it (compared as trees, so formatting alone doesn't count).</summary>
    public static IReadOnlyList<ChangedSyncFile> FindChangedSyncFiles(string backupPath)
    {
        var changed = new List<ChangedSyncFile>();
        if (BackupService.Verify(backupPath, out var manifest) != null) return changed;
        using var zip = ZipFile.OpenRead(backupPath);
        if (manifest!.Files.FirstOrDefault(f => f.Kind == BackupFileKind.SyncSettings && f.Included) is not { } entry) return changed;
        foreach (var source in StorageService.ParseSyncSettings(ReadChecked(zip, entry))!.Sources.Where(s => s.IsActive))
        {
            if (string.IsNullOrWhiteSpace(source.Path) || !File.Exists(source.Path)) continue;
            if (SyncFileCopy(zip, manifest, source.Id) is not { } copy || ReadSyncFile(source.Path) is not { } current) continue;
            if (StorageService.SerializeTree(current).AsSpan().SequenceEqual(StorageService.SerializeTree(StorageService.ParseTree(copy)!))) continue;
            changed.Add(new(source.Id, source.Name, source.Path, File.GetLastWriteTime(source.Path)));
        }
        return changed;
    }

    /// <summary>What keeps a file from being at <paramref name="path"/>; null when it is there.</summary>
    public static SyncLocationProblem? LocationProblem(string path)
    {
        try
        {
            if (File.Exists(path)) return null;
            if (!Path.IsPathFullyQualified(path)) return SyncLocationProblem.Unreachable;
            string full = Path.GetFullPath(path);
            string? folder = Path.GetDirectoryName(full);
            if (folder == null || Path.GetFileName(full).Length == 0 || Directory.Exists(full)
                || full[Path.GetPathRoot(full)!.Length..].Split(Path.DirectorySeparatorChar).Any(name => name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
                return SyncLocationProblem.Unreachable;
            if (Directory.Exists(folder)) return SyncLocationProblem.FileMissing;

            // Creatable when the nearest existing ancestor is a folder: the drive or share root at least.
            for (string? up = folder; up != null; up = Path.GetDirectoryName(up))
            {
                if (File.Exists(up)) return SyncLocationProblem.Unreachable;
                if (Directory.Exists(up)) return SyncLocationProblem.FolderMissing;
            }
            return SyncLocationProblem.Unreachable;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or IOException)
        {
            return SyncLocationProblem.Unreachable;
        }
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

    private static byte[] WithoutCachedFolders(byte[] data, ISet<Guid> sourceIds)
    {
        var tree = StorageService.ParseTree(data)!;
        foreach (var folder in tree.Children.Where(c => c is Models.Folder && c.SyncId != null && sourceIds.Contains(c.Id)).ToList())
            tree.Children.Remove(folder);
        return StorageService.SerializeTree(tree);
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
