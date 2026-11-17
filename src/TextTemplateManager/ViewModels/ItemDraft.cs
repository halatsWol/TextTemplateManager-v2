using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using TextTemplateManager.Models;

namespace TextTemplateManager.ViewModels;

/// <summary>The selected item's edits while autosave is off: a copy the right pane edits, so nothing reaches the
/// tree (and no save of anything else writes it) until <see cref="Commit"/>. Only the fields the pane edits are
/// copied: the title, and a template's language tabs, tags, shortcuts and default paste mode.</summary>
public sealed class ItemDraft
{
    // Each tab's text as the editor shows it after loading, by tab key: its HTML can differ from the stored text
    // (normalised) without any edit, which must not count as one.
    private Dictionary<string, string> _baselines = new();

    // The only tab has no id in the file; keyed alike whether it has one or not.
    private const string OnlyTab = "";

    private ItemDraft(BaseItem original, BaseItem edited)
    {
        Original = original;
        Edited = edited;
        if (original is Template o) _baselines = ContentByKey(o);
        edited.PropertyChanged += Edited_PropertyChanged;
    }

    public BaseItem Original { get; }
    public BaseItem Edited { get; }

    /// <summary>The edits differ from the saved item.</summary>
    public bool IsDirty { get; private set; }

    /// <summary><see cref="IsDirty"/> changed.</summary>
    public event Action? DirtyChanged;

    public static ItemDraft For(BaseItem item)
    {
        BaseItem copy = item is Template t
            ? new Template
            {
                Content = t.Content,
                Language = t.Language,
                LanguageId = t.LanguageId,
                AltLanguages = t.AltLanguages,
                TagsCsv = t.TagsCsv,
                SingleKeyShortcut = t.SingleKeyShortcut,
                MultiKeyShortcut = t.MultiKeyShortcut,
                DefaultPasteMode = t.DefaultPasteMode,
            }
            : new Folder();
        copy.Id = item.Id;
        copy.Title = item.Title;
        copy.ParentId = item.ParentId;
        copy.SyncId = item.SyncId;
        copy.IsSyncRoot = item.IsSyncRoot;
        return new ItemDraft(item, copy);
    }

    /// <summary>The same edits on <paramref name="item"/>, which replaced the original (a sync refresh rebuilds the
    /// synced items).</summary>
    public ItemDraft RebaseOnto(BaseItem item)
    {
        var draft = For(item);
        draft.Edited.Title = Edited.Title;
        if (draft.Edited is Template to && Edited is Template from)
        {
            to.Content = from.Content;
            to.Language = from.Language;
            to.LanguageId = from.LanguageId;
            to.AltLanguages = from.AltLanguages;
            to.TagsCsv = from.TagsCsv;
            to.SingleKeyShortcut = from.SingleKeyShortcut;
            to.MultiKeyShortcut = from.MultiKeyShortcut;
            to.DefaultPasteMode = from.DefaultPasteMode;
            draft._baselines = new(_baselines);
        }
        draft.Update();
        return draft;
    }

    /// <summary>The editor loaded tab <paramref name="tabId"/> (null: the first) and shows it as
    /// <paramref name="html"/>. Taken as the tab's unedited state while nothing was typed in it yet.</summary>
    public void ContentLoaded(string html, string? tabId = null)
    {
        if (Edited is not Template edited) return;
        int at = TemplateTabs.IndexOf(edited, tabId);
        if (at < 0) return;
        var tabs = TemplateTabs.All(edited);
        string key = Key(tabs, at);
        if (!_baselines.TryGetValue(key, out var baseline) || tabs[at].Content != baseline) return;
        _baselines[key] = html;
        TemplateTabs.SetContent(edited, tabs[at].Id, html);
        Update();
    }

    /// <summary>Copies the edits into the saved item (the store then saves them, as for any edit) and makes them
    /// the new unedited state.</summary>
    public void Commit()
    {
        if (!IsDirty) return;
        if (Original.Title != Edited.Title) Original.Title = Edited.Title;
        if (Original is Template o && Edited is Template e)
        {
            var stored = ContentByKey(o);
            var tabs = TemplateTabs.All(e);
            // A tab nobody typed in keeps its stored text rather than the editor's normalised form of it.
            var committed = tabs.Select((tab, i) => IsUnedited(tabs, i) && stored.TryGetValue(Key(tabs, i), out var text)
                ? tab with { Content = text } : tab).ToList();
            TemplateTabs.Apply(o, committed);
            if (o.TagsCsv != e.TagsCsv) o.TagsCsv = e.TagsCsv;
            if (o.SingleKeyShortcut != e.SingleKeyShortcut) o.SingleKeyShortcut = e.SingleKeyShortcut;
            if (o.MultiKeyShortcut != e.MultiKeyShortcut) o.MultiKeyShortcut = e.MultiKeyShortcut;
            if (o.DefaultPasteMode != e.DefaultPasteMode) o.DefaultPasteMode = e.DefaultPasteMode;
            _baselines = tabs.Select((tab, i) => (Key(tabs, i), tab.Content)).ToDictionary(x => x.Item1, x => x.Content);
        }
        Update();
    }

    private static string Key(List<TemplateLanguage> tabs, int index) => tabs.Count == 1 ? OnlyTab : tabs[index].Id ?? OnlyTab;

    private static Dictionary<string, string> ContentByKey(Template t)
    {
        var tabs = TemplateTabs.All(t);
        var byKey = new Dictionary<string, string>();
        for (int i = 0; i < tabs.Count; i++) byKey.TryAdd(Key(tabs, i), tabs[i].Content);
        return byKey;
    }

    private bool IsUnedited(List<TemplateLanguage> tabs, int index) =>
        _baselines.TryGetValue(Key(tabs, index), out var baseline) && tabs[index].Content == baseline;

    private void Edited_PropertyChanged(object? sender, PropertyChangedEventArgs e) => Update();

    private void Update()
    {
        bool dirty = Original.Title != Edited.Title
            || (Original is Template o && Edited is Template e
                && (TabsChanged(o, e) || o.TagsCsv != e.TagsCsv || o.SingleKeyShortcut != e.SingleKeyShortcut
                    || o.MultiKeyShortcut != e.MultiKeyShortcut || o.DefaultPasteMode != e.DefaultPasteMode));
        if (dirty == IsDirty) return;
        IsDirty = dirty;
        DirtyChanged?.Invoke();
    }

    // Tabs added, removed, moved or given another language, or a tab's text edited.
    private bool TabsChanged(Template original, Template edited)
    {
        var before = TemplateTabs.All(original);
        var after = TemplateTabs.All(edited);
        if (before.Count != after.Count) return true;
        for (int i = 0; i < before.Count; i++)
            if (before[i].Language != after[i].Language || Key(before, i) != Key(after, i) || !IsUnedited(after, i)) return true;
        return false;
    }
}
