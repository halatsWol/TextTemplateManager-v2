using TextTemplateManager.Data;
using TextTemplateManager.Models;
using Xunit;

namespace TextTemplateManager.Tests;

/// <summary>Loading a backup replaces the local items only: the pinned sync folders stay, and the
/// replacement is written once, never as an intermediate empty tree.</summary>
public class DataNodeTests
{
    private static async Task<(DataNode node, string path)> NewNodeAsync()
    {
        var node = new DataNode(new StorageService(TestEnvironment.NewScratchDir()));
        await node.InitializeAsync();
        return (node, node.Storage.GetDataPath());
    }

    [Fact]
    public async Task Loading_a_backup_replaces_local_items_and_keeps_sync_folders()
    {
        var (node, path) = await NewNodeAsync();
        await node.AddItemAsync(new Template { Title = "Old local" });
        var syncRoot = new Folder { Title = "Team", IsSyncRoot = true, SyncId = Guid.Empty };
        node.LocalItems.Insert(0, syncRoot);

        await node.ReplaceLocalItemsAsync(new BaseItem[]
        {
            new Template { Title = "From backup" },
            new Folder { Title = "Sync snapshot in backup", SyncId = Guid.NewGuid() },
        });

        Assert.Same(syncRoot, node.LocalItems[0]);
        var titles = node.LocalItems.Select(i => i.Title).ToList();
        Assert.Contains("From backup", titles);
        Assert.DoesNotContain("Old local", titles);
        Assert.DoesNotContain("Sync snapshot in backup", titles);

        var saved = await StorageService.LoadRootAsync(path);
        Assert.Contains(saved!.Children, c => c.Title == "From backup");
        Assert.DoesNotContain(saved.Children, c => c.Title == "Old local");
    }

    [Fact]
    public async Task A_debounced_edit_is_written_at_once_when_flushed()
    {
        // Exit flushes instead of waiting for the edit debounce, which would be cut off by the exit.
        var (node, path) = await NewNodeAsync();
        var template = new Template { Title = "Before" };
        await node.AddItemAsync(template);

        template.Title = "Edited just before exit";
        await node.FlushPendingSaveAsync();

        var saved = await StorageService.LoadRootAsync(path);
        Assert.Contains(saved!.Children, c => c.Title == "Edited just before exit");
    }

    [Fact]
    public async Task A_content_edit_is_saved_by_the_store_alone()
    {
        // The editor only sets Content; the store's debounced save is the one save path.
        var (node, path) = await NewNodeAsync();
        var template = new Template { Title = "Typed", Content = "<p>a</p>" };
        await node.AddItemAsync(template);

        template.Content = "<p>a typed</p>";
        await Task.Delay(1000);   // past the edit debounce

        var saved = await StorageService.LoadRootAsync(path);
        Assert.Equal("<p>a typed</p>", Assert.IsType<Template>(Assert.Single(saved!.Children)).Content);
    }

    private static Folder AddSyncFolder(DataNode node, bool allowSave)
    {
        var source = new SyncSource
        {
            Name = "Team",
            Path = Path.Combine(TestEnvironment.NewScratchDir(), "team.ttmdata"),
            IsActive = true,
            AllowSave = allowSave,
        };
        node.CurrentSyncSettings.Sources.Add(source);
        var root = new Folder { Id = source.Id, Title = "Team", IsSyncRoot = true, SyncId = Guid.Empty };
        node.LocalItems.Insert(0, root);
        return root;
    }

    [Fact]
    public async Task Moving_within_a_folder_puts_the_item_at_the_drop_index()
    {
        var (node, _) = await NewNodeAsync();
        var a = new Template { Title = "A" };
        var b = new Template { Title = "B" };
        var c = new Template { Title = "C" };
        foreach (var t in new[] { a, b, c }) await node.AddItemAsync(t);

        Assert.True(await node.MoveItemAsync(a, null, 2));   // above C
        Assert.Equal(new[] { "B", "A", "C" }, node.LocalItems.Select(i => i.Title));

        Assert.True(await node.MoveItemAsync(c, null, 0));   // above B
        Assert.Equal(new[] { "C", "B", "A" }, node.LocalItems.Select(i => i.Title));
    }

    [Fact]
    public async Task Moving_into_a_folder_reparents_and_saves_once()
    {
        var (node, path) = await NewNodeAsync();
        var folder = new Folder { Title = "Folder" };
        var template = new Template { Title = "Moved" };
        await node.AddItemAsync(folder);
        await node.AddItemAsync(template);
        int saves = 0;
        node.DataSaved += () => Interlocked.Increment(ref saves);

        Assert.True(await node.MoveItemAsync(template, folder, folder.Children.Count));

        Assert.Equal(1, saves);
        Assert.Same(template, Assert.Single(folder.Children));
        Assert.Equal(folder.Id, template.ParentId);
        var saved = await StorageService.LoadRootAsync(path);
        Assert.Equal("Moved", Assert.Single(Assert.Single(saved!.Children).Children).Title);
    }

    [Fact]
    public async Task Shortcut_entries_carry_the_area_and_the_typed_multi_key()
    {
        var (node, _) = await NewNodeAsync();
        var syncRoot = AddSyncFolder(node, allowSave: true);
        var source = node.CurrentSyncSettings.Sources.Single();
        source.ShortcutPrefix = "tm";
        var shared = new Template { Title = "Shared", MultiKeyShortcut = "MSG" };
        syncRoot.Children.Add(new Folder { Title = "Sub", Children = { shared } });
        var local = new Template { Title = "Local", MultiKeyShortcut = "MSG" };
        await node.AddItemAsync(local);

        var entries = node.ShortcutEntries();

        Assert.Equal(2, entries.Count);
        var sharedEntry = entries.Single(e => e.Template == shared);
        Assert.Equal(source.Id, sharedEntry.Area);
        Assert.Equal("tm-MSG", sharedEntry.EffectiveMultiKey);
        var localEntry = entries.Single(e => e.Template == local);
        Assert.Equal(Guid.Empty, localEntry.Area);
        Assert.Equal("MSG", localEntry.EffectiveMultiKey);
    }

    [Fact]
    public async Task A_shortcut_edit_on_a_template_added_later_is_announced()
    {
        var (node, _) = await NewNodeAsync();
        var template = new Template { Title = "Added after load" };
        await node.AddItemAsync(template);
        int announced = 0;
        node.ShortcutsChanged += () => announced++;

        template.SingleKeyShortcut = "Q";
        template.Title = "Renamed";

        Assert.Equal(1, announced);
    }

    [Fact]
    public async Task Deleting_a_nested_item_removes_it_and_saves()
    {
        var (node, path) = await NewNodeAsync();
        var folder = new Folder { Title = "Folder" };
        var keep = new Template { Title = "Keep" };
        var gone = new Template { Title = "Gone" };
        await node.AddItemAsync(folder);
        await node.AddItemAsync(keep, folder);
        await node.AddItemAsync(gone, folder);

        await node.DeleteItemAsync(gone);

        Assert.Same(keep, Assert.Single(folder.Children));
        var saved = await StorageService.LoadRootAsync(path);
        Assert.Equal("Keep", Assert.Single(Assert.Single(saved!.Children).Children).Title);
    }

    [Fact]
    public async Task A_folder_cannot_move_into_its_own_subtree()
    {
        var (node, _) = await NewNodeAsync();
        var outer = new Folder { Title = "Outer" };
        var inner = new Folder { Title = "Inner" };
        await node.AddItemAsync(outer);
        await node.AddItemAsync(inner, outer);

        Assert.False(await node.MoveItemAsync(outer, inner, 0));
        Assert.False(await node.MoveItemAsync(outer, outer, 0));
        Assert.Same(outer, Assert.Single(node.LocalItems));
        Assert.Same(inner, Assert.Single(outer.Children));
    }

    [Fact]
    public async Task Top_level_moves_stay_below_the_sync_folders()
    {
        var (node, _) = await NewNodeAsync();
        var syncRoot = AddSyncFolder(node, allowSave: true);
        var template = new Template { Title = "Local" };
        await node.AddItemAsync(template);

        Assert.True(await node.MoveItemAsync(template, null, 0));

        Assert.Equal(new BaseItem[] { syncRoot, template }, node.LocalItems);
        Assert.False(node.CanMove(syncRoot, null));
    }

    [Fact]
    public async Task Nothing_moves_into_or_out_of_a_read_only_sync_folder()
    {
        var (node, _) = await NewNodeAsync();
        var syncRoot = AddSyncFolder(node, allowSave: false);
        var shared = new Template { Title = "Shared", SyncId = Guid.NewGuid() };
        syncRoot.Children.Add(shared);
        var local = new Template { Title = "Local" };
        await node.AddItemAsync(local);

        Assert.False(await node.MoveItemAsync(local, syncRoot, 0));
        Assert.False(await node.MoveItemAsync(shared, null, 0));
        Assert.Same(shared, Assert.Single(syncRoot.Children));
        Assert.Contains(local, node.LocalItems);
    }

    [Fact]
    public async Task Moving_to_another_area_drops_file_ids_and_resolves_shortcut_clashes()
    {
        var (node, _) = await NewNodeAsync();
        var syncRoot = AddSyncFolder(node, allowSave: true);
        var folder = new Folder { Title = "Shared folder", SyncId = Guid.NewGuid() };
        var shared = new Template { Title = "Shared", SyncId = Guid.NewGuid(), SingleKeyShortcut = "G", MultiKeyShortcut = "MSG" };
        folder.Children.Add(shared);
        syncRoot.Children.Add(folder);
        await node.AddItemAsync(new Template { Title = "Local", SingleKeyShortcut = "G", MultiKeyShortcut = "MSG" });

        Assert.True(await node.MoveItemAsync(folder, null, node.LocalItems.Count));

        Assert.Null(folder.SyncId);
        Assert.Null(shared.SyncId);
        Assert.Equal("", shared.SingleKeyShortcut);
        Assert.Equal("MSG1", shared.MultiKeyShortcut);
    }

    [Fact]
    public async Task A_settings_change_is_saved_without_an_explicit_save()
    {
        var (node, _) = await NewNodeAsync();

        node.CurrentSettings.PasteWindowHotkey = "Ctrl+Alt+P";
        await Task.Delay(1000);   // past the settings debounce

        var saved = await node.Storage.LoadSettingsAsync();
        Assert.Equal("Ctrl+Alt+P", saved.PasteWindowHotkey);
    }

    [Fact]
    public async Task A_pending_settings_change_is_written_at_once_when_flushed()
    {
        var (node, _) = await NewNodeAsync();

        node.CurrentSettings.BrowserConnectorPort = 48000;
        node.CurrentSettings.HideCrossAreaShortcutWarnings = true;
        await node.FlushPendingSaveAsync();

        var saved = await node.Storage.LoadSettingsAsync();
        Assert.Equal(48000, saved.BrowserConnectorPort);
        Assert.True(saved.HideCrossAreaShortcutWarnings);
    }

    [Theory]
    [InlineData("_", "-")]
    [InlineData("x", "-")]
    [InlineData(".", ".")]
    [InlineData("", "")]
    public async Task A_legacy_sync_separator_is_migrated_and_saved_on_load(string stored, string expected)
    {
        var storage = new StorageService(TestEnvironment.NewScratchDir());
        await storage.SaveSyncSettingsAsync(new SyncSettings { Separator = stored });
        var node = new DataNode(storage);

        await node.InitializeAsync();

        Assert.Equal(expected, node.CurrentSyncSettings.Separator);
        Assert.Equal(expected, (await storage.LoadSyncSettingsAsync()).Separator);
    }

    [Fact]
    public async Task Waiting_for_writes_completes_after_the_last_write()
    {
        var storage = new StorageService(TestEnvironment.NewScratchDir());
        string path = Path.Combine(TestEnvironment.NewScratchDir(), "busy.ttmdata");
        var writes = Task.WhenAll(Enumerable.Range(0, 20).Select(i =>
            storage.SaveAsync(path, new Folder { Title = $"Root {i}" })));

        await storage.WaitForWritesAsync();

        Assert.True(writes.IsCompleted);
        Assert.False(storage.WritesInFlight);
    }

    [Fact]
    public async Task Loading_a_backup_saves_once()
    {
        var (node, _) = await NewNodeAsync();
        await node.AddItemAsync(new Template { Title = "Old local" });
        int saves = 0;
        node.DataSaved += () => Interlocked.Increment(ref saves);

        await node.ReplaceLocalItemsAsync(Enumerable.Range(1, 5).Select(i => (BaseItem)new Template { Title = $"T{i}" }).ToList());

        Assert.Equal(1, saves);
    }
}
