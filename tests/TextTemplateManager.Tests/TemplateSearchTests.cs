using TextTemplateManager.Models;
using Xunit;

namespace TextTemplateManager.Tests;

public class TemplateSearchTests
{
    private sealed record Node(BaseItem Item, List<Node> Children);

    [Theory]
    [InlineData("", true)]
    [InlineData("meeting", true)]
    [InlineData("MEET", true)]
    [InlineData("mtg", true)]               // letters in order, not next to each other
    [InlineData("notes meeting", true)]     // word order doesn't matter
    [InlineData("gtm", false)]
    [InlineData("meeting agenda", false)]   // every word must match
    public void Each_word_matches_its_letters_in_order(string query, bool expected) =>
        Assert.Equal(expected, TemplateSearch.Matches(query, "Meeting notes"));

    [Theory]
    [InlineData("", true)]
    [InlineData("invoice", true)]
    [InlineData("INVOICE", true)]                    // case ignored
    [InlineData("invo", true)]                       // parts of words too
    [InlineData("nvoi", true)]
    [InlineData("invoice*", true)]
    [InlineData("*voice", true)]
    [InlineData("monthly invoice", true)]            // one phrase, as typed
    [InlineData("monthly  invoice", true)]           // spaces collapse
    [InlineData("invoice monthly", false)]           // in order
    [InlineData("monthly reminder", false)]          // adjacent, not with words between
    [InlineData("monthly * reminder", true)]
    [InlineData("mon.hly", true)]                    // . is one character
    [InlineData("mon..hly", false)]
    [InlineData("mthly", false)]                     // no fuzzy letters-in-order
    [InlineData("v2.0", true)]
    [InlineData("v2\\.0", true)]                     // \. is a dot
    [InlineData("v2\\.x", false)]
    [InlineData("v2\\*", false)]                     // \* is a star
    public void Strict_search_matches_the_phrase_as_typed(string query, bool expected)
    {
        var t = new Template { Title = "Monthly invoice reminder v2.0" };
        Assert.Equal(expected, TemplateSearch.Matches(query, t, strict: true));
    }

    // The manual's examples.
    [Theory]
    [InlineData("meet*", "Meeting notes", true)]
    [InlineData("invoice * reminder", "Invoice payment reminder", true)]
    [InlineData("colo.r", "Colour", true)]
    [InlineData("colo.r", "Color", false)]
    [InlineData("v2\\.0", "v2.0", true)]
    [InlineData("lunch", "a lunch order", true)]       // anywhere in the text, no wildcard needed
    [InlineData("lunch order", "a lunch order today", true)]
    [InlineData("unch ord", "A lunch order", true)]
    [InlineData("lunch today", "Lunch order for today", false)]
    public void Strict_search_examples(string query, string title, bool expected) =>
        Assert.Equal(expected, TemplateSearch.Matches(query, new Template { Title = title }, strict: true));

    [Fact]
    public void Strict_search_matches_each_field_on_its_own_and_the_text_only_when_asked()
    {
        var t = new Template { Title = "Reply", TagsCsv = "support, billing", Content = "<p>Kind   regards</p>" };

        Assert.True(TemplateSearch.Matches("billing", t, strict: true));
        Assert.False(TemplateSearch.Matches("reply support", t, strict: true));   // not across title and tags
        Assert.False(TemplateSearch.Matches("kind regards", t, strict: true));
        Assert.True(TemplateSearch.Matches("kind regards", t, includeContent: true, strict: true));
        Assert.True(TemplateSearch.Matches("a\\*b", new Template { Title = "a*b" }, strict: true));
    }

    [Theory]
    [InlineData("support billing", true)]
    [InlineData("billing support", true)]            // tags in any order
    [InlineData("bill* sup", true)]                  // each word as typed, wildcards and parts of tags too
    [InlineData("billing support urgent", false)]    // every word in one of the tags
    [InlineData("support, billing", true)]           // the tags as written still match
    [InlineData("first contact", true)]              // a tag of several words
    [InlineData("contact first", true)]
    [InlineData("first\\ contact", true)]            // \ keeps the space inside one word
    [InlineData("contact\\ first", false)]
    public void Strict_search_matches_the_tags_in_any_order(string query, bool expected)
    {
        var t = new Template { Title = "Reply", TagsCsv = "support, billing, first contact" };
        Assert.Equal(expected, TemplateSearch.Matches(query, t, strict: true));
    }

    [Fact]
    public void Tags_count_and_the_text_only_when_asked()
    {
        var t = new Template { Title = "Reply", TagsCsv = "support", Content = "<p>Kind regards</p>" };

        Assert.True(TemplateSearch.Matches("support", t));
        Assert.False(TemplateSearch.Matches("regards", t));
        Assert.True(TemplateSearch.Matches("regards", t, includeContent: true));
    }

    [Fact]
    public void Pruning_keeps_matches_and_the_folders_above_them()
    {
        var hit = new Template { Title = "Invoice" };
        var inner = new Folder { Title = "Billing", Children = { hit, new Template { Title = "Other" } } };
        var items = new BaseItem[] { new Folder { Title = "Work", Children = { inner } }, new Template { Title = "Lunch" } };

        var kept = TemplateSearch.Prune<Node>(items, "inv", false, (item, children) => new Node(item, children));

        var work = Assert.Single(kept);
        Assert.Equal("Work", work.Item.Title);
        var billing = Assert.Single(work.Children);
        Assert.Same(inner, billing.Item);
        Assert.Same(hit, Assert.Single(billing.Children).Item);
    }

    [Fact]
    public void A_matching_folder_keeps_only_its_matching_children()
    {
        var folder = new Folder { Title = "Mail", Children = { new Template { Title = "Lunch" } } };

        var kept = TemplateSearch.Prune<Node>(new BaseItem[] { folder }, "mail", false, (item, children) => new Node(item, children));

        Assert.Same(folder, Assert.Single(kept).Item);
        Assert.Empty(kept[0].Children);
    }
}
