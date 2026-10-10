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

        // Autostart is registry-only now (see StartupManager) — nothing to seed or reconcile here.
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

    /// <summary>Replaces the local items with <paramref name="items"/> in one step and one save. The pinned
    /// sync folders stay; top-level sync folders inside <paramref name="items"/> (a full export contains
    /// snapshots of them) are skipped, since the sync files themselves are the source of truth.</summary>
    public async Task ReplaceLocalItemsAsync(IEnumerable<BaseItem> items)
    {
        using (SuppressSaves())
        {
            var syncRoots = RootFolder.Children.Where(c => c.IsSyncRoot).ToList();
            RootFolder.Children.Clear();
            foreach (var root in syncRoots) RootFolder.Children.Add(root);
            foreach (var item in items.Where(i => i.SyncId == null))
            {
                item.ParentId = Guid.Empty;
                RootFolder.Children.Add(item);
                AttachChangeTracking(item);
            }
        }
        await SaveDataAsync();
    }

    public async Task ExportDataAsync(string path)
    {
        await Storage.SaveAsync(path, RootFolder);
    }

    /// <summary>Export a folder's subtree to a standalone file (the folder becomes the root).</summary>
    public async Task ExportFolderAsync(string path, Folder folder)
    {
        var root = new Folder { Id = folder.Id, Title = folder.Title, LastChange = folder.LastChange };
        foreach (var child in folder.Children) root.Children.Add(child);
        await Storage.SaveAsync(path, root);
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
    public async Task<bool> MoveItemAsync(BaseItem item, Folder? target, int index)
    {
        if (!CanMove(item, target)) return false;

        using (SuppressSaves())
        {
            var originArea = GetAreaKey(item);
            var from = RootFolder.Children.ContainerOf(item)!;
            var to = target?.Children ?? RootFolder.Children;
            int oldIndex = from.IndexOf(item);
            if (ReferenceEquals(from, to) && oldIndex < index) index--;

            from.RemoveAt(oldIndex);
            int first = target == null ? to.Count(c => c.IsSyncRoot) : 0;
            to.Insert(Math.Clamp(index, first, to.Count), item);
            item.ParentId = target?.Id ?? Guid.Empty;

            if (GetAreaKey(item) != originArea)
            {
                foreach (var moved in item.SelfAndDescendants()) moved.SyncId = null;   // the sync write assigns fresh ones
                ResolveShortcutClashes(item);
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

    // Transient/derived properties (expansion, list display projections, conflict flags) are not
    // real data edits: changing them must not bump LastChange or save, which would churn the sync
    // file — e.g. Quick Paste stamps EffectiveMultiKey/SourceLabel on the tree templates when it opens.
    private static readonly HashSet<string> _nonPersistedProps = new()
    {
        nameof(BaseItem.IsExpanded),
        nameof(Template.EffectiveMultiKey),
        nameof(Template.SourceLabel),
        nameof(Template.IsLocalSource),
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
                try { fileRoot = await StorageService.LoadRootSharedAsync(source.Path); }
                catch { fileRoot = null; }
                _lastSeenWrite[source.Id] = GetLastWriteSafe(source.Path); // ignore our own reads later
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
    }

    /// <summary>Writes each active, save-enabled source's folder back to its file (skips missing
    /// files). StorageService skips the write when the content is unchanged.</summary>
    private async Task WriteSyncSourcesAsync()
    {
        // Snapshot: the loop awaits file writes, during which another flow (opening a .ttmdata, a
        // Sync-settings edit, a save/poll timer) may add or remove a source and invalidate the iterator.
        foreach (var source in CurrentSyncSettings.Sources.Where(s => s.IsActive && s.AllowSave).ToList())
        {
            if (RootFolder.Children.FirstOrDefault(c => c.Id == source.Id) is not Folder folder) continue;
            if (folder.SyncFileMissing) continue;
            try
            {
                await Storage.SaveSharedAsync(source.Path, BuildFileTree(folder));
                _lastSeenWrite[source.Id] = GetLastWriteSafe(source.Path); // don't treat our write as external
            }
            catch { /* best effort */ }
        }
    }

    /// <summary>Convert a synced folder's subtree into a sync-file tree keyed by SyncId
    /// (locally-added items get a fresh SyncId here).</summary>
    private static Folder BuildFileTree(Folder syncFolder)
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