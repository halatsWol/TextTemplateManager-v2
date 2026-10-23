using TextTemplateManager.Services.Translation;
using Xunit;

namespace TextTemplateManager.Tests;

/// <summary>The editor's Translate menu: which languages it offers with the installed models, and which it preselects.</summary>
public class TranslationChoicesTests
{
    private static InstalledPair Pair(string from, string to) =>
        new("", new ModelInfo(from, to, "m", "3.0", "base", new() { ["model"] = "m" }, new() { ["model"] = "00" }), 1);

    [Fact]
    public void A_language_installed_both_ways_is_source_and_target_with_English()
    {
        var c = TranslationChoices.From([Pair("en", "de"), Pair("de", "en")]);

        Assert.Equal(new[] { "en", "de" }, c.Sources.Select(s => s.Code));
        Assert.Equal(new[] { "en", "de" }, c.Targets.Select(s => s.Code));
        Assert.Equal(new[] { "English", "German" }, c.Sources.Select(s => s.Name));
    }

    [Fact]
    public void A_one_way_model_offers_only_that_direction()
    {
        var c = TranslationChoices.From([Pair("nb", "en")]);

        Assert.Equal(new[] { "nb" }, c.Sources.Select(s => s.Code));
        Assert.Equal(new[] { "en" }, c.Targets.Select(s => s.Code));
    }

    [Fact]
    public void Nothing_installed_offers_nothing()
    {
        var c = TranslationChoices.From([]);

        Assert.Empty(c.Sources);
        Assert.Empty(c.Targets);
        Assert.Equal(TranslationChoices.Detect, c.FromOrDefault(null));
        Assert.Null(c.ToOrDefault(null));
    }

    [Fact]
    public void The_remembered_languages_count_while_they_are_installed()
    {
        var c = TranslationChoices.From([Pair("en", "de"), Pair("de", "en"), Pair("fr", "en")]);

        Assert.Equal("fr", c.FromOrDefault("fr"));
        Assert.Equal("detect", c.FromOrDefault("detect"));
        Assert.Equal(TranslationChoices.Detect, c.FromOrDefault("es"));
        Assert.Equal("de", c.ToOrDefault("de"));
        Assert.Null(c.ToOrDefault("fr"));
        Assert.Null(c.ToOrDefault(null));
    }
}
