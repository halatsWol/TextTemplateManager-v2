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

    [GeneratedRegex(@"Loc\.(Tour\.)?(Get|Format|Plural)\(""([^""]+)""")]
    private static partial Regex CodeKeyPattern();

    [GeneratedRegex(@"\{(\d+)(?:[,:][^}]*)?\}")]
    private static partial Regex PlaceholderPattern();

    private static List<string> Uids() =>
        SourceFiles("*.xaml").SelectMany(f => UidPattern().Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value)).ToList();

    // The keys the code asks for in the .resw file: Loc.Tour reads Tour.resw, Loc Resources.resw. A Loc.Plural key
    // stands for its two forms.
    private static List<string> CodeKeys(string file = "Resources.resw") =>
        SourceFiles("*.cs").SelectMany(f => CodeKeyPattern().Matches(File.ReadAllText(f)))
            .Where(m => (m.Groups[1].Success ? "Tour.resw" : "Resources.resw") == file)
            .SelectMany(m => m.Groups[2].Value == "Plural"
                ? new[] { m.Groups[3].Value + "_One", m.Groups[3].Value + "_Other" }
                : new[] { m.Groups[3].Value })
            .ToList();

    public static TheoryData<string> EnglishFiles => new("Resources.resw", "Tour.resw");

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

    [Fact]
    public void Uids_are_unique()
    {
        Assert.Empty(Uids().GroupBy(u => u).Where(g => g.Count() > 1).Select(g => g.Key));
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
