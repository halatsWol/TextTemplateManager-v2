using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TextTemplateManager.Data;
using TextTemplateManager.Helpers;

namespace TextTemplateManager;

/// <summary>One backup in the list.</summary>
public sealed class BackupRow
{
    public BackupRow(BackupInfo info, bool fromFile = false)
    {
        Info = info;
        if (info.IsValid) Contents = BackupService.ReadContents(info.Path);
        Title = fromFile ? $"{System.IO.Path.GetFileName(info.Path)} ({info.Created:f})" : info.Created.ToString("f");
        string tier = info.Tier switch
        {
            BackupTier.Recent => Loc.Get("Backups_TierRecent"),
            BackupTier.TwoHourly => Loc.Get("Backups_TierTwoHourly"),
            BackupTier.Daily => Loc.Get("Backups_TierDaily"),
            BackupTier.Exported => Loc.Get("Backups_TierExported"),
            _ => Loc.Get("Backups_TierV1"),
        };
        Detail = info.IsValid
            ? $"{(fromFile ? Loc.Get("Backups_FromFile") + " · " : "")}{tier} · {Loc.Format("Backups_MadeByVersion", info.Manifest!.AppVersion)}"
            : $"{tier} · {Loc.Format("Backups_Damaged", info.Problem)}";
    }

    public BackupInfo Info { get; }

    /// <summary>What the backup can restore; null for a damaged one.</summary>
    public BackupContents? Contents { get; }

    public string Title { get; }
    public string Detail { get; }

    /// <summary>What the restore confirmation calls it.</summary>
    public string Description => Info.Tier == BackupTier.V1 ? "TextTemplateManager 1" : Title;
    public double Opacity => Info.IsValid ? 1 : 0.5;
}

/// <summary>Where the automatic backups are, and restoring from one.</summary>
public sealed partial class BackupsSettingsPage : Page
{
    /// <summary>Opens the page on the backup at <paramref name="Select"/>, with Restore… in view.</summary>
    public sealed record Request(string Select);

    /// <summary>The backups the page lists (those of the window's store: the tour lists its sample ones), and
    /// optionally a <see cref="Request"/>.</summary>
    public sealed record Parameters(BackupService Backups, Request? Request = null);

    private BackupService _backups = null!;   // set on navigation

    private ScrollViewer? _listScroll;

    public BackupsSettingsPage()
    {
        InitializeComponent();
        // Touch and touchpad pans chain from the list to the page on their own, the mouse wheel doesn't: at the
        // list's end it scrolls the page. Also for turns the list already handled.
        BackupList.AddHandler(PointerWheelChangedEvent, new PointerEventHandler(BackupList_PointerWheelChanged), true);
    }

    private void BackupList_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(BackupList).Properties;
        int delta = point.MouseWheelDelta;
        if (delta == 0 || point.IsHorizontalMouseWheel) return;
        _listScroll ??= FindScrollViewer(BackupList);
        if (_listScroll == null) return;
        bool atEnd = delta < 0 ? _listScroll.VerticalOffset >= _listScroll.ScrollableHeight - 1 : _listScroll.VerticalOffset <= 0;
        if (atEnd) PageScroll.ChangeView(null, PageScroll.VerticalOffset - delta, null);
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ScrollViewer sv) return sv;
            if (FindScrollViewer(child) is { } found) return found;
        }
        return null;
    }

    private static App App => (App)Application.Current;

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        var parameters = e.Parameter as Parameters;
        _backups = parameters?.Backups ?? App.Backups;
        FolderText.Text = _backups.Directory;
        var rows = await LoadRowsAsync();
        var requested = parameters?.Request is { } request ? rows.FirstOrDefault(r => r.Info.IsValid && r.Info.Path == request.Select) : null;
        BackupList.SelectedItem = requested ?? rows.FirstOrDefault(r => r.Info.IsValid);   // else the newest one that can be restored
        ShowAvailableParts();
        // Sent here to restore: a long list would leave "Restore…" below the fold. After layout, so the list has its rows.
        if (requested != null)
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () => RestoreButton.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false }));
    }

    private async Task<List<BackupRow>> LoadRowsAsync()
    {
        var backups = _backups;
        var (rows, legacy) = await Task.Run(() => (backups.List().Select(b => new BackupRow(b)).ToList(), backups.HasLegacyData));   // reads every zip
        BackupList.ItemsSource = rows;
        EmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ImportV1Button.Visibility = legacy || rows.Any(r => r.Info.Tier == BackupTier.V1 && r.Info.IsValid) ? Visibility.Visible : Visibility.Collapsed;
        return rows;
    }

    // Version 1's data comes from its v1 backup, made now if there is none yet (so the 1.x files are only read
    // once, checked, and the import can be undone like any restore).
    private async void ImportV1_Click(object sender, RoutedEventArgs e)
    {
        RestoreInfo.IsOpen = false;
        var row = (BackupList.ItemsSource as IEnumerable<BackupRow>)?.FirstOrDefault(r => r.Info.Tier == BackupTier.V1 && r.Info.IsValid);
        if (row == null)
        {
            ImportV1Button.IsEnabled = false;
            var result = await Task.Run(_backups.EnsureV1BackupAsync);
            ImportV1Button.IsEnabled = true;
            if (result.Outcome is not (BackupOutcome.Created or BackupOutcome.Unchanged))
            {
                ShowProblem(result.Outcome == BackupOutcome.NothingToBackUp ? Loc.Get("Backups_NoV1Data")
                    : Loc.Format("Backups_V1Unreadable", result.Problem ?? result.Outcome.ToString()));
                return;
            }
            row = (await LoadRowsAsync()).FirstOrDefault(r => r.Info.Tier == BackupTier.V1 && r.Info.IsValid);
            if (row == null) return;
        }
        BackupList.SelectedItem = row;
        await RestoreAsync(row.Info.Path, row.Description);
    }

    // Only what the selected backup holds can be ticked; all of that is ticked when another backup is chosen, except
    // the synced templates and folders: they overwrite shared sync files, so they are only restored when ticked by hand.
    private void ShowAvailableParts()
    {
        var available = (BackupList.SelectedItem as BackupRow)?.Contents?.Available ?? RestoreParts.All;
        void Offer(CheckBox box, RestoreParts part)
        {
            box.IsEnabled = available.HasFlag(part);
            box.IsChecked = available.HasFlag(part) && part != RestoreParts.SyncedFolders;
        }
        Offer(LocalBox, RestoreParts.Local);
        Offer(SettingsBox, RestoreParts.Settings);
        Offer(SyncSettingsBox, RestoreParts.SyncSettings);
        Offer(SyncedFoldersBox, RestoreParts.SyncedFolders);
        SyncBox.IsEnabled = SyncSettingsBox.IsEnabled || SyncedFoldersBox.IsEnabled;
        SyncPart_Click(this, new RoutedEventArgs());
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.IO.Directory.CreateDirectory(_backups.Directory);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{_backups.Directory}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { ShowProblem(Loc.Format("Backups_FolderOpenFailed", ex.Message)); }
    }

    private async void ExportBackup_Click(object sender, RoutedEventArgs e)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        if (await Helpers.DataFilePicker.PickBackupToSaveAsync(hwnd, Loc.Format("Backups_ExportFileName", DateTime.Now)) is not { } path) return;

        ExportButton.IsEnabled = false;
        var result = await App.ExportBackupAsync(path);
        ExportButton.IsEnabled = true;
        (ExportInfo.Severity, ExportInfo.Message) = result.Outcome switch
        {
            BackupOutcome.Created => (InfoBarSeverity.Success, Loc.Format("Backups_ExportSaved", path)),
            BackupOutcome.NothingToBackUp => (InfoBarSeverity.Informational, Loc.Get("Backups_NothingToBackUp")),
            BackupOutcome.NotQuiet => (InfoBarSeverity.Warning, Loc.Get("Backups_NotQuiet")),
            _ => (InfoBarSeverity.Error, Loc.Format("Backups_ExportFailed", result.Problem)),
        };
        ExportInfo.IsOpen = true;
    }

    // A damaged backup can't be chosen.
    private void BackupList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BackupList.SelectedItem is BackupRow { Info.IsValid: false }) BackupList.SelectedItem = null;
        ShowAvailableParts();
    }

    private void Parts_Click(object sender, RoutedEventArgs e) => UpdateRestoreButton();

    // Sync turns its available parts on or off: off only when all of them were on, so a half-ticked Sync turns them on.
    private void Sync_Click(object sender, RoutedEventArgs e)
    {
        var parts = SyncParts;
        bool on = !parts.All(b => b.IsChecked == true);
        foreach (var box in parts) box.IsChecked = on;
        SyncPart_Click(sender, e);
    }

    // Sync shows its available parts: ticked, empty, or a dash when only some of them are.
    private void SyncPart_Click(object sender, RoutedEventArgs e)
    {
        var parts = SyncParts;
        int ticked = parts.Count(b => b.IsChecked == true);
        SyncBox.IsChecked = parts.Count > 0 && ticked == parts.Count ? true : ticked > 0 ? null : false;
        UpdateRestoreButton();
    }

    private List<CheckBox> SyncParts => new[] { SyncSettingsBox, SyncedFoldersBox }.Where(b => b.IsEnabled).ToList();

    private RestoreParts Parts =>
        (LocalBox.IsChecked == true ? RestoreParts.Local : 0)
        | (SettingsBox.IsChecked == true ? RestoreParts.Settings : 0)
        | (SyncSettingsBox.IsChecked == true ? RestoreParts.SyncSettings : 0)
        | (SyncedFoldersBox.IsChecked == true ? RestoreParts.SyncedFolders : 0);

    private void UpdateRestoreButton() =>
        RestoreButton.IsEnabled = BackupList.SelectedItem is BackupRow { Info.IsValid: true } && Parts != 0;

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (BackupList.SelectedItem is BackupRow { Info.IsValid: true } row) await RestoreAsync(row.Info.Path, row.Description);
    }

    // An exported backup (or any backup zip) from anywhere, checked in full like the listed ones. It joins the list
    // at the top, selected, so its own parts are offered; cancelling the restore leaves it there to adjust them.
    private async void RestoreFromFile_Click(object sender, RoutedEventArgs e)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        if (await Helpers.DataFilePicker.PickBackupToOpenAsync(hwnd) is not { } path) return;

        var (problem, manifest) = await Task.Run(() => (BackupService.Verify(path, out var m), m));
        if (problem != null) { ShowProblem(Loc.Format("Backups_FileCantRestore", System.IO.Path.GetFileName(path), problem)); return; }

        var row = await Task.Run(() => new BackupRow(new BackupInfo(path, manifest!.Tier, manifest.CreatedUtc.ToLocalTime(), manifest, null), fromFile: true));
        var rows = (BackupList.ItemsSource as IEnumerable<BackupRow> ?? Enumerable.Empty<BackupRow>()).Where(r => r.Info.Path != path).ToList();
        rows.Insert(0, row);
        BackupList.ItemsSource = rows;
        EmptyText.Visibility = Visibility.Collapsed;
        BackupList.SelectedItem = row;
        await RestoreAsync(path, row.Title);
    }

    private async Task RestoreAsync(string backupPath, string description)
    {
        RestoreInfo.IsOpen = false;
        var outcome = await new RestoreFlow(XamlRoot).RunAsync(backupPath, Parts, description, confirm: true, restoring: () =>
        {
            RestoreButton.IsEnabled = false;
            RestoreFileButton.IsEnabled = false;
        });
        if (outcome.Cancelled) return;
        ShowProblem(outcome.Problem!);
        RestoreFileButton.IsEnabled = true;
        UpdateRestoreButton();
    }

    private void ShowProblem(string message)
    {
        RestoreInfo.Message = message;
        RestoreInfo.IsOpen = true;
    }
}
