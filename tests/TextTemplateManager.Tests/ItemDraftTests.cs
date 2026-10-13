using TextTemplateManager.Common;
using TextTemplateManager.Models;
using TextTemplateManager.ViewModels;
using Xunit;

namespace TextTemplateManager.Tests;

/// <summary>With autosave off the right pane edits a copy: the saved item only changes on Save, and only real
/// edits count as unsaved (not the editor's own reformatting of the content when it loads).</summary>
public class ItemDraftTests
{
    private static Template Saved() => new()
    {
        Title = "Invoice", Content = "<p>Dear <b>customer</b></p>", TagsCsv = "billing",
        SingleKeyShortcut = "I", MultiKeyShortcut = "INV", DefaultPasteMode = PasteMode.HTML,
    };

    [Fact]
    public void Edits_stay_in_the_draft_until_committed()
    {
        var saved = Saved();
        var draft = ItemDraft.For(saved);
        var edited = (Template)draft.Edited;

        edited.Title = "Invoice reminder";
        edited.Content = "<p>Dear customer, again</p>";
        edited.MultiKeyShortcut = "REM";

        Assert.True(draft.IsDirty);
        Assert.Equal(("Invoice", "<p>Dear <b>customer</b></p>", "INV"), (saved.Title, saved.Content, saved.MultiKeyShortcut));

        draft.Commit();

        Assert.False(draft.IsDirty);
        Assert.Equal(("Invoice reminder", "<p>Dear customer, again</p>", "REM"), (saved.Title, saved.Content, saved.MultiKeyShortcut));
        Assert.Equal(("billing", "I", PasteMode.HTML), (saved.TagsCsv, saved.SingleKeyShortcut, saved.DefaultPasteMode));
    }

    [Fact]
    public void An_edit_undone_by_hand_is_not_unsaved_and_dirty_changes_are_reported()
    {
        var draft = ItemDraft.For(Saved());
        int reports = 0;
        draft.DirtyChanged += () => reports++;

        draft.Edited.Title = "Other";
        draft.Edited.Title = "Invoice";

        Assert.False(draft.IsDirty);
        Assert.Equal(2, reports);
    }

    [Fact]
    public void The_editors_reformatting_on_load_is_not_an_edit_and_isnt_written_back()
    {
        var saved = Saved();
        var draft = ItemDraft.For(saved);
        var edited = (Template)draft.Edited;

        draft.ContentLoaded("<p>Dear <strong>customer</strong></p>");
        Assert.False(draft.IsDirty);

        edited.Title = "Invoice 2";
        draft.Commit();
        Assert.Equal("<p>Dear <b>customer</b></p>", saved.Content);   // only the title was changed

        edited.Content = "<p>Changed</p>";
        Assert.True(draft.IsDirty);
        draft.ContentLoaded("<p>late</p>");   // after a real edit the editor's report changes nothing
        Assert.Equal("<p>Changed</p>", edited.Content);
    }

    [Fact]
    public void A_folders_draft_holds_its_title()
    {
        var folder = new Folder { Title = "Work" };
        var draft = ItemDraft.For(folder);

        draft.Edited.Title = "Office";
        Assert.True(draft.IsDirty);
        draft.Commit();

        Assert.Equal("Office", folder.Title);
        Assert.Equal(folder.Id, draft.Edited.Id);
    }

    [Fact]
    public void Edits_move_onto_an_item_that_replaced_the_original()
    {
        var draft = ItemDraft.For(Saved());
        draft.Edited.Title = "Edited";
        var replacement = Saved();
        replacement.Id = draft.Original.Id;

        var moved = draft.RebaseOnto(replacement);

        Assert.Same(replacement, moved.Original);
        Assert.Equal("Edited", moved.Edited.Title);
        Assert.True(moved.IsDirty);
    }
}
