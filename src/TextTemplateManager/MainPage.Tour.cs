using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using TextTemplateManager.Models;
using TextTemplateManager.Tutorial;

namespace TextTemplateManager
{
    // The guided tour's part in the main window (the window then shows the tour's sample data, see App.StartTutorialAsync).
    public sealed partial class MainPage
    {
        /// <summary>The main window's part of the tour, from <paramref name="startAt"/>, numbered within
        /// <paramref name="total"/> steps; Quick Paste's part follows (the window stays dimmed until <see cref="EndTour"/>).</summary>
        public Task<TourOutcome> RunTourAsync(int startAt, int total) =>
            Tour.RunAsync(TourSteps(), startAt, numberFrom: 1, total, canGoBackFromFirst: false, endsTour: false);

        public int TourStepCount => TourSteps().Count;

        public void EndTour() => Tour.End();

        /// <summary>After a skipped tour: where to start it again.</summary>
        public void ShowTourHint() => TourHintTip.IsOpen = true;

        private Task OpenSettings(Controls.SettingsPage page)
        {
            Settings.Open(page);
            return Task.Delay(500);   // the page loads
        }

        private Task CloseSettings()
        {
            if (Settings.IsOpen) Settings.Close();
            return Task.CompletedTask;
        }

        // A menu the step is about, shown open: a picture of it built from its own entries, drawn by the tour above the
        // dimming. The real menu would take the keyboard and swallow the next click on the card.
        private FrameworkElement? _menuPicture;

        private async Task ShowMenuAsync(MenuBarItem menu)
        {
            await CloseSettings();
            var at = Tour.PositionOf(menu);
            var items = new StackPanel { Padding = new Thickness(4) };
            foreach (var entry in menu.Items)
            {
                if (entry is MenuFlyoutSeparator)
                {
                    items.Children.Add(new Rectangle { Height = 1, Margin = new Thickness(-4, 4, -4, 4),
                        Fill = (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"] });
                    continue;
                }
                if (entry is not MenuFlyoutItem { Visibility: Visibility.Visible } item) continue;
                var row = new Grid { Padding = new Thickness(11, 8, 16, 9), ColumnSpacing = 12 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                IconElement? icon = item.Icon switch
                {
                    SymbolIcon s => new SymbolIcon(s.Symbol),
                    FontIcon f => new FontIcon { Glyph = f.Glyph },
                    _ => null,
                };
                // Menu icons are 16 px; a SymbolIcon is 20, so it's scaled down rather than cut off.
                if (icon != null) row.Children.Add(new Viewbox { Width = 16, Height = 16, Child = icon });
                var text = new TextBlock { Text = item.Text, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(text, 1);
                row.Children.Add(text);
                items.Children.Add(row);
            }
            var picture = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(picture, $"{menu.Title} menu (picture)");
            picture.Children.Add(new Border { Height = menu.ActualHeight, Width = menu.ActualWidth, HorizontalAlignment = HorizontalAlignment.Left });   // the menu's title, seen through the cutout
            picture.Children.Add(new Border
            {
                Child = items,
                Margin = new Thickness(-4, 2, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                BorderBrush = (Brush)Application.Current.Resources["SurfaceStrokeColorFlyoutBrush"],
                Background = (Brush)Application.Current.Resources["SolidBackgroundFillColorQuarternaryBrush"],
            });
            Tour.ShowExtra(picture, at.X, at.Y);
            _menuPicture = picture;
            await Task.Delay(100);
        }

        private void HideMenu()
        {
            Tour.ClearExtra();
            _menuPicture = null;
        }

        // The Save button only shows with unsaved changes; the tour shows it for its step and then puts it back.
        private Task ShowSaveButtonForTour()
        {
            SaveButton.Visibility = Visibility.Visible;
            return Task.Delay(100);
        }

        private void ShowSaveButtonAsIs() =>
            SaveButton.Visibility = ViewModel.HasUnsavedChanges ? Visibility.Visible : Visibility.Collapsed;

        private Task SelectSample(System.Guid id)
        {
            ViewModel.SetAllExpanded(true);
            ViewModel.SelectedItem = _store.LocalItems.Descendants().FirstOrDefault(i => i.Id == id);
            return Task.Delay(400);   // the editor loads the template
        }

        private IReadOnlyList<TourStep> TourSteps() => new List<TourStep>
        {
            new("Welcome to TextTemplateManager",
                "Keep reusable text (replies, snippets, signatures) and insert it into any app with a keystroke. This short tour shows the main parts using sample templates; your own templates aren't touched. Next continues, Esc skips.",
                Enter: () => { ViewModel.SetAllExpanded(true); return Task.CompletedTask; }),
            new("Templates and folders",
                "Your templates, in folders. Synced folders (with the badge) come from a shared file and stay at the top. The search box filters the tree; Ctrl+F jumps to it, and ↓ moves on into the tree.",
                () => LeftPane),
            new("Organizing",
                "Add a template or a folder (into the selected folder), delete the selection, and collapse or expand all folders. Drag and drop moves items; Ctrl+click, Shift+click and Shift+arrow keys select several, Ctrl+A all.",
                () => TreeToolbar),
            new("The editor",
                "Format text with headings, lists, tables, code blocks with syntax highlighting, and callout panels like the Info panel here.",
                () => EditorCard, () => SelectSample(TourSample.ReleaseNotesId)),
            new("Shortcuts and tags",
                "Single Key and Multi Key are the template's shortcuts in Quick Paste, tags help finding it, and the default paste mode decides the format it is pasted in.",
                () => TemplateMetaPanel),
            new("Paste modes",
                "Auto picks the best format for the app you paste into; HTML/Jira keeps callout panels for Jira; HTML, RTF, Markdown and Plaintext paste just that. Right-click a template to copy it in another mode.",
                () => PasteModeBox),
            new("Saving",
                "Changes to a template or folder wait for Save here (or Ctrl+S), and you're asked before leaving unsaved changes. To save as you type, turn on Autosave in Settings ▸ General.",
                () => SaveButton, ShowSaveButtonForTour, ShowSaveButtonAsIs),
            new("Always at hand",
                "Closing the window keeps TextTemplateManager running in the system tray, so Quick Paste is always ready. To quit, use File ▸ Exit or the tray icon."),
            new("Import and export",
                "File ▸ Export saves the selected items, or all of them, to a .ttmdata file; File ▸ Import adds such a file to your tree.",
                () => _menuPicture, () => ShowMenuAsync(FileMenu), HideMenu),
            new("Settings ▸ General",
                "File ▸ Settings: the global shortcut that opens Quick Paste, the paste mode for new templates, starting with Windows (hidden in the tray if you like), Autosave and updates.",
                () => Settings.PageArea, () => OpenSettings(Controls.SettingsPage.General), Corner: TourCorner.TopRight),
            new("Settings ▸ Integrations",
                "The browser connector lets the TTM-Connect browser extension paste your templates on web pages and save selected text as a new template. Opening a .ttmdata file (a double-click in Explorer) starts TextTemplateManager and adds the file as a synced folder; if another app took .ttmdata files over, set TextTemplateManager again in Default apps.",
                () => Settings.PageArea, () => OpenSettings(Controls.SettingsPage.Integrations), Corner: TourCorner.BottomLeft),
            new("Settings ▸ Sync",
                "Share templates with your team through a .ttmdata file on a shared drive or OneDrive. Each linked file becomes a synced folder; choose a shortcut prefix, whether you may change it (or only read it), and link another file if one goes missing.",
                () => Settings.PageArea, () => OpenSettings(Controls.SettingsPage.Sync), Corner: TourCorner.BottomLeft),
            new("Settings ▸ Backups",
                "Backups are made on their own (after changes, every two hours and daily) in your Documents folder. Restore any of them, choosing what to take back, export a backup to move to another PC, or import from TextTemplateManager 1.",
                () => Settings.PageArea, () => OpenSettings(Controls.SettingsPage.Backups), Corner: TourCorner.BottomRight),
            new("Help",
                "The manual, and this tour again: Help ▸ Start tutorial.",
                () => _menuPicture, () => ShowMenuAsync(HelpMenu), HideMenu),
        };
    }
}
