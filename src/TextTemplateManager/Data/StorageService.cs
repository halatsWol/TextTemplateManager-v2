using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using TextTemplateManager.Models;
using TextTemplateManager.Services.Platform;


namespace TextTemplateManager.Data;

/// <summary>All file IO for one data folder: the app's own files (data, settings, sync settings),
/// exports and the shared sync files. Every write goes through one per-path lock, which also feeds
/// the write-activity signals.</summary>
public sealed class StorageService
{
    /// <summary>Env var that redirects the whole data root. Set by the test suite and by dev/UI test runs
    /// so they can never touch real templates or settings; unset everywhere else.</summary>
    public const string DataDirOverrideVariable = "TTM_DATA_DIR";

    private const string DataFileName = "data.ttmdata";
    private const string SettingsFileName = "settings.ttmsettings";
    private const string SyncSettingsFileName = "sync.ttmsettings";

    private static readonly StorageJsonContext Json = new(new JsonSerializerOptions
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Relaxed escaping keeps '<' '>' '&' literal — readable, and safe for a local file.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });

    private readonly string _baseDirectory;

    /// <summary>Storage rooted at <paramref name="baseDirectory"/>, backups in <paramref name="backupDirectory"/>
    /// (default: a Backups folder inside it), the 1.x data read from <paramref name="legacyDirectory"/> (default: a
    /// TTM1 folder inside it). Touches no files; the folder must exist before the first write
    /// (see <see cref="CreateDefault"/>).</summary>
    public StorageService(string baseDirectory, string? backupDirectory = null, string? legacyDirectory = null)
    {
        _baseDirectory = baseDirectory;
        BackupDirectory = backupDirectory ?? Path.Combine(baseDirectory, "Backups");
        LegacyDirectory = legacyDirectory ?? Path.Combine(baseDirectory, "TTM1");
    }

    /// <summary>The app's storage: the environment's data folder, or the one <see cref="DataDirOverrideVariable"/>
    /// points to (backups and the 1.x data are then looked for inside it too). Creates the data folder.</summary>
    public static StorageService CreateDefault(IAppEnvironment environment)
    {
        string? overridden = Environment.GetEnvironmentVariable(DataDirOverrideVariable);
        bool redirected = !string.IsNullOrWhiteSpace(overridden);
        string baseDirectory = redirected ? Path.GetFullPath(overridden!) : environment.DataDirectory;
        Directory.CreateDirectory(baseDirectory);
        return redirected ? new StorageService(baseDirectory)
            : new StorageService(baseDirectory, environment.BackupDirectory, AppEnvironment.LegacyDataDirectory);
    }

    /// <summary>Where automatic backups are written. Created on the first backup.</summary>
    public string BackupDirectory { get; }

    /// <summary>The 1.x data folder. Only ever read.</summary>
    public string LegacyDirectory { get; }

    /// <summary>True when <paramref name="content"/> loads as a file of the given kind with the app's own
    /// loaders (backups only keep files that do).</summary>
    internal static bool Parses(BackupFileKind kind, byte[] content)
    {
        try
        {
            var reader = JsonReaderFor(content);
            return kind switch
            {
                BackupFileKind.Data or BackupFileKind.SyncFile => JsonSerializer.Deserialize(ref reader, Json.Folder) != null,
                BackupFileKind.Settings => JsonSerializer.Deserialize(ref reader, Json.AppSettings) != null,
                BackupFileKind.SyncSettings => JsonSerializer.Deserialize(ref reader, Json.SyncSettings) != null,
                _ => false,
            };
        }
        catch { return false; }
    }

    // Files from older versions may start with a UTF-8 BOM, which the reader doesn't skip by itself.
    private static Utf8JsonReader JsonReaderFor(byte[] content) =>
        new(content.AsSpan().StartsWith(Encoding.UTF8.Preamble) ? content.AsSpan(3) : content);

    /// <summary>Settings as <see cref="SaveSettingsAsync"/> writes them.</summary>
    internal static byte[] SerializeSettings(AppSettings settings) => Utf8NoBom.GetBytes(JsonSerializer.Serialize(settings, Json.AppSettings));

    internal static AppSettings? ParseSettings(byte[] content)
    {
        try { var reader = JsonReaderFor(content); return JsonSerializer.Deserialize(ref reader, Json.AppSettings); }
        catch { return null; }
    }

    /// <summary>The JSON object in a file's content (a leading BOM skipped), or null.</summary>
    internal static System.Text.Json.Nodes.JsonObject? ParseObject(byte[] content)
    {
        try { var reader = JsonReaderFor(content); return System.Text.Json.Nodes.JsonNode.Parse(ref reader) as System.Text.Json.Nodes.JsonObject; }
        catch { return null; }
    }

    internal static SyncSettings? ParseSyncSettings(byte[] content)
    {
        try { var reader = JsonReaderFor(content); return JsonSerializer.Deserialize(ref reader, Json.SyncSettings); }
        catch { return null; }
    }

    /// <summary>Sync settings as <see cref="SaveSyncSettingsAsync"/> writes them.</summary>
    internal static byte[] SerializeSyncSettings(SyncSettings settings) => Utf8NoBom.GetBytes(JsonSerializer.Serialize(settings, Json.SyncSettings));

    internal static byte[] SerializeManifest(BackupManifest manifest) => JsonSerializer.SerializeToUtf8Bytes(manifest, Json.BackupManifest);

    internal static Folder? ParseTree(byte[] content)
    {
        try { var reader = JsonReaderFor(content); return JsonSerializer.Deserialize(ref reader, Json.Folder); }
        catch { return null; }
    }

    /// <summary>A tree as the data file stores it (same text as <see cref="SaveAsync"/> writes).</summary>
    internal static byte[] SerializeTree(Folder root) => Utf8NoBom.GetBytes(JsonSerializer.Serialize(root, Json.Folder));

    internal static byte[] SerializeJournal(RestoreJournal journal) => JsonSerializer.SerializeToUtf8Bytes(journal, Json.RestoreJournal);

    internal static RestoreJournal? ParseJournal(byte[] content)
    {
        try { var reader = JsonReaderFor(content); return JsonSerializer.Deserialize(ref reader, Json.RestoreJournal); }
        catch { return null; }
    }

    internal static BackupManifest? ParseManifest(byte[] content)
    {
        try { var reader = JsonReaderFor(content); return JsonSerializer.Deserialize(ref reader, Json.BackupManifest); }
        catch { return null; }
    }

    // Serialize writes per file path so concurrent saves (e.g. rapid Sync-settings edits, which fire
    // two writes per click) can't open the same file at once -> ERROR_SHARING_VIOLATION. Single-
    // instance app, so an in-process lock per path suffices, and it isolates a slow/locked file (a
    // retrying OneDrive source) from unrelated writes the way one global lock would not.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _writeLocks =
        new(StringComparer.OrdinalIgnoreCase);

    private int _writesInFlight;
    private long _lastWriteTicks;

    /// <summary>True while any persisted write is queued or running. Every write goes through the lock
    /// below, so this covers sync files, templates and settings alike.</summary>
    public bool WritesInFlight => Volatile.Read(ref _writesInFlight) > 0;

    /// <summary>True when nothing has been written for <paramref name="quietMs"/>. Anything that copies the
    /// files (a backup) waits for this: a sync write caught partway is exactly how a copy ends up truncated
    /// or as a cloud conflict copy. A single idle instant isn't enough —
    /// the sync poll writes without any user input, so the quiet period is what makes it meaningful.</summary>
    private long _writeCount;

    /// <summary>Counts writes that changed a file (a save skipped as unchanged doesn't count), so a watcher can
    /// tell that something was saved since it last looked (the last-write time can't: two writes inside one
    /// clock tick look the same).</summary>
    public long WriteCount => Interlocked.Read(ref _writeCount);

    public bool WritesQuiet(int quietMs = 2000) =>
        !WritesInFlight && Environment.TickCount64 - Interlocked.Read(ref _lastWriteTicks) >= quietMs;

    /// <summary>Completes once no write is queued or running.</summary>
    public async Task WaitForWritesAsync()
    {
        while (WritesInFlight) await Task.Delay(50);
    }

    private volatile bool _writesSuspended;

    /// <summary>From now on every save is dropped (until the app restarts or <see cref="ResumeWrites"/>): a restore
    /// swaps in other files, and nothing the running app still holds may be written over them.</summary>
    public async Task SuspendWritesAsync()
    {
        _writesSuspended = true;
        await WaitForWritesAsync();
    }

    /// <summary>Saves are written again (a restore that failed changed nothing).</summary>
    public void ResumeWrites() => _writesSuspended = false;

    private async Task WithWriteLock(string path, Func<Task> write)
    {
        if (_writesSuspended) return;
        if (_protectedPaths.ContainsKey(path)) return;   // unreadable and couldn't be set aside — never overwrite

        // Counted before the wait, so a write queued behind a slow one (a retrying OneDrive target)
        // still reads as work in progress.
        Interlocked.Increment(ref _writesInFlight);
        try
        {
            var gate = _writeLocks.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync();
            try { await write(); }
            finally { gate.Release(); }
        }
        finally
        {
            Interlocked.Exchange(ref _lastWriteTicks, Environment.TickCount64);
            Interlocked.Decrement(ref _writesInFlight);
        }
    }

    /// <summary>One of the app's own files that existed but couldn't be read at load. <c>KeptAs</c> is where
    /// the original was moved; null when it couldn't be moved either (then it is never written this session).</summary>
    public sealed record UnreadableFile(string Path, string? KeptAs);

    private readonly ConcurrentQueue<UnreadableFile> _unreadable = new();
    private readonly ConcurrentDictionary<string, byte> _protectedPaths = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns and clears the files reported unreadable since the last call.</summary>
    public IReadOnlyList<UnreadableFile> TakeUnreadableFiles()
    {
        var files = new List<UnreadableFile>();
        while (_unreadable.TryDequeue(out var f)) files.Add(f);
        return files;
    }

    /// <summary>Loads one of the app's own files (data, settings, sync settings). One that exists but can't
    /// be read or parsed is moved aside and reported, so the next save can't overwrite it with an empty
    /// tree or defaults. Returns null when the file is missing or was set aside.</summary>
    internal async Task<T?> LoadOwnFileAsync<T>(string path) where T : class
    {
        if (!File.Exists(path)) return null;
        var typeInfo = (JsonTypeInfo<T>)Json.GetTypeInfo(typeof(T))!;
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                var value = JsonSerializer.Deserialize(await File.ReadAllTextAsync(path), typeInfo);
                if (value != null) return LegacyRtf.ConvertTree(value);
                break;
            }
            catch (IOException) when (attempt < 2) { await Task.Delay(200); }   // briefly locked (antivirus, backup tool)
            catch (Exception) { break; }
        }
        SetAside(path);
        return null;
    }

    private void SetAside(string path)
    {
        string keptAs = $"{path}.broken-{DateTime.Now:yyyyMMdd-HHmmss}";
        try
        {
            File.Move(path, keptAs);
            _unreadable.Enqueue(new UnreadableFile(path, keptAs));
        }
        catch
        {
            _protectedPaths[path] = 0;
            _unreadable.Enqueue(new UnreadableFile(path, null));
        }
    }

    /// <summary>The live data folder.</summary>
    public string DataDirectory => _baseDirectory;

    public string GetDataPath() => Path.Combine(_baseDirectory, DataFileName);
    public string GetSettingsPath() => Path.Combine(_baseDirectory, SettingsFileName);
    public string GetSyncSettingsPath() => Path.Combine(_baseDirectory, SyncSettingsFileName);

    /// <summary>Append-only log for otherwise-fatal unhandled exceptions.</summary>
    public string GetCrashLogPath() => Path.Combine(_baseDirectory, "crash.log");

    /// <summary>Load the sync config, or a new empty one if none exists (or it was unreadable and set aside).</summary>
    public async Task<SyncSettings> LoadSyncSettingsAsync() =>
        await LoadOwnFileAsync<SyncSettings>(GetSyncSettingsPath()) ?? new SyncSettings();

    public async Task SaveSyncSettingsAsync(SyncSettings settings)
    {
        string json = JsonSerializer.Serialize(settings, Json.SyncSettings);
        string path = GetSyncSettingsPath();
        await WithWriteLock(path, () => WriteAtomicAsync(path, json));
    }

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    // Flushed to disk before the swap, so a crash or power loss leaves either the old file or the new
    // one in place, never a truncated mix.
    private static async Task WriteTempAsync(string tmpPath, string content)
    {
        await using var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 4096, FileOptions.Asynchronous);
        await fs.WriteAsync(Utf8NoBom.GetBytes(content));
        fs.Flush(flushToDisk: true);
    }

    private async Task WriteAtomicAsync(string path, string content)
    {
        string tmp = path + ".tmp";
        await WriteTempAsync(tmp, content);
        File.Move(tmp, path, overwrite: true);
        Interlocked.Increment(ref _writeCount);
    }

    /// <summary>Save the root Folder to path (local or sync location).</summary>
    public async Task SaveAsync(string path, Folder root)
    {
        string json = JsonSerializer.Serialize(root, Json.Folder);
        await WithWriteLock(path, async () =>
        {
            // Skip the write if the file already matches, so its modified-time doesn't churn.
            if (File.Exists(path))
            {
                string existing = await File.ReadAllTextAsync(path);
                if (string.Equals(existing, json, StringComparison.Ordinal))
                    return;
            }

            await WriteAtomicAsync(path, json);
        });
    }

    public static async Task<Folder?> LoadRootAsync(string filePath)
    {
        if (!File.Exists(filePath)) return null;
        try
        {
            string json = await File.ReadAllTextAsync(filePath);
            return LegacyRtf.ConvertTree(JsonSerializer.Deserialize(json, Json.Folder));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Storage Error: {ex.Message}");
            return null;
        }
    }

    // ---- Shared (non-locking) IO for sync files, which may live on OneDrive ----

    /// <summary>Read a root Folder without locking the file, retrying transient locks (OneDrive).</summary>
    public static async Task<Folder?> LoadRootSharedAsync(string filePath) => (await ReadRootSharedAsync(filePath)).Root;

    /// <summary>A sync file read: its tree, or null; <paramref name="Locked"/> when the file is there but stayed
    /// locked through the retries (worth reading again later, unlike a file that doesn't load).</summary>
    public sealed record SharedRead(Folder? Root, bool Locked);

    public static async Task<SharedRead> ReadRootSharedAsync(string filePath)
    {
        if (!File.Exists(filePath)) return new(null, false);
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                return new(LegacyRtf.ConvertTree(await JsonSerializer.DeserializeAsync(fs, Json.Folder)), false);
            }
            catch (IOException) { await Task.Delay(150); }        // transient lock -> retry
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Sync read error: {ex.Message}");
                return new(null, false);
            }
        }
        return new(null, File.Exists(filePath));
    }

    private static async Task<string?> ReadAllTextSharedAsync(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var r = new StreamReader(fs);
            return await r.ReadToEndAsync();
        }
        catch { return null; }
    }

    /// <summary>Write a sync file: skip if unchanged, else temp-then-atomic-move, retrying if the
    /// target is briefly locked (OneDrive).</summary>
    public async Task SaveSharedAsync(string path, Folder root)
    {
        string json = JsonSerializer.Serialize(root, Json.Folder);
        await WithWriteLock(path, async () =>
        {
            string? existing = File.Exists(path) ? await ReadAllTextSharedAsync(path) : null;
            if (string.Equals(existing, json, StringComparison.Ordinal)) return;

            string tmp = path + ".tmp";
            await WriteTempAsync(tmp, json);
            for (int attempt = 0; attempt < 6; attempt++)
            {
                try { File.Move(tmp, path, overwrite: true); Interlocked.Increment(ref _writeCount); return; }
                catch (IOException) { await Task.Delay(200); }        // target locked -> retry
            }
            try { File.Delete(tmp); } catch { /* leave temp if even delete fails */ }
        });
    }

    public async Task<AppSettings> LoadSettingsAsync() =>
        await LoadOwnFileAsync<AppSettings>(GetSettingsPath()) ?? new AppSettings();

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        string path = GetSettingsPath();
        string json = JsonSerializer.Serialize(settings, Json.AppSettings);
        await WithWriteLock(path, () => WriteAtomicAsync(path, json));
    }
}
