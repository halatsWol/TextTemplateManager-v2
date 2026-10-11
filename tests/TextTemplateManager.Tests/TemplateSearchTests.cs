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
