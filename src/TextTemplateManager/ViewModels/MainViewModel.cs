using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using TextTemplateManager.Common;
using TextTemplateManager.Data;
using TextTemplateManager.Helpers;
using TextTemplateManager.Models;
using Windows.Storage.Pickers;


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
    public partial ObservableCollection<BaseItem> RootNodes { get; set; } = new();

    [ObservableProperty] public partial string SearchText { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IncludeContentSearch { get; set; }
    [ObservableProperty] public partial bool StrictSearch { get; set; }

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
                // crashes on an in-place Move of its bound root collection (the reason CloseSettings uses
                // ReloadTree), so rebind the whole collection in that case; otherwise refresh in place to
                // keep expansion state for the frequent content-only refreshes (sync poll, connector add).
                if (RootOrderChanged())
                    ReloadTree();
                else
                    ApplyFilter();
                if (prev != null && !ReferenceEquals(SelectedItem, prev) && AllItems.Descendants().Contains(prev))
                    SelectedItem = prev;
            }
            catch (Exception ex)
            {
                // Runs on a fire-and-forget dispatcher callback, so an escape would be unhandled: never
                // let a tree refresh take down the app — log and leave the current view in place.
                System.Diagnostics.Debug.WriteLine($"[Tree] refresh failed: {ex.Message}");
            }
        });

        _ = Task.Run(async () =>
        {
            await _dataNode.InitializeAsync();
            _ui?.TryEnqueue(() =>
            {
                ApplyFilter();
                StartSyncPolling();
            });

            foreach (var t in AllItems.Descendants().OfType<Template>())
            {
                t.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName is nameof(Template.SingleKeyShortcut)
                        or nameof(Template.MultiKeyShortcut))
                    {
                        ValidateAllShortcuts();
                    }
                };
            }

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
    partial void OnIncludeContentSearchChanged(bool value) => ApplyFilter();
    partial void OnStrictSearchChanged(bool value) => ApplyFilter();

    /// <summary>Full tree rebuild with a fresh RootNodes instance. TreeView does not reliably
    /// reflect a Move on its bound collection (e.g. reordered sync folders), so a reload re-binds
    /// the whole collection instead of moving items in place.</summary>
    public void ReloadTree()
    {
        RootNodes = new ObservableCollection<BaseItem>();
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
            if (!RootNodes.Contains(desired[i])) return false; // membership differs -> in-place is safe
            if (!ReferenceEquals(RootNodes[i], desired[i])) orderDiffers = true;
        }
        return orderDiffers;
    }

    public void ApplyFilter()
    {
        var source = _dataNode.LocalItems;

        if (string.IsNullOrWhiteSpace(SearchText))
        {
            // No search: mirror the real tree.
            MirrorViewChildren(source);
            SyncCollection(RootNodes, source);
        }
        else
        {
            // Search: prune to matches, keeping the hierarchy.
            var filtered = FilterRecursive(source, SearchText);
            SyncCollection(RootNodes, filtered);
        }
    }

    /// <summary>In-place add/remove/move so TreeView containers (and expansion state) survive,
    /// which a wholesale collection swap would destroy.</summary>
    private static void SyncCollection(ObservableCollection<BaseItem> target, IList<BaseItem> desired)
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

    /// <summary>Mirror Children into ViewChildren so the tree shows the full, unpruned hierarchy.</summary>
    private void MirrorViewChildren(IEnumerable<BaseItem> items)
    {
        foreach (var item in items)
        {
            SyncCollection(item.ViewChildren, item.Children);
            MirrorViewChildren(item.Children);
        }
    }

    /// <summary>Pruned tree for the query: an item survives if it matches or has a matching
    /// descendant; each survivor's ViewChildren holds only its matches. Returns the survivors.</summary>
    private List<BaseItem> FilterRecursive(IEnumerable<BaseItem> items, string query)
    {
        var matched = new List<BaseItem>();

        foreach (var item in items)
        {
            string searchPool = item.Title;

            if (item is Template t)
            {
                searchPool += $" {t.TagsCsv}";
                if (IncludeContentSearch)
                {
                    string plainContent = HtmlUtils.ToPlainText(t.Content);
                    searchPool += $" {plainContent}";
                }
            }

            bool isMatch = FuzzySearchEngine.IsMatch(query, searchPool, StrictSearch);

            var matchedChildren = FilterRecursive(item.Children, query);

            if (isMatch || matchedChildren.Count > 0)
            {
                SyncCollection(item.ViewChildren, matchedChildren);

                // Expand so matches are visible.
                if (item is Folder f)
                    f.IsExpanded = true;

                matched.Add(item);
            }
        }
        return matched;
    }

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
            Title = GetUniqueTitle("New Template", siblings),
            Id = Guid.NewGuid(),
            Owner = this,
            DefaultPasteMode = _dataNode.CurrentSettings.DefaultPasteMode
        };

        if (parent != null) parent.IsExpanded = true;   // reveal the new item

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
            Title = GetUniqueTitle("New Folder", siblings),
            Id = Guid.NewGuid()
        };

        if (parent != null) parent.IsExpanded = true;   // reveal the new item

        await _dataNode.AddItemAsync(item, parent);
        ApplyFilter();
        SelectedItem = item;
    }

    /// <summary>baseName, else the first free "baseName N" (fills gaps).</summary>
    private static string GetUniqueTitle(string baseName, IEnumerable<BaseItem> siblings)
    {
        var taken = new HashSet<string>(
            siblings.Select(s => s.Title ?? string.Empty),
            StringComparer.OrdinalIgnoreCase);

        if (!taken.Contains(baseName)) return baseName;

        int n = 1;
        while (taken.Contains($"{baseName} {n}")) n++;
        return $"{baseName} {n}";
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
        string cloneTitle = GetUniqueTitle($"{SelectedItem.Title} (Copy)", siblings);

        BaseItem clone = SelectedItem is Template t
            ? new Template
            {
                Title = cloneTitle,
                Content = t.Content,
                TagsCsv = t.TagsCsv,
                DefaultPasteMode = t.DefaultPasteMode,
                SingleKeyShortcut = t.SingleKeyShortcut,
                MultiKeyShortcut = t.MultiKeyShortcut,
                Owner = this
            }
            : new Folder { Title = cloneTitle };

        await _dataNode.AddItemAsync(clone, parent);

        ApplyFilter();
        SelectedItem = clone;
    }

    [RelayCommand]
    private async Task DeleteItem()
    {
        if (SelectedItem == null) return;
        if (SelectedItem.IsSyncRoot || _dataNode.IsItemReadOnly(SelectedItem)) return;   // save-off sync
        await _dataNode.DeleteItemAsync(SelectedItem);
        ApplyFilter();
        SelectedItem = null;
    }

    public bool IsReadOnly(BaseItem item) => _dataNode.IsItemReadOnly(item);
    public string GetEffectiveMultiKey(Template t) => _dataNode.GetEffectiveMultiKey(t);

    public Guid GetAreaKey(BaseItem item) => _dataNode.GetAreaKey(item);

    #endregion

    #region Backup/Storage

    [RelayCommand]
    private async Task SaveBackup()
    {
        var picker = new FileSavePicker();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        picker.FileTypeChoices.Add("TextTemplateManager data", new List<string> { ".ttmdata" });
        picker.SuggestedFileName = $"Backup_{DateTime.Now:yyyyMMdd}";

        var file = await picker.PickSaveFileAsync();
        if (file != null)
            await _dataNode.ExportDataAsync(file.Path);
    }

    /// <summary>Exports a folder's subtree to a file the user picks (per-folder Export action).</summary>
    public async Task ExportFolderAsync(Folder folder)
    {
        var picker = new FileSavePicker();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        picker.FileTypeChoices.Add("TextTemplateManager data", new List<string> { ".ttmdata" });
        picker.SuggestedFileName = folder.Title;

        var file = await picker.PickSaveFileAsync();
        if (file != null)
            await _dataNode.ExportFolderAsync(file.Path, folder);
    }

    public enum LoadBackupResult { Cancelled, Unreadable, Replaced }

    /// <summary>Picks a backup file and, after <paramref name="confirmReplace"/> agrees (given the file
    /// name), replaces the local items with its contents. Synced folders are left alone.</summary>
    public async Task<LoadBackupResult> LoadBackupAsync(Func<string, Task<bool>> confirmReplace)
    {
        var picker = new FileOpenPicker();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        picker.FileTypeFilter.Add(".ttmdata");

        var file = await picker.PickSingleFileAsync();
        if (file == null) return LoadBackupResult.Cancelled;

        // Backups are a serialized root Folder — take its children (not a bare List<BaseItem>).
        var root = await StorageService.LoadRootAsync(file.Path);
        if (root == null) return LoadBackupResult.Unreadable;
        if (!await confirmReplace(file.Name)) return LoadBackupResult.Cancelled;

        foreach (var t in root.Children.Descendants().OfType<Template>()) t.Owner = this;
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
        if (folder != null) folder.IsExpanded = true;   // reveal the moved item

        // The TreeView crashes on an in-place Move of its bound root collection (see TreeChanged).
        if (RootOrderChanged()) ReloadTree();
        else ApplyFilter();
        SelectedItem = item;
        ValidateAllShortcuts();
    }

    /// <summary>Moves an item out to the top level, after the pinned sync folders and the local items.</summary>
    public Task MoveToRootAsync(BaseItem item) => MoveAsync(item, null, AllItems.Count);

    #endregion

    partial void OnSelectedItemChanged(BaseItem? value)
    {
        // Re-raise so MainPage re-syncs the editor to the new selection.
        OnPropertyChanged(nameof(SelectedItem));
        ValidateAllShortcuts();
    }

    public void ValidateAllShortcuts()
    {
        var templates = AllItems.Descendants()
            .OfType<Template>()
            .ToList();

        foreach (var t in templates)
        {
            t.HasSingleKeyConflict = false;
            t.HasMultiKeyConflict = false;
            t.HasSingleKeyCrossAreaWarning = false;
            t.HasMultiKeyCrossAreaWarning = false;
        }

        // Single-key conflicts are per-area; the same key in different areas is allowed.
        var singleGroups = templates
            .Where(t => !string.IsNullOrWhiteSpace(t.SingleKeyShortcut))
            .GroupBy(t => (Area: _dataNode.GetAreaKey(t), Key: t.SingleKeyShortcut.Trim().ToUpperInvariant()))
            .Where(g => g.Count() > 1);

        foreach (var g in singleGroups)
            foreach (var t in g)
                t.HasSingleKeyConflict = true;

        // Same key across areas: allowed, flagged as a warning only.
        var crossAreaGroups = templates
            .Where(t => !string.IsNullOrWhiteSpace(t.SingleKeyShortcut))
            .GroupBy(t => t.SingleKeyShortcut.Trim().ToUpperInvariant())
            .Where(g => g.Select(t => _dataNode.GetAreaKey(t)).Distinct().Count() > 1);

        foreach (var g in crossAreaGroups)
            foreach (var t in g)
                if (!t.HasSingleKeyConflict)
                    t.HasSingleKeyCrossAreaWarning = true;

        // Multi-key on the effective (prefixed) shortcut, same rule as single-key: same key in the
        // SAME area is a conflict; the same key across areas is allowed (local wins, then sync order).
        var multiSameArea = templates
            .Where(t => !string.IsNullOrWhiteSpace(t.MultiKeyShortcut))
            .GroupBy(t => (Area: _dataNode.GetAreaKey(t), Key: _dataNode.GetEffectiveMultiKey(t).Trim().ToUpperInvariant()))
            .Where(g => g.Count() > 1);

        foreach (var g in multiSameArea)
            foreach (var t in g)
                t.HasMultiKeyConflict = true;

        var multiCrossArea = templates
            .Where(t => !string.IsNullOrWhiteSpace(t.MultiKeyShortcut))
            .GroupBy(t => _dataNode.GetEffectiveMultiKey(t).Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Select(t => _dataNode.GetAreaKey(t)).Distinct().Count() > 1);

        foreach (var g in multiCrossArea)
            foreach (var t in g)
                if (!t.HasMultiKeyConflict)
                    t.HasMultiKeyCrossAreaWarning = true;
    }



}