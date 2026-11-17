using System;
using System.Collections.Generic;
using System.Linq;

namespace TextTemplateManager.Models;

/// <summary>A template's language tabs as the editor shows them, first tab (Content, Language, LanguageId) first. A
/// tab is found by its id; null finds the first tab, whose id stays null while it is the only one. Every change writes
/// the template's fields back in their stored form, so the store sees an ordinary edit.</summary>
public static class TemplateTabs
{
    /// <summary>All tabs in order, the first one built from the template's own fields.</summary>
    public static List<TemplateLanguage> All(Template t) =>
        [new() { Id = t.LanguageId, Language = t.Language, Content = t.Content }, .. t.AltLanguages ?? []];

    /// <summary>The position of the tab with <paramref name="id"/>; -1 when there is none.</summary>
    public static int IndexOf(Template t, string? id) => id == null || id == t.LanguageId ? 0
        : t.AltLanguages?.ToList().FindIndex(a => a.Id == id) is >= 0 and var i ? i + 1 : -1;

    /// <summary>Whether a tab can be added: the first tab needs a language before a second one can come.</summary>
    public static bool CanAdd(Template t) => t.Language != null;

    /// <summary>Adds a tab with no text yet behind the others, in <paramref name="language"/> or without one until
    /// it gets one (not pasted meanwhile); its id.</summary>
    public static string Add(Template t, string? language = null)
    {
        if (!CanAdd(t)) throw new InvalidOperationException("The first tab has no language.");
        var tabs = All(t);
        var taken = new HashSet<string>(tabs.Select(x => x.Id).OfType<string>());
        if (tabs[0].Id == null) tabs[0] = tabs[0] with { Id = TemplateLanguages.NewId(taken) };
        string id = TemplateLanguages.NewId(taken);
        tabs.Add(new() { Id = id, Language = TemplateLanguages.NormalizeCode(language) });
        Apply(t, tabs);
        return id;
    }

    /// <summary>Gives a tab another language. The first tab's language can be changed but not removed.</summary>
    public static void SetLanguage(Template t, string? id, string? language)
    {
        int at = IndexOf(t, id);
        if (at < 0) return;
        language = TemplateLanguages.NormalizeCode(language);
        if (at == 0 && language == null && t.Language != null) return;
        var tabs = All(t);
        tabs[at] = tabs[at] with { Language = language };
        Apply(t, tabs);
    }

    /// <summary>The text of a tab; the first tab's for an id that isn't there (any more).</summary>
    public static string ContentOf(Template t, string? id) => t.ContentFor(id);

    public static void SetContent(Template t, string? id, string content)
    {
        int at = IndexOf(t, id);
        if (at < 0) return;
        if (at == 0) { if (t.Content != content) t.Content = content; return; }
        var tabs = All(t);
        if (tabs[at].Content == content) return;
        tabs[at] = tabs[at] with { Content = content };
        Apply(t, tabs);
    }

    /// <summary>Whether the tab can move to <paramref name="to"/>: the tab that ends up first needs a language.</summary>
    public static bool CanMove(Template t, string? id, int to)
    {
        int at = IndexOf(t, id);
        var tabs = All(t);
        if (at < 0 || to < 0 || to >= tabs.Count || to == at) return false;
        var moved = Moved(tabs, at, to);
        return moved[0].Language != null;
    }

    /// <summary>Moves a tab to <paramref name="to"/>; to 0 it becomes the first tab, the one pasted by default.</summary>
    public static void Move(Template t, string? id, int to)
    {
        if (!CanMove(t, id, to)) return;
        Apply(t, Moved(All(t), IndexOf(t, id), to));
    }

    /// <summary>Puts the tabs in the order of <paramref name="ids"/> (all of the template's tab ids). False, with
    /// nothing changed, when they don't match the tabs or the new first tab has no language.</summary>
    public static bool Reorder(Template t, IReadOnlyList<string?> ids)
    {
        var tabs = All(t);
        if (ids.Count != tabs.Count) return false;
        var ordered = new List<TemplateLanguage>();
        var used = new HashSet<int>();
        foreach (var id in ids)
        {
            int at = IndexOf(t, id);
            if (at < 0 || !used.Add(at)) return false;
            ordered.Add(tabs[at]);
        }
        if (ordered[0].Language == null) return false;
        Apply(t, ordered);
        return true;
    }

    /// <summary>Whether the tab can be deleted: never the only one, and the tab that becomes first needs a language.</summary>
    public static bool CanRemove(Template t, string? id)
    {
        int at = IndexOf(t, id);
        var tabs = All(t);
        if (at < 0 || tabs.Count < 2) return false;
        tabs.RemoveAt(at);
        return tabs.Count == 1 || tabs[0].Language != null;
    }

    public static void Remove(Template t, string? id)
    {
        if (!CanRemove(t, id)) return;
        var tabs = All(t);
        tabs.RemoveAt(IndexOf(t, id));
        Apply(t, tabs);
    }

    /// <summary>Writes <paramref name="tabs"/> (first tab first) to the template, leaving fields that don't change
    /// untouched. A single tab keeps its language but drops its id.</summary>
    public static void Apply(Template t, IReadOnlyList<TemplateLanguage> tabs)
    {
        var first = tabs[0];
        var alts = tabs.Skip(1).ToList();
        string? languageId = alts.Count == 0 ? null : first.Id;
        if (t.Content != first.Content) t.Content = first.Content;
        if (t.Language != first.Language) t.Language = first.Language;
        if (t.LanguageId != languageId) t.LanguageId = languageId;
        if (!(t.AltLanguages ?? []).SequenceEqual(alts)) t.AltLanguages = alts;
    }

    private static List<TemplateLanguage> Moved(List<TemplateLanguage> tabs, int from, int to)
    {
        var moved = tabs.ToList();
        var tab = moved[from];
        moved.RemoveAt(from);
        moved.Insert(to, tab);
        return moved;
    }
}
