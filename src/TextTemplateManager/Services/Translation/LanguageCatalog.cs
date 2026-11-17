using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TextTemplateManager.Models;

namespace TextTemplateManager.Services.Translation;

/// <summary>A language detected in a text; <paramref name="Sure"/> false for a guess from a short text, which is only
/// offered, never set on its own.</summary>
public sealed record LanguageGuess(string Code, bool Sure);

/// <summary>The languages a template tab can be given: every ISO 639-1 language Windows can name (Chinese as
/// zh-Hans and zh-Hant), with the suggested ones (the app's UI languages and the installed translator languages)
/// listed first.</summary>
public static class LanguageCatalog
{
    private static IReadOnlyList<string>? _all;

    /// <summary>All codes, as stored.</summary>
    public static IReadOnlyList<string> All => _all ??= Build();

    private static List<string> Build()
    {
        var codes = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var culture in CultureInfo.GetCultures(CultureTypes.NeutralCultures))
        {
            if (culture.Name.Length != 2 || culture.EnglishName.StartsWith("Unknown", StringComparison.OrdinalIgnoreCase)) continue;
            if (culture.Name == "zh") { codes.Add("zh-Hans"); codes.Add("zh-Hant"); continue; }
            codes.Add(TemplateLanguages.NormalizeCode(culture.Name)!);
        }
        return codes.ToList();
    }

    /// <summary>The suggested codes: the UI languages without their region, then the installed translator languages.</summary>
    public static IReadOnlyList<string> Suggested(IEnumerable<string> uiLanguages, IEnumerable<string> translatorLanguages) =>
        uiLanguages.Select(l => l.Split('-')[0]).Concat(translatorLanguages)
            .Select(TemplateLanguages.NormalizeCode).OfType<string>().Distinct().ToList();

    /// <summary>The languages a tab's text is detected among: the suggested ones and those the templates use.</summary>
    public static IReadOnlyList<string> DetectionCandidates(IEnumerable<string> suggested, IEnumerable<Template> templates) =>
        suggested.Concat(templates.SelectMany(t => TemplateTabs.All(t).Select(x => x.Language)).OfType<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary><see cref="Choices"/> with the language detected in the text, if any and if listed, taken out of the
    /// others to come first.</summary>
    public static (LanguageChoice? Detected, List<LanguageChoice> Suggested, List<LanguageChoice> Others) PickerChoices(
        string? detected, IEnumerable<string> suggested, IEnumerable<string?> taken, string? filter, Func<string, string>? nameOf = null)
    {
        var (s, o) = Choices(suggested, taken, filter, nameOf);
        var hit = detected == null ? null : s.Concat(o).FirstOrDefault(c => c.Code.Equals(detected, StringComparison.OrdinalIgnoreCase));
        return hit == null ? (null, s, o) : (hit, s.Where(c => c != hit).ToList(), o.Where(c => c != hit).ToList());
    }

    /// <summary>What the picker lists for <paramref name="filter"/>: the suggested languages and all others, each by
    /// name, without the ones in <paramref name="taken"/>. A filter matches a name's start or any word in it, or a
    /// code.</summary>
    public static (List<LanguageChoice> Suggested, List<LanguageChoice> Others) Choices(
        IEnumerable<string> suggested, IEnumerable<string?> taken, string? filter, Func<string, string>? nameOf = null)
    {
        nameOf ??= TranslationLanguage.NameOf;
        var excluded = new HashSet<string>(taken.OfType<string>(), StringComparer.OrdinalIgnoreCase);
        var suggestedSet = suggested.Where(c => !excluded.Contains(c)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var all = All.Concat(suggestedSet).Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(c => !excluded.Contains(c))
            .Select(c => new LanguageChoice(c, nameOf(c)))
            .Where(c => Matches(c, filter?.Trim()))
            .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        return (all.Where(c => suggestedSet.Contains(c.Code)).ToList(), all.Where(c => !suggestedSet.Contains(c.Code)).ToList());
    }

    private static bool Matches(LanguageChoice choice, string? filter)
    {
        if (string.IsNullOrEmpty(filter)) return true;
        if (choice.Code.Equals(filter, StringComparison.OrdinalIgnoreCase)) return true;
        return choice.Name.Split([' ', '(', ')', '-'], StringSplitOptions.RemoveEmptyEntries)
            .Any(word => word.StartsWith(filter, StringComparison.CurrentCultureIgnoreCase))
            || choice.Name.StartsWith(filter, StringComparison.CurrentCultureIgnoreCase);
    }
}
