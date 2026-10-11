using TextTemplateManager.Models;
using TextTemplateManager.ViewModels;
using Xunit;

namespace TextTemplateManager.Tests;

public class ConflictReportTests
{
    private static Template Conflict(string title, string key) =>
        new() { Title = title, SingleKeyShortcut = key, HasSingleKeyConflict = true };

    private static Template Note(string title, string key) =>
        new() { Title = title, SingleKeyShortcut = key, HasSingleKeyCrossAreaWarning = true };

    [Fact]
    public void Conflicts_are_grouped_by_key_and_notes_follow()
    {
        var report = ConflictReport.From(new[]
        {
            Conflict("One", "Q"),
            Conflict("Two", "Q"),
            new Template { Title = "Multi", MultiKeyShortcut = "MSG", HasMultiKeyConflict = true },
            Note("Local", "Z"),
        }, hideNotes: false);

        Assert.Equal(new[]
        {
            "Single-Key Conflicts", "Shortcut: Q", "- One", "- Two",
            "Multi-Key Conflicts", "Shortcut: MSG", "- Multi",
            "Note — same single key in different areas (allowed; local wins, then sync order):", "- Z: Local",
        }, report.Lines.Select(l => l.Text));
        Assert.Equal(ConflictLineKind.Key, report.Lines[1].Kind);
        Assert.True(report.HasConflicts);
    }

    [Fact]
    public void The_setting_hides_the_notes()
    {
        var report = ConflictReport.From(new[] { Note("Local", "Z"), Note("Team", "Z") }, hideNotes: true);

        Assert.True(report.IsEmpty);
    }

    [Fact]
    public void Dismissed_notes_stay_hidden_until_they_change()
    {
        var notice = new ConflictNotice();
        var notes = ConflictReport.From(new[] { Note("Local", "Z"), Note("Team", "Z") }, hideNotes: false);

        Assert.True(notice.ShouldShow(notes));
        Assert.True(notice.CanDismiss);
        notice.Dismiss();
        Assert.False(notice.ShouldShow(notes));

        var changed = ConflictReport.From(new[] { Note("Local", "Z"), Note("Team", "Z"), Note("Other", "Y") }, hideNotes: false);
        Assert.True(notice.ShouldShow(changed));
    }

    [Fact]
    public void A_conflict_always_shows_and_forgets_the_dismissal()
    {
        var notice = new ConflictNotice();
        var notes = ConflictReport.From(new[] { Note("Local", "Z"), Note("Team", "Z") }, hideNotes: false);
        notice.ShouldShow(notes);
        notice.Dismiss();

        Assert.True(notice.ShouldShow(ConflictReport.From(new[] { Conflict("One", "Q"), Conflict("Two", "Q") }, hideNotes: false)));
        Assert.False(notice.CanDismiss);
        Assert.True(notice.ShouldShow(notes));   // the earlier dismissal is gone
    }
}
