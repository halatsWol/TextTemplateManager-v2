using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using TextTemplateManager.Helpers;
using Xunit;

namespace TextTemplateManager.Tests;

/// <summary>The Store texts in releases\vX.Y.Z\&lt;language&gt;\store.md: every UI language has a listing in the newest
/// release, and every listing stays within Partner Center's limits, so a pasted text isn't cut or refused.</summary>
public partial class StoreTextTests
{
    private static string ReleasesDir([CallerFilePath] string self = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(self)!, "..", "..", "releases"));

    public static TheoryData<string> Listings()
    {
        var data = new TheoryData<string>();
        foreach (string file in Directory.GetFiles(ReleasesDir(), "store.md", SearchOption.AllDirectories))
            data.Add(Path.GetRelativePath(ReleasesDir(), file));
        return data;
    }

    // The ```text block under a "### <heading>" or "## <heading>" line.
    private static string Block(string markdown, string heading)
    {
        var m = Regex.Match(markdown, @"(?ms)^#{2,3} " + Regex.Escape(heading) + @"[^\n]*\n.*?^```text\r?\n(.*?)\r?\n```");
        Assert.True(m.Success, $"no text block under '{heading}'");
        return m.Groups[1].Value.Replace("\r\n", "\n");
    }

    [Fact]
    public void The_newest_release_has_a_store_listing_in_every_ui_language()
    {
        string newest = Directory.GetDirectories(ReleasesDir(), "v*")
            .OrderByDescending(d => Version.Parse(Path.GetFileName(d)[1..])).First();

        foreach (string language in UiLanguages.Available)
            Assert.True(File.Exists(Path.Combine(newest, language, "store.md")), $"{Path.GetFileName(newest)}/{language}/store.md is missing");
    }

    [Theory]
    [MemberData(nameof(Listings))]
    public void A_store_listing_stays_within_the_partner_center_limits(string listing)
    {
        string text = File.ReadAllText(Path.Combine(ReleasesDir(), listing));

        Assert.Equal("TextTemplateManager", Block(text, "Product name").Trim());
        Assert.InRange(Block(text, "Short description").Length, 1, 1000);
        Assert.InRange(Block(text, "Description").Length, 1, 10000);
        Assert.InRange(Block(text, "What's new").Trim().Length, 1, 1500);

        string[] features = Lines(Block(text, "Product features"));
        Assert.InRange(features.Length, 1, 20);
        Assert.All(features, f => Assert.True(f.Length <= 200, $"feature over 200 characters: {f}"));

        string[] terms = Lines(Block(text, "Search terms"));
        Assert.InRange(terms.Length, 1, 7);
        Assert.All(terms, t => Assert.True(t.Length < 30, $"search term of 30 characters or more: {t}"));
        Assert.True(terms.Sum(t => t.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length) <= 21, "search terms over 21 words");

        string captionHeading = text.Contains("### Screenshot captions") ? "Screenshot captions" : "Screenshots and logo";
        string[] captions = Lines(Block(text, captionHeading));
        Assert.All(captions, c => Assert.True(c[4..].Length <= 200, $"caption over 200 characters: {c}"));   // after "01  "
    }

    [Fact]
    public void Every_listing_of_a_release_has_as_many_features_and_captions_as_the_english_one()
    {
        foreach (string release in Directory.GetDirectories(ReleasesDir(), "v*"))
        {
            string english = File.ReadAllText(Path.Combine(release, "en-US", "store.md"));
            int features = Lines(Block(english, "Product features")).Length;
            int captions = Lines(Block(english, "Screenshots and logo")).Length;
            foreach (string file in Directory.GetFiles(release, "store.md", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(file);
                string captionHeading = text.Contains("### Screenshot captions") ? "Screenshot captions" : "Screenshots and logo";
                Assert.True(features == Lines(Block(text, "Product features")).Length, $"{file}: product features differ from en-US");
                Assert.True(captions == Lines(Block(text, captionHeading)).Length, $"{file}: screenshot captions differ from en-US");
            }
        }
    }

    private static string[] Lines(string block) =>
        block.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
