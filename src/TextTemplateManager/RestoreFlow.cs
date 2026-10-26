using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TextTemplateManager.Data;
using TextTemplateManager.Helpers;

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
            if (await AskAboutMissingFileAsync(issue.SourceId, issue.Name, issue.Path, issue.Problem, issue.HasCopy, Loc.Get("Restore_LeaveMissingButton"), linkElsewhere: !writeFiles) is { } placement)
                placements.Add(placement);
        foreach (var file in changed)
            if (await AskOverwriteSyncFileAsync(file, description))
                placements.Add(new SyncFilePlacement(file.SourceId, file.Path, WriteCopy: true));
        foreach (var target in targets)
            if (await AskAboutSyncedFolderAsync(target) is { } placement) placements.Add(placement);

        restoring?.Invoke();
        var outcome = await App.RestoreFromBackupAsync(backupPath, parts, placements);
        if (!outcome.Restored) return new Outcome(false, outcome.Problem ?? Loc.Get("Restore_Failed"));

        // Restored, but the restart didn't happen: nothing may be saved over the restored files any more.
        await new ContentDialog { XamlRoot = _xamlRoot, Title = Loc.Get("Restore_RestoredTitle"), Content = outcome.Problem, CloseButtonText = Loc.Get("Main_QuitButton") }.ShowAsync();
        App.Shutdown();
        return new Outcome(false, null);
    }

    private async Task<bool> ConfirmAsync(RestoreParts parts, string description, int issues, int changed, int targets)
    {
        var names = new List<string>();
        if (parts.HasFlag(RestoreParts.Local)) names.Add(Loc.Get("Restore_PartLocal"));
        if (parts.HasFlag(RestoreParts.Settings)) names.Add(Loc.Get("Restore_PartSettings"));
        if (parts.HasFlag(RestoreParts.SyncSettings)) names.Add(Loc.Get("Restore_PartSyncSettings"));
        if (parts.HasFlag(RestoreParts.SyncedFolders)) names.Add(Loc.Get("Restore_PartSyncedFolders"));
        string what = names.Count == 1 ? names[0] : Loc.Format("Restore_ListAnd", string.Join(", ", names.GetRange(0, names.Count - 1)), names[^1]);

        string message = Loc.Format("Restore_ConfirmReplace", what, description);
        if (targets > 0)
            message += "\n\n" + Loc.Get("Restore_ConfirmIntoFiles");
        else if (parts.HasFlag(RestoreParts.SyncSettings) && !parts.HasFlag(RestoreParts.SyncedFolders))
            message += "\n\n" + Loc.Get("Restore_ConfirmLinkAsIs");
        else if ((parts & RestoreParts.Sync) != 0)
            message += "\n\n" + Loc.Get("Restore_ConfirmFilesStay");
        if (changed > 0)
            message += "\n\n" + Loc.Plural("Restore_ConfirmChanged", changed);
        if (issues > 0)
            message += "\n\n" + Loc.Plural("Restore_ConfirmMissing", issues);
        message += "\n\n" + Loc.Get("Restore_ConfirmBackupFirst");

        var confirm = new ContentDialog
        {
            XamlRoot = _xamlRoot,
            Title = Loc.Get("Restore_ConfirmTitle"),
            Content = message,
            PrimaryButtonText = Loc.Get("Restore_RestoreButton"),
            CloseButtonText = Loc.Get("Common_CancelButton"),
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
            Title = Loc.Format("Restore_ChangedTitle", file.Name),
            Content = Loc.Format("Restore_ChangedText", file.Path, file.FileChanged, description),
            PrimaryButtonText = Loc.Get("Restore_OverwriteButton"),
            CloseButtonText = Loc.Get("Restore_KeepFileButton"),
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
        string what = path == null ? Loc.Get("Restore_NoFileKnown") : problem switch
        {
            SyncLocationProblem.FileMissing => Loc.Get("Restore_FileMissing"),
            SyncLocationProblem.FolderMissing => Loc.Format("Restore_FolderMissing", System.IO.Path.GetDirectoryName(path)),
            SyncLocationProblem.Unreadable => Loc.Get("Restore_FileUnreadable"),
            _ => Loc.Get("Restore_LocationImpossible"),
        };
        string copy = hasCopy ? Loc.Get("Restore_HasCopy") : Loc.Get("Restore_NoCopy");
        bool placeCopy = hasCopy && !linkElsewhere;
        string recreate = !hasCopy || path == null ? "" : problem switch
        {
            SyncLocationProblem.FileMissing => Loc.Get("Restore_RecreateButton"),
            SyncLocationProblem.FolderMissing => Loc.Get("Restore_CreateFolderButton"),
            _ => "",
        };
        while (true)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = _xamlRoot,
                Title = problem == SyncLocationProblem.Unreadable ? Loc.Format("Restore_UnreadableTitle", name) : Loc.Format("Restore_MissingTitle", name),
                Content = path == null ? $"{what}\n\n{copy}" : $"{path}\n\n{what}\n\n{copy}",
                PrimaryButtonText = recreate,
                SecondaryButtonText = placeCopy ? Loc.Get("Restore_ChooseLocationButton") : Loc.Get("Restore_LinkAnotherButton"),
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
            return await AskAboutMissingFileAsync(target.SourceId, target.Name, target.Path, problem, hasCopy: true, Loc.Get("Restore_SkipFolderButton"));

        var overwriteAll = new RadioButton { Content = Loc.Get("Main_ImportOverwriteAll"), GroupName = "RestoreConflicts" };
        var overwriteOlder = new RadioButton { Content = Loc.Get("Restore_OverwriteOlder"), GroupName = "RestoreConflicts", IsChecked = true };
        var skip = new RadioButton { Content = Loc.Get("Restore_SkipThem"), GroupName = "RestoreConflicts" };
        var content = new StackPanel { Spacing = 4 };
        void Say(string text) => content.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        Say(target.Path!);
        Say(Loc.Plural("Restore_CopyHolds", target.Templates));
        if (target.Conflicts > 0)
        {
            Say(Loc.Plural("Restore_CopyConflicts", target.Conflicts));
            foreach (var choice in new[] { overwriteAll, overwriteOlder, skip }) content.Children.Add(choice);
        }
        if (target.AddsSource) Say(Loc.Get("Restore_RelinkedNote"));

        var dialog = new ContentDialog
        {
            XamlRoot = _xamlRoot,
            Title = Loc.Format("Restore_IntoFileTitle", target.Name),
            Content = content,
            PrimaryButtonText = Loc.Get("Restore_IntoFileButton"),
            CloseButtonText = Loc.Get("Restore_SkipFolderButton"),
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;
        var how = overwriteAll.IsChecked == true ? ImportConflicts.OverwriteAll
            : skip.IsChecked == true ? ImportConflicts.Skip : ImportConflicts.OverwriteOlder;
        return new SyncFilePlacement(target.SourceId, target.Path!, WriteCopy: true, MergeAs: how);
    }
}
