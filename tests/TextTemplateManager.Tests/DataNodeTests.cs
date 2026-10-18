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

    // ---- import ----

    private static Template T(string title, string lastChange = "20261012090000", string content = "") =>
        new() { Title = title, Content = content.Length > 0 ? content : $"<p>{title}</p>", LastChange = lastChange };

    // Existing tree: folder "Work" { "Mail" (older), "Call" (newer) } and "Loose" at the top.
    private static async Task<(DataNode node, Folder work)> NodeForImportAsync()
    {
        var (node, _) = await NewNodeAsync();
        var work = new Folder { Title = "Work" };
        await node.AddItemAsync(work);
        await node.AddItemAsync(T("Mail", "20261001000000", "<p>old mail</p>"), work);
        await node.AddItemAsync(T("Call", "20261020000000", "<p>new call</p>"), work);
        await node.AddItemAsync(T("Loose"));
        return (node, work);
    }

    // Imported: folder "work" (same name, other case) { "Mail" (newer), "Call" (older), "Fax" } and "Extra".
    private static List<BaseItem> Incoming() => new()
    {
        new Folder { Title = "work", Children = { T("Mail", "20261010000000", "<p>imported mail</p>"), T("Call", "20261002000000", "<p>imported call</p>"), T("Fax") } },
        T("Extra"),
    };

    private static string Content(Folder f, string title) => ((Template)f.Children.Single(c => c.Title == title)).Content;

    [Fact]
    public async Task A_direct_import_merges_folders_and_counts_the_existing_templates()
    {
        var (node, work) = await NodeForImportAsync();

        Assert.Equal(2, node.CountImportConflicts(Incoming()));
        var summary = await node.ImportAsync(Incoming(), null, ImportConflicts.OverwriteOlder);

        Assert.Equal(new ImportSummary(Added: 2, Overwritten: 1, Skipped: 1, Conflicts: 2), summary);
        Assert.Equal(new[] { "Work", "Loose", "Extra" }, node.LocalItems.Select(i => i.Title));   // no second "work"
        Assert.Equal(new[] { "Mail", "Call", "Fax" }, work.Children.Select(c => c.Title));
        Assert.Equal("<p>imported mail</p>", Content(work, "Mail"));   // imported one is more recent
        Assert.Equal("<p>new call</p>", Content(work, "Call"));        // existing one is more recent
        var saved = await StorageService.LoadRootAsync(node.Storage.GetDataPath());
        Assert.Equal(3, saved!.Children[0].Children.Count);
    }

    [Theory]
    [InlineData(ImportConflicts.OverwriteAll, "<p>imported mail</p>", "<p>imported call</p>")]
    [InlineData(ImportConflicts.Skip, "<p>old mail</p>", "<p>new call</p>")]
    public async Task Existing_templates_are_overwritten_or_kept_as_chosen(ImportConflicts how, string mail, string call)
    {
        var (node, work) = await NodeForImportAsync();

        await node.ImportAsync(Incoming(), null, how);

        Assert.Equal((mail, call), (Content(work, "Mail"), Content(work, "Call")));
        Assert.Equal(3, work.Children.Count);
    }

    [Fact]
    public async Task An_import_into_a_new_folder_is_a_copy_with_fresh_ids()
    {
        var (node, work) = await NodeForImportAsync();
        await node.AddItemAsync(new Folder { Title = "Team templates" });
        var items = Incoming();
        var oldIds = items.SelectMany(i => i.SelfAndDescendants()).Select(i => i.Id).ToHashSet();

        var summary = await node.ImportAsync(items, "Team templates", ImportConflicts.Skip);

        var folder = (Folder)node.LocalItems.Last();
        Assert.Equal("Team templates 1", folder.Title);
        Assert.Equal(new[] { "work", "Extra" }, folder.Children.Select(c => c.Title));
        Assert.DoesNotContain(folder.SelfAndDescendants(), i => oldIds.Contains(i.Id));
        Assert.Equal(4, summary.Added);
        Assert.Equal(2, work.Children.Count);   // existing ones untouched
    }

    [Fact]
    public async Task An_imported_id_that_is_already_used_elsewhere_gets_a_new_one()
    {
        var (node, work) = await NodeForImportAsync();
        var loose = node.LocalItems.Single(i => i.Title == "Loose");
        var sameId = T("Renamed elsewhere");
        sameId.Id = loose.Id;

        await node.ImportAsync(new List<BaseItem> { new Folder { Title = "New", Children = { sameId } } }, null, ImportConflicts.Skip);

        var added = node.LocalItems.Single(i => i.Title == "New").Children.Single();
        Assert.NotEqual(loose.Id, added.Id);
        Assert.Single(node.LocalItems.Descendants(), i => i.Id == loose.Id);
    }

    [Fact]
    public void A_folder_export_imports_as_that_folder_and_synced_snapshots_are_left_out()
    {
        var folderExport = new Folder { Id = Guid.NewGuid(), Title = "Mine", Children = { T("Inside") } };
        var fullExport = new Folder
        {
            Id = Guid.Empty,
            Title = "Root",
            Children = { new Folder { Title = "Team snapshot", SyncId = Guid.Empty }, T("Local") },
        };

        Assert.Equal(new[] { "Mine" }, DataNode.ImportedItems(folderExport).Select(i => i.Title));
        Assert.Equal(new[] { "Local" }, DataNode.ImportedItems(fullExport).Select(i => i.Title));
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

    // A synced folder whose file existed when it was loaded and is gone now, so only the cached copy shows.
    private static async Task<(DataNode node, SyncSource source)> NodeWithLostSyncFileAsync()
    {
        string dir = TestEnvironment.NewScratchDir();
        var storage = new StorageService(dir);
        var source = new SyncSource { Name = "Team", Path = Path.Combine(dir, "shared", "team.ttmdata"), IsActive = true };
        Directory.CreateDirectory(Path.GetDirectoryName(source.Path)!);
        await storage.SaveSharedAsync(source.Path, new Folder
        {
            Title = "Root",
            Children = { new Template { Title = "Shared one", Content = "<p>s</p>", MultiKeyShortcut = "msg" } },
        });
        await storage.SaveSyncSettingsAsync(new SyncSettings { Sources = { source } });
        var node = new DataNode(storage);
        await node.InitializeAsync();
        source = node.CurrentSyncSettings.Sources[0];

        File.Delete(source.Path);
        await node.ReapplySyncAsync();
        Assert.True(node.LocalItems[0].SyncFileMissing);
        return (node, source);
    }

    [Fact]
    public async Task A_sync_file_locked_after_an_outside_edit_is_read_again_once_it_is_free()
    {
        string dir = TestEnvironment.NewScratchDir();
        var storage = new StorageService(dir);
        string path = Path.Combine(dir, "shared", "team.ttmdata");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await storage.SaveSharedAsync(path, new Folder { Title = "Root", Children = { new Template { Title = "Shared one", Content = "<p>s</p>" } } });
        await storage.SaveSyncSettingsAsync(new SyncSettings { Sources = { new SyncSource { Name = "Team", Path = path, IsActive = true } } });
        var node = new DataNode(storage);
        await node.InitializeAsync();

        // Edited on another PC, then held by OneDrive or a virus scan while the poll notices the change.
        await new StorageService(TestEnvironment.NewScratchDir()).SaveSharedAsync(path, new Folder
        {
            Title = "Root",
            Children = { new Template { Title = "Shared one", Content = "<p>s</p>" }, new Template { Title = "Added outside", Content = "<p>o</p>" } },
        });
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await node.CheckSyncFilesForChangesAsync();
            Assert.True(node.LocalItems[0].SyncFileMissing);
        }

        await node.CheckSyncFilesForChangesAsync();

        Assert.False(node.LocalItems[0].SyncFileMissing);
        Assert.Contains("Added outside", node.LocalItems[0].Children.Select(c => c.Title));
    }

    [Fact]
    public async Task A_sync_file_that_cannot_be_written_shows_a_warning_and_is_written_once_it_can_be()
    {
        string dir = TestEnvironment.NewScratchDir();
        var storage = new StorageService(dir);
        string path = Path.Combine(dir, "shared", "team.ttmdata");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await storage.SaveSharedAsync(path, new Folder { Title = "Root", Children = { new Template { Title = "Shared one", Content = "<p>s</p>" } } });
        await storage.SaveSyncSettingsAsync(new SyncSettings { Sources = { new SyncSource { Name = "Team", Path = path, IsActive = true, AllowSave = true } } });
        var node = new DataNode(storage);
        await node.InitializeAsync();
        var source = node.CurrentSyncSettings.Sources[0];
        var folder = node.LocalItems[0];
        string before = await File.ReadAllTextAsync(path);

        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            ((Template)folder.Children[0]).Content = "<p>edited here</p>";
            await node.SaveDataAsync();

            Assert.True(source.HasWriteProblem);
            Assert.True(folder.HasSyncWriteProblem);
            Assert.Equal(before, await File.ReadAllTextAsync(path));
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally { File.SetAttributes(path, FileAttributes.Normal); }

        await node.CheckSyncFilesForChangesAsync();   // the poll retries

        Assert.False(source.HasWriteProblem);
        Assert.False(folder.HasSyncWriteProblem);
        Assert.Contains("edited here", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task A_lost_sync_file_is_recreated_where_it_was_from_the_cached_copy()
    {
        var (node, source) = await NodeWithLostSyncFileAsync();
        string path = source.Path;
        var changed = new List<string?>();
        source.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        Assert.True(await node.RecreateSyncFileAsync(source, path));

        Assert.False(node.LocalItems[0].SyncFileMissing);
        Assert.False(source.IsFileMissing);
        Assert.Contains(nameof(SyncSource.IsFileMissing), changed);   // the Sync page's warning goes away
        var file = await StorageService.LoadRootSharedAsync(path);
        var shared = Assert.IsType<Template>(Assert.Single(file!.Children));
        Assert.Equal(("Shared one", "MSG"), (shared.Title, shared.MultiKeyShortcut));
    }

    [Fact]
    public async Task A_lost_sync_file_can_be_recreated_somewhere_else_and_the_source_follows()
    {
        var (node, source) = await NodeWithLostSyncFileAsync();
        string elsewhere = Path.Combine(TestEnvironment.NewScratchDir(), "moved.ttmdata");

        Assert.True(await node.RecreateSyncFileAsync(source, elsewhere));

        Assert.Equal(elsewhere, source.Path);
        Assert.Equal(elsewhere, (await node.Storage.LoadSyncSettingsAsync()).Sources[0].Path);
        Assert.False(node.LocalItems[0].SyncFileMissing);
        Assert.Contains("Team", node.LocalItems[0].Title);
    }

    [Fact]
    public async Task An_inactive_source_has_no_cached_copy_to_recreate_from()
    {
        var (node, source) = await NodeWithLostSyncFileAsync();
        source.IsActive = false;
        await node.ReapplySyncAsync();
        string path = source.Path;

        Assert.False(await node.RecreateSyncFileAsync(source, path));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task Exporting_a_synced_folder_writes_a_plain_copy_without_sync_ids()
    {
        var (node, _) = await NodeWithLostSyncFileAsync();
        var shared = (Template)node.LocalItems[0].Children[0];
        Assert.NotNull(shared.SyncId);
        string path = Path.Combine(TestEnvironment.NewScratchDir(), "team.ttmdata");

        await node.ExportFolderAsync(path, (Folder)node.LocalItems[0]);

        var file = await StorageService.LoadRootAsync(path);
        var exported = Assert.IsType<Template>(Assert.Single(file!.Children));
        Assert.Equal("Shared one", exported.Title);
        Assert.Null(exported.SyncId);
        Assert.Equal(file.Id, exported.ParentId);
        Assert.NotNull(shared.SyncId);   // the tree itself is untouched
    }

    [Fact]
    public async Task Exporting_items_puts_them_under_a_plain_root_and_everything_means_the_local_items()
    {
        var (node, _) = await NodeWithLostSyncFileAsync();
        var folder = new Folder { Title = "Mine" };
        var template = new Template { Title = "Note", Content = "<p>n</p>" };
        await node.AddItemAsync(folder);
        await node.AddItemAsync(new Template { Title = "Inside" }, folder);
        await node.AddItemAsync(template);
        string one = Path.Combine(TestEnvironment.NewScratchDir(), "one.ttmdata");
        string all = Path.Combine(TestEnvironment.NewScratchDir(), "all.ttmdata");

        await node.ExportItemsAsync(one, new[] { template });
        await node.ExportItemsAsync(all, node.LocalOnlyItems);

        Assert.Equal(new[] { "Note" }, (await StorageService.LoadRootAsync(one))!.Children.Select(c => c.Title));
        var everything = (await StorageService.LoadRootAsync(all))!;
        Assert.Equal(new[] { "Mine", "Note" }, everything.Children.Select(c => c.Title));   // not the synced "Team"
        Assert.Equal("Inside", everything.Children[0].Children[0].Title);
    }

    [Fact]
    public async Task Several_items_move_into_a_folder_in_tree_order_with_one_save()
    {
        var (node, path) = await NewNodeAsync();
        var target = new Folder { Title = "Target" };
        var a = new Template { Title = "A" };
        var b = new Template { Title = "B" };
        var c = new Template { Title = "C" };
        foreach (var item in new BaseItem[] { target, a, b, c }) await node.AddItemAsync(item);
        await node.AddItemAsync(new Template { Title = "Already in" }, target);
        int saves = 0;
        node.DataSaved += () => Interlocked.Increment(ref saves);

        Assert.True(await node.MoveItemsAsync(new BaseItem[] { c, a }, target, 0));   // selected in another order

        Assert.Equal(1, saves);
        Assert.Equal(new[] { "A", "C", "Already in" }, target.Children.Select(i => i.Title));
        Assert.Equal(new[] { "Target", "B" }, node.LocalItems.Select(i => i.Title));
        Assert.All(new[] { a, c }, i => Assert.Equal(target.Id, i.ParentId));
        var saved = await StorageService.LoadRootAsync(path);
        Assert.Equal(3, saved!.Children[0].Children.Count);
    }

    [Fact]
    public async Task Two_folders_move_into_another_and_a_reorder_on_the_same_level_keeps_their_order()
    {
        var (node, _) = await NewNodeAsync();
        var f1 = new Folder { Title = "F1" };
        var f2 = new Folder { Title = "F2" };
        var x = new Template { Title = "X" };
        var host = new Folder { Title = "Host" };
        foreach (var item in new BaseItem[] { f1, x, f2, host }) await node.AddItemAsync(item);

        Assert.True(await node.MoveItemsAsync(new BaseItem[] { f1, f2 }, null, 4));   // to the end of the top level
        Assert.Equal(new[] { "X", "Host", "F1", "F2" }, node.LocalItems.Select(i => i.Title));

        Assert.True(await node.MoveItemsAsync(new BaseItem[] { f2, f1 }, host, 0));
        Assert.Equal(new[] { "F1", "F2" }, host.Children.Select(i => i.Title));
        Assert.Equal(new[] { "X", "Host" }, node.LocalItems.Select(i => i.Title));
    }

    [Fact]
    public async Task Nothing_moves_when_one_of_the_items_cant_go_there()
    {
        var (node, _) = await NewNodeAsync();
        var outer = new Folder { Title = "Outer" };
        var inner = new Folder { Title = "Inner" };
        var loose = new Template { Title = "Loose" };
        await node.AddItemAsync(outer);
        await node.AddItemAsync(inner, outer);
        await node.AddItemAsync(loose);

        Assert.False(await node.MoveItemsAsync(new BaseItem[] { loose, outer }, inner, 0));   // outer into its own child

        Assert.Equal(new[] { "Outer", "Loose" }, node.LocalItems.Select(i => i.Title));
        Assert.Empty(inner.Children);
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
    public async Task An_import_saves_once()
    {
        var (node, _) = await NewNodeAsync();
        await node.AddItemAsync(new Template { Title = "Old local" });
        int saves = 0;
        node.DataSaved += () => Interlocked.Increment(ref saves);

        await node.ImportAsync(Enumerable.Range(1, 5).Select(i => (BaseItem)new Template { Title = $"T{i}" }).ToList(), null, ImportConflicts.Skip);

        Assert.Equal(1, saves);
    }
}
