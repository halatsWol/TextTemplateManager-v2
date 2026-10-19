using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TextTemplateManager.Common;
using TextTemplateManager.Controls;
using TextTemplateManager.Data;
using TextTemplateManager.Helpers;
using TextTemplateManager.Models;
using TextTemplateManager.Services.Pasting;
using TextTemplateManager.Services.Platform;
using TextTemplateManager.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI.Core;

namespace TextTemplateManager
{
    public sealed partial class MainPage : Page
    {
        public MainViewModel ViewModel { get; }
        private readonly DataNode _store;


        private bool _dialogOpen;                // only one ContentDialog may be open at a time

        public bool IsDialogOpen => _dialogOpen;



        /// <summary>The main window's page over <paramref name="store"/>; <paramref name="tour"/>: the page showing
        /// the guided tour's sample data, which registers nothing with the app.</summary>
        public MainPage(DataNode store, bool tour = false)
        {
            _store = store;
            this.InitializeComponent();

            // handledEventsToo: the TreeView marks Enter/Space handled, so a plain KeyDown handler
            // never sees them — attach here to still get folder expand/collapse on those keys.
            ItemTreeView.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(ItemTreeView_KeyDown), true);
            ItemTreeView.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(ItemTreeView_PointerPressed), true);
            ItemTreeView.PreviewKeyDown += ItemTreeView_PreviewKeyDown;
            TreeViewNames.NameForScreenReaders(ItemTreeView, "Templates");
            // The list marks drag events over its empty space handled; a drop there moves to the end.
            ItemTreeView.AddHandler(UIElement.DragOverEvent, new DragEventHandler(ItemTreeView_DragOver), true);
            ItemTreeView.AddHandler(UIElement.DropEvent, new DragEventHandler(ItemTreeView_Drop), true);
            // Esc and the arrows are taken before the AutoSuggestBox sees them: its own Esc restores the last typed
            // text, and it takes the arrows for its suggestion list (and passes them on, so a KeyDown handler ran twice).
            SearchBox.PreviewKeyDown += SearchBox_PreviewKeyDown;


            ViewModel = new MainViewModel(store);
            // Keep the panels from inheriting the page until x:Bind sets their DataContext,
            // otherwise their {Binding}s briefly resolve against MainPage and log path errors.
            TemplatePanel.DataContext = null;
            FolderPanel.DataContext = null;
            this.DataContext = this;

            _store.DataSaved += () => DispatcherQueue.TryEnqueue(ShowSaveNotification);
            ViewModel.ConflictsChanged += ShowShortcutConflicts;
            ViewModel.SelectionLookChanged += () => DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, ApplySelectionLookToAllRows);

            ViewModel.LeavingEditedItem += OnLeavingEditedItem;
            ViewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ViewModel.EditedItem))
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        PushTemplateToEditor();
                        // Read-only synced item: the fields are read-only via their XAML bindings (text
                        // stays selectable/copyable, like the editor's); dimming is just the visual cue.
                        bool editable = ViewModel.IsSelectedEditable;
                        TemplateTitleBox.Opacity = editable ? 1.0 : 0.6;
                        TemplateMetaPanel.Opacity = editable ? 1.0 : 0.6;
                        FolderPanel.Opacity = editable ? 1.0 : 0.6;

                        // A sync-root folder's name is owned by Settings ▸ Sync — never editable here.
                        bool isSyncRoot = ViewModel.SelectedItem is Folder folder && folder.IsSyncRoot;
                        FolderTitleBox.IsReadOnly = isSyncRoot || !editable;
                        ToolTipService.SetToolTip(FolderTitleBox,
                            isSyncRoot ? "The sync folder name is set in Settings ▸ Sync" : null);
                    });
                }
            };

            // On exit, take the editor's content that it hasn't reported yet; the data save runs after.
            if (!tour) (Application.Current as App)?.AddExitFlush(Editor.FlushAsync);

            Settings.Store = store;
            SearchOptions.Settings = store.CurrentSettings;
            Settings.Closed += Settings_Closed;
            Editor.FocusSearchRequested += (_, _) => FocusSearchBox();
            Editor.SaveRequested += async (_, _) => await SaveAsync();
            Editor.TabOut += Editor_TabOut;
            TreeToolbar.GotFocus += (_, e) => { if (e.OriginalSource is Button button) _lastToolbarButton = button; };
            Editor.ContentLoaded += ViewModel.EditorLoaded;
        }

        private async Task SaveAsync()
        {
            await Editor.FlushAsync();   // what was typed in the last moment, not reported yet
            await ViewModel.SaveEditsAsync();
        }

        private async void Save_Click(object sender, RoutedEventArgs e) => await SaveAsync();

        private void ExpandAll_Click(object sender, RoutedEventArgs e) => ViewModel.SetAllExpanded(true);
        private void CollapseAll_Click(object sender, RoutedEventArgs e) => ViewModel.SetAllExpanded(false);

        private async void SaveAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            args.Handled = true;
            if (Tour.IsRunning) return;
            await SaveAsync();
        }

        public enum UnsavedAnswer { Save, DontSave, Cancel }

        /// <summary>Asks what to do with the edited item's unsaved changes. Cancel also when the question can't be
        /// shown (another dialog is open).</summary>
        public async Task<UnsavedAnswer> AskUnsavedChangesAsync()
        {
            var dialog = new ContentDialog
            {
                Title = "Save changes?",
                Content = $"\"{ViewModel.EditedItem?.Title}\" has changes that aren't saved.",
                PrimaryButtonText = "Save",
                SecondaryButtonText = "Don't save",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
            };
            return await ShowDialogAsync(dialog) switch
            {
                ContentDialogResult.Primary => UnsavedAnswer.Save,
                ContentDialogResult.Secondary => UnsavedAnswer.DontSave,
                _ => UnsavedAnswer.Cancel,
            };
        }

        /// <summary>Before the window closes or the app exits: asks about unsaved edits (autosave off). False when the
        /// user cancels, so nothing closes.</summary>
        public async Task<bool> ResolveUnsavedEditsAsync()
        {
            await Editor.FlushAsync();
            if (!ViewModel.HasUnsavedChanges) return true;
            switch (await AskUnsavedChangesAsync())
            {
                case UnsavedAnswer.Save: await ViewModel.SaveEditsAsync(); return true;
                case UnsavedAnswer.DontSave: ViewModel.DiscardEdits(); return true;
                default: return false;
            }
        }

        /// <summary>Windows is signing out or shutting down: nothing can be asked, so unsaved edits are saved.</summary>
        public async Task KeepUnsavedEditsAsync()
        {
            await Editor.FlushAsync();
            await ViewModel.SaveEditsAsync();
        }

        // Another item was selected (autosave off): the editor's last content first, then ask if anything is unsaved.
        private async void OnLeavingEditedItem()
        {
            await Editor.FlushAsync();
            if (!ViewModel.HasUnsavedChanges) { await ViewModel.ResolveSwitchAsync(save: false); return; }
            switch (await AskUnsavedChangesAsync())
            {
                case UnsavedAnswer.Save: await ViewModel.ResolveSwitchAsync(save: true); break;
                case UnsavedAnswer.DontSave: await ViewModel.ResolveSwitchAsync(save: false); break;
                default: ViewModel.CancelSwitch(); break;
            }
        }

        // Strips what the stored shortcut can't hold while typing, keeping the caret in place (the binding
        // doesn't push the model's cleaned value back into the box it came from).
        private void Shortcut_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is not TextBox tb) return;
            string filtered = TextTemplateManager.Models.Template.CleanShortcut(tb.Text);
            if (tb.Text == filtered) return;
            int removed = tb.Text.Length - filtered.Length;
            int caret = tb.SelectionStart;
            tb.Text = filtered;                                       // re-enters, but now equal -> no loop
            tb.SelectionStart = Math.Clamp(caret - removed, 0, filtered.Length);
        }

        private Microsoft.UI.Dispatching.DispatcherQueueTimer? _saveNotifTimer;
        private Storyboard? _saveFade;

        private void ShowSaveNotification()
        {
            _saveFade?.Stop();
            SaveNotification.Opacity = 1;
            _saveNotifTimer ??= CreateSaveNotifTimer();
            _saveNotifTimer.Stop();
            _saveNotifTimer.Start();         // restart the 3s window on each save
        }

        private Microsoft.UI.Dispatching.DispatcherQueueTimer CreateSaveNotifTimer()
        {
            var t = DispatcherQueue.CreateTimer();
            t.Interval = TimeSpan.FromSeconds(3);
            t.Tick += (_, _) =>
            {
                t.Stop();
                var fade = new DoubleAnimation { To = 0, Duration = new Duration(TimeSpan.FromMilliseconds(500)) };
                Storyboard.SetTarget(fade, SaveNotification);
                Storyboard.SetTargetProperty(fade, "Opacity");
                _saveFade = new Storyboard();
                _saveFade.Children.Add(fade);
                _saveFade.Begin();
            };
            return t;
        }

        public void ClearSearch()
        {
            if (ViewModel != null) ViewModel.SearchText = string.Empty;
        }


        // Shows the selected template in the editor (an empty editor for a folder or no selection).
        private void PushTemplateToEditor() => Editor.Show(ViewModel.EditedTemplate, ViewModel.IsSelectedEditable);
        #region Menu Handlers

        private void MainHelpButton_Click(object sender, RoutedEventArgs e) => MainHelpTip.IsOpen = true;

        private void AddTemplate_Click(object sender, RoutedEventArgs e) => ViewModel.AddTemplateCommand.Execute(null);
        private void AddFolder_Click(object sender, RoutedEventArgs e) => ViewModel.AddFolderCommand.Execute(null);
        private async void DeleteItem_Click(object sender, RoutedEventArgs e) => await ConfirmAndDeleteAsync();

        /// <summary>Tree context menu: Duplicate/Copy/Copy As for templates, Export and Delete for any item but a
        /// pinned sync folder; on one of several selected items, Export and Delete for all of them.</summary>
        private void Tree_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not TreeNode { Item: var item }) return;

            var flyout = new MenuFlyout();

            // On one of several selected items: the menu acts on all of them.
            if (ViewModel.IsInSelection(item))
            {
                int count = ViewModel.SelectedItems.Count;
                var exportAll = new MenuFlyoutItem
                {
                    Text = ViewModel.CanExportSelection ? $"Export {count} items…" : ExportLabel(false),
                    Icon = new SymbolIcon(Symbol.Save),
                    IsEnabled = ViewModel.CanExportSelection,
                };
                exportAll.Click += async (_, _) => await ViewModel.ExportAsync(ViewModel.SelectedItems);
                flyout.Items.Add(exportAll);
                if (ViewModel.DeletableSelection.Count > 0)
                {
                    flyout.Items.Add(new MenuFlyoutSeparator());
                    var deleteAll = new MenuFlyoutItem { Text = $"Delete {ViewModel.DeletableSelection.Count} items", Icon = new SymbolIcon(Symbol.Delete) };
                    deleteAll.Click += async (_, _) => await ConfirmAndDeleteAsync();
                    flyout.Items.Add(deleteAll);
                }
                flyout.ShowAt(fe, new FlyoutShowOptions { Position = e.GetPosition(fe) });
                return;
            }

            ViewModel.SelectedItem = item;   // so the panel and delete act on it

            if (item is Template t)
            {
                var dup = new MenuFlyoutItem { Text = "Duplicate", Icon = new SymbolIcon(Symbol.Copy) };
                dup.Click += (_, _) => ViewModel.CloneItemCommand.Execute(null);
                flyout.Items.Add(dup);

                var copy = new MenuFlyoutItem { Text = $"Copy ({PasteModeLabel.For(t.DefaultPasteMode)})" };
                copy.Click += (_, _) => _ = PasteService.CopyToClipboardAsync(t.Content, t.DefaultPasteMode);
                flyout.Items.Add(copy);

                var copyAs = new MenuFlyoutSubItem { Text = "Copy As" };
                foreach (PasteMode mode in ViewModel.PasteModes)
                {
                    var captured = mode;
                    var mi = new MenuFlyoutItem { Text = PasteModeLabel.For(mode) };
                    mi.Click += (_, _) => _ = PasteService.CopyToClipboardAsync(t.Content, captured);
                    copyAs.Items.Add(mi);
                }
                flyout.Items.Add(copyAs);
            }

            if (!item.IsSyncRoot)   // a pinned sync folder already is a file
            {
                var export = new MenuFlyoutItem { Text = "Export…", Icon = new SymbolIcon(Symbol.Save) };
                export.Click += async (_, _) => await ViewModel.ExportAsync(new[] { item });
                flyout.Items.Add(export);
            }

            // Sync-folder roots are managed in Settings ▸ Sync, and save-off (read-only) sync items
            // can't be modified — no Delete for either.
            if (!item.IsSyncRoot && !ViewModel.IsReadOnly(item))
            {
                if (flyout.Items.Count > 0) flyout.Items.Add(new MenuFlyoutSeparator());
                var del = new MenuFlyoutItem { Text = "Delete", Icon = new SymbolIcon(Symbol.Delete) };
                del.Click += async (_, _) => await ConfirmAndDeleteAsync();
                flyout.Items.Add(del);
            }

            if (flyout.Items.Count > 0)
                flyout.ShowAt(fe, new FlyoutShowOptions { Position = e.GetPosition(fe) });
        }

        /// <summary>Delete the selected item(s); confirm for templates, non-empty folders and several items, but not
        /// for one empty folder.</summary>
        private async Task ConfirmAndDeleteAsync()
        {
            var items = ViewModel.DeletableSelection;   // never a pinned sync folder or a save-off (read-only) sync item
            if (items.Count == 0) return;
            var item = items[0];

            string? message = null;

            if (items.Count > 1)
            {
                int inside = items.Sum(i => i.SelfAndDescendants().Count() - 1);
                message = $"Delete these {items.Count} items" + (inside > 0 ? $" and the {inside} item(s) inside their folders" : "") +
                          "? This cannot be undone.";
                int skipped = ViewModel.SelectedItems.Count - items.Count;
                if (skipped > 0) message += $"\n\n{skipped} selected item(s) can't be deleted here (synced folders or read-only sync items) and stay.";
            }
            else if (item is Folder folder && folder.Children.Count > 0)
            {
                int childCount = folder.SelfAndDescendants().Count() - 1; // exclude the folder itself
                message = $"Delete the folder \"{item.Title}\" and its {childCount} item(s)?\n\n" +
                          "All items inside this folder will be deleted as well. This cannot be undone.";
            }
            else if (item is Template)
            {
                message = $"Delete the template \"{item.Title}\"? This cannot be undone.";
            }
            // else: empty folder -> no confirmation needed.

            if (message != null)
            {
                var dialog = new ContentDialog
                {
                    Title = "Confirm deletion",
                    Content = message,
                    PrimaryButtonText = "Delete",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Close,
                };

                if (await ShowDialogAsync(dialog) != ContentDialogResult.Primary) return;
            }

            ViewModel.DeleteItemCommand.Execute(null);
        }
        // Adds a .ttmdata file's templates to the tree, after asking where they go and what to do with ones that
        // already exist.
        private async void Import_Click(object sender, RoutedEventArgs e)
        {
            var (result, file) = await ViewModel.PickImportFileAsync();
            if (result == MainViewModel.ImportPick.Unreadable)
            {
                await ShowMessageAsync("Nothing imported", "The file couldn't be read as TextTemplateManager data. Nothing was changed.");
                return;
            }
            if (file == null) return;
            if (file.Templates == 0 && file.Folders == 0)
            {
                await ShowMessageAsync("Nothing imported", $"\"{file.Name}\" holds no templates or folders.");
                return;
            }

            var newFolder = new RadioButton { Content = $"Into a new folder \"{file.Name}\"", GroupName = "ImportWhere" };
            var direct = new RadioButton { Content = "Directly into the tree (folders with the same name are merged)", GroupName = "ImportWhere", IsChecked = true };
            var overwriteAll = new RadioButton { Content = "Overwrite all of them", GroupName = "ImportConflicts" };
            var overwriteOlder = new RadioButton { Content = "Overwrite only where the imported one is more recent", GroupName = "ImportConflicts", IsChecked = true };
            var skip = new RadioButton { Content = "Skip them, keep the existing ones", GroupName = "ImportConflicts" };
            var conflicts = new StackPanel { Spacing = 4, Margin = new Thickness(0, 12, 0, 0) };
            conflicts.Children.Add(new TextBlock
            {
                Text = $"{file.Conflicts} of these template(s) already exist (same template, or same title in the same folder):",
                TextWrapping = TextWrapping.Wrap,
            });
            foreach (var choice in new[] { overwriteAll, overwriteOlder, skip }) conflicts.Children.Add(choice);
            void UpdateConflicts() => conflicts.Visibility = direct.IsChecked == true && file.Conflicts > 0 ? Visibility.Visible : Visibility.Collapsed;
            direct.Checked += (_, _) => UpdateConflicts();
            newFolder.Checked += (_, _) => UpdateConflicts();
            UpdateConflicts();

            var content = new StackPanel { Spacing = 4 };
            content.Children.Add(new TextBlock
            {
                Text = $"\"{file.Name}\" holds {file.Templates} template(s) in {file.Folders} folder(s). Where should they go?",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8),
            });
            content.Children.Add(newFolder);
            content.Children.Add(direct);
            content.Children.Add(conflicts);

            var dialog = new ContentDialog
            {
                Title = "Import",
                Content = content,
                PrimaryButtonText = "Import",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
            };
            if (await ShowDialogAsync(dialog) != ContentDialogResult.Primary) return;

            var how = overwriteAll.IsChecked == true ? ImportConflicts.OverwriteAll
                : skip.IsChecked == true ? ImportConflicts.Skip : ImportConflicts.OverwriteOlder;
            await ViewModel.ImportAsync(file, newFolder.IsChecked == true, how);
        }
        // The selected folder(s) and template(s), or all local templates and folders when nothing is selected.
        private async void Export_Click(object sender, RoutedEventArgs e) => await ViewModel.ExportAsync(ViewModel.SelectedItems);

        // A disabled menu item shows no tooltip, so the label says why.
        public string ExportLabel(bool canExport) => canExport ? "Export…" : "Export… (not across areas)";

        private void OpenPreferences_Click(object sender, RoutedEventArgs e) => Settings.Open();

        private async void StartTutorial_Click(object sender, RoutedEventArgs e)
        {
            if (Application.Current is App app) await app.StartTutorialAsync();
        }

        /// <summary>Leaves the Settings view if it is open. Called when the window is hidden to the tray so
        /// that reopening lands on the template list, rather than resuming wherever Settings was left —
        /// and so its normal teardown (reload the tree, refresh conflicts) still runs.</summary>
        public void CloseSettingsIfOpen()
        {
            if (Settings.IsOpen) Settings.Close();
        }

        // Leaving Settings: rebuild the tree so sync-folder order changes are reflected, and revalidate
        // (prefixes may have changed; also reflects "hide cross-area warnings").
        private void Settings_Closed(object? sender, EventArgs e)
        {
            ViewModel.ReloadTree();
            ViewModel.ValidateAllShortcuts();
        }

        // ---- File association (.ttmdata) ----

        /// <summary>Opens the in-window Settings directly on the Sync page.</summary>
        public void OpenSyncSettings() => Settings.Open(SettingsPage.Sync);

        /// <summary>Handles a .ttmdata opened via the file association: links it as a sync source
        /// (unless it's this app's own data file or already linked), then shows Sync settings.</summary>
        public async void HandleOpenTtmDataFile(string path)
        {
            string full;
            try { full = System.IO.Path.GetFullPath(path); }
            catch { return; }
            if (!System.IO.File.Exists(full)) return;

            // The app's own primary data file is never linked as a sync source.
            if (string.Equals(full, _store.Storage.GetDataPath(), StringComparison.OrdinalIgnoreCase))
            {
                OpenSyncSettings();
                await ShowMessageAsync("Not added as a sync source",
                    "The added file is this app's own data file, so it can't be linked as a sync source.");
                return;
            }

            var sync = _store.CurrentSyncSettings;
            bool already = sync.Sources.Any(s => string.Equals(s.Path, full, StringComparison.OrdinalIgnoreCase));
            if (!already)
            {
                sync.Sources.Add(new SyncSource
                {
                    Name = System.IO.Path.GetFileNameWithoutExtension(full),
                    Path = full,
                    IsActive = true,
                    AllowSave = false,
                });
                await _store.SaveSyncSettingsAsync();
                await _store.ReapplySyncAsync();
            }

            OpenSyncSettings();
        }

        private const string GitHubUrl = "https://github.com/halatsWol/TextTemplateManager-v2";
        private const string ContactEmail = "contact@kmarflow.com";

        private void GoToGitHub_Click(object sender, RoutedEventArgs e) => ExternalLinks.Open(GitHubUrl);

        private async void OpenManual_Click(object sender, RoutedEventArgs e)
        {
            string path = System.IO.Path.Combine(AppContext.BaseDirectory, "Manual.pdf");
            if (System.IO.File.Exists(path)) ExternalLinks.Open(path);
            else await ShowMessageAsync("Manual unavailable",
                "The manual PDF was not found next to the app. It is generated at build time.");
        }


        private async void About_Click(object sender, RoutedEventArgs e)
        {
            if (await ShowDialogAsync(new AboutDialog(GitHubUrl, ContactEmail)) == ContentDialogResult.Primary)
                await ShowLicenseAsync();
        }
        private async Task ShowLicenseAsync()
        {
            string text;
            try { text = System.IO.File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "LICENSE")); }
            catch { text = "License file not found."; }

            var scroll = new ScrollViewer
            {
                MaxHeight = 440,
                Content = new TextBlock
                {
                    Text = text,
                    TextWrapping = TextWrapping.Wrap,
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                    FontSize = 12
                }
            };
            await ShowDialogAsync(new ContentDialog
            {
                Title = "License",
                Content = scroll,
                CloseButtonText = "Close",
            });
        }

        /// <summary>Tells the user which of the app's own files couldn't be read at startup and where the
        /// originals were kept, so nothing is silently replaced by an empty tree or default settings.</summary>
        public async void ReportUnreadableFiles(IReadOnlyList<StorageService.UnreadableFile> files)
        {
            var lines = files.Select(f => f.KeptAs != null
                ? $"• {System.IO.Path.GetFileName(f.Path)}: started without it. The original was kept as {System.IO.Path.GetFileName(f.KeptAs)}."
                : $"• {System.IO.Path.GetFileName(f.Path)}: couldn't be read or moved. It won't be changed until the app is restarted.");
            string folder = System.IO.Path.GetDirectoryName(files[0].Path) ?? "";

            await ShowStartupMessageAsync("Some files couldn't be read",
                string.Join("\n", lines) + $"\n\nFolder: {folder}\n\nThe original files were not overwritten.");
        }

        public async void ReportStartupProblem(string title, string message) =>
            await ShowStartupMessageAsync(title, message);

        /// <summary>Once: TextTemplateManager 1 is still installed, offer to remove it with its own uninstaller.
        /// Not counted as asked when the dialog couldn't be shown.</summary>
        public async void OfferLegacyRemoval()
        {
            await WaitUntilDialogCanOpenAsync();
            var dialog = new ContentDialog
            {
                Title = "TextTemplateManager 1 is still installed",
                Content = "This version replaces it. Its data stays on this PC, and a backup of it is listed in Settings ▸ Backups.\n\nRemove the old version now?",
                PrimaryButtonText = "Remove TextTemplateManager 1",
                CloseButtonText = "Keep it",
                DefaultButton = ContentDialogButton.Primary,
            };
            var answer = await ShowDialogAsync(dialog);
            if (dialog.XamlRoot == null) return;   // another dialog was open; "Keep it" also answers None
            _store.CurrentSettings.LegacyRemovalOffered = true;
            if (answer != ContentDialogResult.Primary) return;

            // Holds the dialog slot while the uninstaller runs, or a waiting startup dialog (the tour offer)
            // takes it and the result message below can't be shown.
            _dialogOpen = true;
            bool removed;
            try { removed = await LegacyApp.UninstallAsync(TimeSpan.FromSeconds(60)); }
            finally { _dialogOpen = false; }
            if (removed)
                await ShowMessageAsync("TextTemplateManager 1 removed", "The old version was removed. Its data folder was kept.");
            else
                await ShowMessageAsync("TextTemplateManager 1 wasn't removed",
                    "The old version couldn't be removed from here. Remove it in Windows Settings ▸ Apps ▸ Installed apps.");
        }

        /// <summary>The first start with no data yet, when there is something to start from: the 1.x data (its v1
        /// backup) and/or the newest backup. Importing restores the v1 backup right away; restoring goes through
        /// Settings › Backups to pick the backup; starting fresh
        /// (also on Esc) saves the empty tree, so this isn't asked again; then the tour is offered when
        /// <paramref name="offerTour"/>.</summary>
        public async void OfferFirstStart(BackupInfo? v1, BackupInfo? newest, bool offerTour)
        {
            await WaitUntilDialogCanOpenAsync();
            var import = new RadioButton
            {
                Content = "Import from TextTemplateManager 1: your templates, settings and sync settings as you left them there. The app restarts afterwards.",
                GroupName = "FirstStart",
            };
            var restore = new RadioButton
            {
                Content = $"Restore a backup (the newest is from {newest?.Created:f})",
                GroupName = "FirstStart",
            };
            var fresh = new RadioButton { Content = "Start fresh", GroupName = "FirstStart" };
            var content = new StackPanel { Spacing = 4 };
            content.Children.Add(new TextBlock { Text = "How would you like to start?", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
            if (v1 != null) content.Children.Add(import);
            if (newest != null) content.Children.Add(restore);
            content.Children.Add(fresh);
            (v1 != null ? import : restore).IsChecked = true;
            foreach (var choice in new[] { import, restore, fresh })
                if (choice.Content is string text) choice.Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };

            var dialog = new ContentDialog
            {
                Title = "Welcome to TextTemplateManager",
                Content = content,
                PrimaryButtonText = "Continue",
                DefaultButton = ContentDialogButton.Primary,
            };
            bool chosen = await ShowDialogAsync(dialog) == ContentDialogResult.Primary;
            // A restore brings back a TTM 2 the user knew: no tour offer. An import from 1.x is offered it after the restart.
            if (chosen && restore.IsChecked == true && newest != null)
                _store.CurrentSettings.TutorialOfferDone = true;
            if (chosen && import.IsChecked == true && v1 != null)
                await ImportV1Async(v1);
            else if (chosen && restore.IsChecked == true && newest != null)
                Settings.Open(SettingsPage.Backups, new BackupsSettingsPage.Request(newest.Path));
            else
            {
                await _store.SaveDataAsync();
                if (offerTour) OfferTour();
            }
        }

        // Everything the v1 backup holds except the synced folders' contents (an existing shared sync file stays as it
        // is), asking only about missing sync files; the app restarts. When it fails nothing is saved, so the welcome
        // is offered again on the next start.
        private async Task ImportV1Async(BackupInfo v1)
        {
            var parts = ((await Task.Run(() => BackupService.ReadContents(v1.Path)))?.Available ?? RestoreParts.All) & ~RestoreParts.SyncedFolders;
            _dialogOpen = true;
            RestoreFlow.Outcome outcome;
            try { outcome = await new RestoreFlow(XamlRoot).RunAsync(v1.Path, parts, "TextTemplateManager 1", confirm: false); }
            finally { _dialogOpen = false; }
            await ShowMessageAsync("Import from TextTemplateManager 1 failed",
                $"{outcome.Problem}\n\nYou can try again in Settings ▸ Backups ▸ Import from TextTemplateManager 1.");
        }

        /// <summary>Take the tour, Later (also Esc: asked again on the next start) or Don't show again.</summary>
        public async void OfferTour()
        {
            bool declined = false;
            var dialog = new ContentDialog
            {
                Title = "Take the guided tour?",
                Content = new TextBlock
                {
                    Text = "A short tour shows the main window, the settings and Quick Paste on sample templates; your own templates aren't touched. "
                         + "You can also start it any time from Help ▸ Start tutorial.",
                    TextWrapping = TextWrapping.Wrap,
                },
                PrimaryButtonText = "Take the tour",
                SecondaryButtonText = "Later",
                CloseButtonText = "Don't show again",
                DefaultButton = ContentDialogButton.Primary,
            };
            // Esc also raises CloseButtonClick: it's remembered here and counts as Later.
            bool escaped = false;
            dialog.PreviewKeyDown += (_, e) => escaped |= e.Key == VirtualKey.Escape;
            dialog.CloseButtonClick += (_, _) => declined = !escaped;
            var answer = await ShowWhenFreeAsync(dialog);
            if (declined) _store.CurrentSettings.TutorialOfferDone = true;
            else if (answer == ContentDialogResult.Primary && Application.Current is App app) await app.StartTutorialAsync();
        }

        /// <summary>The release notes' What's new, once: closed with its X (or Esc).</summary>
        public async void ShowWhatsNew(string version, IReadOnlyList<Tutorial.WhatsNewBlock> blocks)
        {
            var text = new StackPanel { Spacing = 8 };
            foreach (var block in blocks)
            {
                var paragraph = new Microsoft.UI.Xaml.Documents.Paragraph();
                foreach (var (run, bold) in block.Runs)
                {
                    var inline = new Microsoft.UI.Xaml.Documents.Run { Text = run };
                    if (bold || block.Kind == Tutorial.WhatsNewKind.Heading) paragraph.Inlines.Add(new Microsoft.UI.Xaml.Documents.Bold { Inlines = { inline } });
                    else paragraph.Inlines.Add(inline);
                }
                var rich = new RichTextBlock { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = false };
                rich.Blocks.Add(paragraph);
                if (block.Kind == Tutorial.WhatsNewKind.Bullet)
                {
                    var row = new Grid { ColumnSpacing = 8 };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.ColumnDefinitions.Add(new ColumnDefinition());
                    row.Children.Add(new TextBlock { Text = "•" });
                    Grid.SetColumn(rich, 1);
                    row.Children.Add(rich);
                    text.Children.Add(row);
                }
                else
                {
                    if (block.Kind == Tutorial.WhatsNewKind.Heading) rich.Margin = new Thickness(0, 6, 0, 0);
                    text.Children.Add(rich);
                }
            }

            var scroll = new ScrollViewer { Content = text, MaxHeight = 420, Padding = new Thickness(0, 0, 12, 0) };
            var dialog = new ContentDialog { Content = scroll };
            var close = new Button
            {
                Content = new FontIcon { Glyph = "", FontSize = 12 },
                Style = (Style)Application.Current.Resources["SubtleButtonStyle"],
                Width = 32,
                Height = 32,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(16),
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(12, -16, -16, 0),   // into the corner, past the dialog's padding
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(close, "Close");
            ToolTipService.SetToolTip(close, "Close");
            close.Click += (_, _) => dialog.Hide();
            dialog.Opened += (_, _) => close.Focus(FocusState.Programmatic);   // so Esc reaches the dialog
            dialog.PreviewKeyDown += (_, e) =>   // without a close button a dialog ignores Esc
            {
                if (e.Key != VirtualKey.Escape) return;
                e.Handled = true;
                dialog.Hide();
            };
            var title = new Grid();
            title.ColumnDefinitions.Add(new ColumnDefinition());
            title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            title.Children.Add(new TextBlock { Text = $"What's new in {version}", TextWrapping = TextWrapping.Wrap });
            Grid.SetColumn(close, 1);
            title.Children.Add(close);
            scroll.SizeChanged += (_, _) => title.Width = scroll.ActualWidth;   // the dialog sizes its title to the text
            dialog.Title = title;

            if (await ShowWhenFreeAsync(dialog) != null)
                _store.CurrentSettings.LastRunVersion = version;
        }

        // A startup dialog waits for the others; tried again when another one opened first. Null when never shown.
        private async Task<ContentDialogResult?> ShowWhenFreeAsync(ContentDialog dialog)
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                await WaitUntilDialogCanOpenAsync();
                var answer = await ShowDialogAsync(dialog);
                if (dialog.XamlRoot != null) return answer;
                await Task.Delay(500);
            }
            return null;
        }

        /// <summary>After an unexpected error: true when the user chooses to quit.</summary>
        public async Task<bool> AskQuitAfterErrorAsync(bool saved, string crashLogPath)
        {
            await WaitUntilDialogCanOpenAsync();
            var dialog = new ContentDialog
            {
                Title = "Something went wrong",
                Content = (saved ? "An unexpected error occurred. Your changes have been saved."
                                 : "An unexpected error occurred, and your latest changes may not have been saved.")
                          + " To be safe, quit and restart TextTemplateManager.\n\nDetails: " + crashLogPath,
                PrimaryButtonText = "Quit",
                CloseButtonText = "Keep running",
                DefaultButton = ContentDialogButton.Primary,
            };
            return await ShowDialogAsync(dialog) == ContentDialogResult.Primary;
        }

        // Startup notices can arrive before the page is loaded and while another one is still open; only
        // one ContentDialog may be open at a time, so wait for both instead of dropping the message.
        private async Task ShowStartupMessageAsync(string title, string message)
        {
            await WaitUntilDialogCanOpenAsync();
            await ShowMessageAsync(title, message);
        }

        private async Task WaitUntilDialogCanOpenAsync()
        {
            if (!IsLoaded)
            {
                var loaded = new TaskCompletionSource();
                RoutedEventHandler? onLoaded = null;
                onLoaded = (_, _) => { Loaded -= onLoaded; loaded.TrySetResult(); };
                Loaded += onLoaded;
                await loaded.Task;
            }
            while (_dialogOpen) await Task.Delay(200);
        }

        private async Task ShowMessageAsync(string title, string message)
        {
            await ShowDialogAsync(new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "OK",
            });
        }

        /// <summary>The single place a ContentDialog is opened. Only one may be open at a time — a second
        /// ShowAsync throws — and notices like the error prompt surface on their own schedule, so they could
        /// otherwise collide with whatever dialog the user already had open. Returns None when it can't be shown.</summary>
        private async Task<ContentDialogResult> ShowDialogAsync(ContentDialog dialog)
        {
            if (this.XamlRoot == null || _dialogOpen) return ContentDialogResult.None;
            _dialogOpen = true;
            try
            {
                dialog.XamlRoot = this.XamlRoot;
                return await dialog.ShowAsync();
            }
            catch { return ContentDialogResult.None; }
            finally { _dialogOpen = false; }
        }

        private async void Exit_Click(object sender, RoutedEventArgs e)
        {
            if (Application.Current is App app) await app.ExitAsync();
        }

        private ContentDialog? _exitProgress;
        private Task? _exitProgressShown;

        /// <summary>"Saving changes…" while exit waits for a slow save. No buttons; closed by the app.</summary>
        public void ShowExitProgress()
        {
            if (_exitProgress != null) return;
            _exitProgress = new ContentDialog
            {
                Title = "Saving changes…",
                Content = new ProgressRing { IsActive = true, Width = 32, Height = 32 },
            };
            _exitProgressShown = ShowDialogAsync(_exitProgress);
        }

        public async Task HideExitProgressAsync()
        {
            if (_exitProgress == null) return;
            _exitProgress.Hide();
            if (_exitProgressShown != null) await _exitProgressShown;   // frees the one-dialog slot
            _exitProgress = null;
            _exitProgressShown = null;
        }

        /// <summary>Asked when exit has waited a long time for a save. True = keep waiting; Esc or a dialog
        /// that can't be shown also count as keep waiting, so nothing is dropped by accident.</summary>
        public async Task<bool> AskKeepWaitingForSaveAsync()
        {
            var dialog = new ContentDialog
            {
                Title = "Still saving",
                Content = "Your latest changes haven't finished saving yet (a synced file may be busy). " +
                          "Quitting now can lose them.",
                PrimaryButtonText = "Keep waiting",
                SecondaryButtonText = "Quit anyway",
                DefaultButton = ContentDialogButton.Primary,
            };
            return await ShowDialogAsync(dialog) != ContentDialogResult.Secondary;
        }

        #endregion

        #region TreeView Logic

        private void ItemTreeView_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
        {
            if (args.InvokedItem is TreeNode { Item: var selected })
            {
                ViewModel.ClickItem(selected, IsCtrlDown(), IsShiftDown());
                PushTemplateToEditor();
            }
        }

        // Click in empty tree space -> clear the selection (so new items go to the root).
        private void ItemTreeView_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (FindItemFromSource(e.OriginalSource) == null)
                ViewModel.SelectedItem = null;
        }

        private async void ItemTreeView_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            // ESC: back to the search box, search text kept (Esc there then steps back further).
            if (e.Key == VirtualKey.Escape)
            {
                e.Handled = true;
                FocusSearchBox();
                if (FindDescendant<TextBox>(SearchBox) is TextBox box)
                {
                    box.SelectionLength = 0;
                    box.SelectionStart = box.Text.Length;
                }
                return;
            }

            // DEL: delete selected (same confirm as the button).
            if (e.Key == VirtualKey.Delete && ViewModel.SelectedItem != null)
            {
                e.Handled = true;
                await ConfirmAndDeleteAsync();
                return;
            }

            // Ctrl+C: copy the template in its default paste mode.
            if (e.Key == VirtualKey.C && IsCtrlDown() && ViewModel.SelectedItem is Template t)
            {
                e.Handled = true;
                await PasteService.CopyToClipboardAsync(t.Content, t.DefaultPasteMode);
            }

            // Enter/Space on a folder: expand/collapse it.
            if ((e.Key is VirtualKey.Enter or VirtualKey.Space) && ViewModel.SelectedNode is { Item: Folder } folderNode)
            {
                e.Handled = true;
                folderNode.IsExpanded = !folderNode.IsExpanded;
                return;
            }

            // A letter or digit goes back to the search box and keeps typing there, as in Quick Paste.
            if (!IsCtrlDown() && !IsAltDown() && SearchNavigation.TryGetCharKey(e.Key, out char typed))
            {
                e.Handled = true;
                ViewModel.SearchText += typed;
                FocusSearchBox();
                if (FindDescendant<TextBox>(SearchBox) is TextBox box)
                {
                    box.SelectionLength = 0;
                    box.SelectionStart = box.Text.Length;
                }
            }
        }

        // Taken before the tree's own arrow handling, which would select just the next row.
        private void ItemTreeView_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (IsAltDown()) return;
            if (e.Key is VirtualKey.Up or VirtualKey.Down && IsShiftDown() && !IsCtrlDown())
            {
                e.Handled = true;
                if (ViewModel.ExtendSelection(e.Key == VirtualKey.Down ? 1 : -1)
                    && ViewModel.SelectedNode is TreeNode node && ItemTreeView.ContainerFromItem(node) is TreeViewItem row)
                    row.Focus(FocusState.Keyboard);
            }
            else if (e.Key == VirtualKey.A && IsCtrlDown() && !IsShiftDown())
            {
                e.Handled = true;
                ViewModel.SelectAllVisible();
            }
        }

        private static bool IsAltDown()
            => (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu) & CoreVirtualKeyStates.Down)
               == CoreVirtualKeyStates.Down;

        private static bool IsCtrlDown()
            => (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) & CoreVirtualKeyStates.Down)
               == CoreVirtualKeyStates.Down;

        private static bool IsShiftDown()
            => (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift) & CoreVirtualKeyStates.Down)
               == CoreVirtualKeyStates.Down;

        // Before the tree changes its own selection for a click: with Ctrl or Shift held, the click adds to the
        // selection instead of replacing it (applied in ItemInvoked).
        private void ItemTreeView_PointerPressed(object sender, PointerRoutedEventArgs e) =>
            ViewModel.ModifierClickPending = IsCtrlDown() || IsShiftDown();

        // Ctrl+F focuses the search box from anywhere in the window (page-level accelerator).
        private void SearchAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            args.Handled = true;
            if (Tour.IsRunning) return;
            FocusSearchBox();
        }

        private void FocusSearchBox() => SearchBox.Focus(FocusState.Programmatic);

        // Tab and Shift+Tab go round a fixed cycle: the tree toolbar, the search box, the tree, then the selected
        // item's fields with the editor between the title and the shortcuts (its page takes the keys while it has the
        // focus and hands Tab back through TabOut), so focus never lands on display-only parts. The toolbar is one
        // stop: Left/Right move between its buttons. From anywhere else (menus, Save, help) Tab works as usual.
        private void MainGrid_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key is VirtualKey.Left or VirtualKey.Right && !IsCtrlDown() && !IsAltDown() && !IsShiftDown()
                && FocusManager.GetFocusedElement(XamlRoot) is Button focusedButton && ToolbarButtons().IndexOf(focusedButton) is int at and >= 0)
            {
                e.Handled = true;
                var buttons = ToolbarButtons();
                buttons[SearchNavigation.NextInRow(at, buttons.Select(IsUsable).ToList(), e.Key == VirtualKey.Left)].Focus(FocusState.Keyboard);
                return;
            }
            if (e.Key != VirtualKey.Tab || IsCtrlDown() || IsAltDown()) return;
            if (FocusedCycleStop() < 0) return;
            e.Handled = true;
            // Right after an arrow key the pane may still show the item before (the editor reports its content
            // first): Tab would land in fields that are about to go, so it waits for the switch.
            if (ViewModel.IsSwitchPending) TabWhenSwitchedAsync(IsShiftDown());
            else MoveInCycle(IsShiftDown());
        }

        private async void TabWhenSwitchedAsync(bool back)
        {
            for (int i = 0; i < 100 && ViewModel.IsSwitchPending; i++) await Task.Delay(50);   // the first template waits for the editor to start
            await Task.Delay(50);   // a layout pass, so the new item's fields can take the focus
            MoveInCycle(back);
        }

        private int FocusedCycleStop()
        {
            var focused = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
            return TabCycle().FindIndex(stop => IsWithin(focused, stop));
        }

        private void MoveInCycle(bool back)
        {
            int current = FocusedCycleStop();
            if (current >= 0) MoveInCycle(TabCycle(), current, back);
        }

        // Tab or Shift+Tab out of the editor's page (from its text forward, from its toolbar back).
        private void Editor_TabOut(bool back)
        {
            var cycle = TabCycle();
            int at = cycle.IndexOf(Editor);
            if (at >= 0) MoveInCycle(cycle, at, back);
        }

        private void MoveInCycle(List<FrameworkElement> cycle, int current, bool back)
        {
            var next = cycle[SearchNavigation.NextInCycle(current, cycle.Count, back)];
            if (ReferenceEquals(next, ItemTreeView)) FocusTree();
            else if (ReferenceEquals(next, TreeToolbar)) FocusToolbar();
            else if (ReferenceEquals(next, Editor)) Editor.FocusPart(text: back);   // forward: its toolbar; back: its text
            else if (next is Control control) control.Focus(FocusState.Keyboard);
        }

        private List<FrameworkElement> TabCycle()
        {
            var cycle = new List<FrameworkElement>();
            if (ToolbarButtons().Any(IsUsable)) cycle.Add(TreeToolbar);
            cycle.Add(SearchBox);
            if (ViewModel.RootNodes.Count > 0) cycle.Add(ItemTreeView);
            if (TemplatePanel.Visibility == Visibility.Visible)
            {
                if (TemplateTitleBox.IsEnabled) cycle.Add(TemplateTitleBox);
                cycle.Add(Editor);
                cycle.AddRange(new Control[] { SingleKeyBox, MultiKeyBox, TagsBox, PasteModeBox }.Where(c => c.IsEnabled));
            }
            else if (FolderPanel.Visibility == Visibility.Visible)
                cycle.Add(FolderTitleBox);
            return cycle;
        }

        private List<Button> ToolbarButtons() => new() { AddTemplateButton, AddFolderButton, DeleteButton, CollapseAllButton, ExpandAllButton };

        private static bool IsUsable(Button button) => button.IsEnabled && button.Visibility == Visibility.Visible;

        private Button? _lastToolbarButton;

        // The toolbar stop: the button last used there, else the first one that can be used.
        private void FocusToolbar()
        {
            var target = _lastToolbarButton is { } last && IsUsable(last) ? last : ToolbarButtons().FirstOrDefault(IsUsable);
            target?.Focus(FocusState.Keyboard);
        }

        private static bool IsWithin(DependencyObject? element, DependencyObject container)
        {
            for (; element != null; element = VisualTreeHelper.GetParent(element))
                if (ReferenceEquals(element, container)) return true;
            return false;
        }

        // Up and Down in the search box work like in Quick Paste: Up puts the caret at the start; Down puts it
        // at the end first, the next Down (from the end of the text) moves into the tree.
        private void SearchArrow(KeyRoutedEventArgs e)
        {
            if (FindDescendant<TextBox>(SearchBox) is not TextBox box) return;
            e.Handled = true;
            if (e.Key == VirtualKey.Up)
            {
                box.SelectionLength = 0;
                box.SelectionStart = 0;
            }
            else if (SearchNavigation.DecideSearchDown(box.SelectionStart, box.SelectionLength, box.Text.Length,
                    ViewModel.RootNodes.Count) == SearchNavigation.SearchDown.EnterTree)
                FocusTree();
            else
            {
                box.SelectionLength = 0;
                box.SelectionStart = box.Text.Length;
            }
        }

        // Esc in the search box steps back one level at a time: it clears the search text, and with the
        // box already empty it clears the tree selection (so new items go to the root).
        private void SearchBox_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key is VirtualKey.Up or VirtualKey.Down) { SearchArrow(e); return; }
            if (e.Key != VirtualKey.Escape) return;
            e.Handled = true;
            if (!string.IsNullOrEmpty(ViewModel.SearchText)) ViewModel.SearchText = string.Empty;
            else ViewModel.SelectedItem = null;
        }

        // Focus the selected item, or select and focus the first one, so the arrow keys continue from there.
        private void FocusTree()
        {
            if (ViewModel.SelectedNode is TreeNode selected
                && ItemTreeView.ContainerFromItem(selected) is TreeViewItem selectedContainer)
            {
                selectedContainer.Focus(FocusState.Keyboard);
                return;
            }
            if (ViewModel.RootNodes.FirstOrDefault() is not TreeNode first) return;
            ViewModel.SelectedNode = first;
            if (ItemTreeView.ContainerFromItem(first) is TreeViewItem firstContainer)
                firstContainer.Focus(FocusState.Keyboard);
        }

        private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T match) return match;
                if (FindDescendant<T>(child) is T nested) return nested;
            }
            return null;
        }

        private IReadOnlyList<BaseItem>? _draggedItems;

        private void TreeItem_DragStarting(UIElement sender, DragStartingEventArgs args)
        {
            // Dragging one of several selected items takes all of them. Pinned sync roots and read-only items
            // can't be moved, so a selection holding one of them can't be dragged.
            if (sender is not FrameworkElement { DataContext: TreeNode { Item: var item } }) { args.Cancel = true; return; }
            var items = ViewModel.IsInSelection(item) ? ViewModel.SelectedItems : new[] { item };
            if (items.Any(i => !ViewModel.CanMove(i, null)))
            {
                args.Cancel = true;
                return;
            }
            _draggedItems = items;
            args.AllowedOperations = DataPackageOperation.Move;
        }

        private void TreeItem_DropCompleted(UIElement sender, DropCompletedEventArgs args) => _draggedItems = null;

        // Folders: the top and bottom quarter of a row drop beside the folder, the middle into it. A drop
        // below an expanded folder lands at the top of its children, which is what the rows below show.
        private (Folder? Folder, int Index, string Caption) DropDestination(DragEventArgs e)
        {
            var row = RowAt(e);
            if (row?.DataContext is not TreeNode { Item: var target } node)
                return (null, ViewModel.AllItems.Count, "Move to the end");

            double y = Math.Clamp(e.GetPosition(row).Y, 0, row.ActualHeight), height = row.ActualHeight;
            var spot = target is Folder
                ? y < height / 4 ? MainViewModel.DropSpot.Before : y > height * 3 / 4 ? MainViewModel.DropSpot.After : MainViewModel.DropSpot.Into
                : y < height / 2 ? MainViewModel.DropSpot.Before : MainViewModel.DropSpot.After;

            if (spot == MainViewModel.DropSpot.After && target is Folder open && node.IsExpanded && node.Children.Count > 0)
                return (open, 0, $"Move into \"{target.Title}\"");

            var (folder, index) = ViewModel.DropDestination(target, spot);
            string caption = spot switch
            {
                MainViewModel.DropSpot.Into => $"Move into \"{target.Title}\"",
                MainViewModel.DropSpot.Before => $"Move above \"{target.Title}\"",
                _ => $"Move below \"{target.Title}\"",
            };
            return (folder, index, caption);
        }

        // The row under the pointer, or the nearest one when the pointer is in the gap between two rows;
        // null below the last row. (A drag event's OriginalSource is the list, not the row.)
        private TreeViewItem? RowAt(DragEventArgs e)
        {
            var bounds = ItemTreeView.TransformToVisual(null)
                .TransformBounds(new Windows.Foundation.Rect(0, 0, ItemTreeView.ActualWidth, ItemTreeView.ActualHeight));
            TreeViewItem? nearest = null;
            double nearestDistance = double.MaxValue;
            bool belowAll = true;
            foreach (var row in VisualTreeHelper.FindElementsInHostCoordinates(bounds, ItemTreeView).OfType<TreeViewItem>())
            {
                double y = e.GetPosition(row).Y;
                if (y < row.ActualHeight) belowAll = false;
                double distance = y < 0 ? -y : y > row.ActualHeight ? y - row.ActualHeight : 0;
                if (distance < nearestDistance) (nearest, nearestDistance) = (row, distance);
            }
            return belowAll ? null : nearest;
        }

        // A row resets the result to None when the pointer enters it, and that enter doesn't reach the
        // TreeView. With no DragOver after it (the pointer stopped on the row) the drop would be refused.
        private void TreeItem_DragEnter(object sender, DragEventArgs e) => ItemTreeView_DragOver(sender, e);

        // A row reused for an item the TreeView re-adds (e.g. a search brings a folder back) is reset to
        // collapsed after it takes the item, and the two-way binding copies that into the node. Put
        // back the expansion the node had when the row took it.
        private void TreeItem_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
        {
            var row = (TreeViewItem)sender;
            WatchSelectionLook(row);
            DispatcherQueue.TryEnqueue(() => ApplySelectionLook(row));   // a reused row may show another node now
            if (args.NewValue is not TreeNode { IsExpanded: true } node) return;
            DispatcherQueue.TryEnqueue(() =>
            {
                if (ReferenceEquals(row.DataContext, node) && !row.IsExpanded) row.IsExpanded = true;
            });
        }

        // ---- Several selected items: the tree's own "Selected" look ----
        // The tree marks only its one selected item; the other selected rows are put into the same visual
        // states. The row resets them on hover and when it is reused, so they are applied again after that.

        private readonly HashSet<TreeViewItem> _watchedRows = new();

        private void WatchSelectionLook(TreeViewItem row)
        {
            if (!_watchedRows.Add(row)) return;
            row.AddHandler(PointerEnteredEvent, new PointerEventHandler((_, _) => DispatcherQueue.TryEnqueue(() => ApplySelectionLook(row, pointerOver: true))), true);
            row.AddHandler(PointerExitedEvent, new PointerEventHandler((_, _) => DispatcherQueue.TryEnqueue(() => ApplySelectionLook(row))), true);
            row.AddHandler(PointerReleasedEvent, new PointerEventHandler((_, _) => DispatcherQueue.TryEnqueue(() => ApplySelectionLook(row, pointerOver: true))), true);
        }

        private void ApplySelectionLook(TreeViewItem row, bool pointerOver = false)
        {
            if (row.DataContext is not TreeNode node || ReferenceEquals(node, ViewModel.SelectedNode)) return;   // the tree draws that one
            string state = node.IsMultiSelected
                ? (pointerOver ? "PointerOverSelected" : "Selected")
                : (pointerOver ? "PointerOver" : "Normal");
            VisualStateManager.GoToState(row, state, false);
        }

        private void ApplySelectionLookToAllRows()
        {
            foreach (var row in _watchedRows) ApplySelectionLook(row);
        }

        private void ItemTreeView_DragOver(object sender, DragEventArgs e)
        {
            e.AcceptedOperation = DataPackageOperation.None;
            if (_draggedItems is not { Count: > 0 } items) return;
            var (folder, _, caption) = DropDestination(e);
            if (items.Any(i => !ViewModel.CanMove(i, folder))) return;

            e.AcceptedOperation = DataPackageOperation.Move;
            e.DragUIOverride.Caption = items.Count > 1 ? caption.Replace("Move ", $"Move {items.Count} items ") : caption;
            e.DragUIOverride.IsGlyphVisible = false;
        }

        private async void ItemTreeView_Drop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            var items = _draggedItems;
            _draggedItems = null;
            if (items is not { Count: > 0 }) return;

            var (folder, index, _) = DropDestination(e);
            await ViewModel.MoveAsync(items, folder, index);
        }

        private static BaseItem? FindItemFromSource(object source)
        {
            var el = source as DependencyObject;
            while (el != null)
            {
                if (el is FrameworkElement fe && fe.DataContext is TreeNode node) return node.Item;
                el = VisualTreeHelper.GetParent(el);
            }
            return null;
        }

        #endregion

        #region Shortcut Conflicts Warning

        private void ShowShortcutConflicts() =>
            ConflictPanel.Show(ConflictReport.From(
                ViewModel.AllItems.Descendants().OfType<TextTemplateManager.Models.Template>(),
                _store.CurrentSettings.HideCrossAreaShortcutWarnings));

        #endregion
    }
}
