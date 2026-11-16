using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace TextTemplateManager.Models;

/// <summary>One of a template's other tabs: its id (unique within the template), language (null while unset) and
/// content. Replaced as a whole, never changed in place, so an edit reaches the template's change tracking.</summary>
public sealed record TemplateLanguage
{
    public string? Id { get; init; }
    public string? Language { get; init; }
    public string Content { get; init; } = string.Empty;

    /// <summary>Fields of a newer version's file, written back behind Content.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? UnknownFields { get; set; }
}

/// <summary>The rules for a template's languages: tab ids, language codes, which tabs can be pasted.</summary>
public static class TemplateLanguages
{
    private static readonly Regex _id = new("^[0-9a-f]{4}$", RegexOptions.Compiled);
    private static readonly Dictionary<string, string> _legacy = new() { ["iw"] = "he", ["in"] = "id", ["ji"] = "yi", ["no"] = "nb" };

    /// <summary>A language code as stored: lowercase language, title-case script, uppercase region ("zh-Hant",
    /// "pt-BR"), legacy codes replaced ("iw" is "he"); null for nothing.</summary>
    public static string? NormalizeCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var parts = code.Trim().Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries);
        string language = parts[0].ToLowerInvariant();
        parts[0] = _legacy.GetValueOrDefault(language, language);
        for (int i = 1; i < parts.Length; i++)
            parts[i] = parts[i].Length == 4 ? char.ToUpperInvariant(parts[i][0]) + parts[i][1..].ToLowerInvariant()
                : parts[i].Length == 2 ? parts[i].ToUpperInvariant()
                : parts[i].ToLowerInvariant();
        return string.Join('-', parts);
    }

    /// <summary>A random tab id not in <paramref name="taken"/>, added to it.</summary>
    public static string NewId(ISet<string> taken)
    {
        string id;
        do id = Random.Shared.Next(0x10000).ToString("x4");
        while (!taken.Add(id));
        return id;
    }

    /// <summary>The tabs that can be pasted, first tab first: the first tab always, the others with a language that
    /// no earlier tab has.</summary>
    public static List<TemplateLanguage> Offered(Template template)
    {
        var offered = new List<TemplateLanguage> { new() { Id = template.LanguageId, Language = template.Language, Content = template.Content } };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (template.Language != null) seen.Add(template.Language);
        foreach (var alt in template.AltLanguages ?? [])
            if (alt.Language != null && seen.Add(alt.Language)) offered.Add(alt);
        return offered;
    }

    /// <summary>Whether two tabs have the same language.</summary>
    public static bool HasDuplicate(Template template)
    {
        if (template.AltLanguages == null) return false;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (template.Language != null) seen.Add(template.Language);
        return template.AltLanguages.Any(a => a.Language != null && !seen.Add(a.Language));
    }

    /// <summary>Puts a loaded tree's language fields in order: codes normalized, ids present and unique within each
    /// template, no first-tab id without other tabs. A file that was already in order is left exactly as it was.</summary>
    public static T? Repair<T>(T? root) where T : class
    {
        if (root is not BaseItem tree) return root;
        foreach (var t in tree.SelfAndDescendants().OfType<Template>())
        {
            string? language = NormalizeCode(t.Language);
            if (language != t.Language) t.Language = language;
            if (t.AltLanguages == null)
            {
                if (t.LanguageId != null) t.LanguageId = null;
                continue;
            }

            var taken = new HashSet<string>();
            if (t.LanguageId == null || !_id.IsMatch(t.LanguageId) || !taken.Add(t.LanguageId)) t.LanguageId = NewId(taken);
            bool changed = false;
            var alts = t.AltLanguages.Select(a =>
            {
                var fixedUp = a with
                {
                    Language = NormalizeCode(a.Language),
                    Id = a.Id != null && _id.IsMatch(a.Id) && taken.Add(a.Id) ? a.Id : NewId(taken),
                };
                changed |= fixedUp.Language != a.Language || fixedUp.Id != a.Id;
                return fixedUp;
            }).ToList();
            if (changed) t.AltLanguages = alts;
        }
        return root;
    }
}
