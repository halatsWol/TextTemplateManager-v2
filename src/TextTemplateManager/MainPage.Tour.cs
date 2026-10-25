using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using TextTemplateManager.Helpers;
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
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(picture, Loc.Tour.Format("MenuPictureName", menu.Title));
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
            new(Loc.Tour.Get("StepWelcome_Title"),
                Loc.Tour.Get("StepWelcome_Text"),
                Enter: () => { ViewModel.SetAllExpanded(true); return Task.CompletedTask; }),
            new(Loc.Tour.Get("StepTree_Title"),
                Loc.Tour.Get("StepTree_Text"),
                () => LeftPane),
            new(Loc.Tour.Get("StepOrganizing_Title"),
                Loc.Tour.Get("StepOrganizing_Text"),
                () => TreeToolbar),
            new(Loc.Tour.Get("StepEditor_Title"),
                Loc.Tour.Get("StepEditor_Text"),
                () => EditorCard, () => SelectSample(TourSample.ReleaseNotesId)),
            new(Loc.Tour.Get("StepTranslating_Title"),
                Loc.Tour.Get("StepTranslating_Text"),
                () => EditorCard),
            new(Loc.Tour.Get("StepShortcuts_Title"),
                Loc.Tour.Get("StepShortcuts_Text"),
                () => TemplateMetaPanel),
            new(Loc.Tour.Get("StepPasteModes_Title"),
                Loc.Tour.Get("StepPasteModes_Text"),
                () => PasteModeBox),
            new(Loc.Tour.Get("StepSaving_Title"),
                Loc.Tour.Get("StepSaving_Text"),
                () => SaveButton, ShowSaveButtonForTour, ShowSaveButtonAsIs),
            new(Loc.Tour.Get("StepTray_Title"),
                Loc.Tour.Get("StepTray_Text")),
            new(Loc.Tour.Get("StepImportExport_Title"),
                Loc.Tour.Get("StepImportExport_Text"),
                () => _menuPicture, () => ShowMenuAsync(FileMenu), HideMenu),
            new(Loc.Tour.Get("StepSettingsGeneral_Title"),
                Loc.Tour.Get("StepSettingsGeneral_Text"),
                () => Settings.PageArea, () => OpenSettings(Controls.SettingsPage.General), Corner: TourCorner.TopRight),
            new(Loc.Tour.Get("StepSettingsIntegrations_Title"),
                Loc.Tour.Get("StepSettingsIntegrations_Text"),
                () => Settings.PageArea, () => OpenSettings(Controls.SettingsPage.Integrations), Corner: TourCorner.BottomLeft),
            new(Loc.Tour.Get("StepSettingsSync_Title"),
                Loc.Tour.Get("StepSettingsSync_Text"),
                () => Settings.PageArea, () => OpenSettings(Controls.SettingsPage.Sync), Corner: TourCorner.BottomLeft),
            new(Loc.Tour.Get("StepSettingsBackups_Title"),
                Loc.Tour.Get("StepSettingsBackups_Text"),
                () => Settings.PageArea, () => OpenSettings(Controls.SettingsPage.Backups), Corner: TourCorner.BottomRight),
            new(Loc.Tour.Get("StepSettingsTranslation_Title"),
                Loc.Tour.Get("StepSettingsTranslation_Text"),
                () => Settings.PageArea, () => OpenSettings(Controls.SettingsPage.Translation), Corner: TourCorner.BottomRight),
            new(Loc.Tour.Get("StepSettingsSupport_Title"),
                Loc.Tour.Get("StepSettingsSupport_Text"),
                () => Settings.PageArea, () => OpenSettings(Controls.SettingsPage.Support), Corner: TourCorner.BottomRight),
            new(Loc.Tour.Get("StepHelp_Title"),
                Loc.Tour.Get("StepHelp_Text"),
                () => _menuPicture, () => ShowMenuAsync(HelpMenu), HideMenu),
        };
    }
}
