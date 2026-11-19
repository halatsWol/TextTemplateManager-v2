using System;
using System.Collections.Generic;
using System.Linq;
using TextTemplateManager.Models;
using TextTemplateManager.Services.Translation;

namespace TextTemplateManager.Services.Platform;

/// <summary>A template's languages as the connector (protocol 3) serves and changes them: only the languages that are
/// pasted, the default first; codes limited to those a tab can be given in the app.</summary>
public static class ConnectorLanguages
{
    /// <summary>The code as stored when a tab can have it; null otherwise.</summary>
    public static string? Code(string? language) =>
        TemplateLanguages.NormalizeCode(language) is { } code && LanguageCatalog.All.Contains(code) ? code : null;

    /// <summary>The default's language, and all pasted languages when there is more than one.</summary>
    public static (string? Language, List<LanguageDto>? Languages) Describe(IReadOnlyList<TemplateLanguage> offered) =>
        (offered[0].Language, offered.Count > 1 ? offered.Select(o => new LanguageDto(o.Id!, o.Language!)).ToList() : null);

    /// <summary>The pasted tab with <paramref name="languageId"/> or <paramref name="language"/>; the default for
    /// neither. A refusal (404) when the template doesn't paste it.</summary>
    public static TemplateLanguage Find(IReadOnlyList<TemplateLanguage> offered, string? language, string? languageId)
    {
        TemplateLanguage? found = languageId != null ? offered.FirstOrDefault(o => o.Id == languageId)
            : language != null ? offered.FirstOrDefault(o => string.Equals(o.Language, TemplateLanguages.NormalizeCode(language), StringComparison.OrdinalIgnoreCase))
            : offered[0];
        return found ?? throw new ConnectorRefusal(404, "language not found");
    }

    /// <summary>Gives a new template its languages: <paramref name="language"/> for its content, plus a tab per entry
    /// of <paramref name="languages"/>. A refusal (400), with the template unchanged, for an unknown or repeated
    /// language, an entry without content, or other languages without the default's.</summary>
    public static void ApplyNew(Template t, string? language, IReadOnlyList<NewLanguageDto>? languages)
    {
        languages ??= [];
        if (language == null && languages.Count == 0) return;
        string code = language == null ? throw new ConnectorRefusal(400, "languages need language")
            : Code(language) ?? throw new ConnectorRefusal(400, "unknown language");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { code };
        var tabs = new List<(string Code, string Content)>();
        foreach (var entry in languages)
        {
            string alt = Code(entry.Language) ?? throw new ConnectorRefusal(400, "unknown language");
            if (!seen.Add(alt)) throw new ConnectorRefusal(400, "duplicate language");
            if (string.IsNullOrEmpty(entry.Content)) throw new ConnectorRefusal(400, "missing content");
            tabs.Add((alt, entry.Content));
        }
        t.Language = code;
        foreach (var (alt, content) in tabs) TemplateTabs.SetContent(t, TemplateTabs.Add(t, alt), content);
    }

    /// <summary>Puts <paramref name="content"/> on the template's tab <paramref name="languageId"/>, or on its pasted
    /// tab in <paramref name="language"/>, adding that tab when there is none. Never the default's text: a refusal
    /// (409) for it, as for a template whose default has no language yet; 404 for an unknown tab, 400 for an
    /// unknown language.</summary>
    public static LanguageWriteDto Write(Template t, string? language, string? languageId, string content)
    {
        if (languageId != null)
        {
            int at = TemplateTabs.IndexOf(t, languageId);
            if (at < 0) throw new ConnectorRefusal(404, "language not found");
            if (at == 0) throw new ConnectorRefusal(409, "default language");
            TemplateTabs.SetContent(t, languageId, content);
            return new LanguageWriteDto(t.Id.ToString(), languageId, TemplateTabs.All(t)[at].Language ?? "", false);
        }

        string code = Code(language) ?? throw new ConnectorRefusal(400, "unknown language");
        if (string.Equals(t.Language, code, StringComparison.OrdinalIgnoreCase)) throw new ConnectorRefusal(409, "default language");
        if (TemplateLanguages.Offered(t).Skip(1).FirstOrDefault(o => string.Equals(o.Language, code, StringComparison.OrdinalIgnoreCase)) is { Id: { } existing })
        {
            TemplateTabs.SetContent(t, existing, content);
            return new LanguageWriteDto(t.Id.ToString(), existing, code, false);
        }
        if (!TemplateTabs.CanAdd(t)) throw new ConnectorRefusal(409, "no default language");
        string id = TemplateTabs.Add(t, code);
        TemplateTabs.SetContent(t, id, content);
        return new LanguageWriteDto(t.Id.ToString(), id, code, true);
    }
}
