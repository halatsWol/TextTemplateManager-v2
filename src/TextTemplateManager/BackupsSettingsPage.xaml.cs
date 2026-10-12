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
    public BackupRow(BackupInfo info)
    {
        Info = info;
        Title = info.Created.ToString("f");
        string tier = info.Tier switch
        {
            BackupTier.Recent => "Recent",
            BackupTier.TwoHourly => "Every 2 hours",
            BackupTier.Daily => "Daily",
            _ => "From Text Template Manager 1",
        };
        Detail = info.IsValid
            ? $"{tier} · made by version {info.Manifest!.AppVersion}"
            : $"{tier} · damaged, can't be restored ({info.Problem})";
    }

    public BackupInfo Info { get; }
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
        UpdateRestoreButton();
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

    // A damaged backup can't be chosen.
    private void BackupList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BackupList.SelectedItem is BackupRow { Info.IsValid: false }) BackupList.SelectedItem = null;
        UpdateRestoreButton();
    }

    private void Parts_Click(object sender, RoutedEventArgs e) => UpdateRestoreButton();

    // Sync turns both of its parts on or off: off only when both were on, so a half-ticked Sync turns them on.
    private void Sync_Click(object sender, RoutedEventArgs e)
    {
        bool on = !(SyncSettingsBox.IsChecked == true && SyncedFoldersBox.IsChecked == true);
        SyncSettingsBox.IsChecked = on;
        SyncedFoldersBox.IsChecked = on;
        SyncPart_Click(sender, e);
    }

    // Sync shows its parts: ticked, empty, or a dash when only one of them is.
    private void SyncPart_Click(object sender, RoutedEventArgs e)
    {
        bool settings = SyncSettingsBox.IsChecked == true, folders = SyncedFoldersBox.IsChecked == true;
        SyncBox.IsChecked = settings && folders ? true : settings || folders ? null : false;
        UpdateRestoreButton();
    }

    private RestoreParts Parts =>
        (LocalBox.IsChecked == true ? RestoreParts.Local : 0)
        | (SettingsBox.IsChecked == true ? RestoreParts.Settings : 0)
        | (SyncSettingsBox.IsChecked == true ? RestoreParts.SyncSettings : 0)
        | (SyncedFoldersBox.IsChecked == true ? RestoreParts.SyncedFolders : 0);

    private void UpdateRestoreButton() =>
        RestoreButton.IsEnabled = BackupList.SelectedItem is BackupRow { Info.IsValid: true } && Parts != 0;

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (BackupList.SelectedItem is not BackupRow { Info.IsValid: true } row) return;
        var parts = Parts;
        var names = new List<string>();
        if (parts.HasFlag(RestoreParts.Local)) names.Add("templates and folders");
        if (parts.HasFlag(RestoreParts.Settings)) names.Add("settings");
        if (parts.HasFlag(RestoreParts.SyncSettings)) names.Add("sync settings");
        if (parts.HasFlag(RestoreParts.SyncedFolders)) names.Add("synced templates and folders");
        string what = names.Count == 1 ? names[0] : string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1];

        string message = $"Your current {what} are replaced with the ones from {row.Title}.";
        if ((parts & RestoreParts.Sync) != 0)
            message += "\n\nShared sync files themselves aren't changed: a file that still exists keeps its contents, and the folder of a missing one shows its copy from the backup.";
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

        RestoreButton.IsEnabled = false;
        var outcome = await App.RestoreFromBackupAsync(row.Info.Path, parts);
        if (!outcome.Restored)
        {
            ShowProblem(outcome.Problem ?? "Restoring failed, nothing was changed.");
            UpdateRestoreButton();
            return;
        }

        // Restored, but the restart didn't happen: nothing may be saved over the restored files any more.
        await new ContentDialog { XamlRoot = XamlRoot, Title = "Backup restored", Content = outcome.Problem, CloseButtonText = "Quit" }.ShowAsync();
        App.Shutdown();
    }

    private void ShowProblem(string message)
    {
        RestoreInfo.Message = message;
        RestoreInfo.IsOpen = true;
    }
}
