using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using TextTemplateManager.Helpers;
using Xunit;

namespace TextTemplateManager.Tests;

/// <summary>The UI text in Strings\&lt;language&gt;\*.resw: every key the XAML (x:Uid) and the code (Loc.Get/Format)
/// asks for exists in en-US, en-US carries nothing unused, and every other language has the same keys with the same
/// placeholders, so a missing translation fails here instead of showing an empty label.</summary>
public partial class LocalizationTests
{
    private static string AppDir([CallerFilePath] string self = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(self)!, "..", "..", "src", "TextTemplateManager"));

    private static string StringsDir => Path.Combine(AppDir(), "Strings");

    private static Dictionary<string, string> Read(string resw) =>
        XDocument.Load(resw).Root!.Elements("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? "");

    private static Dictionary<string, string> English(string file = "Resources.resw") =>
        Read(Path.Combine(StringsDir, "en-US", file));

    private static IEnumerable<string> SourceFiles(string pattern) =>
        Directory.EnumerateFiles(AppDir(), pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    [GeneratedRegex(@"x:Uid=""([^""]+)""")]
    private static partial Regex UidPattern();

    [GeneratedRegex(@"Loc\.(?:(Tour|Editor)\.)?(Get|Format|Plural)\(""([^""]+)""")]
    private static partial Regex CodeKeyPattern();

    // The editor page's own lookups (src/editor/src/main.js): t('Key', ...) and tp('Key', n, ...).
    [GeneratedRegex(@"(?<![\w.])(t|tp)\('([^']+)'")]
    private static partial Regex ScriptKeyPattern();

    private static string EditorSourceDir => Path.GetFullPath(Path.Combine(AppDir(), "..", "editor", "src"));

    [GeneratedRegex(@"\{(\d+)(?:[,:][^}]*)?\}")]
    private static partial Regex PlaceholderPattern();

    private static List<string> Uids() =>
        SourceFiles("*.xaml").SelectMany(f => UidPattern().Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value)).ToList();

    // The keys the code asks for in the .resw file: Loc.Tour reads Tour.resw, Loc.Editor and the editor page's script
    // Editor.resw, Loc Resources.resw. A plural key (Loc.Plural, tp) stands for its two forms.
    private static List<string> CodeKeys(string file = "Resources.resw")
    {
        var keys = SourceFiles("*.cs").SelectMany(f => CodeKeyPattern().Matches(File.ReadAllText(f)))
            .Where(m => (m.Groups[1].Success ? m.Groups[1].Value + ".resw" : "Resources.resw") == file)
            .SelectMany(m => Forms(m.Groups[3].Value, m.Groups[2].Value == "Plural"));
        if (file == "Editor.resw")
            keys = keys.Concat(Directory.EnumerateFiles(EditorSourceDir, "*.js", SearchOption.AllDirectories)
                .SelectMany(f => ScriptKeyPattern().Matches(File.ReadAllText(f)))
                .SelectMany(m => Forms(m.Groups[2].Value, m.Groups[1].Value == "tp")));
        return keys.ToList();
    }

    private static string[] Forms(string key, bool plural) => plural ? [key + "_One", key + "_Other"] : [key];

    public static TheoryData<string> EnglishFiles => new("Resources.resw", "Tour.resw", "Editor.resw");

    private static string[] Placeholders(string value) =>
        PlaceholderPattern().Matches(value).Select(m => m.Groups[1].Value).Distinct().Order().ToArray();

    [Fact]
    public void Loc_reads_the_english_text_in_a_test_run()
    {
        Assert.Equal(English()["Support_MailHint"].Replace("{0}", "a@b.c"), Loc.Format("Support_MailHint", "a@b.c"));
    }

    [Fact]
    public void Plural_picks_the_form_by_count()
    {
        var english = English();
        Assert.Equal(english["Main_DeleteSelection_One"].Replace("{0}", "1"), Loc.Plural("Main_DeleteSelection", 1));
        Assert.Equal(english["Main_DeleteSelection_Other"].Replace("{0}", "3"), Loc.Plural("Main_DeleteSelection", 3));
        Assert.Equal(english["Main_DropInto_Other"].Replace("{0}", "2").Replace("{1}", "Team"), Loc.Plural("Main_DropInto", 2, "Team"));
    }

    [Fact]
    public void Every_x_Uid_has_english_text()
    {
        var keys = English().Keys;
        var missing = Uids().Where(uid => !keys.Any(k => k.StartsWith(uid + ".", StringComparison.Ordinal))).ToList();
        Assert.Empty(missing);
    }

    [Fact]
    public void Loc_Tour_reads_the_tour_file()
    {
        Assert.Equal(English("Tour.resw")["StepWelcome_Title"], Loc.Tour.Get("StepWelcome_Title"));
    }

    [Fact]
    public void The_editor_page_gets_its_text_title_and_language_before_its_scripts()
    {
        var english = English("Editor.resw");
        Assert.Equal(english.Count, Loc.Editor.All().Count);
        Assert.Equal(english["Undo"], Loc.Editor.All()["Undo"]);

        string html = EditorPages.Localized("<!doctype html><html lang=\"en\"><head><title>x</title></head><body><script>main()</script></body></html>",
            "A </script> & title");
        int strings = html.IndexOf("window.ttmStrings", StringComparison.Ordinal);
        Assert.True(strings > 0 && strings < html.IndexOf("main()", StringComparison.Ordinal));
        Assert.Contains("<title>A &lt;/script&gt; &amp; title</title>", html);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "</script>"), m => m.Index < html.IndexOf("<title>", StringComparison.Ordinal));
        Assert.Contains("<html lang=\"", html);
    }

    [Theory]
    [MemberData(nameof(EnglishFiles))]
    public void Every_key_the_code_asks_for_has_english_text(string file)
    {
        var keys = English(file).Keys.ToHashSet();
        Assert.Empty(CodeKeys(file).Where(k => !keys.Contains(k)).Distinct());
    }

    [Theory]
    [MemberData(nameof(EnglishFiles))]
    public void English_has_no_unused_keys(string file)
    {
        var uids = file == "Resources.resw" ? Uids().ToHashSet() : [];
        var code = CodeKeys(file).ToHashSet();
        var unused = English(file).Keys.Where(k => !code.Contains(k) && !(k.Contains('.') && uids.Contains(k[..k.IndexOf('.')])));
        Assert.Empty(unused);
    }

    [GeneratedRegex(@"\s(Text|Content|Header|PlaceholderText|Message|Title|Subtitle|Description|OnContent|OffContent|PrimaryButtonText|SecondaryButtonText|CloseButtonText|ToolTipService\.ToolTip|AutomationProperties\.Name|AutomationProperties\.HelpText)=""([^""{][^""]*)""")]
    private static partial Regex TextAttributePattern();

    // Names that stay as they are in every language.
    private static readonly HashSet<string> Untranslated =
        ["TextTemplateManager", "Marflow Software", "Chrome", "Edge", "Firefox", "HTML/Jira", "HTML", "RTF", "Markdown"];

    [Fact]
    public void Xaml_has_no_literal_ui_text()
    {
        var literal = SourceFiles("*.xaml")
            .SelectMany(f => TextAttributePattern().Matches(File.ReadAllText(f))
                .Select(m => (File: Path.GetFileName(f), Attribute: m.Groups[1].Value, Value: m.Groups[2].Value)))
            .Where(a => a.Value.Any(char.IsLetter) && !a.Value.StartsWith("&#x") && !Untranslated.Contains(a.Value))
            .Select(a => $"{a.File}: {a.Attribute}=\"{a.Value}\"");
        Assert.Empty(literal);
    }

    [Fact]
    public void The_language_switch_offers_exactly_the_shipped_languages()
    {
        var folders = Directory.EnumerateDirectories(StringsDir).Select(Path.GetFileName).Order(StringComparer.OrdinalIgnoreCase);
        Assert.Equal(folders, UiLanguages.Available.Order(StringComparer.OrdinalIgnoreCase));
        Assert.DoesNotContain(UiLanguages.Pseudo, UiLanguages.Available);
    }

    [Fact]
    public void The_language_choice_is_kept_in_the_data_folder_and_the_windows_language_leaves_no_file()
    {
        string dir = TestEnvironment.NewScratchDir();
        Assert.Equal("", UiLanguages.Read(dir));
        UiLanguages.Write(dir, "en-US");
        Assert.Equal("en-US", UiLanguages.Read(dir));
        UiLanguages.Write(dir, "");
        Assert.Equal("", UiLanguages.Read(dir));
        Assert.Empty(Directory.EnumerateFileSystemEntries(dir));
    }

    [Theory]
    [InlineData("de-DE", "de-DE")]
    [InlineData("de-AT", "de-DE")]
    [InlineData("hu-HU", "hu-HU")]
    [InlineData("it-CH", "it-IT")]
    [InlineData("es-ES", "es-ES")]
    [InlineData("es-MX", "es-ES")]
    [InlineData("es-419", "es-ES")]
    [InlineData("en-US,hu-HU", "en-US")]
    [InlineData("en-GB", "en-US")]
    [InlineData("fr-FR", "fr-FR")]
    [InlineData("fr-CA", "fr-FR")]
    [InlineData("fr-FR,de-CH", "fr-FR")]
    [InlineData("pl-PL,de-CH", "de-DE")]
    [InlineData("en-GB,de-DE", "en-US")]
    [InlineData("", "en-US")]
    public void The_windows_language_picks_the_first_shipped_match(string windows, string expected)
    {
        Assert.Equal(expected, UiLanguages.ForWindows(windows.Split(',', StringSplitOptions.RemoveEmptyEntries)));
    }

    [Theory]
    [InlineData("Ctrl+Shift+Space", "Strg+Umschalt+Leertaste")]
    [InlineData("Shift+Alt+Y", "Umschalt+Alt+Y")]
    [InlineData("Win+F12", "Win+F12")]
    [InlineData("None", "")]
    [InlineData("", "")]
    public void Hotkeys_show_the_key_names_of_the_ui_language(string stored, string german)
    {
        var de = Read(Path.Combine(StringsDir, "de-DE", "Resources.resw"));
        Assert.Equal(german, HotkeyText.For(stored, k => de.GetValueOrDefault("Key_" + k)));
        Assert.Equal(stored == "None" ? "" : stored, HotkeyText.For(stored));   // the test host reads en-US
    }

    [Theory]
    [InlineData("en-US", "hu-HU", "2026. október 30., péntek 20:04")]
    [InlineData("en-US", "de-DE", "Freitag, 30. Oktober 2026 20:04")]
    [InlineData("en-GB", "en-US", "Friday, 30 October 2026 20:04")]
    [InlineData("de-AT", "de-DE", "Freitag, 30. Jänner 2026 20:04")]
    public void Dates_take_the_app_language_unless_the_region_speaks_it(string region, string ui, string shown)
    {
        var culture = Loc.CultureFor(new System.Globalization.CultureInfo(region), new System.Globalization.CultureInfo(ui));
        var date = region == "de-AT" ? new DateTime(2026, 1, 30, 20, 4, 0) : new DateTime(2026, 10, 30, 20, 4, 0);
        Assert.Equal(shown, date.ToString("f", culture));
    }

    [Fact]
    public void Numbers_keep_the_region_when_dates_take_the_app_language()
    {
        var culture = Loc.CultureFor(new System.Globalization.CultureInfo("de-DE"), new System.Globalization.CultureInfo("en-US"));
        Assert.Equal("1,5", 1.5.ToString(culture));
        Assert.Equal("Friday, October 30, 2026", new DateTime(2026, 10, 30).ToString("D", culture));
    }

    [Fact]
    public void Language_names_follow_the_ui_language()
    {
        var before = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            System.Globalization.CultureInfo.CurrentUICulture = new("de-DE");
            Assert.Equal("Französisch", Services.Translation.TranslationLanguage.NameOf("fr"));
            Assert.Equal("Englisch", Services.Translation.TranslationLanguage.NameOf("en"));
            System.Globalization.CultureInfo.CurrentUICulture = new("hu-HU");
            Assert.Equal("Francia", Services.Translation.TranslationLanguage.NameOf("fr"));
            System.Globalization.CultureInfo.CurrentUICulture = new("en-US");
            Assert.Equal("French", Services.Translation.TranslationLanguage.NameOf("fr"));
        }
        finally { System.Globalization.CultureInfo.CurrentUICulture = before; }
    }

    [Theory]
    [InlineData("en-US", "English")]
    [InlineData("de-DE", "Deutsch")]
    [InlineData("hu-HU", "Magyar")]
    [InlineData("it-IT", "Italiano")]
    [InlineData("fr-FR", "Français")]
    [InlineData("es-ES", "Español (España y Latinoamérica)")]
    public void Languages_are_named_in_their_own_language(string code, string name)
    {
        Assert.Equal(name, UiLanguages.NameOf(code));
    }

    [Fact]
    public void Uids_are_unique()
    {
        Assert.Empty(Uids().GroupBy(u => u).Where(g => g.Count() > 1).Select(g => g.Key));
    }

    [Fact]
    public void No_language_leaves_an_english_text_empty()
    {
        var empty = new List<string>();
        foreach (string file in Directory.EnumerateFiles(Path.Combine(StringsDir, "en-US"), "*.resw"))
        {
            var english = Read(file);
            foreach (string dir in Directory.EnumerateDirectories(StringsDir).Where(d => Path.GetFileName(d) != "en-US"))
            {
                string other = Path.Combine(dir, Path.GetFileName(file));
                if (!File.Exists(other)) continue;   // the key test reports a missing file
                var translated = Read(other);
                empty.AddRange(english.Where(e => e.Value.Trim().Length > 0
                        && translated.TryGetValue(e.Key, out var t) && t.Trim().Length == 0)
                    .Select(e => $"{Path.GetFileName(dir)}/{Path.GetFileName(file)}: {e.Key}"));
            }
        }
        Assert.Empty(empty);
    }

    [Fact]
    public void Every_language_has_the_english_keys_and_placeholders()
    {
        var problems = new List<string>();
        foreach (string file in Directory.EnumerateFiles(Path.Combine(StringsDir, "en-US"), "*.resw"))
        {
            var english = Read(file);
            foreach (string dir in Directory.EnumerateDirectories(StringsDir).Where(d => Path.GetFileName(d) != "en-US"))
            {
                string language = Path.GetFileName(dir);
                string other = Path.Combine(dir, Path.GetFileName(file));
                if (!File.Exists(other)) { problems.Add($"{language}: {Path.GetFileName(file)} missing"); continue; }
                var translated = Read(other);
                problems.AddRange(english.Keys.Except(translated.Keys).Select(k => $"{language}: {k} missing"));
                problems.AddRange(translated.Keys.Except(english.Keys).Select(k => $"{language}: {k} not in en-US"));
                problems.AddRange(english.Where(e => translated.TryGetValue(e.Key, out var t)
                        && !Placeholders(e.Value).SequenceEqual(Placeholders(t)))
                    .Select(e => $"{language}: {e.Key} placeholders differ"));
            }
        }
        Assert.Empty(problems);
    }
}
