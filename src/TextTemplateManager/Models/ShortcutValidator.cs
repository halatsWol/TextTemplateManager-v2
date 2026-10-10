using System;
using System.Collections.Generic;
using System.Linq;

namespace TextTemplateManager.Models;

/// <summary>Flags shortcut clashes. The same key twice in one area (local, or one sync folder) is a
/// conflict that must be resolved; the same key in different areas is allowed (local wins, then sync
/// order) and only flagged as a warning. Multi keys are compared as typed, with the sync prefix.</summary>
public static class ShortcutValidator
{
    /// <summary>A template with its area (sync source id, <see cref="Guid.Empty"/> for local) and its
    /// multi key as typed.</summary>
    public readonly record struct Entry(Template Template, Guid Area, string EffectiveMultiKey);

    /// <summary>Sets the four conflict/warning flags on every template in <paramref name="entries"/>.</summary>
    public static void Apply(IReadOnlyCollection<Entry> entries)
    {
        foreach (var e in entries)
        {
            e.Template.HasSingleKeyConflict = false;
            e.Template.HasMultiKeyConflict = false;
            e.Template.HasSingleKeyCrossAreaWarning = false;
            e.Template.HasMultiKeyCrossAreaWarning = false;
        }

        foreach (var group in entries
            .Where(e => !string.IsNullOrWhiteSpace(e.Template.SingleKeyShortcut))
            .GroupBy(e => e.Template.SingleKeyShortcut.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            Flag(group, t => t.HasSingleKeyConflict = true, t => t.HasSingleKeyCrossAreaWarning = true);
        }

        foreach (var group in entries
            .Where(e => !string.IsNullOrWhiteSpace(e.Template.MultiKeyShortcut))
            .GroupBy(e => e.EffectiveMultiKey.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            Flag(group, t => t.HasMultiKeyConflict = true, t => t.HasMultiKeyCrossAreaWarning = true);
        }
    }

    // Within one key: a template sharing it with another in its own area is in conflict; otherwise, if the
    // key also appears in another area, it gets the warning.
    private static void Flag(IEnumerable<Entry> sameKey, Action<Template> conflict, Action<Template> warning)
    {
        var entries = sameKey.ToList();
        if (entries.Count < 2) return;
        bool severalAreas = entries.Select(e => e.Area).Distinct().Count() > 1;
        foreach (var e in entries)
        {
            if (entries.Count(o => o.Area == e.Area) > 1) conflict(e.Template);
            else if (severalAreas) warning(e.Template);
        }
    }
}
