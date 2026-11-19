using System.Text;
using TextTemplateManager.Common;
using TextTemplateManager.Data;
using TextTemplateManager.Models;
using TextTemplateManager.Services.Translation;
using TextTemplateManager.ViewModels;
using Xunit;

namespace TextTemplateManager.Tests;

/// <summary>A template's languages: the file format older versions keep, the repair at load, the sync rule for files
/// written by a version that doesn't know them, and every path that copies a template.</summary>
public class TemplateLanguageTests
{
    private const string Older = "20260101120000";
    private const string Newer = "20260601120000";

    private static TemplateLanguage Alt(string id, string? language, string content) => new() { Id = id, Language = language, Content = content };

    private static Template Multi(string title = "Mail", string lastChange = Older) => new()
    {
        Title = title,
        Content = "<p>Hello</p>",
        Language = "en",
        LanguageId = "a1b2",
        AltLanguages = [Alt("c3d4", "de", "<p>Hallo</p>"), Alt("e5f6", "it", "<p>Ciao</p>")],
        LastChange = lastChange,
    };

    private static Folder Root(params BaseItem[] children)
    {
        var root = new Folder { Id = Guid.Empty, Title = "Root", LastChange = Older };
        foreach (var c in children) root.Children.Add(c);
        return root;
    }

    private static string Text(Folder root) => Encoding.UTF8.GetString(StorageService.SerializeTree(root));

    private static async Task<DataNode> NewNodeAsync()
    {
        var node = new DataNode(new StorageService(TestEnvironment.NewScratchDir()));
        await node.InitializeAsync();
        return node;
    }

    // ---- file format ----

    // As 2.7.0 writes a newer version's template back: the language fields behind Title, in this order (VM run, 2026-11-06).
    private const string WrittenBy27 = """
        {
          "Id": "00000000-0000-0000-0000-000000000000",
          "ItemType": 0,
          "LastChange": "20260101120000",
          "Children": [
            {
              "$type": "template",
              "Content": "<p>Hello</p>",
              "SingleKeyShortcut": "",
              "MultiKeyShortcut": "",
              "TagsCsv": "",
              "DefaultPasteMode": 4,
              "HasSingleKeyConflict": false,
              "HasMultiKeyConflict": false,
              "Id": "22222222-2222-2222-2222-222222222222",
              "ParentId": "00000000-0000-0000-0000-000000000000",
              "ItemType": 1,
              "LastChange": "20260101120002",
              "Children": [],
              "Title": "Greeting",
              "Language": "en",
              "LanguageId": "a1b2",
              "AltLanguages": [
                {
                  "Id": "c3d4",
                  "Language": "de",
                  "Content": "<p>Hallo ä</p>",
                  "Future": 1
                }
              ],
              "FutureField": true
            }
          ],
          "Title": "Root"
        }
        """;

    private static string As27Writes() => WrittenBy27.ReplaceLineEndings("\r\n").Replace("\\u00E4", "ä");

    [Fact]
    public void A_file_as_older_versions_write_it_back_round_trips_to_the_same_bytes()
    {
        string text = As27Writes();
        var root = StorageService.ParseTree(Encoding.UTF8.GetBytes(text))!;

        var t = Assert.IsType<Template>(root.Children[0]);
        Assert.Equal(("en", "a1b2"), (t.Language, t.LanguageId));
        var alt = Assert.Single(t.AltLanguages!);
        Assert.Equal(("c3d4", "de", "<p>Hallo ä</p>"), (alt.Id, alt.Language, alt.Content));
        Assert.Equal("1", alt.UnknownFields!["Future"].GetRawText());
        Assert.Equal(text, Text(root));
    }

    [Fact]
    public void A_template_without_languages_writes_none_of_the_fields()
    {
        string text = Text(Root(new Template { Title = "Plain", AltLanguages = [] }));

        Assert.DoesNotContain("Language", text);
    }

    [Fact]
    public void A_first_tab_language_alone_is_written_behind_the_title()
    {
        string text = Text(Root(new Template { Title = "Plain", Language = "de" }));

        Assert.Contains("\"Title\": \"Plain\",\r\n      \"Language\": \"de\"\r\n", text);
        Assert.DoesNotContain("LanguageId", text);
    }

    // ---- repair at load ----

    private static Template Repaired(Template t) => (Template)TemplateLanguages.Repair(Root(t))!.Children[0];

    [Fact]
    public void A_template_in_order_is_left_exactly_as_it_was()
    {
        var root = Root(Multi());
        string before = Text(root);

        TemplateLanguages.Repair(root);

        Assert.Equal(before, Text(root));
    }

    [Fact]
    public void Missing_bad_and_repeated_tab_ids_get_new_unique_ones()
    {
        var t = Repaired(new Template
        {
            Language = "en",
            AltLanguages = [Alt("c3d4", "de", "1"), Alt("c3d4", "it", "2"), Alt("XYZ", "fr", "3"), new() { Language = "es" }],
        });

        var ids = t.AltLanguages!.Select(a => a.Id).Prepend(t.LanguageId).ToList();
        Assert.All(ids, id => Assert.Matches("^[0-9a-f]{4}$", id));
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.Equal("c3d4", t.AltLanguages![0].Id);
    }

    [Fact]
    public void A_first_tab_id_without_other_tabs_is_dropped()
    {
        var t = Repaired(new Template { Language = "en", LanguageId = "a1b2" });

        Assert.Null(t.LanguageId);
        Assert.Equal("en", t.Language);
    }

    [Theory]
    [InlineData("DE", "de")]
    [InlineData(" iw ", "he")]
    [InlineData("in", "id")]
    [InlineData("no", "nb")]
    [InlineData("zh-hant", "zh-Hant")]
    [InlineData("ZH-HANS", "zh-Hans")]
    [InlineData("pt_br", "pt-BR")]
    [InlineData("", null)]
    public void Language_codes_are_stored_in_one_form(string code, string? expected)
    {
        Assert.Equal(expected, TemplateLanguages.NormalizeCode(code));
        Assert.Equal(expected, Repaired(new Template { Language = code, AltLanguages = [Alt("c3d4", code, "x")] }).AltLanguages![0].Language);
    }

    [Fact]
    public async Task A_loaded_file_is_repaired()
    {
        string path = Path.Combine(TestEnvironment.NewScratchDir(), "team.ttmdata");
        File.WriteAllText(path, As27Writes().Replace("\"Id\": \"c3d4\"", "\"Id\": \"a1b2\"").Replace("\"de\"", "\"DE\""));

        var t = (Template)(await StorageService.LoadRootAsync(path))!.Children[0];

        Assert.NotEqual("a1b2", t.AltLanguages![0].Id);
        Assert.Equal("de", t.AltLanguages[0].Language);
    }

    // ---- tabs ----

    [Fact]
    public void Content_for_a_tab_id()
    {
        var t = Multi();

        Assert.Equal("<p>Hallo</p>", t.ContentFor("c3d4"));
        Assert.Equal("<p>Hello</p>", t.ContentFor("a1b2"));
        Assert.Equal("<p>Hello</p>", t.ContentFor(null));
        Assert.Equal("<p>Hello</p>", t.ContentFor("ffff"));
    }

    [Fact]
    public void Only_tabs_with_a_language_no_earlier_tab_has_are_offered()
    {
        var t = Multi();
        t.AltLanguages = [Alt("c3d4", "de", "1"), Alt("e5f6", null, "2"), Alt("0a0b", "DE", "3"), Alt("0c0d", "en", "4"), Alt("0e0f", "it", "5")];

        Assert.Equal(new[] { "a1b2", "c3d4", "0e0f" }, TemplateLanguages.Offered(t).Select(o => o.Id));
        Assert.True(t.HasLanguageConflict);
    }

    [Fact]
    public void The_first_tab_is_offered_without_a_language()
    {
        var t = new Template { Content = "x" };

        Assert.Equal("x", Assert.Single(TemplateLanguages.Offered(t)).Content);
        Assert.False(t.HasLanguageConflict);
    }

    [Fact]
    public void A_language_conflict_shows_in_the_conflict_report()
    {
        var t = Multi();
        t.AltLanguages = [Alt("c3d4", "de", "1"), Alt("e5f6", "de", "2")];

        var report = ConflictReport.From(new[] { t, Multi("Other") }, hideNotes: false);

        Assert.True(report.HasConflicts);
        Assert.Contains(report.Lines, l => l.Kind == ConflictLineKind.Key && l.Text.Contains(TranslationLanguage.NameOf("de")));
        Assert.Contains(report.Lines, l => l.Kind == ConflictLineKind.Template && l.Text == "- Mail");
        Assert.DoesNotContain(report.Lines, l => l.Text == "- Other");
    }

    [Fact]
    public void A_search_in_the_text_finds_any_tab()
    {
        Assert.True(TemplateSearch.Matches("Ciao", Multi(), includeContent: true));
        Assert.True(TemplateSearch.Matches("Ciao", Multi(), includeContent: true, strict: true));
        Assert.False(TemplateSearch.Matches("Ciao", Multi(), includeContent: false));
    }

    // ---- sync files written by a version that doesn't know the languages ----

    private static (Folder target, Template cached) Cached(Template cached)
    {
        cached.SyncId = Guid.NewGuid();
        return (new Folder { Children = { cached } }, cached);
    }

    private static Template FileCopy(Template cached, string content, string lastChange, bool withLanguages = false) => new()
    {
        Id = cached.SyncId!.Value,
        Title = cached.Title,
        Content = content,
        LastChange = lastChange,
        Language = withLanguages ? cached.Language : null,
        LanguageId = withLanguages ? cached.LanguageId : null,
        AltLanguages = withLanguages ? cached.AltLanguages : null,
    };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Languages_an_older_version_dropped_are_kept_here(bool allowSave)
    {
        var (target, cached) = Cached(Multi());

        SyncEngine.Merge(target, Root(FileCopy(cached, "<p>Hello</p>", Older)), "Team", Guid.NewGuid(), allowSave);

        Assert.Same(cached, target.Children[0]);
        Assert.Equal(("en", 2), (cached.Language, cached.AltLanguages!.Count));
        Assert.True(cached.LanguagesKeptLocally);
        Assert.False(cached.DefaultChangedSinceLanguages);
    }

    [Fact]
    public void An_older_versions_edit_is_taken_and_the_languages_stay()
    {
        var (target, cached) = Cached(Multi());

        SyncEngine.Merge(target, Root(FileCopy(cached, "<p>Hi there</p>", Newer)), "Team", Guid.NewGuid(), allowSave: true);

        Assert.Equal(("<p>Hi there</p>", Newer), (cached.Content, cached.LastChange));
        Assert.Equal(2, cached.AltLanguages!.Count);
        Assert.True(cached.DefaultChangedSinceLanguages);
    }

    [Fact]
    public void Tabs_removed_by_a_newer_version_are_removed_here()
    {
        var (target, cached) = Cached(Multi());
        var file = FileCopy(cached, "<p>Hello</p>", Newer);
        file.Language = "en";

        SyncEngine.Merge(target, Root(file), "Team", Guid.NewGuid(), allowSave: true);

        Assert.Null(cached.AltLanguages);
        Assert.Null(cached.LanguageId);
        Assert.Equal("en", cached.Language);
        Assert.False(cached.LanguagesKeptLocally);
    }

    [Theory]
    [InlineData(Older, "en")]    // unchanged otherwise: only stripped
    [InlineData(Newer, null)]    // edited by the older version: its file wins
    public void A_first_tab_language_alone_is_kept_only_while_the_file_brings_no_newer_edit(string fileStamp, string? expected)
    {
        var (target, cached) = Cached(new Template { Title = "Mail", Content = "x", Language = "en", LastChange = Older });

        SyncEngine.Merge(target, Root(FileCopy(cached, "x", fileStamp)), "Team", Guid.NewGuid(), allowSave: true);

        Assert.Equal(expected, cached.Language);
    }

    [Fact]
    public void Kept_languages_arent_written_back_until_the_template_is_edited_here()
    {
        var t = Multi();
        t.SyncId = Guid.NewGuid();
        t.LanguagesKeptLocally = true;
        var folder = new Folder { Children = { t } };

        var kept = (Template)DataNode.BuildFileTree(folder).Children[0];
        t.LanguagesKeptLocally = false;
        var written = (Template)DataNode.BuildFileTree(folder).Children[0];

        Assert.Equal((null, null, null), (kept.Language, kept.LanguageId, kept.AltLanguages));
        Assert.Equal(("en", "a1b2", 2), (written.Language, written.LanguageId, written.AltLanguages!.Count));
    }

    [Fact]
    public async Task An_edit_here_ends_keeping_them_locally()
    {
        var node = await NewNodeAsync();
        var t = Multi();
        await node.AddItemAsync(t);
        t.LanguagesKeptLocally = true;

        t.TagsCsv = "mail";

        Assert.False(t.LanguagesKeptLocally);
    }

    [Fact]
    public async Task A_sync_file_from_an_older_version_stays_as_it_is()
    {
        string dir = TestEnvironment.NewScratchDir();
        var storage = new StorageService(dir);
        string path = Path.Combine(TestEnvironment.NewScratchDir(), "team.ttmdata");
        File.WriteAllText(path, As27Writes());
        await storage.SaveSyncSettingsAsync(new SyncSettings { Sources = { new SyncSource { Name = "Team", Path = path, IsActive = true, AllowSave = true } } });
        var node = new DataNode(storage);
        await node.InitializeAsync();
        string stripped = As27Writes().Replace("\r\n      \"Language\": \"en\",\r\n      \"LanguageId\": \"a1b2\",", "")
            .Replace(",\r\n      \"AltLanguages\": [\r\n        {\r\n          \"Id\": \"c3d4\",\r\n          \"Language\": \"de\",\r\n          \"Content\": \"<p>Hallo ä</p>\",\r\n          \"Future\": 1\r\n        }\r\n      ]", "");
        Assert.DoesNotContain("Language", stripped);

        File.WriteAllText(path, stripped);   // an older version saved it
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
        await node.CheckSyncFilesForChangesAsync();

        var t = (Template)node.LocalItems[0].Children[0];
        Assert.Equal("de", t.AltLanguages![0].Language);
        Assert.Equal(stripped, File.ReadAllText(path));
    }

    // ---- copies ----

    [Fact]
    public void A_duplicate_takes_the_languages()
    {
        var clone = (Template)Multi().DuplicateAs("Mail copy");

        Assert.Equal(("en", "a1b2", 2), (clone.Language, clone.LanguageId, clone.AltLanguages!.Count));
    }

    [Fact]
    public void A_translated_copy_is_in_the_target_language_only()
    {
        var copy = TranslatedCopy.Of(Multi(), "Mail (FR)", "<p>Bonjour</p>", "en", "fr");

        Assert.Equal("fr", copy.Language);
        Assert.Null(copy.LanguageId);
        Assert.Null(copy.AltLanguages);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]   // the whole item wins: an old file's template without languages
    public async Task An_import_overwrite_takes_the_imported_languages(bool importedHasThem)
    {
        var node = await NewNodeAsync();
        await node.AddItemAsync(Multi());
        var imported = importedHasThem ? Multi(lastChange: Newer) : new Template { Title = "Mail", LastChange = Newer };
        if (importedHasThem) imported.AltLanguages = [Alt("c3d4", "fr", "<p>Salut</p>")];

        await node.ImportAsync(new List<BaseItem> { imported }, null, ImportConflicts.OverwriteAll);

        var t = (Template)node.LocalItems[0];
        Assert.Equal(importedHasThem ? "fr" : null, t.AltLanguages?[0].Language);
        Assert.Equal(importedHasThem ? "en" : null, t.Language);
    }

    [Fact]
    public void A_restore_overwrite_takes_the_restored_languages()
    {
        var into = Root(new Template { Title = "Mail", Content = "x" });
        var restored = Multi(lastChange: Newer);

        RestoreService.MergeFileTree(into, Root(restored), ImportConflicts.OverwriteAll, dryRun: false);

        var t = (Template)into.Children[0];
        Assert.Equal(("en", "a1b2", 2), (t.Language, t.LanguageId, t.AltLanguages!.Count));
    }

    // ---- change tracking ----

    [Fact]
    public async Task A_new_tab_list_is_an_edit_and_a_new_conflict_revalidates()
    {
        var node = await NewNodeAsync();
        var t = Multi();
        await node.AddItemAsync(t);
        t.LastChange = Older;
        int validations = 0;
        node.ShortcutsChanged += () => validations++;

        t.AltLanguages = [Alt("c3d4", "en", "<p>again</p>")];

        Assert.NotEqual(Older, t.LastChange);
        Assert.True(validations > 0);
    }
}
