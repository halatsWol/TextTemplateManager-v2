using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TextTemplateManager.Data;

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
            BackupTier.Recent => "Recent",
            BackupTier.TwoHourly => "Every 2 hours",
            BackupTier.Daily => "Daily",
            BackupTier.Exported => "Exported",
            _ => "From TextTemplateManager v1",
        };
        Detail = info.IsValid
            ? $"{(fromFile ? "From a file · " : "")}{tier} · made by version {info.Manifest!.AppVersion}"
            : $"{tier} · damaged, can't be restored ({info.Problem})";
    }

    public BackupInfo Info { get; }

    /// <summary>What the backup can restore; null for a damaged one.</summary>
    public BackupContents? Contents { get; }

    public string Title { get; }
    public string Detail { get; }
    public double Opacity => Info.IsValid ? 1 : 0.5;
}

/// <summary>Where the automatic backups are, and restoring from one.</summary>
public sealed partial class BackupsSettingsPage : Page
{
    public BackupsSettingsPage() => InitializeComponent();

    private static App App => (App)Application.Current;

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        FolderText.Text = App.Backups.Directory;
        var rows = await Task.Run(() => App.Backups.List().Select(b => new BackupRow(b)).ToList());   // reads every zip
        BackupList.ItemsSource = rows;
        EmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        BackupList.SelectedItem = rows.FirstOrDefault(r => r.Info.IsValid);   // the newest one that can be restored
        ShowAvailableParts();
    }

    // Only what the selected backup holds can be ticked; all of that is ticked when another backup is chosen.
    private void ShowAvailableParts()
    {
        var available = (BackupList.SelectedItem as BackupRow)?.Contents?.Available ?? RestoreParts.All;
        void Offer(CheckBox box, RestoreParts part)
        {
            box.IsEnabled = available.HasFlag(part);
            box.IsChecked = available.HasFlag(part);
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
            System.IO.Directory.CreateDirectory(App.Backups.Directory);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{App.Backups.Directory}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { ShowProblem($"The folder couldn't be opened: {ex.Message}"); }
    }

    private async void ExportBackup_Click(object sender, RoutedEventArgs e)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        if (await Helpers.DataFilePicker.PickBackupToSaveAsync(hwnd, $"TextTemplateManager backup {DateTime.Now:yyyy-MM-dd}") is not { } path) return;

        ExportButton.IsEnabled = false;
        var result = await App.ExportBackupAsync(path);
        ExportButton.IsEnabled = true;
        (ExportInfo.Severity, ExportInfo.Message) = result.Outcome switch
        {
            BackupOutcome.Created => (InfoBarSeverity.Success, $"Saved to {path}"),
            BackupOutcome.NothingToBackUp => (InfoBarSeverity.Informational, "There is nothing to back up yet."),
            BackupOutcome.NotQuiet => (InfoBarSeverity.Warning, "Files were still being saved. Try again in a moment."),
            _ => (InfoBarSeverity.Error, $"The backup couldn't be saved: {result.Problem}"),
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
        if (BackupList.SelectedItem is BackupRow { Info.IsValid: true } row) await RestoreAsync(row.Info.Path, row.Title);
    }

    // An exported backup (or any backup zip) from anywhere, checked in full like the listed ones. It joins the list
    // at the top, selected, so its own parts are offered; cancelling the restore leaves it there to adjust them.
    private async void RestoreFromFile_Click(object sender, RoutedEventArgs e)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        if (await Helpers.DataFilePicker.PickBackupToOpenAsync(hwnd) is not { } path) return;

        var (problem, manifest) = await Task.Run(() => (BackupService.Verify(path, out var m), m));
        if (problem != null) { ShowProblem($"\"{System.IO.Path.GetFileName(path)}\" can't be restored: {problem}."); return; }

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
        var parts = Parts;
        var names = new List<string>();
        if (parts.HasFlag(RestoreParts.Local)) names.Add("templates and folders");
        if (parts.HasFlag(RestoreParts.Settings)) names.Add("settings");
        if (parts.HasFlag(RestoreParts.SyncSettings)) names.Add("sync settings");
        if (parts.HasFlag(RestoreParts.SyncedFolders)) names.Add("synced templates and folders");
        string what = names.Count == 1 ? names[0] : string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1];

        var issues = parts.HasFlag(RestoreParts.SyncSettings)
            ? await Task.Run(() => RestoreService.FindSyncLocationIssues(backupPath))
            : Array.Empty<SyncLocationIssue>();

        string message = $"Your current {what} are replaced with the ones from {description}.";
        if ((parts & RestoreParts.Sync) != 0)
            message += "\n\nShared sync files themselves aren't changed: a file that still exists keeps its contents, and the folder of a missing one shows its copy from the backup.";
        if (issues.Count > 0)
            message += issues.Count == 1
                ? "\n\nOne sync file of the backup isn't on this PC. You're asked next what to do with it."
                : $"\n\n{issues.Count} sync files of the backup aren't on this PC. You're asked next what to do with each.";
        message += "\n\nThe current state is backed up first, then the app restarts.";

        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Restore this backup?",
            Content = message,
            PrimaryButtonText = "Restore",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

        var placements = new List<SyncFilePlacement>();
        foreach (var issue in issues)
            if (await AskAboutSyncFileAsync(issue) is { } placement) placements.Add(placement);

        RestoreButton.IsEnabled = false;
        RestoreFileButton.IsEnabled = false;
        var outcome = await App.RestoreFromBackupAsync(backupPath, parts, placements);
        if (!outcome.Restored)
        {
            ShowProblem(outcome.Problem ?? "Restoring failed, nothing was changed.");
            RestoreFileButton.IsEnabled = true;
            UpdateRestoreButton();
            return;
        }

        // Restored, but the restart didn't happen: nothing may be saved over the restored files any more.
        await new ContentDialog { XamlRoot = XamlRoot, Title = "Backup restored", Content = outcome.Problem, CloseButtonText = "Quit" }.ShowAsync();
        App.Shutdown();
    }

    // A missing sync file of the restored sync settings: recreated where it was (with its folder if that is gone
    // too), placed somewhere else, or left missing (null). Without a copy in the backup an existing file can only
    // be linked instead.
    private async Task<SyncFilePlacement?> AskAboutSyncFileAsync(SyncLocationIssue issue)
    {
        string problem = issue.Problem switch
        {
            SyncLocationProblem.FileMissing => "The file isn't there any more.",
            SyncLocationProblem.FolderMissing => $"Its folder doesn't exist on this PC:\n{System.IO.Path.GetDirectoryName(issue.Path)}",
            _ => "This location can't exist on this PC, for example because the drive or network share isn't there.",
        };
        string copy = issue.HasCopy
            ? "The backup holds a copy of its templates and folders."
            : "The backup holds no copy of it, so only an existing file can be linked instead.";
        string recreate = !issue.HasCopy ? "" : issue.Problem switch
        {
            SyncLocationProblem.FileMissing => "Recreate it there",
            SyncLocationProblem.FolderMissing => "Create the folder and the file",
            _ => "",
        };
        while (true)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = $"Sync file \"{issue.Name}\" is missing",
                Content = $"{issue.Path}\n\n{problem}\n\n{copy}",
                PrimaryButtonText = recreate,
                SecondaryButtonText = issue.HasCopy ? "Choose a location…" : "Link another file…",
                CloseButtonText = "Leave it missing",
                DefaultButton = ContentDialogButton.Close,
            };
            switch (await dialog.ShowAsync())
            {
                case ContentDialogResult.Primary:
                    return new SyncFilePlacement(issue.SourceId, issue.Path, WriteCopy: true);
                case ContentDialogResult.Secondary:
                    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
                    string? path = issue.HasCopy
                        ? await Helpers.DataFilePicker.PickToSaveAsync(hwnd, System.IO.Path.GetFileNameWithoutExtension(issue.Path))
                        : await Helpers.DataFilePicker.PickToOpenAsync(hwnd);
                    if (path != null) return new SyncFilePlacement(issue.SourceId, path, WriteCopy: issue.HasCopy);
                    break;   // the picker was cancelled: ask again
                default:
                    return null;
            }
        }
    }

    private void ShowProblem(string message)
    {
        RestoreInfo.Message = message;
        RestoreInfo.IsOpen = true;
    }
}
