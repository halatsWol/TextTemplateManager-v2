using System;
using System.Collections.Generic;
using System.Linq;

namespace TextTemplateManager.Services.Translation;

/// <summary>A language as the editor's Translate menu lists it.</summary>
public sealed record LanguageChoice(string Code, string Name);

/// <summary>What the Translate menu offers with the installed models: the languages it can translate from and to.
/// Every pair goes to or from English, so a language is a source when its model to English is installed (English
/// itself when any model from English is), and a target the other way round; any source then reaches any target.</summary>
public sealed record TranslationChoices(IReadOnlyList<LanguageChoice> Sources, IReadOnlyList<LanguageChoice> Targets)
{
    public const string Detect = "detect";

    public static TranslationChoices From(IEnumerable<InstalledPair> installed)
    {
        var pairs = installed.Select(p => (p.From, p.To)).ToList();
        var sources = pairs.Where(p => p.To == "en").Select(p => p.From)
            .Concat(pairs.Any(p => p.From == "en") ? ["en"] : Array.Empty<string>());
        var targets = pairs.Where(p => p.From == "en").Select(p => p.To)
            .Concat(pairs.Any(p => p.To == "en") ? ["en"] : Array.Empty<string>());
        return new(Named(sources), Named(targets));
    }

    /// <summary>The From to show: the remembered one while it is still installed, else Detect.</summary>
    public string FromOrDefault(string? remembered) =>
        remembered != null && (remembered == Detect || Sources.Any(s => s.Code == remembered)) ? remembered : Detect;

    /// <summary>The To to show: the remembered one while it is still installed, else none (the editor then picks one
    /// that differs from the detected From).</summary>
    public string? ToOrDefault(string? remembered) => Targets.FirstOrDefault(t => t.Code == remembered)?.Code;

    private static IReadOnlyList<LanguageChoice> Named(IEnumerable<string> codes) =>
        codes.Distinct().Select(c => new LanguageChoice(c, c == "en" ? "English" : TranslationLanguage.NameOf(c)))
            .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
}
