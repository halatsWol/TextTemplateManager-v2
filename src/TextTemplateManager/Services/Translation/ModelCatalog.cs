using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TextTemplateManager.Services.Translation;

/// <summary>One file of a model as Mozilla publishes it: zstd-compressed, with the hashes of both forms.</summary>
public sealed record ModelFile(string Type, string Name, string Location, string Hash, long Size, string DecompressedHash,
    long DecompressedSize);

/// <summary>The model for one direction ("en" to "de"): model, lexical shortlist and vocabulary files.</summary>
public sealed record ModelPair(string From, string To, string Version, string Architecture, IReadOnlyList<ModelFile> Files)
{
    public string Key => $"{From}-{To}";

    /// <summary>The model file's registry name; its suffix tells the engine the gemm precision.</summary>
    public string ModelName => Files.First(f => f.Type == "model").Name;

    public long DownloadSize => Files.Sum(f => f.Size);
    public long DiskSize => Files.Sum(f => f.DecompressedSize);
}

/// <summary>A language as offered for download: its models to and from English (one of them can be missing).</summary>
public sealed record TranslationLanguage(string Code, string Name, ModelPair? ToEnglish, ModelPair? FromEnglish)
{
    public IEnumerable<ModelPair> Pairs => new[] { ToEnglish, FromEnglish }.OfType<ModelPair>();
    public long DownloadSize => Pairs.Sum(p => p.DownloadSize);
    public long DiskSize => Pairs.Sum(p => p.DiskSize);

    /// <summary>Name of a language code in the app's language ("zh-Hans": "Chinese (Simplified)", "Chinesisch
    /// (Vereinfacht)"); the code if Windows doesn't know it.</summary>
    public static string NameOf(string code)
    {
        try
        {
            // ICU makes up a culture for codes it doesn't know, named "Unknown language".
            var culture = CultureInfo.GetCultureInfo(code);
            return culture.EnglishName.StartsWith("Unknown", StringComparison.OrdinalIgnoreCase) ? code : culture.DisplayName;
        }
        catch (CultureNotFoundException) { return code; }
    }
}

/// <summary>The models Firefox Translations offers, read from Mozilla's Remote Settings, the same list Firefox
/// downloads its models from.</summary>
public static class ModelCatalog
{
    public const string RecordsUrl =
        "https://firefox.settings.services.mozilla.com/v1/buckets/main/collections/translations-models-v2/changeset?_expected=0";

    // The server's attachment base_url; a record's attachment location is relative to it.
    public const string AttachmentBaseUrl = "https://firefox-settings-attachments.cdn.mozilla.net/";

    // The model format the bundled engine (Bergamot v0.6.0) reads.
    private const int SupportedMajorVersion = 3;

    // Firefox's filters: records for Android or for Nightly only don't apply to a desktop release.
    private static readonly HashSet<string> DesktopFilters = new() { "", "env.appinfo.OS != 'Android'" };

    private static readonly string[] ArchitecturePreference = { "base", "base-memory", "tiny" };

    public static async Task<IReadOnlyList<ModelPair>> LoadAsync(HttpClient http, CancellationToken cancel = default)
    {
        string json = await http.GetStringAsync(RecordsUrl, cancel);
        return Parse(json);
    }

    /// <summary>The models in a Remote Settings changeset: per direction the newest release version this engine
    /// reads, complete (model, shortlist and vocabulary).</summary>
    public static IReadOnlyList<ModelPair> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var records = new List<(string From, string To, string Version, string Architecture, ModelFile File)>();
        foreach (var r in doc.RootElement.GetProperty("changes").EnumerateArray())
        {
            if (!r.TryGetProperty("attachment", out var a) || a.ValueKind != JsonValueKind.Object) continue;
            if (!DesktopFilters.Contains(Text(r, "filter_expression"))) continue;
            string version = Text(r, "version");
            if (!TryParseVersion(version, out int major, out _, out bool prerelease) || major != SupportedMajorVersion || prerelease)
                continue;
            records.Add((Text(r, "sourceLanguage"), Text(r, "targetLanguage"), version, Text(r, "architecture"),
                new ModelFile(Text(r, "fileType"), Text(r, "name"), Text(a, "location"), Text(a, "hash"),
                    a.GetProperty("size").GetInt64(), Text(r, "decompressedHash"), r.GetProperty("decompressedSize").GetInt64())));
        }

        var pairs = new List<ModelPair>();
        foreach (var direction in records.GroupBy(r => (r.From, r.To)))
        {
            var best = direction
                .GroupBy(r => (r.Version, r.Architecture))
                .Select(g => new ModelPair(direction.Key.From, direction.Key.To, g.Key.Version, g.Key.Architecture,
                    g.Select(r => r.File).OrderBy(f => f.Type, StringComparer.Ordinal).ToList()))
                .Where(IsComplete)
                .OrderByDescending(p => VersionOrder(p.Version))
                .ThenBy(p => ArchitectureRank(p.Architecture))
                .FirstOrDefault();
            if (best != null) pairs.Add(best);
        }
        return pairs.OrderBy(p => p.Key, StringComparer.Ordinal).ToList();
    }

    /// <summary>The languages the pairs make up, each with its models to and from English, by name.</summary>
    public static IReadOnlyList<TranslationLanguage> Languages(IEnumerable<ModelPair> pairs)
    {
        var byKey = pairs.ToDictionary(p => p.Key);
        return byKey.Values.SelectMany(p => new[] { p.From, p.To }).Where(c => c != "en").Distinct()
            .Select(c => new TranslationLanguage(c, TranslationLanguage.NameOf(c),
                byKey.GetValueOrDefault($"{c}-en"), byKey.GetValueOrDefault($"en-{c}")))
            .OrderBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static bool IsComplete(ModelPair p)
    {
        var types = p.Files.Select(f => f.Type).ToList();
        if (types.Count != types.Distinct().Count()) return false;
        bool vocab = types.Contains("vocab") || (types.Contains("srcvocab") && types.Contains("trgvocab"));
        return types.Contains("model") && types.Contains("lex") && vocab;
    }

    internal static bool TryParseVersion(string version, out int major, out int minor, out bool prerelease)
    {
        major = minor = 0;
        int end = 0;
        while (end < version.Length && (char.IsDigit(version[end]) || version[end] == '.')) end++;
        prerelease = end < version.Length;
        var parts = version[..end].Split('.');
        return parts.Length >= 1 && int.TryParse(parts[0], out major)
            && (parts.Length < 2 || int.TryParse(parts[1], out minor));
    }

    private static int VersionOrder(string version) =>
        TryParseVersion(version, out int major, out int minor, out _) ? major * 10_000 + minor : 0;

    private static int ArchitectureRank(string architecture)
    {
        int i = Array.IndexOf(ArchitecturePreference, architecture);
        return i < 0 ? ArchitecturePreference.Length : i;
    }

    private static string Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
}
