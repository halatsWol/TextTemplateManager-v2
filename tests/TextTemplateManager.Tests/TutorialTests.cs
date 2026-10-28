using TextTemplateManager.Controls;
using TextTemplateManager.Models;
using TextTemplateManager.Tutorial;
using Windows.Foundation;
using Xunit;

namespace TextTemplateManager.Tests;

/// <summary>The guided tour runs on its own sample data in a throwaway folder, and its card sits beside the control
/// it explains.</summary>
public class TutorialTests
{
    [Fact]
    public async Task The_sample_has_local_and_synced_templates_and_the_users_settings_and_goes_away_after()
    {
        var real = new AppSettings { PasteWindowHotkey = "Ctrl+Alt+Q", StrictSearch = true };

        string dir;
        using (var sample = await TourSample.CreateAsync(real))
        {
            dir = sample.Directory;
            var items = sample.Store.LocalItems.Descendants().ToList();

            Assert.Equal(("Ctrl+Alt+Q", true), (sample.Store.CurrentSettings.PasteWindowHotkey, sample.Store.CurrentSettings.StrictSearch));
            Assert.NotSame(real, sample.Store.CurrentSettings);
            var team = Assert.Single(sample.Store.LocalItems, i => i.IsSyncRoot);
            Assert.Equal(("Team (sample)", "Out of office"), (team.Title, team.Children.Single().Title));
            Assert.Contains(items, i => i.Id == TourSample.ReleaseNotesId && i is Template t && t.Content.Contains("data-panel-type"));
            Assert.Contains(items, i => i is Template t && t.Content.Contains("<pre><code"));
            Assert.StartsWith(Path.GetTempPath(), dir);
            var backups = new TextTemplateManager.Data.BackupService(sample.Store.Storage).List();
            Assert.Equal(new[] { TextTemplateManager.Data.BackupTier.Recent, TextTemplateManager.Data.BackupTier.Daily },
                backups.Select(b => b.Tier));
            Assert.All(backups, b => Assert.True(b.IsValid, b.Problem));
            Assert.StartsWith(dir, backups[0].Path);
        }
        Assert.False(Directory.Exists(dir));
    }

    [Theory]
    [InlineData(false, "", Greeting.TourOffer)]          // first run
    [InlineData(false, "2.0.0", Greeting.TourOffer)]     // Later, or an update before the tour was done with
    [InlineData(true, "", Greeting.None)]                // first run that restored a backup
    [InlineData(true, "2.1.0", Greeting.None)]           // same version again
    [InlineData(true, "2.0.0", Greeting.WhatsNew)]       // updated
    [InlineData(true, "2.0.9", Greeting.WhatsNew)]
    [InlineData(true, "2.2.0", Greeting.None)]           // older version than the last run
    public void The_tour_is_offered_until_done_then_whats_new_once_per_update(bool tourDone, string lastRun, Greeting expected)
    {
        Assert.Equal(expected, StartupGreeting.For(new AppSettings { TutorialOfferDone = tourDone, LastRunVersion = lastRun }, "2.1.0"));
    }

    [Fact]
    public void Whats_new_is_the_release_notes_section_with_its_headings_bullets_and_bold()
    {
        string notes = "## TextTemplateManager {{VERSION}}\r\n\r\nIntro text.\r\n\r\n### What's new in this release\r\n\r\n#### Fixes\r\n\r\n"
            + "- **Search.** Finds `more`,\r\n  see [the manual](https://example.com).\r\n- Second.\r\n\r\nA closing\r\nparagraph.\r\n\r\n{{CHANGELOG}}\r\n\r\n"
            + "### Browser extension\r\n\r\n- [Chrome](https://example.com)\r\n";

        var blocks = WhatsNew.Parse(notes);

        Assert.Equal(new[] { WhatsNewKind.Heading, WhatsNewKind.Bullet, WhatsNewKind.Bullet, WhatsNewKind.Paragraph }, blocks.Select(b => b.Kind));
        Assert.Equal(new[] { ("Search.", true), (" Finds more, see the manual.", false) }, blocks[1].Runs);
        Assert.Equal(("A closing paragraph.", false), Assert.Single(blocks[3].Runs));
        Assert.Empty(WhatsNew.Parse("## No such section\n- x\n"));
    }

    [Fact]
    public void The_shipped_release_notes_have_a_whats_new_section_the_app_can_show()
    {
        // The repository's files, not the copies the app build puts next to the test binaries.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TextTemplateManager.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var files = Directory.GetFiles(Path.Combine(dir!.FullName, "releases"), "release.md", SearchOption.AllDirectories);
        Assert.NotEmpty(files);

        foreach (string file in files)
        {
            var blocks = WhatsNew.Parse(File.ReadAllText(file));

            Assert.Contains(blocks, b => b.Kind == WhatsNewKind.Bullet);
            Assert.DoesNotContain(blocks, b => b.Runs.Any(r => r.Text.Contains("{{") || r.Text.Contains("**")));
        }
    }

    [Fact]
    public void Whats_new_takes_the_notes_of_its_own_version_else_the_newest()
    {
        string root = TestEnvironment.NewScratchDir();
        foreach (string version in new[] { "2.9.0", "2.10.0", "2.6.0" })
        {
            Directory.CreateDirectory(Path.Combine(root, "v" + version, "en-US"));
            File.WriteAllText(Path.Combine(root, "v" + version, "en-US", "release.md"), version);
        }
        Directory.CreateDirectory(Path.Combine(root, "vnext", "en-US"));

        Assert.Equal("2.6.0", File.ReadAllText(WhatsNew.NotesFile(root, "2.6.0")!));
        Assert.Equal("2.10.0", File.ReadAllText(WhatsNew.NotesFile(root, "0.0.0-dev")!));
        Assert.Null(WhatsNew.NotesFile(Path.Combine(root, "missing"), "2.6.0"));
    }

    [Theory]
    [InlineData(10, 10, 100, 40, 122, 12)]   // room on the right, kept inside at the top
    [InlineData(700, 100, 280, 40, 328, 100)]  // too close to the right edge: on the left
    [InlineData(4, 10, 900, 40, 12, 62)]       // full-width control: below, kept inside at the left
    [InlineData(10, 520, 900, 60, 12, 168)]    // at the bottom, full width: above
    [InlineData(300, 500, 100, 80, 412, 248)]  // low on the screen: beside it, moved up to fit
    public void The_card_goes_on_the_first_side_with_room(double x, double y, double w, double h, double cardX, double cardY)
    {
        var (cx, cy) = TutorialOverlay.Beside(new Rect(x, y, w, h), new Size(360, 340), 1000, 600);
        Assert.Equal((cardX, cardY), (cx, cy));
    }
}
