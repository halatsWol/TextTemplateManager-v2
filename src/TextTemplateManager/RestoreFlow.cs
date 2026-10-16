using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TextTemplateManager.Data;

namespace TextTemplateManager;

/// <summary>Restoring a backup from the UI: the confirmation, the questions about its sync files, then the restore,
/// which restarts the app. Used by Settings ▸ Backups and by the first start's import from 1.x.</summary>
public sealed class RestoreFlow
{
    /// <summary>Not restored: cancelled (no <paramref name="Problem"/>) or failed with nothing changed.</summary>
    public sealed record Outcome(bool Cancelled, string? Problem);

    private readonly XamlRoot _xamlRoot;

    public RestoreFlow(XamlRoot xamlRoot) => _xamlRoot = xamlRoot;

    private static App App => (App)Application.Current;

    /// <summary>Restores <paramref name="parts"/> of the backup; returns only when it wasn't restored. Without
    /// <paramref name="confirm"/> the user already chose it, and only the sync files are asked about.
    /// <paramref name="restoring"/> runs once the questions are answered. Without <see cref="RestoreParts.SyncedFolders"/>
    /// a shared sync file is only written when the user recreates a missing one.</summary>
    public async Task<Outcome> RunAsync(string backupPath, RestoreParts parts, string description, bool confirm, Action? restoring = null)
    {
        bool writeFiles = parts.HasFlag(RestoreParts.SyncedFolders);
        var issues = parts.HasFlag(RestoreParts.SyncSettings)
            ? await Task.Run(() => RestoreService.FindSyncLocationIssues(backupPath))
            : Array.Empty<SyncLocationIssue>();
        var changed = parts.HasFlag(RestoreParts.SyncSettings) && writeFiles
            ? await Task.Run(() => RestoreService.FindChangedSyncFiles(backupPath))
            : Array.Empty<ChangedSyncFile>();
        var targets = parts.HasFlag(RestoreParts.SyncedFolders) && !parts.HasFlag(RestoreParts.SyncSettings)
            ? await Task.Run(() => App.SyncedFolderTargets(backupPath))
            : Array.Empty<SyncedFolderTarget>();

        if (confirm && !await ConfirmAsync(parts, description, issues.Count, changed.Count, targets.Count))
            return new Outcome(true, null);

        var placements = new List<SyncFilePlacement>();
        foreach (var issue in issues)
            if (await AskAboutMissingFileAsync(issue.SourceId, issue.Name, issue.Path, issue.Problem, issue.HasCopy, "Leave it missing", linkElsewhere: !writeFiles) is { } placement)
                placements.Add(placement);
        foreach (var file in changed)
            if (await AskOverwriteSyncFileAsync(file, description))
                placements.Add(new SyncFilePlacement(file.SourceId, file.Path, WriteCopy: true));
        foreach (var target in targets)
            if (await AskAboutSyncedFolderAsync(target) is { } placement) placements.Add(placement);

        restoring?.Invoke();
        var outcome = await App.RestoreFromBackupAsync(backupPath, parts, placements);
        if (!outcome.Restored) return new Outcome(false, outcome.Problem ?? "Restoring failed, nothing was changed.");

        // Restored, but the restart didn't happen: nothing may be saved over the restored files any more.
        await new ContentDialog { XamlRoot = _xamlRoot, Title = "Backup restored", Content = outcome.Problem, CloseButtonText = "Quit" }.ShowAsync();
        App.Shutdown();
        return new Outcome(false, null);
    }

    private async Task<bool> ConfirmAsync(RestoreParts parts, string description, int issues, int changed, int targets)
    {
        var names = new List<string>();
        if (parts.HasFlag(RestoreParts.Local)) names.Add("templates and folders");
        if (parts.HasFlag(RestoreParts.Settings)) names.Add("settings");
        if (parts.HasFlag(RestoreParts.SyncSettings)) names.Add("sync settings");
        if (parts.HasFlag(RestoreParts.SyncedFolders)) names.Add("synced templates and folders");
        string what = names.Count == 1 ? names[0] : string.Join(", ", names.GetRange(0, names.Count - 1)) + " and " + names[^1];

        string message = $"Your current {what} are replaced with the ones from {description}.";
        if (targets > 0)
            message += "\n\nWithout the sync settings, the synced templates and folders are restored into their sync files. You're asked next for each synced folder.";
        else if (parts.HasFlag(RestoreParts.SyncSettings) && !parts.HasFlag(RestoreParts.SyncedFolders))
            message += "\n\nAn existing shared sync file is linked as it is; you're asked about a missing one. To restore the synced templates and folders into the files, also tick \"Synced templates and folders\".";
        else if ((parts & RestoreParts.Sync) != 0)
            message += "\n\nA shared sync file that still exists stays as it is, unless you choose to overwrite it; the folder of a missing one shows its copy from the backup.";
        if (changed > 0)
            message += changed == 1
                ? "\n\nOne shared sync file has changed since the backup. You're asked next whether to overwrite it or keep it."
                : $"\n\n{changed} shared sync files have changed since the backup. You're asked next whether to overwrite or keep each.";
        if (issues > 0)
            message += issues == 1
                ? "\n\nOne sync file of the backup isn't on this PC. You're asked next what to do with it."
                : $"\n\n{issues} sync files of the backup aren't on this PC. You're asked next what to do with each.";
        message += "\n\nThe current state is backed up first, then the app restarts.";

        var confirm = new ContentDialog
        {
            XamlRoot = _xamlRoot,
            Title = "Restore this backup?",
            Content = message,
            PrimaryButtonText = "Restore",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        return await confirm.ShowAsync() == ContentDialogResult.Primary;
    }

    // A shared sync file that has changed since the backup: true to overwrite it with the backup's copy. Keeping it is
    // the default, also on Esc.
    private async Task<bool> AskOverwriteSyncFileAsync(ChangedSyncFile file, string description)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = _xamlRoot,
            Title = $"Sync file \"{file.Name}\" has changed",
            Content = $"{file.Path}\n\nThe file was last changed {file.FileChanged:f} and differs from the backup's copy ({description}).\n\n"
                + "Keep it: the folder shows the file as it is now.\n"
                + "Overwrite it: the file gets the backup's copy, for everyone who uses it.",
            PrimaryButtonText = "Overwrite with the backup's copy",
            CloseButtonText = "Keep the file",
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    // A sync file that isn't usable where it should be: recreated there (with its folder if that is gone too), placed
    // somewhere else, or not (null, the close button). Without a copy in the backup, or with <paramref name="linkElsewhere"/>
    // (synced folders not restored), only an existing file can be linked somewhere else.
    private async Task<SyncFilePlacement?> AskAboutMissingFileAsync(Guid sourceId, string name, string? path,
        SyncLocationProblem problem, bool hasCopy, string closeText, bool linkElsewhere = false)
    {
        string what = path == null ? "No sync file is known for it, neither in the current sync settings nor in the backup's." : problem switch
        {
            SyncLocationProblem.FileMissing => "The file isn't there any more.",
            SyncLocationProblem.FolderMissing => $"Its folder doesn't exist on this PC:\n{System.IO.Path.GetDirectoryName(path)}",
            SyncLocationProblem.Unreadable => "A file is there, but it isn't a valid sync file, so it isn't overwritten.",
            _ => "This location can't exist on this PC, for example because the drive or network share isn't there.",
        };
        string copy = hasCopy ? "The backup holds a copy of its templates and folders."
            : "The backup holds no copy of it, so only an existing file can be linked instead.";
        bool placeCopy = hasCopy && !linkElsewhere;
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
                XamlRoot = _xamlRoot,
                Title = problem == SyncLocationProblem.Unreadable ? $"Sync file \"{name}\" can't be read" : $"Sync file \"{name}\" is missing",
                Content = path == null ? $"{what}\n\n{copy}" : $"{path}\n\n{what}\n\n{copy}",
                PrimaryButtonText = recreate,
                SecondaryButtonText = placeCopy ? "Choose a location…" : "Link another file…",
                CloseButtonText = closeText,
                DefaultButton = ContentDialogButton.Close,
            };
            switch (await dialog.ShowAsync())
            {
                case ContentDialogResult.Primary:
                    return new SyncFilePlacement(sourceId, path!, WriteCopy: true);
                case ContentDialogResult.Secondary:
                    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
                    string? chosen = placeCopy
                        ? await Helpers.DataFilePicker.PickToSaveAsync(hwnd, path == null ? name : System.IO.Path.GetFileNameWithoutExtension(path))
                        : await Helpers.DataFilePicker.PickToOpenAsync(hwnd);
                    if (chosen != null) return new SyncFilePlacement(sourceId, chosen, WriteCopy: placeCopy);
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
            XamlRoot = _xamlRoot,
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
}
