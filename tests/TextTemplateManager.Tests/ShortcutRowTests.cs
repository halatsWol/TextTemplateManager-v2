using TextTemplateManager.Models;
using TextTemplateManager.ViewModels;
using Xunit;
using Entry = TextTemplateManager.Models.ShortcutValidator.Entry;

namespace TextTemplateManager.Tests;

public class ShortcutRowTests
{
    private readonly SyncSource _first = new() { Name = "First", IsActive = true };
    private readonly SyncSource _second = new() { Name = "Second", IsActive = true };

    [Fact]
    public void Rows_list_local_first_then_the_sync_sources_in_order()
    {
        var entries = new[]
        {
            new Entry(new Template { Title = "From second", SingleKeyShortcut = "A" }, _second.Id, ""),
            new Entry(new Template { Title = "From first", SingleKeyShortcut = "B" }, _first.Id, ""),
            new Entry(new Template { Title = "Local", SingleKeyShortcut = "C" }, Guid.Empty, ""),
            new Entry(new Template { Title = "No single key" }, Guid.Empty, ""),
        };

        var rows = ShortcutRow.For(entries, new[] { _first, _second }, multiKey: false);

        Assert.Equal(new[] { "Local", "From first", "From second" }, rows.Select(r => r.Title));
        Assert.Equal(new[] { "C", "B", "A" }, rows.Select(r => r.Key));
        Assert.Equal(new[] { "local", "First", "Second" }, rows.Select(r => r.Source));
        Assert.Equal(new[] { true, false, false }, rows.Select(r => r.IsLocal));
    }

    [Fact]
    public void Multi_key_rows_show_the_key_as_typed()
    {
        var shared = new Template { Title = "Shared", MultiKeyShortcut = "MSG" };
        var entries = new[]
        {
            new Entry(shared, _first.Id, "tm-MSG"),
            new Entry(new Template { Title = "Only single", SingleKeyShortcut = "Q" }, Guid.Empty, ""),
        };

        var row = Assert.Single(ShortcutRow.For(entries, new[] { _first, _second }, multiKey: true));

        Assert.Same(shared, row.Template);
        Assert.Equal("tm-MSG", row.Key);
    }

    // Screen readers name Quick Paste's rows and tree items by their text.
    [Fact]
    public void Rows_and_items_read_as_their_title_not_as_a_type_or_record_dump()
    {
        var template = new Template { Title = "Team msg" };

        Assert.Equal("Team msg, shortcut tm-MSG, Team", new ShortcutRow(template, "tm-MSG", "Team", false).ToString());
        Assert.Equal("Team msg", template.ToString());
        Assert.Equal("Work", new Folder { Title = "Work" }.ToString());
    }
}
