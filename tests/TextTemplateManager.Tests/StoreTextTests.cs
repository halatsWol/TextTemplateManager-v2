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

    [Fact]
    public void Every_listing_has_the_screenshot_texts_of_the_english_images()
    {
        foreach (string release in Directory.GetDirectories(ReleasesDir(), "v*"))
        {
            string englishFile = Path.Combine(release, "en-US", "store.md");
            if (!File.ReadAllText(englishFile).Contains("### Screenshot images")) continue;   // releases before 2.6.0
            var english = Lines(Block(File.ReadAllText(englishFile), "Screenshot images")).Select(ImageFields).ToList();
            foreach (string file in Directory.GetFiles(release, "store.md", SearchOption.AllDirectories))
            {
                var images = Lines(Block(File.ReadAllText(file), "Screenshot images")).Select(ImageFields).ToList();
                Assert.True(english.Select(i => (i.Name, i.Shot)).SequenceEqual(images.Select(i => (i.Name, i.Shot))),
                    $"{file}: the images (name | capture) differ from en-US");
                Assert.All(images, i => Assert.True(i.Caption.Length > 0 && i.Sub.Length > 0, $"{file}: {i.Name} without caption or subline"));
            }
        }
    }

    private static (string Name, string Shot, string Caption, string Sub) ImageFields(string line)
    {
        var f = line.Split('|').Select(s => s.Trim()).ToArray();
        Assert.True(f.Length == 4, $"not 'name | capture | caption | subline': {line}");
        return (f[0], f[1], f[2], f[3]);
    }

    [Fact]
    public void Every_ui_language_has_screenshot_sample_data_whose_shots_exist()
    {
        string samples = Path.Combine(ReleasesDir(), "..", "design", "store", "samples");
        foreach (string language in UiLanguages.Available)
        {
            string file = Path.Combine(samples, language + ".json");
            Assert.True(File.Exists(file), $"design/store/samples/{language}.json is missing");
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));
            var root = doc.RootElement;
            var titles = new HashSet<string>();
            void Collect(System.Text.Json.JsonElement items)
            {
                foreach (var e in items.EnumerateArray())
                    if (e.TryGetProperty("folder", out _)) Collect(e.GetProperty("items"));
                    else titles.Add(e.GetProperty("title").GetString()!);
            }
            Collect(root.GetProperty("local"));
            Collect(root.GetProperty("team"));
            foreach (var shot in root.GetProperty("shots").EnumerateObject())
                Assert.True(titles.Contains(shot.Value.GetString()!), $"{language}: shot '{shot.Name}' names a template that isn't in the sample data");
            foreach (string key in new[] { "notepadFile", "notepadText", "searchTerm", "teamName", "teamFile" })
                Assert.False(string.IsNullOrWhiteSpace(root.GetProperty(key).GetString()), $"{language}: {key} is empty");
        }
    }

    private static string[] Lines(string block) =>
        block.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
