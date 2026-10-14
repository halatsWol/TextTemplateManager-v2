using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TextTemplateManager.Common;

namespace TextTemplateManager.Data;

/// <summary>The files a backup is made from: the app's live folder, or the 1.x data folder for the v1 backup.</summary>
public sealed record BackupSource(string DataPath, string SettingsPath, string SyncSettingsPath)
{
    public static BackupSource Live(StorageService storage) =>
        new(storage.GetDataPath(), storage.GetSettingsPath(), storage.GetSyncSettingsPath());

    public static BackupSource Folder(string directory) => new(
        Path.Combine(directory, "data.ttmdata"), Path.Combine(directory, "settings.ttmsettings"),
        Path.Combine(directory, "sync.ttmsettings"));
}

public enum BackupOutcome
{
    Created,
    /// <summary>Same files as the backup it was compared with; nothing written.</summary>
    Unchanged,
    /// <summary>Files kept being written; try again later.</summary>
    NotQuiet,
    /// <summary>No data file yet (fresh start).</summary>
    NothingToBackUp,
    /// <summary>One of the app's own files didn't load; no backup, try again later.</summary>
    SourceUnreadable,
    Failed,
}

public sealed record BackupResult(BackupOutcome Outcome, string? Path = null, string? Problem = null);

/// <summary>What the first start can start from: the 1.x data (its v1 backup) and the newest other backup that
/// can be restored. Without either there is nothing to ask.</summary>
public sealed record FirstStartOffer(BackupInfo? V1, BackupInfo? Newest)
{
    public bool HasChoice => V1 != null || Newest != null;

    /// <summary>From the backups listed newest first.</summary>
    public static FirstStartOffer From(IEnumerable<BackupInfo> backups)
    {
        var valid = backups.Where(b => b.IsValid).ToList();
        return new(valid.FirstOrDefault(b => b.Tier == BackupTier.V1), valid.FirstOrDefault(b => b.Tier != BackupTier.V1));
    }
}

/// <summary>A backup zip found in the folder. Only a valid one may be restored.</summary>
public sealed record BackupInfo(string Path, BackupTier Tier, DateTime Created, BackupManifest? Manifest, string? Problem)
{
    public bool IsValid => Problem == null;
}

/// <summary>Which restore parts a backup can give: settings only if it holds a settings file, sync settings only
/// if they list at least one source, synced folders only if its data has any.</summary>
public sealed record BackupContents(bool Local, bool Settings, bool SyncSettings, bool SyncedFolders)
{
    public RestoreParts Available =>
        (Local ? RestoreParts.Local : 0) | (Settings ? RestoreParts.Settings : 0)
        | (SyncSettings ? RestoreParts.SyncSettings : 0) | (SyncedFolders ? RestoreParts.SyncedFolders : 0);
}

/// <summary>Writes and checks backup zips: data, settings, sync settings and copies of the linked sync files,
/// with a manifest of sizes and hashes. A backup only ever appears under its final name once it has been
/// written, flushed and read back intact.</summary>
public sealed class BackupService
{
    private const string ManifestName = "manifest.json";
    private const string PartialSuffix = ".partial";
    private static readonly Regex NamePattern = new(@"^ttm-backup-(recent|2h|daily|v1)-(\d{8}-\d{6})\.zip$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly StorageService _storage;
    private readonly Func<DateTime> _now;

    public BackupService(StorageService storage, Func<DateTime>? now = null)
    {
        _storage = storage;
        _now = now ?? (() => DateTime.Now);
    }

    public string Directory => _storage.BackupDirectory;

    internal static string TierToken(BackupTier tier) => tier switch
    {
        BackupTier.Recent => "recent",
        BackupTier.TwoHourly => "2h",
        BackupTier.Daily => "daily",
        BackupTier.Exported => "exported",
        _ => "v1",
    };

    private static BackupTier TierFromToken(string token) => token.ToLowerInvariant() switch
    {
        "recent" => BackupTier.Recent,
        "2h" => BackupTier.TwoHourly,
        "daily" => BackupTier.Daily,
        _ => BackupTier.V1,
    };

    /// <summary>Makes one backup of <paramref name="source"/>. Waits until the app's files have been quiet for
    /// <paramref name="quietMs"/> (giving up after <paramref name="quietTimeout"/>), so no file is caught mid-write.
    /// With <paramref name="skipIfSameAs"/>, nothing is written when the files match that backup's. With
    /// <paramref name="targetPath"/>, the zip goes there instead of the backups folder (an exported backup).</summary>
    public async Task<BackupResult> CreateAsync(BackupTier tier, BackupSource source, int quietMs = 2000,
        TimeSpan? quietTimeout = null, BackupManifest? skipIfSameAs = null, string? targetPath = null, CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow + (quietTimeout ?? TimeSpan.FromSeconds(30));
        while (!_storage.WritesQuiet(quietMs))
        {
            if (DateTime.UtcNow >= deadline) return new(BackupOutcome.NotQuiet);
            await Task.Delay(100, ct);
        }

        if (!File.Exists(source.DataPath)) return new(BackupOutcome.NothingToBackUp);

        var files = new List<(BackupEntry Entry, byte[]? Content)>();
        foreach (var (kind, path, name) in new[]
        {
            (BackupFileKind.Data, source.DataPath, "data.ttmdata"),
            (BackupFileKind.Settings, source.SettingsPath, "settings.ttmsettings"),
            (BackupFileKind.SyncSettings, source.SyncSettingsPath, "sync.ttmsettings"),
        })
        {
            if (!File.Exists(path)) continue;
            byte[]? content = await ReadSharedAsync(path, ct);
            if (content == null || !StorageService.Parses(kind, content))
                return new(BackupOutcome.SourceUnreadable, Problem: $"{name} couldn't be read");
            files.Add((Entry(name, kind, content), content));
        }

        var syncSettings = files.FirstOrDefault(f => f.Entry.Kind == BackupFileKind.SyncSettings).Content is { } s
            ? StorageService.ParseSyncSettings(s) : null;
        foreach (var syncSource in syncSettings?.Sources ?? new())
        {
            if (string.IsNullOrWhiteSpace(syncSource.Path)) continue;
            string name = $"sync/{syncSource.Id:N}.ttmdata";
            byte[]? content = File.Exists(syncSource.Path) ? await ReadSharedAsync(syncSource.Path, ct) : null;
            string? problem = content == null ? (File.Exists(syncSource.Path) ? "couldn't be read" : "missing")
                : StorageService.Parses(BackupFileKind.SyncFile, content) ? null : "not a valid sync file";
            var entry = problem == null
                ? Entry(name, BackupFileKind.SyncFile, content!)
                : new BackupEntry { Name = name, Kind = BackupFileKind.SyncFile, Included = false, Problem = problem };
            entry.OriginalPath = syncSource.Path;
            entry.SourceId = syncSource.Id;
            files.Add((entry, problem == null ? content : null));
        }

        if (skipIfSameAs != null && Fingerprint(skipIfSameAs.Files) == Fingerprint(files.Select(f => f.Entry)))
            return new(BackupOutcome.Unchanged);

        DateTime now = _now();
        var manifest = new BackupManifest
        {
            AppVersion = AppInfo.Version,
            CreatedUtc = now.ToUniversalTime(),
            Tier = tier,
            Files = files.Select(f => f.Entry).ToList(),
        };

        string finalPath = targetPath ?? Path.Combine(Directory,
            $"ttm-backup-{TierToken(tier)}-{now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.zip");
        string partialPath = finalPath + PartialSuffix;
        try
        {
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
            await using (var fs = new FileStream(partialPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                using (var zip = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: true))
                {
                    foreach (var (entry, content) in files)
                        if (content != null) await WriteEntryAsync(zip, entry.Name, content, ct);
                    await WriteEntryAsync(zip, ManifestName, StorageService.SerializeManifest(manifest), ct);   // last
                }
                fs.Flush(flushToDisk: true);
            }

            string? problem = Verify(partialPath, out _);
            if (problem != null)
            {
                TryDelete(partialPath);
                return new(BackupOutcome.Failed, Problem: problem);
            }
            File.Move(partialPath, finalPath, overwrite: targetPath != null);   // the save dialog already asked
            return new(BackupOutcome.Created, finalPath);
        }
        catch (Exception ex)
        {
            TryDelete(partialPath);
            return new(BackupOutcome.Failed, Problem: ex.Message);
        }
    }

    /// <summary>Every backup zip in the folder, newest first, each checked in full.</summary>
    public IReadOnlyList<BackupInfo> List()
    {
        if (!System.IO.Directory.Exists(Directory)) return Array.Empty<BackupInfo>();
        var backups = new List<BackupInfo>();
        foreach (var path in System.IO.Directory.EnumerateFiles(Directory, "ttm-backup-*.zip"))
        {
            var match = NamePattern.Match(Path.GetFileName(path));
            if (!match.Success) continue;
            if (!DateTime.TryParseExact(match.Groups[2].Value, "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var created)) continue;
            string? problem = Verify(path, out var manifest);
            backups.Add(new BackupInfo(path, TierFromToken(match.Groups[1].Value), created, manifest, problem));
        }
        return backups.OrderByDescending(b => b.Created).ThenBy(b => b.Tier).ToList();
    }

    /// <summary>What a valid backup holds, for offering only the restore parts it can give. Null when it can't be read.</summary>
    public static BackupContents? ReadContents(string path)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            var manifest = zip.GetEntry(ManifestName) is { } m ? StorageService.ParseManifest(ReadEntry(m)) : null;
            if (manifest == null) return null;
            byte[]? Content(BackupFileKind kind) =>
                manifest.Files.FirstOrDefault(f => f.Kind == kind && f.Included) is { } entry && zip.GetEntry(entry.Name) is { } e ? ReadEntry(e) : null;

            var data = Content(BackupFileKind.Data) is { } d ? StorageService.ParseTree(d) : null;
            var sync = Content(BackupFileKind.SyncSettings) is { } s ? StorageService.ParseSyncSettings(s) : null;
            return new BackupContents(
                Local: data != null,
                Settings: Content(BackupFileKind.Settings) != null,
                SyncSettings: sync is { Sources.Count: > 0 },
                SyncedFolders: data?.Children.Any(c => c is Models.Folder && c.SyncId != null) == true);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>1.x data is on this PC (whether or not its v1 backup exists yet).</summary>
    public bool HasLegacyData => File.Exists(BackupSource.Folder(_storage.LegacyDirectory).DataPath);

    /// <summary>A permanent backup of the 1.x data, made once: nothing when there is no 1.x data or a valid v1 backup
    /// already exists. One that fails is tried again at the next start.</summary>
    public async Task<BackupResult> EnsureV1BackupAsync()
    {
        var source = BackupSource.Folder(_storage.LegacyDirectory);
        if (!File.Exists(source.DataPath)) return new(BackupOutcome.NothingToBackUp);
        if (List().Any(b => b.Tier == BackupTier.V1 && b.IsValid)) return new(BackupOutcome.Unchanged);
        return await CreateAsync(BackupTier.V1, source, quietMs: 0);
    }

    /// <summary>Deletes the oldest valid backups of <paramref name="tier"/> beyond <paramref name="keep"/>. Damaged
    /// ones don't count and are left alone.</summary>
    public void Prune(BackupTier tier, int keep)
    {
        foreach (var old in List().Where(b => b.Tier == tier && b.IsValid).Skip(keep))
            TryDelete(old.Path);
    }

    // Which files a backup holds and their content; unreadable sync files count by name and problem.
    private static string Fingerprint(IEnumerable<BackupEntry> files) => string.Join("|",
        files.OrderBy(f => f.Name, StringComparer.Ordinal).Select(f => f.Included ? $"{f.Name}:{f.Sha256}" : $"{f.Name}!{f.Problem}"));

    /// <summary>Removes backups left half-written by a crash or power loss.</summary>
    public void DeleteStalePartials()
    {
        if (!System.IO.Directory.Exists(Directory)) return;
        foreach (var path in System.IO.Directory.EnumerateFiles(Directory, "ttm-backup-*.zip" + PartialSuffix))
            TryDelete(path);
    }

    /// <summary>Null when the zip has a manifest and every included file is there with its recorded size and
    /// hash and loads; otherwise what is wrong.</summary>
    internal static string? Verify(string path, out BackupManifest? manifest)
    {
        manifest = null;
        try
        {
            using var zip = ZipFile.OpenRead(path);
            var manifestEntry = zip.GetEntry(ManifestName);
            if (manifestEntry == null) return "no manifest";
            manifest = StorageService.ParseManifest(ReadEntry(manifestEntry));
            if (manifest == null) return "unreadable manifest";
            if (manifest.Format > BackupManifest.CurrentFormat) return "made by a newer version";
            if (!manifest.Files.Any(f => f.Kind == BackupFileKind.Data && f.Included)) return "no template data";

            foreach (var file in manifest.Files.Where(f => f.Included))
            {
                var entry = zip.GetEntry(file.Name);
                if (entry == null) return $"{file.Name} is missing";
                byte[] content = ReadEntry(entry);
                if (content.LongLength != file.Size || !string.Equals(Hash(content), file.Sha256, StringComparison.OrdinalIgnoreCase))
                    return $"{file.Name} is damaged";
                if (!StorageService.Parses(file.Kind, content)) return $"{file.Name} doesn't load";
            }
            return null;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return "not a readable zip";
        }
    }

    private static BackupEntry Entry(string name, BackupFileKind kind, byte[] content) =>
        new() { Name = name, Kind = kind, Size = content.LongLength, Sha256 = Hash(content) };

    private static string Hash(byte[] content) => Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static async Task WriteEntryAsync(ZipArchive zip, string name, byte[] content, CancellationToken ct)
    {
        await using var stream = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
        await stream.WriteAsync(content, ct);
    }

    // Sync files may be open in OneDrive or another TTM; read without locking, retrying brief locks.
    private static async Task<byte[]?> ReadSharedAsync(string path, CancellationToken ct)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var buffer = new MemoryStream();
                await fs.CopyToAsync(buffer, ct);
                return buffer.ToArray();
            }
            catch (IOException) when (attempt < 4) { await Task.Delay(150, ct); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
        }
        return null;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }
}
