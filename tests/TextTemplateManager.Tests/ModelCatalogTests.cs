using System.Text.Json;
using TextTemplateManager.Services.Translation;
using Xunit;

namespace TextTemplateManager.Tests;

/// <summary>Settings ▸ Translation: which of Mozilla's model records the app offers.</summary>
public class ModelCatalogTests
{
    private static object Record(string from, string to, string type, string version = "3.0", string architecture = "base-memory",
        string filter = "", string? name = null) => new
    {
        name = name ?? $"{type}.{from}{to}.bin",
        version,
        fileType = type,
        architecture,
        sourceLanguage = from,
        targetLanguage = to,
        filter_expression = filter,
        decompressedHash = $"{from}{to}{type}{version}{architecture}",
        decompressedSize = 2000,
        attachment = new { hash = "h", size = 1000, location = $"loc/{from}{to}{type}{version}{architecture}.zst" },
    };

    private static IEnumerable<object> Pair(string from, string to, string version = "3.0", string architecture = "base-memory",
        string filter = "") =>
        new[] { "model", "lex", "vocab" }.Select(t => Record(from, to, t, version, architecture, filter));

    private static string Changeset(IEnumerable<object> records) => JsonSerializer.Serialize(new { changes = records.ToArray() });

    [Fact]
    public void A_complete_pair_is_offered_with_its_files_and_sizes()
    {
        var pairs = ModelCatalog.Parse(Changeset(Pair("en", "de")));

        var p = Assert.Single(pairs);
        Assert.Equal(("en", "de", "3.0", "base-memory"), (p.From, p.To, p.Version, p.Architecture));
        Assert.Equal(new[] { "lex", "model", "vocab" }, p.Files.Select(f => f.Type));
        Assert.Equal("model.ende.bin", p.ModelName);
        Assert.Equal(3000, p.DownloadSize);
        Assert.Equal(6000, p.DiskSize);
    }

    [Fact]
    public void The_newest_release_version_wins_and_prereleases_or_other_majors_are_skipped()
    {
        var pairs = ModelCatalog.Parse(Changeset(Pair("en", "de", "3.0").Concat(Pair("en", "de", "3.1"))
            .Concat(Pair("en", "de", "3.2a1")).Concat(Pair("en", "de", "4.0"))));

        Assert.Equal("3.1", Assert.Single(pairs).Version);
    }

    [Fact]
    public void Between_sizes_of_the_same_version_the_better_one_wins()
    {
        var pairs = ModelCatalog.Parse(Changeset(Pair("en", "hu", architecture: "tiny").Concat(Pair("en", "hu", architecture: "base"))));

        Assert.Equal("base", Assert.Single(pairs).Architecture);
    }

    [Fact]
    public void Android_and_nightly_records_are_left_out()
    {
        var pairs = ModelCatalog.Parse(Changeset(
            Pair("en", "de", "3.1", filter: "env.appinfo.OS == 'Android'")
                .Concat(Pair("en", "de", "3.0", filter: "env.appinfo.OS != 'Android'"))
                .Concat(Pair("nn", "en", filter: "env.channel == 'default' || env.channel == 'nightly'"))));

        var p = Assert.Single(pairs);
        Assert.Equal(("en-de", "3.0"), (p.Key, p.Version));
    }

    [Fact]
    public void An_incomplete_pair_is_not_offered()
    {
        var records = new[] { Record("en", "fr", "model"), Record("en", "fr", "lex") };

        Assert.Empty(ModelCatalog.Parse(Changeset(records)));
    }

    [Fact]
    public void Separate_source_and_target_vocabularies_count_as_complete()
    {
        var records = new[] { Record("ja", "en", "model"), Record("ja", "en", "lex"), Record("ja", "en", "srcvocab"), Record("ja", "en", "trgvocab") };

        Assert.Single(ModelCatalog.Parse(Changeset(records)));
    }

    [Fact]
    public void Languages_combine_both_directions_and_are_named()
    {
        var pairs = ModelCatalog.Parse(Changeset(Pair("en", "de").Concat(Pair("de", "en")).Concat(Pair("nb", "en")).Concat(Pair("en", "zh-Hans"))));

        var languages = ModelCatalog.Languages(pairs);

        var de = languages.Single(l => l.Code == "de");
        Assert.Equal("German", de.Name);
        Assert.Equal(("de-en", "en-de"), (de.ToEnglish!.Key, de.FromEnglish!.Key));
        Assert.Equal(6000, de.DownloadSize);
        var nb = languages.Single(l => l.Code == "nb");
        Assert.NotNull(nb.ToEnglish);
        Assert.Null(nb.FromEnglish);
        Assert.Equal("Chinese (Simplified)", languages.Single(l => l.Code == "zh-Hans").Name);
        Assert.DoesNotContain(languages, l => l.Code == "en");
    }

    [Fact]
    public void An_unknown_code_keeps_its_code_as_name() => Assert.Equal("xq", TranslationLanguage.NameOf("xq"));
}
