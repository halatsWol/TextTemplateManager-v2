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
    [NotifyPropertyChangedFor(nameof(IsEditorEnabled))]
    [NotifyPropertyChangedFor(nameof(SelectedTemplate))]
    [NotifyPropertyChangedFor(nameof(IsFolderSelected))]
    [NotifyPropertyChangedFor(nameof(IsNoSelection))]
    [NotifyPropertyChangedFor(nameof(IsSelectedReadOnly))]
    [NotifyPropertyChangedFor(nameof(IsSelectedEditable))]
    [NotifyPropertyChangedFor(nameof(CanAddHere))]
    [NotifyPropertyChangedFor(nameof(CanDeleteSelection))]
    public partial BaseItem? SelectedItem { get; set; }

    public bool IsFolderSelected => SelectedItem is Folder;
    public bool IsNoSelection => SelectedItem is null;

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

    [ObservableProperty] public partial string SearchText { get; set; } = string.Empty;

    // Search looks in the template text too; a saved setting, shared with Quick Paste.
    private bool IncludeContentSearch => _dataNode.CurrentSettings.SearchInContent;

    public bool IsEditorEnabled => SelectedItem is Template;

    // Null when a Folder is selected — avoids cast exceptions in XAML.
    public Template? SelectedTemplate => SelectedItem as Template;

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
        _dataNode.CurrentSettings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppSettings.SearchInContent)) ApplyFilter();
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
    private void StartSyncPolling()
    {
        _syncPollTimer = _ui.CreateTimer();
        _syncPollTimer.Interval = TimeSpan.FromSeconds(4);
        _syncPollTimer.Tick += async (s, e) =>
        {
            try { await _dataNode.CheckSyncFilesForChangesAsync(); } catch { }
        };
        _syncPollTimer.Start();
    }

    #region Search Logic

    partial void OnSearchTextChanged(string value) => ApplyFilter();

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
        });

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

    [RelayCommand]
    private async Task DeleteItem()
    {
        if (SelectedItem == null) return;
        if (SelectedItem.IsSyncRoot || _dataNode.IsItemReadOnly(SelectedItem)) return;   // save-off sync
        await _dataNode.DeleteItemAsync(SelectedItem);
        ApplyFilter();
        SelectedItem = null;
        ValidateAllShortcuts();
    }

    public bool IsReadOnly(BaseItem item) => _dataNode.IsItemReadOnly(item);

    #endregion

    #region Backup/Storage

    private static IntPtr MainWindowHandle => WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);

    /// <summary>Exports <paramref name="item"/> (a folder with its contents, or a template), or all local templates
    /// and folders when it is null, to a .ttmdata file the user picks.</summary>
    public async Task ExportAsync(BaseItem? item)
    {
        string name = item?.Title is { Length: > 0 } title ? title : $"Templates_{DateTime.Now:yyyyMMdd}";
        if (await DataFilePicker.PickToSaveAsync(MainWindowHandle, name) is not { } path) return;
        if (item is Folder folder) await _dataNode.ExportFolderAsync(path, folder);
        else if (item != null) await _dataNode.ExportItemsAsync(path, new[] { item });
        else await _dataNode.ExportItemsAsync(path, _dataNode.LocalOnlyItems);
    }

    public enum LoadBackupResult { Cancelled, Unreadable, Replaced }

    /// <summary>Picks a backup file and, after <paramref name="confirmReplace"/> agrees (given the file
    /// name), replaces the local items with its contents. Synced folders are left alone.</summary>
    public async Task<LoadBackupResult> LoadBackupAsync(Func<string, Task<bool>> confirmReplace)
    {
        if (await DataFilePicker.PickToOpenAsync(MainWindowHandle) is not { } path) return LoadBackupResult.Cancelled;

        // Backups are a serialized root Folder — take its children (not a bare List<BaseItem>).
        var root = await StorageService.LoadRootAsync(path);
        if (root == null) return LoadBackupResult.Unreadable;
        if (!await confirmReplace(System.IO.Path.GetFileName(path))) return LoadBackupResult.Cancelled;

        await _dataNode.ReplaceLocalItemsAsync(root.Children.ToList());

        SelectedItem = null;
        ValidateAllShortcuts();
        ApplyFilter();
        return LoadBackupResult.Replaced;
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

    /// <summary>Moves an item and keeps it selected. Moving into another area may have cleared or renamed
    /// shortcuts, so the conflict flags are refreshed too.</summary>
    public async Task MoveAsync(BaseItem item, Folder? folder, int index)
    {
        if (!await _dataNode.MoveItemAsync(item, folder, index)) return;
        Expand(folder);   // reveal the moved item

        // The TreeView crashes on an in-place Move of its bound root collection (see TreeChanged).
        if (RootOrderChanged()) ReloadTree();
        else ApplyFilter();
        SelectedItem = item;
        ValidateAllShortcuts();
    }

    /// <summary>Moves an item out to the top level, after the pinned sync folders and the local items.</summary>
    public Task MoveToRootAsync(BaseItem item) => MoveAsync(item, null, AllItems.Count);

    #endregion

    // Re-raise so MainPage re-syncs the editor to the new selection.
    partial void OnSelectedItemChanged(BaseItem? value)
    {
        SelectedNode = value == null ? null : NodeFor(value);
        OnPropertyChanged(nameof(SelectedItem));
    }

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