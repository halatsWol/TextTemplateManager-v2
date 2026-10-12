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



        public MainPage(DataNode store)
        {
            _store = store;
            this.InitializeComponent();

            // handledEventsToo: the TreeView marks Enter/Space handled, so a plain KeyDown handler
            // never sees them — attach here to still get folder expand/collapse on those keys.
            ItemTreeView.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(ItemTreeView_KeyDown), true);
            // The list marks drag events over its empty space handled; a drop there moves to the end.
            ItemTreeView.AddHandler(UIElement.DragOverEvent, new DragEventHandler(ItemTreeView_DragOver), true);
            ItemTreeView.AddHandler(UIElement.DropEvent, new DragEventHandler(ItemTreeView_Drop), true);
            // Same for the search box: the AutoSuggestBox takes the arrow keys for its suggestion list.
            SearchBox.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(SearchBox_KeyDown), true);
            // Esc is taken before the AutoSuggestBox sees it: its own Esc restores the last typed text.
            SearchBox.PreviewKeyDown += SearchBox_PreviewKeyDown;


            ViewModel = new MainViewModel(store);
            // Keep the panels from inheriting the page until x:Bind sets their DataContext,
            // otherwise their {Binding}s briefly resolve against MainPage and log path errors.
            TemplatePanel.DataContext = null;
            FolderPanel.DataContext = null;
            this.DataContext = this;

            _store.DataSaved += () => DispatcherQueue.TryEnqueue(ShowSaveNotification);
            ViewModel.ConflictsChanged += ShowShortcutConflicts;

            ViewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ViewModel.SelectedItem))
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
            (Application.Current as App)?.AddExitFlush(Editor.FlushAsync);

            Settings.Store = store;
            SearchOptions.Settings = store.CurrentSettings;
            Settings.Closed += Settings_Closed;
            Editor.FocusSearchRequested += (_, _) => FocusSearchBox();
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
        private void PushTemplateToEditor() => Editor.Show(ViewModel.SelectedTemplate, ViewModel.IsSelectedEditable);
        #region Menu Handlers

        private void MainHelpButton_Click(object sender, RoutedEventArgs e) => MainHelpTip.IsOpen = true;

        private void AddTemplate_Click(object sender, RoutedEventArgs e) => ViewModel.AddTemplateCommand.Execute(null);
        private void AddFolder_Click(object sender, RoutedEventArgs e) => ViewModel.AddFolderCommand.Execute(null);
        private async void DeleteItem_Click(object sender, RoutedEventArgs e) => await ConfirmAndDeleteAsync();

        /// <summary>Tree context menu: Delete for any item; Duplicate/Copy/Copy As for templates;
        /// Export for non-root folders.</summary>
        private void Tree_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not TreeNode { Item: var item }) return;

            ViewModel.SelectedItem = item;   // so the panel and delete act on it

            var flyout = new MenuFlyout();

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
                export.Click += async (_, _) => await ViewModel.ExportAsync(item);
                flyout.Items.Add(export);
            }

            // Move to Root — for a nested, editable item (a reliable alternative to dragging it out).
            if (!item.IsSyncRoot && !ViewModel.IsReadOnly(item)
                && ViewModel.AllItems.ParentOf(item) != null)
            {
                var toRoot = new MenuFlyoutItem { Text = "Move to Root" };
                toRoot.Click += async (_, _) => await ViewModel.MoveToRootAsync(item);
                flyout.Items.Add(toRoot);
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

        /// <summary>Delete the selected item; confirm for templates and non-empty folders, but not
        /// for an empty folder.</summary>
        private async Task ConfirmAndDeleteAsync()
        {
            if (ViewModel.SelectedItem is not BaseItem item) return;
            if (item.IsSyncRoot || ViewModel.IsReadOnly(item)) return;   // sync roots via Settings; save-off is read-only

            string? message = null;

            if (item is Folder folder && folder.Children.Count > 0)
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
        // The selected folder or template, or all local templates and folders when nothing is selected.
        private async void Export_Click(object sender, RoutedEventArgs e) => await ViewModel.ExportAsync(ViewModel.SelectedItem);

        private void OpenPreferences_Click(object sender, RoutedEventArgs e) => Settings.Open();

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

        private const string GitHubUrl = "https://github.com/halatsWol/TextTemplateManager";
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
                ViewModel.SelectedItem = selected;
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
            }
        }

        private static bool IsCtrlDown()
            => (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) & CoreVirtualKeyStates.Down)
               == CoreVirtualKeyStates.Down;

        // Ctrl+F focuses the search box from anywhere in the window (page-level accelerator).
        private void SearchAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            args.Handled = true;
            FocusSearchBox();
        }

        private void FocusSearchBox() => SearchBox.Focus(FocusState.Programmatic);

        // Down in the search box works like in Quick Paste: the caret goes to the end first, the next
        // Down (from the end of the text) moves into the tree.
        private void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key != VirtualKey.Down || FindDescendant<TextBox>(SearchBox) is not TextBox box) return;
            e.Handled = true;
            if (PasteWindow.DecideSearchDown(box.SelectionStart, box.SelectionLength, box.Text.Length,
                    ViewModel.RootNodes.Count) == PasteWindow.SearchDown.EnterTree)
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

        private BaseItem? _draggedItem;

        private void TreeItem_DragStarting(UIElement sender, DragStartingEventArgs args)
        {
            // Pinned sync roots and read-only items can't be moved.
            if (sender is not FrameworkElement { DataContext: TreeNode { Item: var item } } || !ViewModel.CanMove(item, null))
            {
                args.Cancel = true;
                return;
            }
            _draggedItem = item;
            args.AllowedOperations = DataPackageOperation.Move;
        }

        private void TreeItem_DropCompleted(UIElement sender, DropCompletedEventArgs args) => _draggedItem = null;

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
            if (args.NewValue is not TreeNode { IsExpanded: true } node) return;
            var row = (TreeViewItem)sender;
            DispatcherQueue.TryEnqueue(() =>
            {
                if (ReferenceEquals(row.DataContext, node) && !row.IsExpanded) row.IsExpanded = true;
            });
        }

        private void ItemTreeView_DragOver(object sender, DragEventArgs e)
        {
            e.AcceptedOperation = DataPackageOperation.None;
            if (_draggedItem is not BaseItem item) return;
            var (folder, _, caption) = DropDestination(e);
            if (!ViewModel.CanMove(item, folder)) return;

            e.AcceptedOperation = DataPackageOperation.Move;
            e.DragUIOverride.Caption = caption;
            e.DragUIOverride.IsGlyphVisible = false;
        }

        private async void ItemTreeView_Drop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            var item = _draggedItem;
            _draggedItem = null;
            if (item == null) return;

            var (folder, index, _) = DropDestination(e);
            await ViewModel.MoveAsync(item, folder, index);
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
