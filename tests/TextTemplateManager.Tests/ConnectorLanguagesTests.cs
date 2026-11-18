using System.Linq;
using TextTemplateManager.Models;
using TextTemplateManager.Services.Platform;
using Xunit;

namespace TextTemplateManager.Tests;

/// <summary>A template's languages through the connector (protocol 3): only pasted languages are served, a new
/// template gets its languages checked before anything is added, and a write never touches the default's text.</summary>
public class ConnectorLanguagesTests
{
    // English (default), German, a tab without a language (not pasted), French.
    private static Template Offer() => new()
    {
        Title = "Offer", Content = "<p>Hello</p>", Language = "en", LanguageId = "a1a1",
        AltLanguages =
        [
            new() { Id = "b2b2", Language = "de", Content = "<p>Hallo</p>" },
            new() { Id = "c3c3", Content = "<p>Ciao</p>" },
            new() { Id = "d4d4", Language = "fr", Content = "<p>Bonjour</p>" },
        ],
    };

    private static int Status(System.Action act) => Assert.Throws<ConnectorRefusal>(act).Status;

    [Fact]
    public void Only_the_pasted_languages_are_described_and_found()
    {
        var offered = TemplateLanguages.Offered(Offer());

        var (language, languages) = ConnectorLanguages.Describe(offered);

        Assert.Equal("en", language);
        Assert.Equal(["a1a1:en", "b2b2:de", "d4d4:fr"], languages!.Select(l => $"{l.Id}:{l.Language}"));
        Assert.Equal("<p>Hallo</p>", ConnectorLanguages.Find(offered, "DE", null).Content);
        Assert.Equal("<p>Bonjour</p>", ConnectorLanguages.Find(offered, null, "d4d4").Content);
        Assert.Equal("<p>Hello</p>", ConnectorLanguages.Find(offered, null, null).Content);
        Assert.Equal(404, Status(() => ConnectorLanguages.Find(offered, null, "c3c3")));
        Assert.Equal(404, Status(() => ConnectorLanguages.Find(offered, "it", null)));
    }

    [Fact]
    public void A_template_in_one_language_has_no_language_list()
    {
        var offered = TemplateLanguages.Offered(new Template { Content = "x", Language = "en" });

        Assert.Equal(("en", null), ConnectorLanguages.Describe(offered));
        Assert.Equal((null, null), ConnectorLanguages.Describe(TemplateLanguages.Offered(new Template { Content = "x" })));
    }

    [Fact]
    public void A_new_template_gets_its_language_and_a_tab_per_other_language()
    {
        var t = new Template { Content = "<p>Hello</p>" };

        ConnectorLanguages.ApplyNew(t, "EN", [new("de", "<p>Hallo</p>"), new("zh-hant", "<p>你好</p>")]);

        Assert.Equal(["en", "de", "zh-Hant"], TemplateTabs.All(t).Select(x => x.Language));
        Assert.Equal("<p>Hallo</p>", t.AltLanguages![0].Content);
        Assert.NotNull(t.LanguageId);
    }

    [Theory]
    [InlineData(null, "de", "x")]       // other languages without the default's
    [InlineData("xx", null, null)]      // unknown language
    [InlineData("en", "qq", "x")]
    [InlineData("en", "EN", "x")]       // the default's language again
    [InlineData("en", "de", "")]        // no content
    public void A_new_template_with_wrong_languages_is_refused_and_left_alone(string? language, string? alt, string? content)
    {
        var t = new Template { Content = "<p>Hello</p>" };

        Assert.Equal(400, Status(() => ConnectorLanguages.ApplyNew(t, language, alt == null ? null : [new(alt, content)])));
        Assert.Null(t.Language);
        Assert.Null(t.AltLanguages);
    }

    [Fact]
    public void A_new_template_without_languages_stays_without()
    {
        var t = new Template { Content = "<p>Hello</p>" };

        ConnectorLanguages.ApplyNew(t, null, []);

        Assert.Null(t.Language);
    }

    [Fact]
    public void Writing_a_new_language_adds_a_tab_and_writing_a_pasted_one_replaces_its_text()
    {
        var t = Offer();

        var added = ConnectorLanguages.Write(t, "it", null, "<p>Ciao!</p>");
        var replaced = ConnectorLanguages.Write(t, "DE", null, "<p>Guten Tag</p>");
        var byId = ConnectorLanguages.Write(t, null, "d4d4", "<p>Salut</p>");

        Assert.True(added.Added);
        Assert.Equal("it", added.Language);
        Assert.Equal("<p>Ciao!</p>", t.ContentFor(added.LanguageId));
        Assert.Equal(("b2b2", false), (replaced.LanguageId, replaced.Added));
        Assert.Equal("<p>Guten Tag</p>", t.ContentFor("b2b2"));
        Assert.Equal(("fr", false), (byId.Language, byId.Added));
        Assert.Equal("<p>Salut</p>", t.ContentFor("d4d4"));
        Assert.Equal("<p>Hello</p>", t.Content);
        Assert.Equal(5, TemplateTabs.All(t).Count);
    }

    [Fact]
    public void A_write_never_changes_the_default_and_refuses_what_it_cant_place()
    {
        var t = Offer();

        Assert.Equal(409, Status(() => ConnectorLanguages.Write(t, "en", null, "x")));
        Assert.Equal(409, Status(() => ConnectorLanguages.Write(t, null, "a1a1", "x")));
        Assert.Equal(404, Status(() => ConnectorLanguages.Write(t, null, "ffff", "x")));
        Assert.Equal(400, Status(() => ConnectorLanguages.Write(t, "qq", null, "x")));
        Assert.Equal(409, Status(() => ConnectorLanguages.Write(new Template { Content = "x" }, "de", null, "y")));
        Assert.Equal("<p>Hello</p>", t.Content);
        Assert.Equal(4, TemplateTabs.All(t).Count);
    }
}
