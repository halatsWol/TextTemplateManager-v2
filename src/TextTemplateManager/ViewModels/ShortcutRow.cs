using System;
using System.Collections.Generic;
using System.Linq;
using TextTemplateManager.Models;
using Windows.UI.Text;

namespace TextTemplateManager.ViewModels;

/// <summary>One row of a Quick Paste shortcut list: the template, its key as typed and where it comes from.</summary>
public sealed record ShortcutRow(Template Template, string Key, string Source, bool IsLocal)
{
    public string Title => Template.Title;

    public FontStyle SourceStyle => IsLocal ? FontStyle.Italic : FontStyle.Normal;

    // What a screen reader says for the row; a record would print all its members.
    public override string ToString() => $"{Title}, shortcut {Key}, {Source}";

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
                sources.FirstOrDefault(s => s.Id == e.Area)?.Name ?? "local",
                e.Area == Guid.Empty))
            .ToList();
    }
}
