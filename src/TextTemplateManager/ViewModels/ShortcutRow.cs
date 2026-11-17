using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using TextTemplateManager.Helpers;
using TextTemplateManager.Models;
using TextTemplateManager.Services.Translation;
using Windows.UI.Text;

namespace TextTemplateManager.ViewModels;

/// <summary>One row of a Quick Paste shortcut list: the template, its key as typed and where it comes from.</summary>
public sealed record ShortcutRow(Template Template, string Key, string Source, bool IsLocal)
{
    public string Title => Template.Title;

    /// <summary>The languages the row pastes and the one chosen (Left/Right in the multi-key list).</summary>
    public RowLanguages Languages { get; } = new(TemplateLanguages.Offered(Template));

    public FontStyle SourceStyle => IsLocal ? FontStyle.Italic : FontStyle.Normal;

    // What a screen reader says for the row; a record would print all its members.
    public override string ToString() => Languages.Count > 1
        ? Loc.Format("QuickPaste_RowNameLanguages", Title, Key, Source, Languages.Names)
        : Loc.Format("QuickPaste_RowName", Title, Key, Source);

    /// <summary>The rows for every template with a single key (or a multi key, with the sync prefix),
    /// local first, then in sync-source order, as a single-key press resolves them.</summary>
    public static List<ShortcutRow> For(IEnumerable<ShortcutValidator.Entry> entries, IReadOnlyList<SyncSource> sources, bool multiKey)
    {
        var active = sources.Where(s => s.IsActive).ToList();
        int Priority(Guid area)
        {
            if (area == Guid.Empty) return 0;
            int index = active.FindIndex(s => s.Id == area);
            return index < 0 ? int.MaxValue : index + 1;
        }

        return entries
            .Where(e => multiKey
                ? !string.IsNullOrEmpty(e.Template.MultiKeyShortcut)
                : !string.IsNullOrWhiteSpace(e.Template.SingleKeyShortcut))
            .OrderBy(e => Priority(e.Area))
            .Select(e => new ShortcutRow(
                e.Template,
                multiKey ? e.EffectiveMultiKey : e.Template.SingleKeyShortcut,
                sources.FirstOrDefault(s => s.Id == e.Area)?.Name ?? Loc.Get("QuickPaste_LocalSource"),
                e.Area == Guid.Empty))
            .ToList();
    }
}

/// <summary>The languages a Quick Paste row pastes, the default first, and the one chosen for the next paste.</summary>
public sealed class RowLanguages(IReadOnlyList<TemplateLanguage> offered) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public int Count => offered.Count;
    public int Index { get; private set; }
    public TemplateLanguage Chosen => offered[Index];

    /// <summary>The chosen language's name; empty for a template in one language.</summary>
    public string Text => Count > 1 ? $"\u2039 {NameOf(Chosen)} \u203A" : "";

    public string ChosenName => NameOf(Chosen);

    public string Names => string.Join(", ", offered.Select(NameOf));

    /// <summary>The next or previous language, wrapping at both ends. False for a template in one language.</summary>
    public bool Step(int delta)
    {
        if (Count < 2) return false;
        Index = ((Index + delta) % Count + Count) % Count;
        Changed();
        return true;
    }

    public void Reset()
    {
        if (Index == 0) return;
        Index = 0;
        Changed();
    }

    private void Changed()
    {
        PropertyChanged?.Invoke(this, new(nameof(Index)));
        PropertyChanged?.Invoke(this, new(nameof(Text)));
    }

    private static string NameOf(TemplateLanguage tab) =>
        tab.Language is { } code ? TranslationLanguage.NameOf(code) : Loc.Get("LanguageTabs_NoLanguage");
}
