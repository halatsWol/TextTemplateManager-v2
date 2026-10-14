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
    [InlineData(10, 10, 100, 40, 122, 12)]     // room on the right, kept inside at the top
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
