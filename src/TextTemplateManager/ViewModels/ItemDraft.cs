using System;
using System.ComponentModel;
using TextTemplateManager.Models;

namespace TextTemplateManager.ViewModels;

/// <summary>The selected item's edits while autosave is off: a copy the right pane edits, so nothing reaches the
/// tree (and no save of anything else writes it) until <see cref="Commit"/>. Only the fields the pane edits are
/// copied: the title, and a template's content, tags, shortcuts and default paste mode.</summary>
public sealed class ItemDraft
{
    // The content as the editor shows it after loading: its HTML can differ from the stored text (normalised)
    // without any edit, which must not count as one.
    private string _contentBaseline;

    private ItemDraft(BaseItem original, BaseItem edited)
    {
        Original = original;
        Edited = edited;
        _contentBaseline = (original as Template)?.Content ?? string.Empty;
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
        draft._contentBaseline = _contentBaseline;
        draft.Edited.Title = Edited.Title;
        if (draft.Edited is Template to && Edited is Template from)
        {
            to.Content = from.Content;
            to.TagsCsv = from.TagsCsv;
            to.SingleKeyShortcut = from.SingleKeyShortcut;
            to.MultiKeyShortcut = from.MultiKeyShortcut;
            to.DefaultPasteMode = from.DefaultPasteMode;
        }
        draft.Update();
        return draft;
    }

    /// <summary>The editor loaded the content and shows it as <paramref name="html"/>. Taken as the unedited state
    /// while nothing was typed yet.</summary>
    public void ContentLoaded(string html)
    {
        if (Edited is not Template edited || edited.Content != _contentBaseline) return;
        _contentBaseline = html;
        edited.Content = html;
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
            if (e.Content != _contentBaseline && o.Content != e.Content) o.Content = e.Content;
            if (o.TagsCsv != e.TagsCsv) o.TagsCsv = e.TagsCsv;
            if (o.SingleKeyShortcut != e.SingleKeyShortcut) o.SingleKeyShortcut = e.SingleKeyShortcut;
            if (o.MultiKeyShortcut != e.MultiKeyShortcut) o.MultiKeyShortcut = e.MultiKeyShortcut;
            if (o.DefaultPasteMode != e.DefaultPasteMode) o.DefaultPasteMode = e.DefaultPasteMode;
            _contentBaseline = e.Content;
        }
        Update();
    }

    private void Edited_PropertyChanged(object? sender, PropertyChangedEventArgs e) => Update();

    private void Update()
    {
        bool dirty = Original.Title != Edited.Title
            || (Original is Template o && Edited is Template e
                && (e.Content != _contentBaseline || o.TagsCsv != e.TagsCsv || o.SingleKeyShortcut != e.SingleKeyShortcut
                    || o.MultiKeyShortcut != e.MultiKeyShortcut || o.DefaultPasteMode != e.DefaultPasteMode));
        if (dirty == IsDirty) return;
        IsDirty = dirty;
        DirtyChanged?.Invoke();
    }
}
