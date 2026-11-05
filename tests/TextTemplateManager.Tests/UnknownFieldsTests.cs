using System.Runtime.CompilerServices;
using System.Text.Json;
using TextTemplateManager.Common;
using TextTemplateManager.Data;
using TextTemplateManager.Models;
using TextTemplateManager.Services.Translation;
using TextTemplateManager.ViewModels;
using Xunit;

namespace TextTemplateManager.Tests;

/// <summary>Fields written by a newer version survive everything this one does with an item. A path that drops them
/// loses the user's data and, on a sync file, rewrites the file against the newer version.</summary>
public class UnknownFieldsTests
{
    private const string Older = "20260101120000";
    private const string Newer = "20260601120000";

    private static string FutureFixture([CallerFilePath] string here = "") =>
        Path.Combine(Path.GetDirectoryName(here)!, "Fixtures", "format-future.ttmdata");

    private static Dictionary<string, JsonElement> Unknown(string value)
    {
        using var doc = JsonDocument.Parse(value);
        return new() { ["Future"] = doc.RootElement.Clone() };
    }

    private static string? Value(BaseItem item) => item.UnknownFields?["Future"].GetRawText();

    private static Template T(string title, string? unknown = null, string lastChange = Older) => new()
    {
        Title = title,
        LastChange = lastChange,
        UnknownFields = unknown == null ? null : Unknown(unknown),
    };

    private static BaseItem Find(DataNode node, string title) =>
        node.LocalItems.SelectMany(i => i.SelfAndDescendants()).First(i => i.Title == title);

    private static async Task<DataNode> NewNodeAsync()
    {
        var node = new DataNode(new StorageService(TestEnvironment.NewScratchDir()));
        await node.InitializeAsync();
        return node;
    }

    // A node whose only sync source is a copy of the fixture.
    private static async Task<(DataNode node, string path)> NodeOnFutureSyncFileAsync()
    {
        string dir = TestEnvironment.NewScratchDir();
        var storage = new StorageService(dir);
        string path = Path.Combine(TestEnvironment.NewScratchDir(), "team.ttmdata");
        File.Copy(FutureFixture(), path);
        await storage.SaveSyncSettingsAsync(new SyncSettings { Sources = { new SyncSource { Name = "Team", Path = path, IsActive = true, AllowSave = true } } });
        var node = new DataNode(storage);
        await node.InitializeAsync();
        return (node, path);
    }

    // ---- whole files ----

    [Fact]
    public async Task The_local_data_file_keeps_them_through_load_and_save()
    {
        var storage = new StorageService(TestEnvironment.NewScratchDir());
        File.Copy(FutureFixture(), storage.GetDataPath());
        var node = new DataNode(storage);
        await node.InitializeAsync();

        await node.SaveDataAsync();

        Assert.Equal(File.ReadAllText(FutureFixture()), File.ReadAllText(storage.GetDataPath()));
    }

    [Fact]
    public async Task A_sync_file_is_written_back_byte_identically()
    {
        var (node, path) = await NodeOnFutureSyncFileAsync();

        await node.SaveDataAsync();

        Assert.Equal(File.ReadAllText(FutureFixture()), File.ReadAllText(path));
    }

    [Fact]
    public async Task Editing_a_known_field_changes_only_that_field_and_the_timestamp()
    {
        var (node, path) = await NodeOnFutureSyncFileAsync();
        var greeting = (Template)Find(node, "Greeting");

        greeting.Content = "<p>Edited</p>";
        await node.FlushPendingSaveAsync();

        string expected = File.ReadAllText(FutureFixture())
            .Replace("<p>Hello</p>", "<p>Edited</p>")
            .Replace("20260101120002", greeting.LastChange);
        Assert.Equal(expected, File.ReadAllText(path));
    }

    [Fact]
    public async Task A_change_by_the_newer_version_isnt_written_over_by_the_poll()
    {
        var (node, path) = await NodeOnFutureSyncFileAsync();
        string changed = File.ReadAllText(FutureFixture()).Replace("\"FutureFormat\": 3", "\"FutureFormat\": 4");
        File.WriteAllText(path, changed);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));

        await node.CheckSyncFilesForChangesAsync();

        Assert.Equal(changed, File.ReadAllText(path));
    }

    // ---- sync merge ----

    private static Folder FileRoot(string? unknown, params BaseItem[] children)
    {
        var root = new Folder { Id = Guid.Empty, Title = "Root", LastChange = Older, UnknownFields = unknown == null ? null : Unknown(unknown) };
        foreach (var c in children) root.Children.Add(c);
        return root;
    }

    private static (Folder target, Template cached) Cached(Guid syncId, string? unknown, string lastChange)
    {
        var cached = T("Cached", unknown, lastChange);
        cached.SyncId = syncId;
        return (new Folder { Children = { cached } }, cached);
    }

    [Fact]
    public void An_item_new_in_the_file_brings_them()
    {
        var target = new Folder();

        SyncEngine.Merge(target, FileRoot(null, new Folder { Title = "F", UnknownFields = Unknown("1"), Children = { T("In", "2") } }), "S", Guid.NewGuid(), allowSave: true);

        Assert.Equal("1", Value(target.Children[0]));
        Assert.Equal("2", Value(target.Children[0].Children[0]));
    }

    [Fact]
    public void When_the_file_wins_it_brings_its_own()
    {
        var sid = Guid.NewGuid();
        var (target, cached) = Cached(sid, "1", Older);
        var file = T("File", "2", Newer);
        file.Id = sid;

        SyncEngine.Merge(target, FileRoot(null, file), "S", Guid.NewGuid(), allowSave: true);

        Assert.Equal("2", Value(cached));
    }

    [Fact]
    public void When_the_file_wins_without_any_they_are_gone()
    {
        var sid = Guid.NewGuid();
        var (target, cached) = Cached(sid, "1", Older);
        var file = T("File", null, Newer);
        file.Id = sid;

        SyncEngine.Merge(target, FileRoot(null, file), "S", Guid.NewGuid(), allowSave: true);

        Assert.Null(cached.UnknownFields);
    }

    [Fact]
    public void When_the_cache_wins_it_keeps_its_own()
    {
        var sid = Guid.NewGuid();
        var (target, cached) = Cached(sid, "1", Newer);
        var file = T("File", "2", Older);
        file.Id = sid;

        SyncEngine.Merge(target, FileRoot(null, file), "S", Guid.NewGuid(), allowSave: true);

        Assert.Equal("1", Value(cached));
    }

    [Fact]
    public void The_sync_root_follows_the_file_and_goes_back_into_it()
    {
        var target = new Folder();

        SyncEngine.Merge(target, FileRoot("1"), "S", Guid.NewGuid(), allowSave: true);

        Assert.Equal("1", Value(target));
        Assert.Equal("1", Value(DataNode.BuildFileTree(target)));
    }

    [Fact]
    public void The_file_tree_carries_them_on_every_level()
    {
        var folder = new Folder { Title = "F", UnknownFields = Unknown("1"), Children = { T("In", "2") } };

        var file = DataNode.BuildFileTree(new Folder { SyncId = Guid.Empty, Children = { folder } });

        Assert.Equal("1", Value(file.Children[0]));
        Assert.Equal("2", Value(file.Children[0].Children[0]));
    }

    // ---- import ----

    [Theory]
    [InlineData("2", "2")]
    [InlineData(null, null)]   // the whole item wins, even an old file's without any
    public async Task An_import_overwrite_takes_the_imported_items(string? imported, string? expected)
    {
        var node = await NewNodeAsync();
        await node.AddItemAsync(T("Mail", "1"));

        await node.ImportAsync(new List<BaseItem> { T("Mail", imported, Newer) }, null, ImportConflicts.OverwriteAll);

        Assert.Equal(expected, Value(Find(node, "Mail")));
    }

    [Fact]
    public async Task A_skipped_import_changes_nothing()
    {
        var node = await NewNodeAsync();
        await node.AddItemAsync(T("Mail", "1"));

        await node.ImportAsync(new List<BaseItem> { T("Mail", "2", Newer) }, null, ImportConflicts.Skip);

        Assert.Equal("1", Value(Find(node, "Mail")));
    }

    [Fact]
    public async Task A_folder_matched_by_name_keeps_its_own_and_new_items_bring_theirs()
    {
        var node = await NewNodeAsync();
        await node.AddItemAsync(new Folder { Title = "Work", UnknownFields = Unknown("1") });

        await node.ImportAsync(new List<BaseItem> { new Folder { Title = "work", UnknownFields = Unknown("2"), Children = { T("New", "3") } } }, null, ImportConflicts.Skip);

        Assert.Equal("1", Value(Find(node, "Work")));
        Assert.Equal("3", Value(Find(node, "New")));
    }

    // ---- export ----

    [Fact]
    public async Task A_folder_export_keeps_them_on_the_folder_and_its_items()
    {
        var node = await NewNodeAsync();
        var folder = new Folder { Title = "Work", UnknownFields = Unknown("1") };
        await node.AddItemAsync(folder);
        await node.AddItemAsync(T("In", "2"), folder);
        string path = Path.Combine(TestEnvironment.NewScratchDir(), "work.ttmdata");

        await node.ExportFolderAsync(path, folder);

        var file = await StorageService.LoadRootAsync(path);
        Assert.Equal("1", Value(file!));
        Assert.Equal("2", Value(file!.Children[0]));
    }

    [Fact]
    public async Task An_item_export_keeps_them_on_the_folder_copies_above_it()
    {
        var node = await NewNodeAsync();
        var folder = new Folder { Title = "Work", UnknownFields = Unknown("1") };
        await node.AddItemAsync(folder);
        await node.AddItemAsync(T("In", "2"), folder);
        await node.AddItemAsync(T("Loose", "3"));
        string path = Path.Combine(TestEnvironment.NewScratchDir(), "items.ttmdata");

        await node.ExportItemsAsync(path, new[] { Find(node, "In"), Find(node, "Loose") });

        var file = await StorageService.LoadRootAsync(path);
        Assert.Equal("1", Value(file!.Children[0]));
        Assert.Equal("2", Value(file.Children[0].Children[0]));
        Assert.Equal("3", Value(file.Children[1]));
    }

    // ---- restore ----

    [Theory]
    [InlineData("2", "2")]
    [InlineData(null, null)]
    public void A_restore_merge_overwrite_takes_the_restored_items(string? restored, string? expected)
    {
        var into = FileRoot(null, T("Mail", "1"));

        RestoreService.MergeFileTree(into, FileRoot(null, T("Mail", restored, Newer)), ImportConflicts.OverwriteAll, dryRun: false);

        Assert.Equal(expected, Value(into.Children[0]));
    }

    [Fact]
    public void A_restore_merge_keeps_a_matched_folders_own_and_brings_new_items()
    {
        var into = FileRoot(null, new Folder { Title = "Work", UnknownFields = Unknown("1") });

        RestoreService.MergeFileTree(into, FileRoot(null, new Folder { Title = "work", UnknownFields = Unknown("2"), Children = { T("New", "3") } }), ImportConflicts.Skip, dryRun: false);

        Assert.Equal("1", Value(into.Children[0]));
        Assert.Equal("3", Value(into.Children[0].Children[0]));
    }

    [Theory]
    [InlineData(RestoreParts.Local, "1")]
    [InlineData(RestoreParts.SyncedFolders, "2")]
    public async Task A_partial_restore_keeps_the_root_of_the_side_the_local_items_come_from(RestoreParts parts, string expected)
    {
        var storage = new StorageService(TestEnvironment.NewScratchDir());
        await storage.SaveAsync(storage.GetDataPath(), FileRoot("1", T("Local")));
        await storage.SaveSyncSettingsAsync(new SyncSettings());
        string backup = (await new BackupService(storage).CreateAsync(BackupTier.Recent, BackupSource.Live(storage), quietMs: 0)).Path!;
        await storage.SaveAsync(storage.GetDataPath(), FileRoot("2", T("Local")));

        var result = await new RestoreService(storage).RestoreAsync(backup, parts);

        Assert.True(result.Succeeded, result.Problem);
        Assert.Equal(expected, Value((await StorageService.LoadRootAsync(storage.GetDataPath()))!));
    }

    // ---- copies ----

    [Fact]
    public void A_duplicate_gets_its_own_copy()
    {
        var original = T("Mail", "1");
        original.Content = "<p>m</p>";
        original.TagsCsv = "a, b";
        original.DefaultPasteMode = PasteMode.RTF;
        original.SingleKeyShortcut = "m";
        original.MultiKeyShortcut = "mail";

        var clone = Assert.IsType<Template>(original.DuplicateAs("Mail copy"));

        Assert.Equal(("Mail copy", "<p>m</p>", "a, b", PasteMode.RTF, "M", "MAIL"),
            (clone.Title, clone.Content, clone.TagsCsv, clone.DefaultPasteMode, clone.SingleKeyShortcut, clone.MultiKeyShortcut));
        Assert.Equal("1", Value(clone));
        Assert.NotSame(original.UnknownFields, clone.UnknownFields);
    }

    [Fact]
    public void A_duplicated_folder_keeps_them_too()
    {
        var clone = new Folder { Title = "Work", UnknownFields = Unknown("1"), Children = { T("In") } }.DuplicateAs("Work copy");

        Assert.IsType<Folder>(clone);
        Assert.Equal("Work copy", clone.Title);
        Assert.Equal("1", Value(clone));
        Assert.Empty(clone.Children);
    }

    [Fact]
    public void A_translated_copy_doesnt_take_them()
    {
        var copy = TranslatedCopy.Of(T("Mail", "1"), "Mail (DE)", "<p>Hallo</p>", "en", "de");

        Assert.Equal(("Mail (DE)", "<p>Hallo</p>"), (copy.Title, copy.Content));
        Assert.Null(copy.UnknownFields);
    }

    [Fact]
    public void A_draft_commit_leaves_them_on_the_item()
    {
        var item = T("Mail", "1");
        var draft = ItemDraft.For(item);

        draft.Edited.Title = "Mail 2";
        draft.Commit();

        Assert.Equal("Mail 2", item.Title);
        Assert.Equal("1", Value(item));
    }
}
