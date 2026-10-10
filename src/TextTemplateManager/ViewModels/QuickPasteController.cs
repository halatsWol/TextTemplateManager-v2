using System;
using System.Collections.Generic;
using System.Linq;
using TextTemplateManager.Models;

namespace TextTemplateManager.ViewModels;

public enum ShortcutTab { Single, Multi }

/// <summary>A paste Quick Paste asks for: the template, and whether to paste it as plain text.</summary>
public sealed record PasteRequest(Template Template, bool PlainText);

/// <summary>Quick Paste's shortcut entry, without the window: the two shortcut lists and their highlighted
/// row, the active tab, and the multi-key entry typed while Alt is held ('-' and '.' separate a sync prefix,
/// a trailing '_' pastes as plain text). The window feeds it keys and shows its state.</summary>
public sealed class QuickPasteController(
    Func<IReadOnlyList<ShortcutValidator.Entry>> entries,
    Func<IReadOnlyList<SyncSource>> sources,
    Func<string, Template?> resolveSingleKey)
{
    private string _search = "";
    private ShortcutTab? _tabBeforeAlt;
    private bool _shortcutMode = true;

    public string Buffer { get; private set; } = "";
    public bool AltHeld { get; private set; }
    public ShortcutTab Tab { get; private set; } = ShortcutTab.Single;
    public IReadOnlyList<ShortcutRow> SingleRows { get; private set; } = Array.Empty<ShortcutRow>();
    public IReadOnlyList<ShortcutRow> MultiRows { get; private set; } = Array.Empty<ShortcutRow>();
    public int SingleIndex { get; private set; } = -1;
    public int MultiIndex { get; private set; } = -1;

    /// <summary>The typed multi key without the plain-text marker.</summary>
    public string BufferKey => Buffer.TrimEnd('_');
    public bool IsPlainText => Buffer.EndsWith('_');

    /// <summary>Whether the typed multi key begins some shortcut (shown in red when it doesn't).</summary>
    public bool BufferMatches => IsMultiKeyPrefix(BufferKey);

    /// <summary>Starts a fresh session: nothing typed, single-key tab, no search.</summary>
    public void Open()
    {
        Buffer = "";
        AltHeld = false;
        _tabBeforeAlt = null;
        Tab = ShortcutTab.Single;
        SetSearch("");
    }

    /// <summary>The search text filters both lists by title (not while a multi key is being typed).</summary>
    public void SetSearch(string text)
    {
        _search = text;
        if (Buffer.Length > 0) return;
        RefreshMulti();
        RefreshSingle();
    }

    /// <summary>Keyboard focus is in shortcut mode (not the search box or the tree). Only then does the
    /// visible list show a highlighted row, so it never looks live while typing in search.</summary>
    public void SetShortcutMode(bool active)
    {
        _shortcutMode = active;
        SyncHighlight();
    }

    /// <summary>The user picked a tab.</summary>
    public void SelectTab(ShortcutTab tab)
    {
        if (Tab == tab) return;
        Tab = tab;
        SyncHighlight();
    }

    /// <summary>The user clicked a row.</summary>
    public void Select(ShortcutTab tab, int index)
    {
        if (tab == ShortcutTab.Single) SingleIndex = index; else MultiIndex = index;
    }

    /// <summary>Arrow up/down on the visible list, wrapping at both ends.</summary>
    public void MoveHighlight(int delta) => MoveHighlight(Tab, delta);

    private void MoveHighlight(ShortcutTab tab, int delta)
    {
        bool single = tab == ShortcutTab.Single;
        int next = NextIndex(single ? SingleIndex : MultiIndex, delta, (single ? SingleRows : MultiRows).Count);
        if (next < 0) return;
        if (single) SingleIndex = next; else MultiIndex = next;
    }

    /// <summary>Enter in shortcut mode: the highlighted row of the visible list.</summary>
    public PasteRequest? CommitHighlighted()
    {
        var (rows, index) = Tab == ShortcutTab.Single ? (SingleRows, SingleIndex) : (MultiRows, MultiIndex);
        return index >= 0 && index < rows.Count ? new PasteRequest(rows[index].Template, false) : null;
    }

    /// <summary>A letter or digit in shortcut mode: the template with that single key (local first), or
    /// null when none has it.</summary>
    public PasteRequest? SingleKey(char key) =>
        resolveSingleKey(key.ToString()) is { } template ? new PasteRequest(template, false) : null;

    /// <summary>Alt went down: show the multi-key list with its top row highlighted.</summary>
    public void AltDown()
    {
        AltHeld = true;
        _tabBeforeAlt ??= Tab;
        SelectTab(ShortcutTab.Multi);
        MultiIndex = MultiRows.Count > 0 ? 0 : -1;
    }

    /// <summary>Arrow up/down while Alt is held: always the multi-key list, the typed key stays.</summary>
    public void MoveMultiHighlight(int delta) => MoveHighlight(ShortcutTab.Multi, delta);

    /// <summary>A key typed while Alt is held. Returns false when the key is refused (it would no longer
    /// begin any shortcut, or '_' with nothing typed yet); nothing is typed after the plain-text marker.</summary>
    public bool TypeMulti(char c)
    {
        if (IsPlainText) return true;
        string candidate = Buffer + c;
        bool accepted = c == '_' ? Buffer.Length > 0 : IsMultiKeyPrefix(candidate);
        if (!accepted) return false;
        Buffer = candidate;
        FilterMultiByBuffer();
        return true;
    }

    public void Backspace()
    {
        if (Buffer.Length == 0) return;
        Buffer = Buffer[..^1];
        FilterMultiByBuffer();
    }

    /// <summary>Enter while Alt is held (with or without anything typed), or the Alt release (only with
    /// something typed): the highlighted multi-key row.</summary>
    public PasteRequest? CommitMulti(bool requireBuffer)
    {
        if (requireBuffer && Buffer.Length == 0) return null;
        if (MultiIndex < 0 || MultiIndex >= MultiRows.Count) return null;
        return new PasteRequest(MultiRows[MultiIndex].Template, IsPlainText);
    }

    /// <summary>Alt released: pastes what was typed, or else puts the previous tab back and clears the entry.</summary>
    public PasteRequest? AltUp()
    {
        AltHeld = false;
        if (CommitMulti(requireBuffer: true) is { } paste) return paste;
        RestoreTab();
        Buffer = "";
        RefreshMulti();
        return null;
    }

    /// <summary>Drops the typed multi key. With <paramref name="altReleased"/> (the window lost focus, so the
    /// Alt release never arrives) the Alt state and the tab are reset too.</summary>
    public void CancelEntry(bool altReleased)
    {
        if (altReleased)
        {
            AltHeld = false;
            RestoreTab();
        }
        if (Buffer.Length == 0) return;
        Buffer = "";
        RefreshMulti();
    }

    /// <summary>Where an arrow-key step lands, wrapping at both ends: past the last item goes back to
    /// the first, before the first goes to the last. With nothing selected yet, Down starts at the top
    /// and Up at the bottom. Returns -1 when there is nothing to select.</summary>
    internal static int NextIndex(int current, int delta, int count)
    {
        if (count <= 0) return -1;
        if (current < 0) return delta > 0 ? 0 : count - 1;
        return ((current + delta) % count + count) % count;   // stays in range for a negative delta
    }

    private void RestoreTab()
    {
        if (_tabBeforeAlt is not { } tab) return;
        _tabBeforeAlt = null;
        SelectTab(tab);
    }

    private void SyncHighlight()
    {
        if (_shortcutMode)
        {
            if (Tab == ShortcutTab.Single && SingleIndex < 0 && SingleRows.Count > 0) SingleIndex = 0;
            if (Tab == ShortcutTab.Multi && MultiIndex < 0 && MultiRows.Count > 0) MultiIndex = 0;
        }
        else
        {
            SingleIndex = -1;
            MultiIndex = -1;
        }
    }

    private List<ShortcutRow> Rows(bool multiKey) => ShortcutRow.For(entries(), sources(), multiKey);

    private List<ShortcutRow> FilterByTitle(List<ShortcutRow> rows) =>
        string.IsNullOrWhiteSpace(_search)
            ? rows
            : rows.Where(r => r.Title.Contains(_search, StringComparison.OrdinalIgnoreCase)).ToList();

    // Armed only while a multi key is typed under Alt.
    private void RefreshMulti()
    {
        MultiRows = FilterByTitle(Rows(multiKey: true));
        MultiIndex = -1;
    }

    // The first row is highlighted only in shortcut mode.
    private void RefreshSingle()
    {
        SingleRows = FilterByTitle(Rows(multiKey: false));
        SingleIndex = _shortcutMode && SingleRows.Count > 0 ? 0 : -1;
    }

    // While typing, the list shows the shortcuts that begin with the typed key, top one highlighted (what
    // Enter or the Alt release commits).
    private void FilterMultiByBuffer()
    {
        var rows = Rows(multiKey: true);
        MultiRows = BufferKey.Length == 0
            ? rows
            : rows.Where(r => r.Key.StartsWith(BufferKey, StringComparison.OrdinalIgnoreCase)).ToList();
        MultiIndex = MultiRows.Count > 0 ? 0 : -1;
    }

    private bool IsMultiKeyPrefix(string prefix) =>
        prefix.Length == 0 || entries().Any(e =>
            !string.IsNullOrEmpty(e.Template.MultiKeyShortcut) &&
            e.EffectiveMultiKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
}
