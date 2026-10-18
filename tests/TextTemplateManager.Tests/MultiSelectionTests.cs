using TextTemplateManager.Data;
using TextTemplateManager.Models;
using TextTemplateManager.ViewModels;
using Xunit;

namespace TextTemplateManager.Tests;

/// <summary>Selecting several tree items as in Explorer: click, Ctrl+click, Shift+click over the visible rows.</summary>
public class MultiSelectionTests
{
    private static readonly Template A = new() { Title = "A" }, B = new() { Title = "B" }, C = new() { Title = "C" }, D = new() { Title = "D" };
    private static readonly List<BaseItem> Rows = new() { A, B, C, D };
    private readonly MultiSelection _selection = new();

    private string Selected() => string.Concat(_selection.Items.Select(i => i.Title));

    [Fact]
    public void A_plain_click_selects_one_and_ctrl_adds_and_removes()
    {
        _selection.Click(B, false, false, Rows);
        Assert.Equal(D, _selection.Click(D, ctrl: true, false, Rows));
        Assert.Equal("BD", Selected());

        Assert.Equal(B, _selection.Click(D, ctrl: true, false, Rows));   // off again: the focus goes back to B
        Assert.Equal("B", Selected());
        Assert.Equal(B, _selection.Click(B, ctrl: true, false, Rows));   // the last one stays
        Assert.Equal("B", Selected());

        _selection.Click(C, false, false, Rows);
        Assert.Equal("C", Selected());
    }

    [Fact]
    public void Shift_selects_the_visible_rows_from_the_anchor_either_way()
    {
        _selection.Click(B, false, false, Rows);
        _selection.Click(D, false, shift: true, Rows);
        Assert.Equal("BCD", Selected());

        _selection.Click(A, false, shift: true, Rows);   // the anchor stays B
        Assert.Equal("BA", Selected());
        Assert.Equal(A, _selection.Items[^1]);

        _selection.Click(D, ctrl: true, false, Rows);     // Ctrl moves the anchor
        _selection.Click(C, ctrl: true, shift: true, Rows);   // Ctrl+Shift adds the range
        Assert.Equal("BADC", Selected());
    }

    [Fact]
    public void A_shift_click_on_a_row_that_isnt_visible_selects_just_it()
    {
        var hidden = new Template { Title = "H" };
        _selection.Click(A, false, false, Rows);

        _selection.Click(hidden, false, shift: true, Rows);

        Assert.Equal("H", Selected());
    }

    [Fact]
    public void Shift_arrows_extend_from_the_anchor_and_shrink_back()
    {
        _selection.Click(B, false, false, Rows);

        Assert.Equal(C, _selection.Extend(B, 1, Rows));
        Assert.Equal(D, _selection.Extend(C, 1, Rows));
        Assert.Equal("BCD", Selected());
        Assert.Null(_selection.Extend(D, 1, Rows));   // last row: nothing changes
        Assert.Equal("BCD", Selected());

        Assert.Equal(C, _selection.Extend(D, -1, Rows));
        Assert.Equal(B, _selection.Extend(C, -1, Rows));
        Assert.Equal(A, _selection.Extend(B, -1, Rows));   // past the anchor the other way
        Assert.Equal("BA", Selected());
        Assert.Null(_selection.Extend(A, -1, Rows));
    }

    [Fact]
    public void Shift_arrows_start_at_the_focus_when_the_anchor_is_no_longer_visible()
    {
        var hidden = new Template { Title = "H" };
        var withHidden = new List<BaseItem> { A, hidden, B, C, D };
        _selection.Click(hidden, false, false, withHidden);
        _selection.Click(B, false, shift: true, withHidden);   // anchor H, focus B; then H's folder is collapsed

        Assert.Equal(C, _selection.Extend(B, 1, Rows));

        Assert.Equal("BC", Selected());
    }

    [Fact]
    public void Ctrl_a_selects_every_visible_row_and_keeps_the_focus()
    {
        _selection.Click(C, false, false, Rows);

        Assert.Equal(C, _selection.SelectAll(Rows, C));
        Assert.Equal("ABDC", Selected());

        Assert.Equal(B, _selection.Extend(C, -1, Rows));   // the focus is the new anchor
        Assert.Equal("CB", Selected());
    }

    [Fact]
    public void Ctrl_a_without_a_focus_focuses_the_first_row()
    {
        Assert.Equal(A, _selection.SelectAll(Rows, null));
        Assert.Equal("BCDA", Selected());
    }

    [Fact]
    public void Items_inside_a_selected_folder_are_left_out_of_the_top_most()
    {
        var inner = new Template { Title = "Inner" };
        var folder = new Folder { Title = "F", Children = { inner } };

        Assert.Equal(new BaseItem[] { folder, A }, MultiSelection.TopMost(new BaseItem[] { inner, folder, A }));
    }

    [Fact]
    public async Task Several_items_are_deleted_with_one_save()
    {
        var node = new DataNode(new StorageService(TestEnvironment.NewScratchDir()));
        await node.InitializeAsync();
        var keep = new Template { Title = "Keep" };
        var gone1 = new Template { Title = "Gone 1" };
        var gone2 = new Folder { Title = "Gone 2", Children = { new Template { Title = "Inside" } } };
        foreach (var item in new BaseItem[] { keep, gone1, gone2 }) await node.AddItemAsync(item);
        int saves = 0;
        node.DataSaved += () => Interlocked.Increment(ref saves);

        await node.DeleteItemsAsync(new BaseItem[] { gone1, gone2 });

        Assert.Equal(1, saves);
        Assert.Equal(new[] { "Keep" }, node.LocalItems.Select(i => i.Title));
        Assert.Equal(new[] { "Keep" }, (await StorageService.LoadRootAsync(node.Storage.GetDataPath()))!.Children.Select(i => i.Title));
    }
}
