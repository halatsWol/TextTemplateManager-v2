using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TextTemplateManager.Common;
using TextTemplateManager.Data;
using TextTemplateManager.Helpers;
using TextTemplateManager.Models;


namespace TextTemplateManager.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public List<PasteMode> PasteModes { get; } = PasteModeLabel.DisplayOrder.ToList();
    private readonly DataNode _dataNode;
    private DispatcherQueue _ui;
    private DispatcherQueueTimer _syncPollTimer = null!;   // set in the constructor's timer setup

    public ObservableCollection<BaseItem> AllItems => _dataNode.LocalItems;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedTemplate))]
    [NotifyPropertyChangedFor(nameof(IsSelectedReadOnly))]
    [NotifyPropertyChangedFor(nameof(IsSelectedEditable))]
    [NotifyPropertyChangedFor(nameof(CanAddHere))]
    [NotifyPropertyChangedFor(nameof(CanDeleteSelection))]
    public partial BaseItem? SelectedItem { get; set; }

    // The pane's panels follow the edited item, which stays on the item left while the unsaved-changes question is open.
    public bool IsFolderSelected => EditedItem is Folder;
    public bool IsNoSelection => EditedItem is null;

    public bool IsSelectedReadOnly => SelectedItem != null && _dataNode.IsItemReadOnly(SelectedItem);
    public bool IsSelectedEditable => !IsSelectedReadOnly;

    // Add lands in the selected folder (or the selected item's parent); it's blocked when that target
    // is a read-only (save-off) sync folder. A null target means local root, which is always writable.
    public bool CanAddHere
    {
        get { var target = AddTargetFolder(); return target == null || !_dataNode.IsItemReadOnly(target); }
    }

    // Delete is blocked for a read-only (save-off) sync item and for pinned sync roots (managed in
    // Settings ▸ Sync).
    public bool CanDeleteSelection =>
        SelectedItem != null && !SelectedItem.IsSyncRoot && !_dataNode.IsItemReadOnly(SelectedItem);

    [ObservableProperty]
    public partial ObservableCollection<TreeNode> RootNodes { get; set; } = new();

    /// <summary>The tree's selection; kept in step with <see cref="SelectedItem"/>.</summary>
    [ObservableProperty]
    public partial TreeNode? SelectedNode { get; set; }

    partial void OnSelectedNodeChanged(TreeNode? value) => SelectedItem = value?.Item;

    private readonly Dictionary<BaseItem, TreeNode> _nodes = new();

    /// <summary>The tree node of <paramref name="item"/>; the same one for as long as the item exists.</summary>
    public TreeNode NodeFor(BaseItem item)
    {
        if (!_nodes.TryGetValue(item, out var node)) _nodes[item] = node = new TreeNode(item);
        return node;
    }

    private void Expand(BaseItem? item)
    {
        if (item != null) NodeFor(item).IsExpanded = true;
    }

    /// <summary>Opens or closes every folder in the tree.</summary>
    public void SetAllExpanded(bool expanded)
    {
        foreach (var folder in AllItems.Descendants().OfType<Folder>()) NodeFor(folder).IsExpanded = expanded;
    }

    [ObservableProperty] public partial string SearchText { get; set; } = string.Empty;

    // Search looks in the template text too; a saved setting, shared with Quick Paste.
    private bool IncludeContentSearch => _dataNode.CurrentSettings.SearchInContent;
    private bool StrictSearch => _dataNode.CurrentSettings.StrictSearch;

    public bool IsEditorEnabled => EditedItem is Template;

    // Null when a Folder is selected — avoids cast exceptions in XAML.
    public Template? SelectedTemplate => SelectedItem as Template;

    #region Editing (autosave off: a draft)

    /// <summary>What the right pane shows and edits: the selected item itself with autosave on, otherwise its
    /// <see cref="ItemDraft"/> copy. While the unsaved-changes question is open it stays on the item left.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditedTemplate))]
    [NotifyPropertyChangedFor(nameof(IsEditorEnabled))]
    [NotifyPropertyChangedFor(nameof(IsFolderSelected))]
    [NotifyPropertyChangedFor(nameof(IsNoSelection))]
    public partial BaseItem? EditedItem { get; set; }

    public Template? EditedTemplate => EditedItem as Template;

    private ItemDraft? _draft;
    private bool _switchCheckQueued;

    /// <summary>The edited item has changes that aren't saved yet (autosave off).</summary>
    public bool HasUnsavedChanges => _draft?.IsDirty == true;

    /// <summary>The selection left the edited item (autosave off). The page takes the editor's last content, asks
    /// when <see cref="HasUnsavedChanges"/>, and answers with <see cref="ResolveSwitchAsync"/> or
    /// <see cref="CancelSwitch"/>.</summary>
    public event Action? LeavingEditedItem;

    private bool Autosave => _dataNode.CurrentSettings.Autosave;

    // The pane edits item: directly with autosave on, otherwise a fresh draft of it.
    private void Edit(BaseItem? item)
    {
        if (item != null && !Autosave) { Adopt(ItemDraft.For(item)); return; }
        if (_draft != null) _draft.DirtyChanged -= OnDraftDirtyChanged;
        _draft = null;
        EditedItem = item;
        OnPropertyChanged(nameof(HasUnsavedChanges));
    }

    private void Adopt(ItemDraft draft)
    {
        if (_draft != null) _draft.DirtyChanged -= OnDraftDirtyChanged;
        _draft = draft;
        _draft.DirtyChanged += OnDraftDirtyChanged;
        EditedItem = draft.Edited;
        OnPropertyChanged(nameof(HasUnsavedChanges));
    }

    private void OnDraftDirtyChanged() => OnPropertyChanged(nameof(HasUnsavedChanges));

    // Leaving a draft is decided once the selection has settled: a tree refresh can pass through no selection
    // and back to the same item, which isn't leaving it.
    private void OnSelectionMoved(BaseItem? value)
    {
        if (_draft is { } draft)
        {
            if (!ReferenceEquals(value, draft.Original) && !_switchCheckQueued)
            {
                _switchCheckQueued = true;
                _ui.TryEnqueue(DispatcherQueuePriority.Low, CheckSwitch);
            }
            return;
        }
        if (!ReferenceEquals(EditedItem, value)) Edit(value);
    }

    private void CheckSwitch()
    {
        _switchCheckQueued = false;
        if (_draft is not { } draft) { Edit(SelectedItem); return; }
        if (ReferenceEquals(SelectedItem, draft.Original)) return;
        if (!AllItems.Descendants().Contains(draft.Original))
        {
            // Replaced by a sync refresh: the edits move onto the new item. Deleted: they go with it.
            if (AllItems.Descendants().FirstOrDefault(i => i.Id == draft.Original.Id) is { } replacement)
            {
                Adopt(draft.RebaseOnto(replacement));
                SelectedItem = replacement;
            }
            else Edit(SelectedItem);
            return;
        }
        LeavingEditedItem?.Invoke();
    }

    /// <summary>Saves the edits and writes them to disk now (with autosave on: whatever is still waiting).</summary>
    public async Task SaveEditsAsync()
    {
        if (_draft is { IsDirty: true } draft) draft.Commit();
        await _dataNode.FlushPendingSaveAsync();
    }

    /// <summary>Puts the edited item back as it was last saved.</summary>
    public void DiscardEdits() => Edit(_draft?.Original ?? SelectedItem);

    /// <summary>After <see cref="LeavingEditedItem"/>: save the edits or drop them, then edit the new selection.</summary>
    public async Task ResolveSwitchAsync(bool save)
    {
        if (save) await SaveEditsAsync();
        Edit(SelectedItem);
    }

    /// <summary>After <see cref="LeavingEditedItem"/>: go back to the item with the edits, which are kept.</summary>
    public void CancelSwitch()
    {
        if (_draft != null) SelectedItem = _draft.Original;
    }

    /// <summary>The editor shows the edited template's content as <paramref name="html"/> after loading it.</summary>
    public void EditorLoaded(Template shown, string html)
    {
        if (_draft is { } draft && ReferenceEquals(draft.Edited, shown)) draft.ContentLoaded(html);
    }

    #endregion

    public MainViewModel(DataNode store)
    {
        _dataNode = store;
        _ui = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

        // Re-project on sync re-apply; re-select if the refresh dropped the selection.
        _dataNode.TreeChanged += () => _ui?.TryEnqueue(() =>
        {
            try
            {
                var prev = SelectedItem;
                // A sync-folder reorder is the one refresh that needs a top-level Move. WinUI's TreeView
                // crashes on an in-place Move of its bound root collection (the reason leaving Settings uses
                // ReloadTree), so rebind the whole collection in that case; otherwise refresh in place to
                // keep expansion state for the frequent content-only refreshes (sync poll, connector add).
                if (RootOrderChanged())
                    ReloadTree();
                else
                    ApplyFilter();
                if (prev != null && !ReferenceEquals(SelectedItem, prev) && AllItems.Descendants().Contains(prev))
                    SelectedItem = prev;
                ValidateAllShortcuts();
            }
            catch (Exception ex)
            {
                // Runs on a fire-and-forget dispatcher callback, so an escape would be unhandled: never
                // let a tree refresh take down the app — log and leave the current view in place.
                System.Diagnostics.Debug.WriteLine($"[Tree] refresh failed: {ex.Message}");
            }
        });

        _dataNode.ShortcutsChanged += QueueValidation;

        // Turned on or off here or in Quick Paste.
        _dataNode.CurrentSettings.PropertyChanged += async (_, e) =>
        {
            if (e.PropertyName is nameof(AppSettings.SearchInContent) or nameof(AppSettings.StrictSearch)) ApplyFilter();
            if (e.PropertyName == nameof(AppSettings.Autosave))
            {
                await SaveEditsAsync();   // switched on with edits pending: they are kept
                Edit(SelectedItem);
            }
        };

        _ = Task.Run(async () =>
        {
            await _dataNode.InitializeAsync();
            _ui?.TryEnqueue(() =>
            {
                _loaded = true;
                ApplyFilter();
                ValidateAllShortcuts();
                StartSyncPolling();
            });
        });
    }

    // Set on the UI thread once the store has loaded. Before that the tree is still being filled on a
    // background thread, so validating would read it mid-change; the load ends with one validation.
    private bool _loaded;
    private int _validationQueued;

    // Shortcut edits and sync applies can change many templates in a row: validate once, on the UI thread.
    private void QueueValidation()
    {
        if (Interlocked.Exchange(ref _validationQueued, 1) == 1) return;
        _ui.TryEnqueue(() =>
        {
            Interlocked.Exchange(ref _validationQueued, 0);
            if (_loaded) ValidateAllShortcuts();
        });
    }

    // Poll sync files for external edits (timestamps only; reconcile on real change).
    /// <summary>Stops the sync file polling (the tour's sample window, when the tour ends).</summary>
    public void StopBackgroundWork()
    {
        _stopped = true;
        _syncPollTimer?.Stop();
    }

    private bool _stopped;

    private void StartSyncPolling()
    {
        if (_stopped) return;
        _syncPollTimer = _ui.CreateTimer();
        _syncPollTimer.Interval = TimeSpan.FromSeconds(4);
        _syncPollTimer.Tick += async (s, e) =>
        {
            try { await _dataNode.CheckSyncFilesForChangesAsync(); } catch { }
        };
        _syncPollTimer.Start();
    }

    #region Search Logic

    // A new search starts from the focused item alone, so nothing that the filter hides stays selected.
    partial void OnSearchTextChanged(string value)
    {
        ResetSelection(SelectedItem);
        ApplyFilter();
    }

    /// <summary>Full tree rebuild with a fresh RootNodes instance. TreeView does not reliably
    /// reflect a Move on its bound collection (e.g. reordered sync folders), so a reload re-binds
    /// the whole collection instead of moving items in place.</summary>
    public void ReloadTree()
    {
        RootNodes = new ObservableCollection<TreeNode>();
        ApplyFilter();
    }

    /// <summary>True when the data model's top-level items match RootNodes as a set but in a
    /// different order — the only refresh needing an in-place root Move, which the TreeView
    /// mishandles. Add/remove (count differs) and filtering are refreshed in place safely.</summary>
    private bool RootOrderChanged()
    {
        var desired = _dataNode.LocalItems;
        if (desired.Count != RootNodes.Count) return false;
        bool orderDiffers = false;
        for (int i = 0; i < desired.Count; i++)
        {
            if (!RootNodes.Any(n => ReferenceEquals(n.Item, desired[i]))) return false; // membership differs -> in-place is safe
            if (!ReferenceEquals(RootNodes[i].Item, desired[i])) orderDiffers = true;
        }
        return orderDiffers;
    }

    public void ApplyFilter()
    {
        var source = _dataNode.LocalItems;

        if (string.IsNullOrWhiteSpace(SearchText))
        {
            // No search: mirror the real tree.
            MirrorChildren(source);
            SyncCollection(RootNodes, source.Select(NodeFor).ToList());
        }
        else
        {
            // Search: prune to matches, keeping the hierarchy.
            var filtered = FilterRecursive(source, SearchText);
            SyncCollection(RootNodes, filtered);
        }

        // Forget the nodes of items that are gone (deleted, or dropped by a sync re-apply).
        var present = source.Descendants().ToHashSet();
        foreach (var gone in _nodes.Keys.Where(i => !present.Contains(i)).ToList()) _nodes.Remove(gone);
    }

    /// <summary>In-place add/remove/move so TreeView containers (and expansion state) survive,
    /// which a wholesale collection swap would destroy.</summary>
    private static void SyncCollection(ObservableCollection<TreeNode> target, IList<TreeNode> desired)
    {
        for (int i = target.Count - 1; i >= 0; i--)
        {
            if (!desired.Contains(target[i]))
                target.RemoveAt(i);
        }

        for (int i = 0; i < desired.Count; i++)
        {
            var item = desired[i];
            int currentIndex = target.IndexOf(item);

            if (currentIndex < 0)
                target.Insert(i, item);
            else if (currentIndex != i)
                target.Move(currentIndex, i);
        }
    }

    /// <summary>Show every child under each node, so the tree is the full, unpruned hierarchy.</summary>
    private void MirrorChildren(IEnumerable<BaseItem> items)
    {
        foreach (var item in items)
        {
            SyncCollection(NodeFor(item).Children, item.Children.Select(NodeFor).ToList());
            MirrorChildren(item.Children);
        }
    }

    /// <summary>The nodes of the items left by the search, each showing only its remaining children,
    /// folders expanded so the matches are visible.</summary>
    private List<TreeNode> FilterRecursive(IEnumerable<BaseItem> items, string query) =>
        TemplateSearch.Prune<TreeNode>(items, query, IncludeContentSearch, (item, children) =>
        {
            var node = NodeFor(item);
            SyncCollection(node.Children, children);
            if (item is Folder) node.IsExpanded = true;
            return node;
        }, StrictSearch);

    #endregion

    #region CRUD Commands

    /// <summary>Target for a new item: the selected folder, a selected template's parent, else root.</summary>
    private Folder? AddTargetFolder() =>
        SelectedItem as Folder ?? (SelectedItem != null ? AllItems.ParentOf(SelectedItem) as Folder : null);

    [RelayCommand]
    private async Task AddTemplate()
    {
        Folder? parent = AddTargetFolder();
        if (parent != null && _dataNode.IsItemReadOnly(parent)) return;   // save-off sync folder
        var siblings = parent?.Children ?? AllItems;

        var item = new Template
        {
            Title = ItemTitles.Unique("New Template", siblings),
            Id = Guid.NewGuid(),
            DefaultPasteMode = _dataNode.CurrentSettings.DefaultPasteMode
        };

        Expand(parent);   // reveal the new item

        await _dataNode.AddItemAsync(item, parent);
        ApplyFilter();
        SelectedItem = item;
    }

    [RelayCommand]
    private async Task AddFolder()
    {
        Folder? parent = AddTargetFolder();
        if (parent != null && _dataNode.IsItemReadOnly(parent)) return;   // save-off sync folder
        var siblings = parent?.Children ?? AllItems;

        var item = new Folder
        {
            Title = ItemTitles.Unique("New Folder", siblings),
            Id = Guid.NewGuid()
        };

        Expand(parent);   // reveal the new item

        await _dataNode.AddItemAsync(item, parent);
        ApplyFilter();
        SelectedItem = item;
    }

    [RelayCommand]
    private async Task CloneItem()
    {
        if (SelectedItem == null) return;

        // Read-only item → clone locally (outside the sync folder); else sibling of the original.
        Folder? parent = _dataNode.IsItemReadOnly(SelectedItem)
            ? null
            : AllItems.ParentOf(SelectedItem) as Folder;
        var siblings = parent?.Children ?? AllItems;
        string cloneTitle = ItemTitles.Unique($"{SelectedItem.Title} (Copy)", siblings);

        BaseItem clone = SelectedItem is Template t
            ? new Template
            {
                Title = cloneTitle,
                Content = t.Content,
                TagsCsv = t.TagsCsv,
                DefaultPasteMode = t.DefaultPasteMode,
                SingleKeyShortcut = t.SingleKeyShortcut,
                MultiKeyShortcut = t.MultiKeyShortcut,
            }
            : new Folder { Title = cloneTitle };

        await _dataNode.AddItemAsync(clone, parent);

        ApplyFilter();
        SelectedItem = clone;
        ValidateAllShortcuts();   // a clone repeats the original's shortcuts
    }

    /// <summary>The selected items that may be deleted: not a pinned sync folder (managed in Settings › Sync) and
    /// nothing in a save-off (read-only) sync folder.</summary>
    public IReadOnlyList<BaseItem> DeletableSelection =>
        SelectedItems.Where(i => !i.IsSyncRoot && !_dataNode.IsItemReadOnly(i)).ToList();

    [RelayCommand]
    private async Task DeleteItem()
    {
        var items = DeletableSelection;
        if (items.Count == 0) return;
        await _dataNode.DeleteItemsAsync(items);
        ApplyFilter();
        SelectedItem = null;
        ValidateAllShortcuts();
    }

    public bool IsReadOnly(BaseItem item) => _dataNode.IsItemReadOnly(item);

    #endregion

    #region Backup/Storage

    private static IntPtr MainWindowHandle => WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);

    /// <summary>Exports <paramref name="items"/> to a .ttmdata file the user picks: one folder as itself (with its
    /// contents), one template or several items under a plain root, and all local templates and folders when
    /// nothing is given.</summary>
    public async Task ExportAsync(IReadOnlyList<BaseItem> items)
    {
        string name = items.Count == 1 && items[0].Title is { Length: > 0 } title ? title : $"Templates_{DateTime.Now:yyyyMMdd}";
        if (await DataFilePicker.PickToSaveAsync(MainWindowHandle, name) is not { } path) return;
        if (items is [Folder folder]) await _dataNode.ExportFolderAsync(path, folder);
        else if (items.Count > 0) await _dataNode.ExportItemsAsync(path, items);
        else await _dataNode.ExportItemsAsync(path, _dataNode.LocalOnlyItems);
    }

    /// <summary>A .ttmdata file picked for import, with what it holds.</summary>
    public sealed record ImportFile(string Name, List<BaseItem> Items, int Templates, int Folders, int Conflicts);

    public enum ImportPick { Cancelled, Unreadable, Picked }

    /// <summary>Lets the user pick a .ttmdata file and reads it; nothing is changed yet.</summary>
    public async Task<(ImportPick Result, ImportFile? File)> PickImportFileAsync()
    {
        if (await DataFilePicker.PickToOpenAsync(MainWindowHandle) is not { } path) return (ImportPick.Cancelled, null);
        var root = await StorageService.LoadRootAsync(path);
        if (root == null) return (ImportPick.Unreadable, null);

        var items = DataNode.ImportedItems(root);
        var all = items.SelectMany(i => i.SelfAndDescendants()).ToList();
        return (ImportPick.Picked, new ImportFile(System.IO.Path.GetFileNameWithoutExtension(path), items,
            all.OfType<Template>().Count(), all.OfType<Folder>().Count(), _dataNode.CountImportConflicts(items)));
    }

    /// <summary>Adds the file's items to the local templates (see <see cref="DataNode.ImportAsync"/>).</summary>
    public async Task<ImportSummary> ImportAsync(ImportFile file, bool intoNewFolder, ImportConflicts conflicts)
    {
        var summary = await _dataNode.ImportAsync(file.Items, intoNewFolder ? file.Name : null, conflicts);
        ValidateAllShortcuts();
        ApplyFilter();
        return summary;
    }


    #endregion

    #region Moving Items

    public enum DropSpot { Before, Into, After }

    /// <summary>Where a drop on <paramref name="target"/> puts an item: the folder (null = top level) and the
    /// index among its children.</summary>
    public (Folder? Folder, int Index) DropDestination(BaseItem target, DropSpot spot)
    {
        if (spot == DropSpot.Into && target is Folder folder) return (folder, folder.Children.Count);
        var parent = AllItems.ParentOf(target) as Folder;
        var siblings = parent?.Children ?? AllItems;
        int index = siblings.IndexOf(target);
        return (parent, spot == DropSpot.After ? index + 1 : index);
    }

    public bool CanMove(BaseItem item, Folder? folder) => _dataNode.CanMove(item, folder);

    public Task MoveAsync(BaseItem item, Folder? folder, int index) => MoveAsync(new[] { item }, folder, index);

    /// <summary>Moves items (all of them or none) and keeps them selected. Moving into another area may have
    /// cleared or renamed shortcuts, so the conflict flags are refreshed too.</summary>
    public async Task MoveAsync(IReadOnlyList<BaseItem> items, Folder? folder, int index)
    {
        if (!await _dataNode.MoveItemsAsync(items, folder, index)) return;
        Expand(folder);   // reveal the moved items

        // The TreeView crashes on an in-place Move of its bound root collection (see TreeChanged).
        if (RootOrderChanged()) ReloadTree();
        else ApplyFilter();
        _selection.Set(items);
        _clicking = true;
        try { SelectedItem = items[^1]; }
        finally { _clicking = false; }
        UpdateSelectionHighlight();
        ValidateAllShortcuts();
    }


    #endregion

    // Re-raise so MainPage re-syncs the editor to the new selection.
    partial void OnSelectedItemChanged(BaseItem? value)
    {
        // Any selection change but a Ctrl/Shift click (arrow keys, a plain click, a new item) selects just this one.
        if (!_clicking && !ModifierClickPending && _settlingFocus == null) ResetSelection(value);
        SelectedNode = value == null ? null : NodeFor(value);
        OnPropertyChanged(nameof(SelectedItem));
        OnSelectionMoved(value);
    }

    #region Several selected items

    private readonly MultiSelection _selection = new();
    private bool _clicking;

    /// <summary>Set when the pointer goes down on the tree with Ctrl or Shift held, so the tree's own selection
    /// change that follows doesn't reset the selection before <see cref="ClickItem"/> applies the click.</summary>
    public bool ModifierClickPending { get; set; }

    /// <summary>How many items are selected (the focused one included).</summary>
    public int SelectionCount => _selection.Items.Count;

    /// <summary>True when <paramref name="item"/> is one of several selected items.</summary>
    public bool IsInSelection(BaseItem item) => _selection.Items.Count > 1 && _selection.Contains(item);

    /// <summary>What Export and Delete act on: the selected items without those inside a selected folder, or the
    /// focused item alone.</summary>
    public IReadOnlyList<BaseItem> SelectedItems =>
        _selection.Items.Count > 1 ? MultiSelection.TopMost(_selection.Items)
        : SelectedItem is { } item ? new[] { item } : Array.Empty<BaseItem>();

    /// <summary>A click on a tree row, with the modifier keys held (see <see cref="MultiSelection"/>).</summary>
    public void ClickItem(BaseItem item, bool ctrl, bool shift)
    {
        ModifierClickPending = false;
        var focus = _selection.Click(item, ctrl, shift, VisibleItems());
        _clicking = true;
        try { SelectedItem = focus; }
        finally { _clicking = false; }
        UpdateSelectionHighlight();

        // The TreeView handles the same click after this, and on a Ctrl+click its own single-select logic
        // deselects: put the focus back once it is done, and don't let its interim changes reset the selection.
        _settlingFocus = focus;
        _ui?.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            var settled = _settlingFocus;
            _settlingFocus = null;
            if (settled != null && !ReferenceEquals(SelectedItem, settled))
            {
                _clicking = true;
                try { SelectedItem = settled; }
                finally { _clicking = false; }
            }
            UpdateSelectionHighlight();
        });
    }

    private BaseItem? _settlingFocus;

    private void ResetSelection(BaseItem? item)
    {
        _selection.Reset(item);
        UpdateSelectionHighlight();
    }

    private void UpdateSelectionHighlight()
    {
        bool several = _selection.Items.Count > 1;
        foreach (var node in _nodes.Values) node.IsMultiSelected = several && _selection.Contains(node.Item);
        SelectionLookChanged?.Invoke();
    }

    /// <summary>Raised when which rows count as selected changed, so the page can draw them.</summary>
    public event Action? SelectionLookChanged;

    // The tree's rows top to bottom: the children of expanded folders only.
    private List<BaseItem> VisibleItems()
    {
        var rows = new List<BaseItem>();
        void Walk(IEnumerable<TreeNode> nodes)
        {
            foreach (var node in nodes)
            {
                rows.Add(node.Item);
                if (node.IsExpanded) Walk(node.Children);
            }
        }
        Walk(RootNodes);
        return rows;
    }

    #endregion

    /// <summary>Raised on the UI thread after the conflict flags were recomputed.</summary>
    public event Action? ConflictsChanged;

    /// <summary>Recomputes every template's conflict flags. Call on the UI thread after a change to the
    /// tree or to the sync settings; shortcut edits are picked up on their own.</summary>
    public void ValidateAllShortcuts()
    {
        ShortcutValidator.Apply(_dataNode.ShortcutEntries());
        ConflictsChanged?.Invoke();
    }



}