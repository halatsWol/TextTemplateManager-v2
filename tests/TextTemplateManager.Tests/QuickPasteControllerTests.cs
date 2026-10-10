using TextTemplateManager.Models;
using TextTemplateManager.ViewModels;
using Xunit;
using Entry = TextTemplateManager.Models.ShortcutValidator.Entry;

namespace TextTemplateManager.Tests;

public class QuickPasteControllerTests
{
    private readonly SyncSource _team = new() { Name = "Team", IsActive = true, ShortcutPrefix = "tm" };
    private readonly Template _mail = new() { Title = "Mail", SingleKeyShortcut = "M", MultiKeyShortcut = "MSG" };
    private readonly Template _memo = new() { Title = "Memo", MultiKeyShortcut = "MEMO" };
    private readonly Template _teamMsg = new() { Title = "Team note", SingleKeyShortcut = "T", MultiKeyShortcut = "MSG" };
    private readonly QuickPasteController _entry;

    public QuickPasteControllerTests()
    {
        var entries = new[]
        {
            new Entry(_mail, Guid.Empty, "MSG"),
            new Entry(_memo, Guid.Empty, "MEMO"),
            new Entry(_teamMsg, _team.Id, "tm-MSG"),
        };
        var sources = new[] { _team };
        _entry = new QuickPasteController(() => entries, () => sources,
            key => entries.Select(e => e.Template).FirstOrDefault(t => string.Equals(t.SingleKeyShortcut, key, StringComparison.OrdinalIgnoreCase)));
        _entry.Open();
    }

    private string[] MultiTitles => _entry.MultiRows.Select(r => r.Title).ToArray();

    [Fact]
    public void Opening_lists_both_tabs_with_the_first_single_key_row_highlighted()
    {
        Assert.Equal(ShortcutTab.Single, _entry.Tab);
        Assert.Equal(new[] { "Mail", "Team note" }, _entry.SingleRows.Select(r => r.Title));
        Assert.Equal(new[] { "Mail", "Memo", "Team note" }, MultiTitles);
        Assert.Equal(0, _entry.SingleIndex);
        Assert.Equal(-1, _entry.MultiIndex);
    }

    [Fact]
    public void Holding_alt_shows_the_multi_key_tab_and_releasing_with_nothing_typed_restores_it()
    {
        _entry.AltDown();
        Assert.Equal(ShortcutTab.Multi, _entry.Tab);
        Assert.Equal(0, _entry.MultiIndex);

        Assert.Null(_entry.AltUp());
        Assert.Equal(ShortcutTab.Single, _entry.Tab);
        Assert.False(_entry.AltHeld);
        Assert.Equal(-1, _entry.MultiIndex);
    }

    [Fact]
    public void Typing_narrows_the_list_and_keys_that_begin_no_shortcut_are_refused()
    {
        _entry.AltDown();

        Assert.True(_entry.TypeMulti('m'));
        Assert.Equal(new[] { "Mail", "Memo" }, MultiTitles);
        Assert.True(_entry.TypeMulti('e'));
        Assert.Equal(new[] { "Memo" }, MultiTitles);
        Assert.False(_entry.TypeMulti('x'));
        Assert.Equal("me", _entry.Buffer);

        _entry.Backspace();
        Assert.Equal(new[] { "Mail", "Memo" }, MultiTitles);
    }

    [Fact]
    public void A_sync_shortcut_is_typed_with_its_prefix()
    {
        _entry.AltDown();
        foreach (char c in "tm-msg") Assert.True(_entry.TypeMulti(c));

        var paste = _entry.AltUp();

        Assert.Same(_teamMsg, paste!.Template);
        Assert.False(paste.PlainText);
    }

    [Fact]
    public void A_trailing_underscore_pastes_as_plain_text_and_ends_the_entry()
    {
        _entry.AltDown();
        Assert.False(_entry.TypeMulti('_'));   // can't start an entry
        foreach (char c in "msg") _entry.TypeMulti(c);

        Assert.True(_entry.TypeMulti('_'));
        Assert.True(_entry.TypeMulti('x'));    // ignored after the marker, no beep
        Assert.Equal("msg_", _entry.Buffer);
        Assert.True(_entry.BufferMatches);

        var paste = _entry.AltUp();
        Assert.Same(_mail, paste!.Template);
        Assert.True(paste.PlainText);
    }

    [Fact]
    public void Enter_under_alt_commits_the_highlight_even_with_nothing_typed()
    {
        _entry.AltDown();
        _entry.MoveMultiHighlight(1);

        Assert.Same(_memo, _entry.CommitMulti(requireBuffer: false)!.Template);
        Assert.Null(_entry.CommitMulti(requireBuffer: true));
    }

    [Fact]
    public void The_highlight_wraps_and_enter_pastes_it()
    {
        _entry.MoveHighlight(-1);
        Assert.Equal(1, _entry.SingleIndex);
        _entry.MoveHighlight(1);
        Assert.Equal(0, _entry.SingleIndex);

        Assert.Same(_mail, _entry.CommitHighlighted()!.Template);
    }

    [Fact]
    public void A_single_key_pastes_its_template()
    {
        Assert.Same(_teamMsg, _entry.SingleKey('t')!.Template);
        Assert.Null(_entry.SingleKey('q'));
    }

    [Fact]
    public void Search_filters_by_title_and_leaving_shortcut_mode_clears_the_highlight()
    {
        _entry.SetShortcutMode(false);
        _entry.SetSearch("team");

        Assert.Equal(new[] { "Team note" }, _entry.SingleRows.Select(r => r.Title));
        Assert.Equal(-1, _entry.SingleIndex);

        _entry.SetShortcutMode(true);
        Assert.Equal(0, _entry.SingleIndex);
    }

    [Fact]
    public void Losing_focus_mid_entry_resets_alt_the_tab_and_the_typed_key()
    {
        _entry.AltDown();
        _entry.TypeMulti('m');

        _entry.CancelEntry(altReleased: true);

        Assert.False(_entry.AltHeld);
        Assert.Equal(ShortcutTab.Single, _entry.Tab);
        Assert.Equal("", _entry.Buffer);
        Assert.Equal(new[] { "Mail", "Memo", "Team note" }, MultiTitles);
    }
}
