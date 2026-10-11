using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using TextTemplateManager.Data;
using TextTemplateManager.Helpers;
using TextTemplateManager.Models;

namespace TextTemplateManager
{
    /// <summary>Lists the sync sources and persists changes to sync.ttmsettings.</summary>
    public sealed partial class SyncSettingsPage : Page
    {
        /// <summary>What MainPage passes on navigation: the store and the window that owns the file pickers.</summary>
        public sealed record Parameters(DataNode Store, IntPtr Hwnd);

        private DataNode _store = null!;   // set in OnNavigatedTo
        private SyncSettings _sync = new();
        private IntPtr _hwnd;
        private bool _loading;

        public SyncSettingsPage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            var parameters = (Parameters)e.Parameter;
            _store = parameters.Store;
            _hwnd = parameters.Hwnd;

            _sync = _store.CurrentSyncSettings;   // separator already valid: the store migrates it on load

            _loading = true;
            SeparatorBox.SelectedIndex = _sync.Separator switch { "." => 1, "" => 2, _ => 0 };
            _loading = false;

            SourceList.ItemsSource = _sync.Sources;
            foreach (var s in _sync.Sources) Subscribe(s);
            _sync.Sources.CollectionChanged += Sources_CollectionChanged;
            UpdateMoveAffordances();

            base.OnNavigatedTo(e);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            _sync.Sources.CollectionChanged -= Sources_CollectionChanged;
            foreach (var s in _sync.Sources) Unsubscribe(s);
            _ = SaveAsync();
            base.OnNavigatedFrom(e);
        }

        private void SyncHelpButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => SyncHelpTip.IsOpen = true;

        // ---- Persistence ----
        private System.Threading.Tasks.Task SaveAsync() => _store.SaveSyncSettingsAsync();

        private void Subscribe(SyncSource s) => s.PropertyChanged += Source_PropertyChanged;
        private void Unsubscribe(SyncSource s) => s.PropertyChanged -= Source_PropertyChanged;

        private async void Source_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // Computed/transient props: reflect in the UI but never persist or reconcile.
            if (e.PropertyName is nameof(SyncSource.IsFileMissing)
                or nameof(SyncSource.CanMoveUp) or nameof(SyncSource.CanMoveDown)) return;
            await SaveAsync();

            switch (e.PropertyName)
            {
                case nameof(SyncSource.IsActive):   // load/drop the folder
                case nameof(SyncSource.Path):       // re-link to a different file
                    await _store.ReapplySyncAsync();
                    break;
                case nameof(SyncSource.Name):       // just rename the folder live
                    if (sender is SyncSource s) UpdateFolderTitle(s);
                    break;
                    // AllowSave (write-through checks it live) and ShortcutPrefix need no reconcile.
            }
        }

        private void UpdateFolderTitle(SyncSource s)
        {
            if (_store.LocalItems.FirstOrDefault(c => c.Id == s.Id) is BaseItem folder)
                folder.Title = s.Name;
        }

        private async void Sources_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null) foreach (SyncSource s in e.OldItems) Unsubscribe(s);
            if (e.NewItems != null) foreach (SyncSource s in e.NewItems) Subscribe(s);
            UpdateMoveAffordances();
            await SaveAsync();
            if (e.Action != NotifyCollectionChangedAction.Move)
                await _store.ReapplySyncAsync();   // add / remove -> refresh tree (Move handled by buttons)
        }

        private async void Separator_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            _sync.Separator = SeparatorBox.SelectedIndex switch { 1 => ".", 2 => "", _ => "-" };
            await SaveAsync();
        }

        // Prefixes are always uppercase (matches how the multi-key buffer is shown). The x:Bind
        // carries the uppercased text into ShortcutPrefix.
        private void Prefix_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loading || sender is not TextBox tb) return;
            string upper = tb.Text.ToUpperInvariant();
            if (tb.Text == upper) return;
            int caret = tb.SelectionStart;
            tb.Text = upper;                                    // re-enters, but now equal -> no loop
            tb.SelectionStart = Math.Min(caret, tb.Text.Length);
        }

        // ---- Add / create ----
        private async void AddExisting_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (await DataFilePicker.PickToOpenAsync(_hwnd) is not { } path) return;
            AddSource(path);
        }

        private async void CreateNew_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (await DataFilePicker.PickToSaveAsync(_hwnd, "Sync") is not { } path) return;

            // Write an empty, valid sync file (a serialized root Folder).
            await _store.Storage.SaveAsync(path, new Folder { Id = Guid.Empty, Title = "Root" });
            AddSource(path);
        }

        private void AddSource(string path) => _sync.Sources.Add(new SyncSource
        {
            Name = System.IO.Path.GetFileNameWithoutExtension(path),
            Path = path,
            IsActive = true,
            AllowSave = false,
        });

        // ---- Per-row actions ----
        private static SyncSource? Source(object sender) =>
            (sender as Microsoft.UI.Xaml.FrameworkElement)?.DataContext as SyncSource;

        private async void Relink_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (Source(sender) is not SyncSource s) return;

            if (await DataFilePicker.PickToOpenAsync(_hwnd) is not { } path) return;
            s.Path = path; // raises IsFileMissing + persists via Source_PropertyChanged
        }

        private async void MoveUp_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (Source(sender) is not SyncSource s) return;
            int i = _sync.Sources.IndexOf(s);
            if (i > 0) { _sync.Sources.Move(i, i - 1); await ReapplyAsync(); }
        }

        private async void MoveDown_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (Source(sender) is not SyncSource s) return;
            int i = _sync.Sources.IndexOf(s);
            if (i >= 0 && i < _sync.Sources.Count - 1) { _sync.Sources.Move(i, i + 1); await ReapplyAsync(); }
        }

        // Grey out ↑ on the first row and ↓ on the last so priority can't be nudged past the ends.
        private void UpdateMoveAffordances()
        {
            var sources = _sync.Sources;
            for (int i = 0; i < sources.Count; i++)
            {
                sources[i].CanMoveUp = i > 0;
                sources[i].CanMoveDown = i < sources.Count - 1;
            }
        }

        private async Task ReapplyAsync()
        {
            await SaveAsync();
            await _store.ReapplySyncAsync();
        }

        private void Remove_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (Source(sender) is SyncSource s) _sync.Sources.Remove(s);
        }
    }
}
