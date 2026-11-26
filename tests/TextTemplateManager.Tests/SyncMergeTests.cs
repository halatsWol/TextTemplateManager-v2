using TextTemplateManager.Common;
using TextTemplateManager.Data;
using TextTemplateManager.Models;
using Xunit;

namespace TextTemplateManager.Tests;

/// <summary>Field-by-field sync merge against the copy last read from or written to the file: edits made on two
/// devices to different fields or language tabs of one template are both kept; the same field changed on both sides
/// goes to the newer one.</summary>
public class SyncMergeTests
{
    private const string Synced = "20260101120000";
    private const string Edit1 = "20260301120000";
    private const string Edit2 = "20260401120000";

    private static readonly Guid Sid = Guid.NewGuid();

    private static TemplateLanguage Alt(string id, string? language, string content) => new() { Id = id, Language = language, Content = content };

    private static Template Mail(string lastChange = Synced, string en = "<p>Hello</p>", string de = "<p>Hallo</p>") => new()
    {
        Id = Sid,
        Title = "Mail",
        Content = en,
        Language = "en",
        LanguageId = "a1b2",
        AltLanguages = [Alt("c3d4", "de", de)],
        LastChange = lastChange,
    };

    private static Template Single(string lastChange = Synced, string en = "<p>Hello</p>") =>
        new() { Id = Sid, Title = "Mail", Content = en, Language = "en", LastChange = lastChange };

    // Merges file into cached with synced as the last synced copy; returns the merged template (the cached object).
    private static Template Run(Template synced, Template cached, Template file, bool allowSave = true)
    {
        cached.Id = Guid.NewGuid();
        cached.SyncId = Sid;
        var target = new Folder();
        target.Children.Add(cached);
        var root = new Folder { Id = Guid.Empty, Title = "Root", LastChange = Synced };
        root.Children.Add(file);

        SyncEngine.Merge(target, root, "Team", Guid.NewGuid(), allowSave, lastSynced: new Dictionary<Guid, BaseItem> { [Sid] = synced });

        var merged = (Template)Assert.Single(target.Children);
        Assert.Same(cached, merged);
        return merged;
    }

    private static string Tabs(Template t) =>
        string.Join(" | ", new[] { $"{t.LanguageId}:{t.Language}:{t.Content}" }
            .Concat((t.AltLanguages ?? []).Select(a => $"{a.Id}:{a.Language}:{a.Content}")));

    [Fact]
    public void Different_language_tabs_edited_on_each_side_are_both_kept()
    {
        var merged = Run(Mail(), Mail(Edit2, de: "<p>Hallo hier</p>"), Mail(Edit1, en: "<p>Hello there</p>"));

        Assert.Equal("a1b2:en:<p>Hello there</p> | c3d4:de:<p>Hallo hier</p>", Tabs(merged));
        Assert.True(string.CompareOrdinal(merged.LastChange, Edit2) > 0);   // a new version, newer than both
    }

    [Fact]
    public void Different_fields_edited_on_each_side_are_both_kept()
    {
        var cached = Mail(Edit2);
        cached.Title = "Mail (new)";
        cached.MultiKeyShortcut = "ML";
        var file = Mail(Edit1, de: "<p>Hallo dort</p>");
        file.TagsCsv = "customer";
        file.DefaultPasteMode = PasteMode.Plaintext;

        var merged = Run(Mail(), cached, file);

        Assert.Equal("Mail (new)", merged.Title);
        Assert.Equal("ML", merged.MultiKeyShortcut);
        Assert.Equal("customer", merged.TagsCsv);
        Assert.Equal(PasteMode.Plaintext, merged.DefaultPasteMode);
        Assert.Equal("<p>Hallo dort</p>", merged.AltLanguages![0].Content);
    }

    [Theory]
    [InlineData(Edit2, Edit1, "<p>here</p>")]
    [InlineData(Edit1, Edit2, "<p>there</p>")]
    public void The_same_tab_changed_on_both_sides_goes_to_the_newer_side(string cachedChange, string fileChange, string expected)
    {
        var merged = Run(Mail(), Mail(cachedChange, de: "<p>here</p>"), Mail(fileChange, de: "<p>there</p>"));

        Assert.Equal(expected, merged.AltLanguages![0].Content);
    }

    [Fact]
    public void A_tab_added_on_one_side_and_another_edited_on_the_other_are_both_kept()
    {
        var file = Mail(Edit1);
        file.AltLanguages = [.. file.AltLanguages!, Alt("e5f6", "it", "<p>Ciao</p>")];

        var merged = Run(Mail(), Mail(Edit2, de: "<p>Hallo hier</p>"), file);

        Assert.Equal("a1b2:en:<p>Hello</p> | c3d4:de:<p>Hallo hier</p> | e5f6:it:<p>Ciao</p>", Tabs(merged));
    }

    [Fact]
    public void A_tab_removed_on_the_other_side_is_removed()
    {
        var file = Single(Edit1);

        var merged = Run(Mail(), Mail(Edit2), file);

        Assert.Equal(":en:<p>Hello</p>", Tabs(merged));
        Assert.Null(merged.LanguageId);
        Assert.Null(merged.AltLanguages);
    }

    [Fact]
    public void A_tab_removed_on_the_other_side_but_changed_here_is_kept()
    {
        var merged = Run(Mail(), Mail(Edit2, de: "<p>Hallo hier</p>"), Single(Edit1));

        Assert.Equal("a1b2:en:<p>Hello</p> | c3d4:de:<p>Hallo hier</p>", Tabs(merged));
    }

    [Fact]
    public void A_second_tab_added_here_keeps_the_first_tab_edited_elsewhere()
    {
        // Adding the second tab gave the first tab its id; the file still has the first tab without one.
        var merged = Run(Single(), Mail(Edit2), Single(Edit1, en: "<p>Hello there</p>"));

        Assert.Equal("a1b2:en:<p>Hello there</p> | c3d4:de:<p>Hallo</p>", Tabs(merged));
    }

    [Fact]
    public void A_second_tab_added_on_both_sides_keeps_one_first_tab_and_both_new_tabs()
    {
        var cached = Single(Edit2);
        cached.LanguageId = "aaaa";
        cached.AltLanguages = [Alt("c3d4", "de", "<p>Hallo</p>")];
        var file = Single(Edit1);
        file.LanguageId = "bbbb";
        file.AltLanguages = [Alt("e5f6", "it", "<p>Ciao</p>")];

        var merged = Run(Single(), cached, file);

        Assert.Equal("bbbb:en:<p>Hello</p> | e5f6:it:<p>Ciao</p> | c3d4:de:<p>Hallo</p>", Tabs(merged));
    }

    [Fact]
    public void A_tab_made_first_on_one_side_keeps_an_edit_of_it_from_the_other()
    {
        var cached = Mail(Edit2);
        (cached.Content, cached.Language, cached.LanguageId) = ("<p>Hallo</p>", "de", "c3d4");
        cached.AltLanguages = [Alt("a1b2", "en", "<p>Hello</p>")];

        var merged = Run(Mail(), cached, Mail(Edit1, en: "<p>Hello there</p>"));

        Assert.Equal("c3d4:de:<p>Hallo</p> | a1b2:en:<p>Hello there</p>", Tabs(merged));
    }

    [Fact]
    public void A_cached_copy_older_than_the_last_sync_takes_the_file()
    {
        // E.g. the local save was lost after the file was written: the old local text is no edit made here.
        var merged = Run(Mail(Edit1), Mail(Synced, de: "<p>old</p>"), Mail(Edit1));

        Assert.Equal("<p>Hallo</p>", merged.AltLanguages![0].Content);
        Assert.Equal(Edit1, merged.LastChange);
    }

    [Fact]
    public void A_read_only_source_takes_the_file()
    {
        var merged = Run(Mail(), Mail(Edit2, de: "<p>Hallo hier</p>"), Mail(Edit1, en: "<p>Hello there</p>"), allowSave: false);

        Assert.Equal("a1b2:en:<p>Hello there</p> | c3d4:de:<p>Hallo</p>", Tabs(merged));
        Assert.Equal(Edit1, merged.LastChange);
    }

    [Fact]
    public void A_merge_equal_to_the_file_keeps_the_file_timestamp()
    {
        // Nothing to write back, so the file isn't rewritten.
        var merged = Run(Mail(), Mail(), Mail(Edit1, de: "<p>Hallo dort</p>"));

        Assert.Equal("<p>Hallo dort</p>", merged.AltLanguages![0].Content);
        Assert.Equal(Edit1, merged.LastChange);
    }

    [Fact]
    public void Edits_made_only_here_keep_their_timestamp()
    {
        var merged = Run(Mail(), Mail(Edit2, de: "<p>Hallo hier</p>"), Mail());

        Assert.Equal("<p>Hallo hier</p>", merged.AltLanguages![0].Content);
        Assert.Equal(Edit2, merged.LastChange);
    }

    [Fact]
    public void A_file_from_an_older_version_still_keeps_the_languages_here()
    {
        // 2.6 and older drop the language fields: that is no removal of the tabs.
        var file = new Template { Id = Sid, Title = "Mail", Content = "<p>Hello there</p>", LastChange = Edit1 };

        var merged = Run(Mail(), Mail(), file);

        Assert.Equal("a1b2:en:<p>Hello there</p> | c3d4:de:<p>Hallo</p>", Tabs(merged));
        Assert.True(merged.LanguagesKeptLocally);
    }

    [Fact]
    public void A_folder_renamed_elsewhere_keeps_an_edit_inside_it_made_here()
    {
        var folderId = Guid.NewGuid();
        Folder FolderWith(string title, string lastChange, Template child)
        {
            var f = new Folder { Id = folderId, Title = title, LastChange = lastChange };
            f.Children.Add(child);
            return f;
        }
        var synced = FolderWith("Offers", Synced, Mail());
        var cachedMail = Mail(Edit2, de: "<p>Hallo hier</p>");
        cachedMail.Id = Guid.NewGuid();
        cachedMail.SyncId = Sid;
        var cached = FolderWith("Offers", Synced, cachedMail);
        cached.Id = Guid.NewGuid();
        cached.SyncId = folderId;
        var target = new Folder();
        target.Children.Add(cached);
        var root = new Folder { Id = Guid.Empty, Title = "Root", LastChange = Synced };
        root.Children.Add(FolderWith("Offers 2026", Edit1, Mail(Edit1, en: "<p>Hello there</p>")));
        var lastSynced = synced.SelfAndDescendants().ToDictionary(i => i.Id);

        SyncEngine.Merge(target, root, "Team", Guid.NewGuid(), allowSave: true, lastSynced: lastSynced);

        Assert.Equal("Offers 2026", cached.Title);
        Assert.Equal("a1b2:en:<p>Hello there</p> | c3d4:de:<p>Hallo hier</p>", Tabs(cachedMail));
    }

    // ---- two devices on one sync file ----

    // A device on its own data folder; a data folder that is already set up is started again (an app restart).
    private static async Task<DataNode> DeviceAsync(string sharedPath, string? dataDir = null)
    {
        var storage = new StorageService(dataDir ?? TestEnvironment.NewScratchDir());
        if (dataDir == null)
            await storage.SaveSyncSettingsAsync(new SyncSettings { Sources = { new SyncSource { Id = SourceId, Name = "Team", Path = sharedPath, IsActive = true, AllowSave = true } } });
        var node = new DataNode(storage);
        await node.InitializeAsync();
        return node;
    }

    private static readonly Guid SourceId = Guid.NewGuid();

    private static Template MailOn(DataNode node) => (Template)node.LocalItems[0].Children.Single();

    private static async Task<string> SharedMailAsync()
    {
        string shared = Path.Combine(TestEnvironment.NewScratchDir(), "team.ttmdata");
        var root = new Folder { Id = Guid.Empty, Title = "Root", LastChange = Synced };
        root.Children.Add(Mail());
        await new StorageService(Path.GetDirectoryName(shared)!).SaveSharedAsync(shared, root);
        return shared;
    }

    private static string Now(int addMinutes = 0) => DateTime.Now.AddMinutes(addMinutes).ToString("yyyyMMddHHmmss");

    // What 2.7 and 2.8.x do: merge without a last synced copy (the newer template wins), then write the result.
    private static async Task<Folder> OldVersionMergeAndWriteAsync(string shared, Folder cache)
    {
        var file = await StorageService.LoadRootSharedAsync(shared);
        SyncEngine.Merge(cache, file!, "Team", SourceId, allowSave: true);
        var written = new Folder { Id = Guid.Empty, Title = "Root", LastChange = file!.LastChange };
        foreach (var t in cache.Children.OfType<Template>())
            written.Children.Add(new Template
            {
                Id = t.SyncId!.Value, Title = t.Title, Content = t.Content, Language = t.Language, LanguageId = t.LanguageId,
                AltLanguages = t.AltLanguages, LastChange = t.LastChange,
            });
        await new StorageService(Path.GetDirectoryName(shared)!).SaveSharedAsync(shared, written);
        return cache;
    }

    private static async Task<Folder> OldVersionDeviceAsync(string shared)
    {
        var cache = new Folder();
        SyncEngine.Merge(cache, (await StorageService.LoadRootSharedAsync(shared))!, "Team", SourceId, allowSave: true);
        return cache;
    }

    [Fact]
    public async Task Two_devices_editing_different_languages_of_one_template_keep_both_edits()
    {
        string shared = Path.Combine(TestEnvironment.NewScratchDir(), "team.ttmdata");
        var root = new Folder { Id = Guid.Empty, Title = "Root", LastChange = Synced };
        root.Children.Add(Mail());
        await new StorageService(Path.GetDirectoryName(shared)!).SaveSharedAsync(shared, root);
        var a = await DeviceAsync(shared);
        var b = await DeviceAsync(shared);

        MailOn(a).Content = "<p>Hello from A</p>";
        await a.FlushPendingSaveAsync();
        MailOn(b).AltLanguages = [Alt("c3d4", "de", "<p>Hallo von B</p>")];
        await b.FlushPendingSaveAsync();   // the file changed since B read it: not written yet

        await b.CheckSyncFilesForChangesAsync();   // B merges A's edit and writes both
        await a.CheckSyncFilesForChangesAsync();

        const string both = "a1b2:en:<p>Hello from A</p> | c3d4:de:<p>Hallo von B</p>";
        var file = await StorageService.LoadRootSharedAsync(shared);
        Assert.Equal(both, Tabs((Template)file!.Children.Single()));
        Assert.Equal(both, Tabs(MailOn(a)));
        Assert.Equal(both, Tabs(MailOn(b)));
    }

    [Fact]
    public async Task The_last_synced_copy_survives_a_restart()
    {
        string shared = await SharedMailAsync();
        var a = await DeviceAsync(shared);
        var b = await DeviceAsync(shared);

        MailOn(a).Content = "<p>Hello from A</p>";
        await a.FlushPendingSaveAsync();
        MailOn(b).AltLanguages = [Alt("c3d4", "de", "<p>Hallo von B</p>")];
        await b.FlushPendingSaveAsync();   // the file changed since B read it: not written, and B is closed

        var restarted = await DeviceAsync(shared, b.Storage.DataDirectory);

        const string both = "a1b2:en:<p>Hello from A</p> | c3d4:de:<p>Hallo von B</p>";
        Assert.Equal(both, Tabs(MailOn(restarted)));
        var file = await StorageService.LoadRootSharedAsync(shared);
        Assert.Equal(both, Tabs((Template)file!.Children.Single()));
    }

    [Fact]
    public async Task Last_synced_copies_of_old_paths_and_inactive_sources_are_deleted()
    {
        string shared = await SharedMailAsync();
        var node = await DeviceAsync(shared);
        var source = node.CurrentSyncSettings.Sources.Single();
        string first = node.Storage.GetSyncStatePath(source.Id, shared);
        Assert.True(File.Exists(first));

        string moved = Path.Combine(TestEnvironment.NewScratchDir(), "team.ttmdata");
        File.Copy(shared, moved);
        source.Path = moved;
        await node.ReapplySyncAsync();
        Assert.False(File.Exists(first));
        Assert.True(File.Exists(node.Storage.GetSyncStatePath(source.Id, moved)));

        source.IsActive = false;
        await node.ReapplySyncAsync();
        Assert.Empty(Directory.EnumerateFiles(node.Storage.SyncStateDirectory));
    }

    [Fact]
    public async Task An_older_version_takes_the_merged_template_of_a_newer_one()
    {
        string shared = await SharedMailAsync();
        var n = await DeviceAsync(shared);
        var old = await OldVersionDeviceAsync(shared);

        var oldMail = (Template)old.Children.Single();
        oldMail.Content = "<p>Hello from the old PC</p>";
        oldMail.LastChange = Now();
        await OldVersionMergeAndWriteAsync(shared, old);
        MailOn(n).AltLanguages = [Alt("c3d4", "de", "<p>Hallo vom neuen PC</p>")];
        await n.FlushPendingSaveAsync();   // the file changed since N read it: not written yet

        await n.CheckSyncFilesForChangesAsync();   // N merges field by field and writes both
        await OldVersionMergeAndWriteAsync(shared, old);

        const string both = "a1b2:en:<p>Hello from the old PC</p> | c3d4:de:<p>Hallo vom neuen PC</p>";
        Assert.Equal(both, Tabs(MailOn(n)));
        Assert.Equal(both, Tabs(oldMail));
        Assert.Equal(both, Tabs((Template)(await StorageService.LoadRootSharedAsync(shared))!.Children.Single()));
    }

    [Fact]
    public async Task An_older_version_merging_first_wins_with_its_whole_template_on_every_device()
    {
        string shared = await SharedMailAsync();
        var n = await DeviceAsync(shared);
        var old = await OldVersionDeviceAsync(shared);

        MailOn(n).AltLanguages = [Alt("c3d4", "de", "<p>Hallo vom neuen PC</p>")];
        await n.FlushPendingSaveAsync();   // written
        var oldMail = (Template)old.Children.Single();
        oldMail.Content = "<p>Hello from the old PC</p>";
        oldMail.LastChange = Now(1);
        await OldVersionMergeAndWriteAsync(shared, old);   // newer: its whole template replaces N's

        await n.CheckSyncFilesForChangesAsync();

        // The new version can't tell an older version's whole-template write from an edit: the German edit is lost,
        // as with the old rule, and every device ends up with the same template.
        const string olds = "a1b2:en:<p>Hello from the old PC</p> | c3d4:de:<p>Hallo</p>";
        Assert.Equal(olds, Tabs(MailOn(n)));
        Assert.Equal(olds, Tabs(oldMail));
        Assert.Equal(olds, Tabs((Template)(await StorageService.LoadRootSharedAsync(shared))!.Children.Single()));
    }
}
