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

    [Fact]
    public async Task A_sync_reapply_during_a_drag_does_not_switch_saving_back_on()
    {
        var (node, _) = await NewNodeAsync();
        var template = new Template { Title = "Before" };
        await node.AddItemAsync(template);
        int saves = 0;
        node.DataSaved += () => Interlocked.Increment(ref saves);

        node.BeginDrag();
        await node.ReapplySyncAsync();   // the sync poll can land mid-drag
        template.Title = "Edited mid-drag";
        await Task.Delay(800);           // past the edit debounce

        Assert.Equal(0, saves);

        node.EndDrag();
        await node.SaveDataAsync();      // the drag's single authoritative save
        Assert.Equal(1, saves);
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
