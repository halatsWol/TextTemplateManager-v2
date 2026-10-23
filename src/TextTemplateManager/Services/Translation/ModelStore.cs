using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TextTemplateManager.Services.Translation;

/// <summary>What model.json in a pair's folder says: the registry name of the model (the engine reads its gemm
/// precision from it), and per file type the file name and the SHA-256 of its content.</summary>
public sealed record ModelInfo(string From, string To, string Name, string Version, string Architecture,
    Dictionary<string, string> Files, Dictionary<string, string> Hashes);

/// <summary>An installed direction: its folder and model.json.</summary>
public sealed record InstalledPair(string Directory, ModelInfo Info, long Size)
{
    public string From => Info.From;
    public string To => Info.To;

    /// <summary>Same files as the catalog's model for this direction (else an update is available).</summary>
    public bool Matches(ModelPair pair) =>
        pair.Version == Info.Version && pair.Files.Count == Info.Hashes.Count
        && pair.Files.All(f => Info.Hashes.TryGetValue(f.Type, out var h) && string.Equals(h, f.DecompressedHash, StringComparison.OrdinalIgnoreCase));
}

/// <summary>The installed translation models: one folder per direction ("en-de") with the model files and a
/// model.json, which the editor's translator reads (src/editor/src/translator/worker.js). Installs download
/// Mozilla's files, check both hashes, unpack them and only then put the folder in place.</summary>
public sealed class ModelStore
{
    public const string InfoFileName = "model.json";
    private const string PartialPrefix = ".partial-";
    private const string OldPrefix = ".old-";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly string _attachmentBaseUrl;

    public ModelStore(string directory, string attachmentBaseUrl = ModelCatalog.AttachmentBaseUrl)
    {
        Directory = directory;
        _attachmentBaseUrl = attachmentBaseUrl;
    }

    public string Directory { get; }

    public static string FolderName(string from, string to) => $"{from}-{to}";

    /// <summary>The installed directions; folders without a readable model.json or with files missing are left out.</summary>
    public IReadOnlyList<InstalledPair> Installed()
    {
        var result = new List<InstalledPair>();
        if (!System.IO.Directory.Exists(Directory)) return result;
        foreach (var dir in System.IO.Directory.GetDirectories(Directory))
        {
            if (Path.GetFileName(dir).StartsWith('.')) continue;
            if (ReadInfo(dir) is not { } info || Path.GetFileName(dir) != FolderName(info.From, info.To)) continue;
            long size = 0;
            bool complete = true;
            foreach (var file in info.Files.Values)
            {
                var fi = new FileInfo(Path.Combine(dir, file));
                if (!fi.Exists) { complete = false; break; }
                size += fi.Length;
            }
            if (complete) result.Add(new InstalledPair(dir, info, size));
        }
        return result.OrderBy(p => FolderName(p.From, p.To), StringComparer.Ordinal).ToList();
    }

    /// <summary>Downloads and installs one direction, replacing an installed one. Reports the downloaded bytes.
    /// Nothing changes unless every file arrives with the published hashes.</summary>
    public async Task InstallAsync(HttpClient http, ModelPair pair, IProgress<long>? downloaded = null, CancellationToken cancel = default)
    {
        string partial = Path.Combine(Directory, PartialPrefix + FolderName(pair.From, pair.To));
        DeleteQuietly(partial);
        System.IO.Directory.CreateDirectory(partial);
        try
        {
            long total = 0;
            foreach (var file in pair.Files)
            {
                string packed = Path.Combine(partial, file.Name + ".zst");
                await DownloadAsync(http, file, packed, n => downloaded?.Report(total += n), cancel);
                await UnpackAsync(file, packed, Path.Combine(partial, file.Name), cancel);
                File.Delete(packed);
            }
            WriteInfo(partial, new ModelInfo(pair.From, pair.To, pair.ModelName, pair.Version, pair.Architecture,
                pair.Files.ToDictionary(f => f.Type, f => f.Name),
                pair.Files.ToDictionary(f => f.Type, f => f.DecompressedHash.ToLowerInvariant())));
            PutInPlace(partial, FolderName(pair.From, pair.To));
        }
        catch
        {
            DeleteQuietly(partial);
            throw;
        }
    }

    public void Remove(string from, string to) => DeleteQuietly(Path.Combine(Directory, FolderName(from, to)));

    /// <summary>Writes the given installed directions to a zip, to be imported on a PC without internet access.</summary>
    public void Export(IEnumerable<InstalledPair> pairs, string zipPath)
    {
        string temp = zipPath + ".tmp";
        File.Delete(temp);
        using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
        {
            foreach (var pair in pairs)
            {
                string folder = FolderName(pair.From, pair.To);
                zip.CreateEntryFromFile(Path.Combine(pair.Directory, InfoFileName), $"{folder}/{InfoFileName}", CompressionLevel.Optimal);
                foreach (var file in pair.Info.Files.Values)
                    zip.CreateEntryFromFile(Path.Combine(pair.Directory, file), $"{folder}/{file}", CompressionLevel.Optimal);
            }
        }
        File.Move(temp, zipPath, overwrite: true);
    }

    /// <summary>Installs the directions in an exported zip, each only if all its files match the hashes in its
    /// model.json. Returns the imported directions; throws <see cref="InvalidDataException"/> when the zip holds none.</summary>
    public IReadOnlyList<ModelInfo> Import(string zipPath)
    {
        var imported = new List<ModelInfo>();
        using var zip = ZipFile.OpenRead(zipPath);
        var folders = zip.Entries.Where(e => e.FullName.EndsWith("/" + InfoFileName, StringComparison.Ordinal))
            .Select(e => e.FullName[..^(InfoFileName.Length + 1)]).Where(f => !f.Contains('/')).ToList();
        foreach (var folder in folders)
        {
            string partial = Path.Combine(Directory, PartialPrefix + folder);
            DeleteQuietly(partial);
            System.IO.Directory.CreateDirectory(partial);
            try
            {
                zip.GetEntry($"{folder}/{InfoFileName}")!.ExtractToFile(Path.Combine(partial, InfoFileName));
                if (ReadInfo(partial) is not { } info || folder != FolderName(info.From, info.To) || !IsSafe(info))
                    throw new InvalidDataException($"{folder}: model.json is not valid");
                foreach (var (type, file) in info.Files)
                {
                    var entry = zip.GetEntry($"{folder}/{file}") ?? throw new InvalidDataException($"{folder}: {file} is missing");
                    string target = Path.Combine(partial, file);
                    entry.ExtractToFile(target);
                    if (!info.Hashes.TryGetValue(type, out var expected) || !string.Equals(HashOf(target), expected, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"{folder}: {file} is damaged");
                }
                PutInPlace(partial, folder);
                imported.Add(info);
            }
            catch
            {
                DeleteQuietly(partial);
                throw;
            }
        }
        if (imported.Count == 0) throw new InvalidDataException("The file holds no translation models.");
        return imported;
    }

    private async Task DownloadAsync(HttpClient http, ModelFile file, string path, Action<long> read, CancellationToken cancel)
    {
        using var response = await http.GetAsync(_attachmentBaseUrl + file.Location, HttpCompletionOption.ResponseHeadersRead, cancel);
        response.EnsureSuccessStatusCode();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using (var input = await response.Content.ReadAsStreamAsync(cancel))
        await using (var output = File.Create(path))
        {
            var buffer = new byte[81920];
            int n;
            while ((n = await input.ReadAsync(buffer, cancel)) > 0)
            {
                hash.AppendData(buffer, 0, n);
                await output.WriteAsync(buffer.AsMemory(0, n), cancel);
                read(n);
            }
        }
        if (!string.Equals(Convert.ToHexString(hash.GetHashAndReset()), file.Hash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"{file.Name} arrived damaged.");
    }

    private static async Task UnpackAsync(ModelFile file, string packed, string path, CancellationToken cancel)
    {
        await using (var source = File.OpenRead(packed))
        await using (var input = new ZstdSharp.DecompressionStream(source))
        await using (var output = File.Create(path))
            await input.CopyToAsync(output, cancel);
        if (!string.Equals(HashOf(path), file.DecompressedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"{file.Name} couldn't be unpacked.");
    }

    // Swaps the finished folder in, so the translator never sees half a model.
    private void PutInPlace(string partial, string folder)
    {
        string target = Path.Combine(Directory, folder);
        string old = Path.Combine(Directory, OldPrefix + folder);
        DeleteQuietly(old);
        if (System.IO.Directory.Exists(target)) System.IO.Directory.Move(target, old);
        System.IO.Directory.Move(partial, target);
        DeleteQuietly(old);
    }

    // File names come from the zip's model.json and end up in paths: no folders, no "..".
    private static bool IsSafe(ModelInfo info) =>
        info.Files.Count > 0 && info.Files.Values.All(f => f.Length > 0 && f == Path.GetFileName(f) && f != InfoFileName && !f.StartsWith('.'));

    private static ModelInfo? ReadInfo(string dir)
    {
        try
        {
            var info = JsonSerializer.Deserialize<ModelInfo>(File.ReadAllText(Path.Combine(dir, InfoFileName)), Json);
            return info is { From.Length: > 0, To.Length: > 0, Name.Length: > 0, Files: not null, Hashes: not null } ? info : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    private static void WriteInfo(string dir, ModelInfo info) =>
        File.WriteAllText(Path.Combine(dir, InfoFileName), JsonSerializer.Serialize(info, Json));

    private static string HashOf(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void DeleteQuietly(string dir)
    {
        try { if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
