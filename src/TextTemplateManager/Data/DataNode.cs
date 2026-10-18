using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TextTemplateManager.Models;

namespace TextTemplateManager.Data;

public class DataNode
{
    private readonly System.Threading.SemaphoreSlim _saveLock = new(1, 1);
    private readonly System.Threading.SemaphoreSlim _syncApplyLock = new(1, 1);
    private readonly string _localDataPath;
    private bool _isInitialized = false;
    private int _saveSuppressions;   // open SuppressSaves() scopes: load, move, replace, sync apply

    /// <summary>The file IO for this store's data folder.</summary>
    public StorageService Storage { get; }

    /// <summary>A snapshot of every template in the tree.</summary>
    public IEnumerable<Template> AllItems => RootFolder.Children.Descendants().OfType<Template>().ToList();

    // Guid.Empty = root level.
    private Folder RootFolder { get; set; } = new Folder
    {
        Id = Guid.Empty,
        Title = "Root"
    };

    public ObservableCollection<BaseItem> LocalItems => RootFolder.Children;
    public AppSettings CurrentSettings { get; private set; } = new();
    public SyncSettings CurrentSyncSettings { get; private set; } = new();

    /// <summary>Raised (after a sync re-apply) so the UI can refresh the tree projection.</summary>
    public event Action? TreeChanged;

    /// <summary>Raise TreeChanged for a structural change made outside the ViewModel (e.g. a template
    /// created via the browser connector), so the main-window tree re-projects.</summary>
    public void NotifyTreeChanged() => TreeChanged?.Invoke();

    /// <summary>Raised after template data is successfully written to disk (may be off the UI thread).</summary>
    public event Action? DataSaved;

    /// <summary>Raised when a template's shortcut changes, from any thread (an edit, a sync apply).</summary>
    public event Action? ShortcutsChanged;

    /// <summary>Last observed write time per source, so the poller ignores our own writes.</summary>
    private readonly Dictionary<Guid, DateTime> _lastSeenWrite = new();

    public DataNode(StorageService storage)
    {
        Storage = storage;
        _localDataPath = storage.GetDataPath();
    }

    #region Initialization


    public async Task InitializeAsync()
    {
        if (_isInitialized) return;

        var loadedRoot = await Storage.LoadOwnFileAsync<Folder>(_localDataPath);
        if (loadedRoot != null)
        {
            using (SuppressSaves())   // no save-loops during load
            {
                // Keep persisted root metadata so an unchanged tree re-saves identically.
                RootFolder.Id = loadedRoot.Id;
                RootFolder.LastChange = loadedRoot.LastChange;

                RootFolder.Children.Clear();
                foreach (var item in loadedRoot.Children)
                {
                    RootFolder.Children.Add(item);
                    AttachChangeTracking(item);
                }
            }
        }

        CurrentSettings = await Storage.LoadSettingsAsync();
        CurrentSettings.PropertyChanged += (_, _) => ScheduleSettingsSave();

        CurrentSyncSettings = await Storage.LoadSyncSettingsAsync();
        // Valid separators are '-', '.', or "" (none); migrate any legacy value (e.g. '_') so
        // multi-key shortcuts resolve consistently even before Sync settings is opened.
        if (CurrentSyncSettings.Separator is not ("." or "-" or ""))
        {
            CurrentSyncSettings.Separator = "-";
            await SaveSyncSettingsAsync();
        }

        // Merge active sources; guard the churn from autosaving.
        using (SuppressSaves()) await ApplySyncAsync();
        foreach (var item in RootFolder.Children) AttachChangeTracking(item);

        _isInitialized = true;

        // Persist merged tree + push newer local changes to the sync files.
        if (CurrentSyncSettings.Sources.Any(s => s.IsActive))
            await SaveDataAsync();
    }

    public Task SaveSyncSettingsAsync() => Storage.SaveSyncSettingsAsync(CurrentSyncSettings);

    #endregion

    #region Tree Operations (Move, Add, Delete)

    public async Task AddItemAsync(BaseItem item, Folder? parent = null)
    {
        var target = parent ?? RootFolder;
        item.ParentId = target.Id;
        target.Children.Add(item);
        AttachChangeTracking(item);
        await SaveDataAsync();
    }

    public async Task DeleteItemAsync(BaseItem item)
    {
        if (item == null) return;
        if (RootFolder.Children.Descendants().FirstOrDefault(i => i.Id == item.Id) is { } stored)
            RootFolder.Children.ContainerOf(stored)!.Remove(stored);
        await SaveDataAsync();
    }

    /// <summary>Deletes several items with one save.</summary>
    public async Task DeleteItemsAsync(IEnumerable<BaseItem> items)
    {
        using (SuppressSaves())
        {
            foreach (var item in items.ToList())
                if (RootFolder.Children.Descendants().FirstOrDefault(i => i.Id == item.Id) is { } stored)
                    RootFolder.Children.ContainerOf(stored)?.Remove(stored);
        }
        await SaveDataAsync();
    }

    /// <summary>What an imported file brings: a folder export is that folder; any other file is its top-level
    /// items, without the synced-folder snapshots an older full export carries.</summary>
    public static List<BaseItem> ImportedItems(Folder fileRoot) =>
        fileRoot.Id != Guid.Empty
            ? new List<BaseItem> { fileRoot }
            : fileRoot.Children.Where(c => !(c is Folder && c.SyncId != null)).ToList();

    /// <summary>How many imported templates would meet an existing one when imported directly.</summary>
    public int CountImportConflicts(IEnumerable<BaseItem> items) => Merge(RootFolder.Children, Guid.Empty, items, ImportConflicts.Skip, dryRun: true).Conflicts;

    /// <summary>Adds <paramref name="items"/> to the local templates in one save: into a new folder named
    /// <paramref name="newFolderTitle"/>, or, when that is null, directly, merging folders of the same name and
    /// handling templates that already exist as <paramref name="conflicts"/> says.</summary>
    public async Task<ImportSummary> ImportAsync(IEnumerable<BaseItem> items, string? newFolderTitle, ImportConflicts conflicts)
    {
        var taken = RootFolder.Children.Descendants().Select(i => i.Id).ToHashSet();
        ImportSummary summary;
        using (SuppressSaves())
        {
            if (newFolderTitle != null)
            {
                var folder = new Folder { Title = ItemTitles.Unique(newFolderTitle, LocalOnlyItems), ParentId = Guid.Empty };
                foreach (var item in items) { PrepareNew(item, folder.Id, taken, renewAll: true); folder.Children.Add(item); }
                RootFolder.Children.Add(folder);
                AttachChangeTracking(folder);
                summary = new ImportSummary(items.Sum(i => i.SelfAndDescendants().OfType<Template>().Count()), 0, 0);
            }
            else summary = Merge(RootFolder.Children, Guid.Empty, items, conflicts, dryRun: false, taken);
        }
        await SaveDataAsync();
        return summary;
    }

    private ImportSummary Merge(ObservableCollection<BaseItem> into, Guid parentId, IEnumerable<BaseItem> items,
        ImportConflicts conflicts, bool dryRun, HashSet<Guid>? taken = null)
    {
        int added = 0, overwritten = 0, skipped = 0, met = 0;
        foreach (var item in items.ToList())
        {
            var existing = into.Where(c => !c.IsSyncRoot && c.SyncId == null && c.GetType() == item.GetType()).ToList();
            var match = existing.FirstOrDefault(c => c.Id == item.Id)
                ?? existing.FirstOrDefault(c => string.Equals(c.Title, item.Title, StringComparison.OrdinalIgnoreCase));

            if (match is Folder folder)
            {
                var inner = Merge(folder.Children, folder.Id, item.Children, conflicts, dryRun, taken);
                (added, overwritten, skipped, met) = (added + inner.Added, overwritten + inner.Overwritten, skipped + inner.Skipped, met + inner.Conflicts);
            }
            else if (match is Template current && item is Template imported)
            {
                met++;
                bool overwrite = conflicts == ImportConflicts.OverwriteAll
                    || (conflicts == ImportConflicts.OverwriteOlder && string.CompareOrdinal(imported.LastChange, current.LastChange) > 0);
                if (!overwrite) { skipped++; continue; }
                overwritten++;
                if (dryRun) continue;
                current.Title = imported.Title;
                current.Content = imported.Content;
                current.TagsCsv = imported.TagsCsv;
                current.DefaultPasteMode = imported.DefaultPasteMode;
                current.SingleKeyShortcut = imported.SingleKeyShortcut;
                current.MultiKeyShortcut = imported.MultiKeyShortcut;
                current.LastChange = imported.LastChange;
            }
            else
            {
                added += item.SelfAndDescendants().OfType<Template>().Count();
                if (dryRun) continue;
                PrepareNew(item, parentId, taken!, renewAll: false);
                into.Add(item);
                AttachChangeTracking(item);
            }
        }
        return new ImportSummary(added, overwritten, skipped, met);
    }

    // An imported item becomes a plain local one; an id already in the tree (or every id, for a copy) is renewed.
    private static void PrepareNew(BaseItem item, Guid parentId, HashSet<Guid> taken, bool renewAll)
    {
        item.ParentId = parentId;
        item.SyncId = null;
        if (renewAll || !taken.Add(item.Id))
        {
            item.Id = Guid.NewGuid();
            taken.Add(item.Id);
        }
        foreach (var child in item.Children) PrepareNew(child, item.Id, taken, renewAll);
    }

    /// <summary>Export a folder's subtree to a standalone file (the folder becomes the root).</summary>
    public Task ExportFolderAsync(string path, Folder folder) =>
        ExportAsync(path, new Folder { Id = folder.Id, Title = folder.Title, LastChange = folder.LastChange }, folder.Children);

    /// <summary>Export templates and folders into one file, under a plain root, as <see cref="ExportTree"/> lays
    /// them out.</summary>
    public Task ExportItemsAsync(string path, IEnumerable<BaseItem> items) =>
        ExportAsync(path, new Folder { Id = Guid.Empty, Title = "Root" }, ExportTree(items.ToList()));

    /// <summary>Whether <paramref name="items"/> lie in one area: all local, or all in the same synced folder.</summary>
    public bool InOneArea(IEnumerable<BaseItem> items) =>
        items.Select(AreaOf).Distinct().Count() <= 1;

    /// <summary>The top level of an export of <paramref name="items"/>: in tree order, each under copies of its
    /// folders down from the deepest folder they all share (a synced folder's root at the most, which plays the
    /// file's root). The copies hold only the exported items; an item inside another exported folder comes along
    /// with it. Throws when the items lie in different areas.</summary>
    public List<BaseItem> ExportTree(IReadOnlyList<BaseItem> items)
    {
        if (!InOneArea(items)) throw new InvalidOperationException("Items from different areas can't be exported together.");

        var order = RootFolder.Children.Descendants().Select((item, index) => (item, index)).ToDictionary(p => p.item, p => p.index);
        var entries = items
            .SelectMany(i => i.IsSyncRoot ? i.Children.Select(c => (Item: c, Path: new List<BaseItem> { i })) : new[] { (Item: i, Path: PathTo(i)) })
            .ToList();
        entries = entries.Where(e => !entries.Any(other => e.Path.Contains(other.Item))).ToList();
        entries.Sort((a, b) => order.GetValueOrDefault(a.Item).CompareTo(order.GetValueOrDefault(b.Item)));

        int shared = entries.Count == 0 ? 0 : entries.Min(e => e.Path.Count);
        for (int level = 0; level < shared; level++)
            if (entries.Any(e => !ReferenceEquals(e.Path[level], entries[0].Path[level]))) { shared = level; break; }

        var top = new List<BaseItem>();
        var copies = new Dictionary<BaseItem, Folder>();
        foreach (var (item, path) in entries)
        {
            IList<BaseItem> into = top;
            foreach (var folder in path.Skip(shared))
            {
                if (!copies.TryGetValue(folder, out var copy))
                {
                    copy = new Folder { Id = folder.Id, Title = folder.Title, LastChange = folder.LastChange };
                    copies[folder] = copy;
                    into.Add(copy);
                }
                into = copy.Children;
            }
            into.Add(item);
        }
        return top;
    }

    // The folders above an item, from the top level down (empty for a top-level item).
    private List<BaseItem> PathTo(BaseItem item)
    {
        var path = new List<BaseItem>();
        for (var parent = RootFolder.Children.ParentOf(item); parent != null; parent = RootFolder.Children.ParentOf(parent))
            path.Insert(0, parent);
        return path;
    }

    // The synced folder's root for an item in (or being) a synced folder, null for a local one.
    private BaseItem? AreaOf(BaseItem item) =>
        RootFolder.Children.TopLevelOf(item) is { IsSyncRoot: true } root ? root : null;

    /// <summary>The local templates and folders (not the synced folders, which have their own files).</summary>
    public IEnumerable<BaseItem> LocalOnlyItems => RootFolder.Children.Where(c => !c.IsSyncRoot && c.SyncId == null).ToList();

    // Written as a copy without sync ids: an export is a plain file, and imports treat items carrying a sync
    // id as another PC's synced folder.
    private async Task ExportAsync(string path, Folder root, IEnumerable<BaseItem> items)
    {
        foreach (var item in items.ToList()) root.Children.Add(item);
        var copy = StorageService.ParseTree(StorageService.SerializeTree(root))!;
        static void Clean(BaseItem parent)
        {
            foreach (var child in parent.Children)
            {
                child.SyncId = null;
                child.ParentId = parent.Id;
                Clean(child);
            }
        }
        Clean(copy);
        await Storage.SaveAsync(path, copy);
    }

    /// <summary>Whether <paramref name="item"/> may move into <paramref name="target"/> (null = top level):
    /// not a pinned sync root, nothing read-only on either side, never into itself or its own subtree.</summary>
    public bool CanMove(BaseItem item, Folder? target)
    {
        if (item.IsSyncRoot || IsItemReadOnly(item) || RootFolder.Children.ContainerOf(item) == null) return false;
        if (target == null) return true;
        if (RootFolder.Children.ContainerOf(target) == null || IsItemReadOnly(target)) return false;
        return !item.SelfAndDescendants().Contains(target);
    }

    /// <summary>Moves <paramref name="item"/> to <paramref name="index"/> among <paramref name="target"/>'s
    /// children (null = top level, after the pinned sync folders) with one save. Moving into another area
    /// (sync folder or local) gives the item new file ids and resolves its shortcut clashes there.</summary>
    public Task<bool> MoveItemAsync(BaseItem item, Folder? target, int index) => MoveItemsAsync(new[] { item }, target, index);

    /// <summary>Moves several items together to <paramref name="index"/> among <paramref name="target"/>'s
    /// children, in their tree order, with one save. Nothing moves unless every one of them may go there.</summary>
    public async Task<bool> MoveItemsAsync(IReadOnlyList<BaseItem> items, Folder? target, int index)
    {
        if (items.Count == 0 || items.Any(i => !CanMove(i, target))) return false;
        var treeOrder = RootFolder.Children.Descendants().ToList();

        using (SuppressSaves())
        {
            var to = target?.Children ?? RootFolder.Children;
            foreach (var item in items.OrderBy(treeOrder.IndexOf))
            {
                var originArea = GetAreaKey(item);
                var from = RootFolder.Children.ContainerOf(item)!;
                int oldIndex = from.IndexOf(item);
                if (ReferenceEquals(from, to) && oldIndex < index) index--;

                from.RemoveAt(oldIndex);
                int first = target == null ? to.Count(c => c.IsSyncRoot) : 0;
                index = Math.Clamp(index, first, to.Count);
                to.Insert(index++, item);   // the next one goes right after it
                item.ParentId = target?.Id ?? Guid.Empty;

                if (GetAreaKey(item) != originArea)
                {
                    foreach (var moved in item.SelfAndDescendants()) moved.SyncId = null;   // the sync write assigns fresh ones
                    ResolveShortcutClashes(item);
                }
            }
        }
        await SaveDataAsync();
        return true;
    }

    // A duplicate single key is cleared; a duplicate multi key gets a numeric suffix (MSG -> MSG1).
    private void ResolveShortcutClashes(BaseItem movedItem)
    {
        var moved = movedItem.SelfAndDescendants().OfType<Template>().ToList();
        if (moved.Count == 0) return;

        var movedSet = new HashSet<Template>(moved);
        Guid area = GetAreaKey(movedItem);
        var areaTemplates = AllItems.Where(t => !movedSet.Contains(t) && GetAreaKey(t) == area).ToList();

        foreach (var t in moved)
        {
            if (!string.IsNullOrWhiteSpace(t.SingleKeyShortcut) &&
                areaTemplates.Any(o => string.Equals(o.SingleKeyShortcut, t.SingleKeyShortcut, StringComparison.OrdinalIgnoreCase)))
            {
                t.SingleKeyShortcut = string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(t.MultiKeyShortcut))
            {
                var taken = new HashSet<string>(
                    areaTemplates.Where(o => !string.IsNullOrWhiteSpace(o.MultiKeyShortcut)).Select(o => o.MultiKeyShortcut),
                    StringComparer.OrdinalIgnoreCase);

                if (taken.Contains(t.MultiKeyShortcut))
                {
                    string baseKey = t.MultiKeyShortcut;
                    int n = 1;
                    while (taken.Contains(baseKey + n)) n++;
                    t.MultiKeyShortcut = baseKey + n;
                }
            }

            areaTemplates.Add(t);   // so later moved siblings avoid it too
        }
    }

    #endregion

    #region Tracking & Persistence

    // Counted, so a sync re-apply nested in another suppressed change can't switch saving back on.
    private bool SavesSuppressed => Volatile.Read(ref _saveSuppressions) > 0;

    private SaveSuppression SuppressSaves()
    {
        Interlocked.Increment(ref _saveSuppressions);
        return new SaveSuppression(this);
    }

    private sealed class SaveSuppression(DataNode node) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                Interlocked.Decrement(ref node._saveSuppressions);
        }
    }

    public void AttachChangeTracking(BaseItem item)
    {
        foreach (var each in item.SelfAndDescendants())
        {
            each.PropertyChanged -= OnItemPropertyChanged;
            each.PropertyChanged += OnItemPropertyChanged;
            each.Children.CollectionChanged -= OnChildrenCollectionChanged;
            each.Children.CollectionChanged += OnChildrenCollectionChanged;
        }
    }

    // Derived properties (conflict flags) are not real data edits: changing them must not bump LastChange
    // or save, which would churn the sync file.
    private static readonly HashSet<string> _nonPersistedProps = new()
    {
        nameof(Template.HasSingleKeyConflict),
        nameof(Template.HasMultiKeyConflict),
        nameof(Template.HasSingleKeyCrossAreaWarning),
        nameof(Template.HasMultiKeyCrossAreaWarning),
    };

    private CancellationTokenSource? _editSaveDebounce;

    private void OnItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Template.SingleKeyShortcut) or nameof(Template.MultiKeyShortcut))
            ShortcutsChanged?.Invoke();

        if (SavesSuppressed || e.PropertyName == null || _nonPersistedProps.Contains(e.PropertyName)) return;
        if (sender is BaseItem item)
        {
            item.LastChange = DateTime.Now.ToString("yyyyMMddHHmmss");
            ScheduleEditSave();
        }
    }

    // Debounce the disk write for property edits: typing a title/tags/shortcut (and the editor's own
    // debounced content changes) coalesce into one save a short moment after the last edit, instead of
    // writing on every keystroke. Structural changes (add/delete/move) still call SaveDataAsync directly.
    private void ScheduleEditSave()
    {
        _editSaveDebounce?.Cancel();
        var cts = _editSaveDebounce = new CancellationTokenSource();
        _ = SaveAfterDelayAsync(cts.Token);
    }

    /// <summary>Writes debounced edits and settings changes now instead of after the delay (on exit).</summary>
    public async Task FlushPendingSaveAsync()
    {
        _editSaveDebounce?.Cancel();
        await SaveDataAsync();
        if (_settingsSaveDebounce is { IsCancellationRequested: false } pending)
        {
            pending.Cancel();
            await Storage.SaveSettingsAsync(CurrentSettings);
        }
    }

    private CancellationTokenSource? _settingsSaveDebounce;

    // Every settings change (a toggle, the hotkey, a generated connector token) is saved here, once,
    // shortly after the last change, so the pages never have to remember to save.
    private void ScheduleSettingsSave()
    {
        _settingsSaveDebounce?.Cancel();
        var cts = _settingsSaveDebounce = new CancellationTokenSource();
        _ = SaveSettingsAfterDelayAsync(cts);
    }

    private async Task SaveSettingsAfterDelayAsync(CancellationTokenSource cts)
    {
        await Task.Delay(500);
        if (cts.IsCancellationRequested) return;
        cts.Cancel();   // marks it done, so a flush doesn't write it a second time
        await Storage.SaveSettingsAsync(CurrentSettings);
    }

    private async Task SaveAfterDelayAsync(CancellationToken ct)
    {
        // No token on Task.Delay on purpose: passing it would throw TaskCanceledException on every
        // superseding keystroke (caught, but a noisy first-chance exception while typing). Instead the
        // delay always completes and we drop the save here if a newer edit has replaced it.
        await Task.Delay(500);
        if (!ct.IsCancellationRequested) await SaveDataAsync();
    }

    private async void OnChildrenCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (SavesSuppressed) return;
        if (e.NewItems != null)
        {
            foreach (BaseItem newItem in e.NewItems) AttachChangeTracking(newItem);
        }
        await SaveDataAsync();
    }

    public async Task SaveDataAsync()
    {
        if (SavesSuppressed || !_isInitialized) return;
        await _saveLock.WaitAsync();
        bool saved = false;
        try
        {
            await Storage.SaveAsync(_localDataPath, RootFolder);
            await WriteSyncSourcesAsync();
            saved = true;
        }
        finally
        {
            _saveLock.Release();
        }
        if (saved) DataSaved?.Invoke();
    }

    #endregion

    #region Sync

    /// <summary>Re-runs the sync reconcile at runtime (after settings change or an external file
    /// change), refreshes the tree via <see cref="TreeChanged"/>, and persists.</summary>
    public async Task ReapplySyncAsync()
    {
        if (!_isInitialized) return;

        // Serialize re-applies. A manual reorder, an add/remove, and the 4s external-change poll can
        // otherwise overlap and run two ApplySyncAsync at once, mutating RootFolder.Children (Clear +
        // re-add) concurrently across their file-read awaits — a garbled tree or a crash. Callers
        // await, so a re-apply that arrives mid-flight still runs once the current one finishes.
        await _syncApplyLock.WaitAsync();
        try
        {
            using (SuppressSaves()) await ApplySyncAsync();

            foreach (var item in RootFolder.Children) AttachChangeTracking(item);
            TreeChanged?.Invoke();
            await SaveDataAsync();
        }
        finally { _syncApplyLock.Release(); }
    }

    /// <summary>Writes the cached copy of <paramref name="source"/>'s folder out as its sync file at
    /// <paramref name="path"/> (where it was, or a new place) and links the source to it. False when there is
    /// no cached copy (an inactive source) or the file couldn't be written.</summary>
    public async Task<bool> RecreateSyncFileAsync(SyncSource source, string path)
    {
        if (RootFolder.Children.FirstOrDefault(c => c.Id == source.Id) is not Folder folder) return false;
        if (await Storage.SaveSharedAsync(path, BuildFileTree(folder)) != null || !File.Exists(path)) return false;

        source.Path = path;
        await SaveSyncSettingsAsync();
        await ReapplySyncAsync();
        return true;
    }

    /// <summary>Polls the active sources' files and re-applies sync if any changed EXTERNALLY.
    /// Cheap when nothing changed (just reads each file's timestamp; never opens/locks them).</summary>
    public async Task CheckSyncFilesForChangesAsync()
    {
        if (!_isInitialized) return;

        bool changed = false;
        foreach (var source in CurrentSyncSettings.Sources.Where(s => s.IsActive).ToList())
        {
            DateTime write = GetLastWriteSafe(source.Path);
            if (_lastSeenWrite.TryGetValue(source.Id, out var prev) && write != prev)
                changed = true;
            _lastSeenWrite[source.Id] = write;
        }

        if (changed) await ReapplySyncAsync();
        else if (CurrentSyncSettings.Sources.Any(s => s.HasWriteProblem)) await SaveDataAsync();   // retry the failed write
    }

    private static DateTime GetLastWriteSafe(string path)
    {
        try { return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : default; }
        catch { return default; }
    }

    /// <summary>Returns the sync source an item belongs to (via its top-level ancestor), or null
    /// if the item is local.</summary>
    public SyncSource? GetSyncSourceForItem(BaseItem item)
    {
        var top = RootFolder.Children.TopLevelOf(item);
        return top == null ? null : CurrentSyncSettings.Sources.FirstOrDefault(s => s.Id == top.Id);
    }

    public bool IsItemReadOnly(BaseItem item)
    {
        var src = GetSyncSourceForItem(item);
        return src != null && !src.AllowSave;
    }

    /// <summary>Area identity: the sync source's Id, or Guid.Empty for a local item.</summary>
    public Guid GetAreaKey(BaseItem item) => GetSyncSourceForItem(item)?.Id ?? Guid.Empty;

    /// <summary>Area priority: 0 = local (wins), then 1..N by active-source order. Resolves which
    /// template wins when a single key repeats across areas.</summary>
    public int GetSourcePriority(BaseItem item)
    {
        var src = GetSyncSourceForItem(item);
        if (src == null) return 0; // local always wins
        var active = CurrentSyncSettings.Sources.Where(s => s.IsActive).ToList();
        int idx = active.IndexOf(src);
        return idx < 0 ? int.MaxValue : idx + 1;
    }

    /// <summary>Highest-priority template for a single key (local first, then sync order).</summary>
    public Template? ResolveSingleKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        return AllItems
            .Where(t => string.Equals(t.SingleKeyShortcut, key, StringComparison.OrdinalIgnoreCase))
            .OrderBy(GetSourcePriority)
            .FirstOrDefault();
    }

    /// <summary>The multikey as it must be TYPED: prefix-namespaced in a sync folder (and-msg),
    /// else the raw shortcut. The template still stores just "msg".</summary>
    public string GetEffectiveMultiKey(Template t) => EffectiveMultiKey(t, GetSyncSourceForItem(t));

    private string EffectiveMultiKey(Template t, SyncSource? source) =>
        source != null && !string.IsNullOrEmpty(t.MultiKeyShortcut) && !string.IsNullOrWhiteSpace(source.ShortcutPrefix)
            ? source.ShortcutPrefix + (CurrentSyncSettings.Separator ?? "-") + t.MultiKeyShortcut
            : t.MultiKeyShortcut;

    /// <summary>Every template with its area and its multi key as typed, for <see cref="ShortcutValidator"/>.
    /// Looks up each top-level folder's sync source once instead of once per template.</summary>
    public IReadOnlyList<ShortcutValidator.Entry> ShortcutEntries()
    {
        var entries = new List<ShortcutValidator.Entry>();
        foreach (var top in RootFolder.Children)
        {
            var source = CurrentSyncSettings.Sources.FirstOrDefault(s => s.Id == top.Id);
            foreach (var t in top.SelfAndDescendants().OfType<Template>())
                entries.Add(new(t, source?.Id ?? Guid.Empty, EffectiveMultiKey(t, source)));
        }
        return entries;
    }

    /// <summary>Reconcile the tree with the sync sources: a pinned folder per active source
    /// (file merged with cache), inactive/removed dropped, synced folders first then local.</summary>
    private async Task ApplySyncAsync()
    {
        var sources = CurrentSyncSettings.Sources;

        // Split existing top-level children: a synced folder root is a Folder with a SyncId set.
        var cachedSynced = new Dictionary<Guid, Folder>();
        var localItems = new List<BaseItem>();
        foreach (var child in RootFolder.Children)
        {
            if (child is Folder f && child.SyncId != null) cachedSynced[child.Id] = f;
            else localItems.Add(child);
        }

        var syncedFolders = new List<Folder>();
        // Snapshot: the loop awaits a file read per source, during which the collection may change.
        foreach (var source in sources.Where(s => s.IsActive).ToList())
        {
            cachedSynced.TryGetValue(source.Id, out var folder);
            folder ??= new Folder { Id = source.Id };
            folder.IsSyncRoot = true;

            Folder? fileRoot = null;
            if (!string.IsNullOrWhiteSpace(source.Path) && File.Exists(source.Path))
            {
                bool locked = false;
                try { (fileRoot, locked) = await StorageService.ReadRootSharedAsync(source.Path); }
                catch { fileRoot = null; }
                // A file still locked (OneDrive upload, virus scan) is read again at the next poll: recording its
                // time would leave it unread, and an outside edit made meanwhile would later be overwritten.
                _lastSeenWrite[source.Id] = locked ? default : GetLastWriteSafe(source.Path);
            }

            if (fileRoot != null)
            {
                SyncEngine.Merge(folder, fileRoot, source.Name, source.Id, source.AllowSave);
                folder.SyncFileMissing = false;
            }
            else
            {
                // Missing/unreadable file: keep the cached content, flag it, use the settings name.
                folder.Title = source.Name;
                folder.SyncId ??= Guid.Empty;
                folder.SyncFileMissing = true;
            }

            syncedFolders.Add(folder);
        }

        // Synced folders first (settings order), then local. Dropped sources aren't re-added.
        RootFolder.Children.Clear();
        foreach (var f in syncedFolders) RootFolder.Children.Add(f);
        foreach (var it in localItems) RootFolder.Children.Add(it);

        foreach (var source in sources.ToList()) source.RefreshFileState();
    }

    /// <summary>Writes each active, save-enabled source's folder back to its file (skips missing
    /// files). StorageService skips the write when the content is unchanged. A failed write is shown on the
    /// source and its folder until a later write (the next save, or the poll's retry) succeeds.</summary>
    private async Task WriteSyncSourcesAsync()
    {
        // Snapshot: the loop awaits file writes, during which another flow (opening a .ttmdata, a
        // Sync-settings edit, a save/poll timer) may add or remove a source and invalidate the iterator.
        foreach (var source in CurrentSyncSettings.Sources.ToList())
        {
            var folder = RootFolder.Children.FirstOrDefault(c => c.Id == source.Id) as Folder;
            if (!source.IsActive || !source.AllowSave || folder == null || folder.SyncFileMissing)
            {
                SetWriteProblem(source, folder, null);   // nothing written, nothing failing
                continue;
            }
            string? problem;
            try { problem = await Storage.SaveSharedAsync(source.Path, BuildFileTree(folder)); }
            catch (Exception ex) { problem = ex.Message; }
            if (problem == null) _lastSeenWrite[source.Id] = GetLastWriteSafe(source.Path); // don't treat our write as external
            SetWriteProblem(source, folder, problem);
        }
    }

    private static void SetWriteProblem(SyncSource source, Folder? folder, string? problem)
    {
        string? message = problem == null ? null
            : $"Couldn't save to the sync file: {problem} Your changes are kept here and saved to the file as soon as it can be written.";
        source.WriteProblem = message;
        if (folder != null) folder.SyncWriteProblem = message;
    }

    /// <summary>Convert a synced folder's subtree into a sync-file tree keyed by SyncId
    /// (locally-added items get a fresh SyncId here).</summary>
    internal static Folder BuildFileTree(Folder syncFolder)
    {
        var root = new Folder
        {
            Id = syncFolder.SyncId ?? Guid.Empty,
            // Fixed root title so the shared file doesn't churn with each PC's local name.
            Title = "Root",
            LastChange = syncFolder.LastChange,
        };
        foreach (var child in syncFolder.Children)
            root.Children.Add(ToFileItem(child, root.Id));
        return root;
    }

    private static BaseItem ToFileItem(BaseItem appItem, Guid parentSyncId)
    {
        appItem.SyncId ??= Guid.NewGuid();   // assign a file id to locally-added items
        Guid fileId = appItem.SyncId.Value;

        BaseItem fileItem = appItem is Template t
            ? new Template
            {
                Title = t.Title,
                Content = t.Content,
                SingleKeyShortcut = t.SingleKeyShortcut,
                MultiKeyShortcut = t.MultiKeyShortcut,
                TagsCsv = t.TagsCsv,
                DefaultPasteMode = t.DefaultPasteMode,
            }
            : new Folder { Title = appItem.Title };

        fileItem.Id = fileId;
        fileItem.ParentId = parentSyncId;
        fileItem.LastChange = appItem.LastChange;
        foreach (var c in appItem.Children)
            fileItem.Children.Add(ToFileItem(c, fileId));
        return fileItem;
    }

    #endregion
}