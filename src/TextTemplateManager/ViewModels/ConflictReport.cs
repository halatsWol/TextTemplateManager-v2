using System;
using System.Collections.Generic;
using System.Linq;
using TextTemplateManager.Models;

namespace TextTemplateManager.ViewModels;

public enum ConflictLineKind { Heading, Key, Template, NoteHeading, Note }

public sealed record ConflictLine(ConflictLineKind Kind, string Text);

/// <summary>What the conflict panel lists: same-area conflicts (blocking, grouped by key) and, unless hidden
/// by the setting, cross-area notes (allowed, dismissible).</summary>
public sealed class ConflictReport
{
    public IReadOnlyList<ConflictLine> Lines { get; }

    /// <summary>Same-area conflicts are listed; they must be resolved, so the panel can't be dismissed.</summary>
    public bool HasConflicts { get; }

    public bool IsEmpty => Lines.Count == 0;

    /// <summary>Identifies the listed notes, so a dismissed set stays hidden until it changes.</summary>
    public string NotesSignature { get; }

    private ConflictReport(List<ConflictLine> lines, bool hasConflicts, string notesSignature)
    {
        Lines = lines;
        HasConflicts = hasConflicts;
        NotesSignature = notesSignature;
    }

    public static ConflictReport From(IEnumerable<Template> templates, bool hideNotes)
    {
        var all = templates.ToList();
        var single = all.Where(t => t.HasSingleKeyConflict).GroupBy(t => t.SingleKeyShortcut).ToList();
        var multi = all.Where(t => t.HasMultiKeyConflict).GroupBy(t => t.MultiKeyShortcut).ToList();
        var singleNotes = hideNotes ? new List<IGrouping<string, Template>>() : all.Where(t => t.HasSingleKeyCrossAreaWarning).GroupBy(t => t.SingleKeyShortcut).ToList();
        var multiNotes = hideNotes ? new List<IGrouping<string, Template>>() : all.Where(t => t.HasMultiKeyCrossAreaWarning).GroupBy(t => t.MultiKeyShortcut).ToList();

        var lines = new List<ConflictLine>();
        AddConflicts(lines, "Single-Key Conflicts", single);
        AddConflicts(lines, "Multi-Key Conflicts", multi);
        AddNotes(lines, "Note — same single key in different areas (allowed; local wins, then sync order):", singleNotes);
        AddNotes(lines, "Note — same multi-key in different areas (allowed; local wins, then sync order):", multiNotes);

        string signature = string.Join("|",
            singleNotes.SelectMany(g => g.Select(t => "S:" + g.Key + "~" + t.Title))
                .Concat(multiNotes.SelectMany(g => g.Select(t => "M:" + g.Key + "~" + t.Title)))
                .OrderBy(s => s, StringComparer.Ordinal));

        return new ConflictReport(lines, single.Count > 0 || multi.Count > 0, signature);
    }

    private static void AddConflicts(List<ConflictLine> lines, string heading, List<IGrouping<string, Template>> groups)
    {
        if (groups.Count == 0) return;
        lines.Add(new(ConflictLineKind.Heading, heading));
        foreach (var g in groups)
        {
            lines.Add(new(ConflictLineKind.Key, $"Shortcut: {g.Key}"));
            lines.AddRange(g.Select(t => new ConflictLine(ConflictLineKind.Template, $"- {t.Title}")));
        }
    }

    private static void AddNotes(List<ConflictLine> lines, string heading, List<IGrouping<string, Template>> groups)
    {
        if (groups.Count == 0) return;
        lines.Add(new(ConflictLineKind.NoteHeading, heading));
        foreach (var g in groups)
            lines.AddRange(g.Select(t => new ConflictLine(ConflictLineKind.Note, $"- {g.Key}: {t.Title}")));
    }
}

/// <summary>Whether the conflict panel shows a report. Notes alone can be dismissed and stay hidden until
/// the note set changes; a conflict always shows and forgets the dismissal.</summary>
public sealed class ConflictNotice
{
    private string? _dismissed;
    private string _current = "";

    public bool CanDismiss { get; private set; }

    public bool ShouldShow(ConflictReport report)
    {
        _current = report.NotesSignature;
        CanDismiss = !report.IsEmpty && !report.HasConflicts;
        if (report.IsEmpty || report.HasConflicts) _dismissed = null;
        if (report.IsEmpty) return false;
        return !(CanDismiss && _dismissed == _current);
    }

    public void Dismiss() => _dismissed = _current;
}
