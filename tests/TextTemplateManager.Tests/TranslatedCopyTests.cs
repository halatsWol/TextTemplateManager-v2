using TextTemplateManager.Services.Translation;
using Xunit;

namespace TextTemplateManager.Tests;

/// <summary>The Translate menu's "Create new template": the copy's title and multi-key shortcut.</summary>
public class TranslatedCopyTests
{
    [Fact]
    public void The_title_gets_the_target_language_tag() =>
        Assert.Equal("Not reachable (DE)", TranslatedCopy.Title("Not reachable", "en", "de"));

    [Fact]
    public void A_source_language_tag_is_replaced_not_stacked() =>
        Assert.Equal("Offer (EN)", TranslatedCopy.Title("Offer (DE)", "de", "en"));

    [Fact]
    public void Another_bracket_at_the_end_stays() =>
        Assert.Equal("Offer (Copy) (FR)", TranslatedCopy.Title("Offer (Copy)", "en", "fr"));

    [Fact]
    public void Chinese_gets_short_tags()
    {
        Assert.Equal("Offer (ZH)", TranslatedCopy.Title("Offer", "en", "zh-Hans"));
        Assert.Equal("Offer (ZH-TW)", TranslatedCopy.Title("Offer", "en", "zh-Hant"));
        Assert.Equal("MSGZHTW", TranslatedCopy.MultiKey("MSG", "Offer", "en", "zh-Hant"));
    }

    [Fact]
    public void The_multi_key_shortcut_gets_the_language_code() =>
        Assert.Equal("ANDMSGDE", TranslatedCopy.MultiKey("ANDMSG", "Offer", "en", "de"));

    [Fact]
    public void An_empty_multi_key_shortcut_stays_empty() =>
        Assert.Equal("", TranslatedCopy.MultiKey("", "Offer", "en", "de"));

    [Fact]
    public void A_translation_of_a_translation_swaps_the_code() =>
        Assert.Equal("ANDMSGEN", TranslatedCopy.MultiKey("ANDMSGDE", "Offer (DE)", "de", "en"));

    [Fact]
    public void Letters_that_only_look_like_a_code_stay_when_the_title_has_no_tag() =>
        Assert.Equal("CODEEN", TranslatedCopy.MultiKey("CODE", "Code review", "de", "en"));
}
