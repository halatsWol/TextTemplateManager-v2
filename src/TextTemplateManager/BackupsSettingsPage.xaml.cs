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

    /// <summary>What the restore confirmation calls it.</summary>
    public string Description => Info.Tier == BackupTier.V1 ? "TextTemplateManager 1" : Title;
    public double Opacity => Info.IsValid ? 1 : 0.5;
}

/// <summary>Where the automatic backups are, and restoring from one.</summary>
public sealed partial class BackupsSettingsPage : Page
{
    /// <summary>Opens the page on the backup at <paramref name="Select"/>, and with <paramref name="Confirm"/> straight
    /// on its restore confirmation (cancelling it leaves the page to adjust the parts).</summary>
    public sealed record Request(string Select, bool Confirm);

    /// <summary>The backups the page lists (those of the window's store: the tour lists its sample ones), and
    /// optionally a <see cref="Request"/>.</summary>
    public sealed record Parameters(BackupService Backups, Request? Request = null);

    private BackupService _backups = null!;   // set on navigation

    public BackupsSettingsPage() => InitializeComponent();

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
        if (requested != null && parameters!.Request!.Confirm) await RestoreAsync(requested.Info.Path, requested.Description);
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
                ShowProblem(result.Outcome == BackupOutcome.NothingToBackUp ? "There is no TextTemplateManager 1 data on this PC."
                    : $"The data of TextTemplateManager 1 couldn't be read: {result.Problem ?? result.Outcome.ToString()}");
                return;
            }
            row = (await LoadRowsAsync()).FirstOrDefault(r => r.Info.Tier == BackupTier.V1 && r.Info.IsValid);
            if (row == null) return;
        }
        BackupList.SelectedItem = row;
        await RestoreAsync(row.Info.Path, row.Description);
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
            System.IO.Directory.CreateDirectory(_backups.Directory);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{_backups.Directory}\"") { UseShellExecute = true });
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
        if (BackupList.SelectedItem is BackupRow { Info.IsValid: true } row) await RestoreAsync(row.Info.Path, row.Description);
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
        var targets = parts.HasFlag(RestoreParts.SyncedFolders) && !parts.HasFlag(RestoreParts.SyncSettings)
            ? await Task.Run(() => App.SyncedFolderTargets(backupPath))
            : Array.Empty<SyncedFolderTarget>();

        string message = $"Your current {what} are replaced with the ones from {description}.";
        if (targets.Count > 0)
            message += "\n\nWithout the sync settings, the synced templates and folders are restored into their sync files. You're asked next for each synced folder.";
        else if ((parts & RestoreParts.Sync) != 0)
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
            if (await AskAboutMissingFileAsync(issue.SourceId, issue.Name, issue.Path, issue.Problem, issue.HasCopy, "Leave it missing") is { } placement)
                placements.Add(placement);
        foreach (var target in targets)
            if (await AskAboutSyncedFolderAsync(target) is { } placement) placements.Add(placement);

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

    // A sync file that isn't usable where it should be: recreated there (with its folder if that is gone too), placed
    // somewhere else, or not (null, the close button). Without a copy in the backup an existing file can only be
    // linked instead.
    private async Task<SyncFilePlacement?> AskAboutMissingFileAsync(Guid sourceId, string name, string? path,
        SyncLocationProblem problem, bool hasCopy, string closeText)
    {
        string what = path == null ? "No sync file is known for it, neither in the current sync settings nor in the backup's." : problem switch
        {
            SyncLocationProblem.FileMissing => "The file isn't there any more.",
            SyncLocationProblem.FolderMissing => $"Its folder doesn't exist on this PC:\n{System.IO.Path.GetDirectoryName(path)}",
            SyncLocationProblem.Unreadable => "A file is there, but it isn't a valid sync file, so it isn't overwritten.",
            _ => "This location can't exist on this PC, for example because the drive or network share isn't there.",
        };
        string copy = hasCopy
            ? "The backup holds a copy of its templates and folders."
            : "The backup holds no copy of it, so only an existing file can be linked instead.";
        string recreate = !hasCopy || path == null ? "" : problem switch
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
                Title = problem == SyncLocationProblem.Unreadable ? $"Sync file \"{name}\" can't be read" : $"Sync file \"{name}\" is missing",
                Content = path == null ? $"{what}\n\n{copy}" : $"{path}\n\n{what}\n\n{copy}",
                PrimaryButtonText = recreate,
                SecondaryButtonText = hasCopy ? "Choose a location…" : "Link another file…",
                CloseButtonText = closeText,
                DefaultButton = ContentDialogButton.Close,
            };
            switch (await dialog.ShowAsync())
            {
                case ContentDialogResult.Primary:
                    return new SyncFilePlacement(sourceId, path!, WriteCopy: true);
                case ContentDialogResult.Secondary:
                    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
                    string? chosen = hasCopy
                        ? await Helpers.DataFilePicker.PickToSaveAsync(hwnd, path == null ? name : System.IO.Path.GetFileNameWithoutExtension(path))
                        : await Helpers.DataFilePicker.PickToOpenAsync(hwnd);
                    if (chosen != null) return new SyncFilePlacement(sourceId, chosen, WriteCopy: hasCopy);
                    break;   // the picker was cancelled: ask again
                default:
                    return null;
            }
        }
    }

    // A synced folder restored without the sync settings goes into its sync file: merged like an import when the file
    // is there, otherwise placed like a missing one. Skipping it leaves the file as it is.
    private async Task<SyncFilePlacement?> AskAboutSyncedFolderAsync(SyncedFolderTarget target)
    {
        if (target.Problem is { } problem)
            return await AskAboutMissingFileAsync(target.SourceId, target.Name, target.Path, problem, hasCopy: true, "Skip this folder");

        var overwriteAll = new RadioButton { Content = "Overwrite all of them", GroupName = "RestoreConflicts" };
        var overwriteOlder = new RadioButton { Content = "Overwrite only where the backup's is more recent", GroupName = "RestoreConflicts", IsChecked = true };
        var skip = new RadioButton { Content = "Skip them, keep the ones in the file", GroupName = "RestoreConflicts" };
        var content = new StackPanel { Spacing = 4 };
        void Say(string text) => content.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        Say(target.Path!);
        Say($"The backup's copy holds {target.Templates} template(s). Folders with the same name are merged.");
        if (target.Conflicts > 0)
        {
            Say($"{target.Conflicts} of these template(s) already exist in the file (same template, or same title in the same folder):");
            foreach (var choice in new[] { overwriteAll, overwriteOlder, skip }) content.Children.Add(choice);
        }
        if (target.AddsSource) Say("The file isn't linked in your current sync settings; it is linked again.");

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"Restore \"{target.Name}\" into its sync file?",
            Content = content,
            PrimaryButtonText = "Restore into the file",
            CloseButtonText = "Skip this folder",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;
        var how = overwriteAll.IsChecked == true ? ImportConflicts.OverwriteAll
            : skip.IsChecked == true ? ImportConflicts.Skip : ImportConflicts.OverwriteOlder;
        return new SyncFilePlacement(target.SourceId, target.Path!, WriteCopy: true, MergeAs: how);
    }

    private void ShowProblem(string message)
    {
        RestoreInfo.Message = message;
        RestoreInfo.IsOpen = true;
    }
}
