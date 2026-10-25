using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using TextTemplateManager.Helpers;
using TextTemplateManager.Services.Updates;

namespace TextTemplateManager
{
    // The update notice above the panes (App.OnUpdateStatus sends the updater's status here, never to the tour's page).
    public sealed partial class MainPage
    {
        private enum UpdateAction { None, Install, Download, Retry }

        private UpdateAction _updateAction;
        private (UpdateStage, Version?)? _updateBarClosedFor;

        public void ShowUpdateStatus(UpdateStatus s)
        {
            string v = VersionName(s.Version);
            string extra = string.IsNullOrEmpty(s.Message) ? "" : " " + s.Message;
            (InfoBarSeverity severity, string title, string message, UpdateAction action, bool closable) = s.Stage switch
            {
                UpdateStage.Downloading => (InfoBarSeverity.Informational, Loc.Get("Updates_DownloadingTitle"),
                    Loc.Get("Updates_DownloadingText"), UpdateAction.None, true),
                UpdateStage.Ready => (InfoBarSeverity.Success, Loc.Get("Updates_ReadyTitle"),
                    Loc.Get("Updates_ReadyText") + extra, UpdateAction.Install, true),
                UpdateStage.Installing => (InfoBarSeverity.Informational, Loc.Get("Updates_InstallingTitle"),
                    s.Version is { } target ? Loc.Format("Updates_InstallingVersion", Short(target)) : Loc.Get("Updates_InstallingNew"),
                    UpdateAction.None, false),
                UpdateStage.Available when s.CanDownload => (InfoBarSeverity.Informational, Loc.Get("Updates_AvailableTitle"),
                    Loc.Format("Updates_Available", v) + (extra.Length > 0 ? extra
                        : s.StoreBlocked ? " " + Loc.Get("Updates_StoreBlocked") : ""),
                    UpdateAction.Download, true),
                UpdateStage.Available => (InfoBarSeverity.Informational, Loc.Get("Updates_AvailableTitle"),
                    Loc.Format("Updates_AvailableManaged", v), UpdateAction.None, true),
                UpdateStage.Failed => (InfoBarSeverity.Warning, Loc.Get("Updates_FailedTitle"), s.Message ?? Loc.Get("Updates_FailedText"),
                    UpdateAction.Retry, true),
                UpdateStage.ManualStepsNeeded => (InfoBarSeverity.Warning, Loc.Get("Updates_ManualStepsTitle"),
                    Loc.Get("Updates_ManualStepsText"), UpdateAction.None, true),
                _ => (InfoBarSeverity.Informational, "", "", UpdateAction.None, true),
            };

            bool show = title.Length > 0 && _updateBarClosedFor != (s.Stage, s.Version);
            UpdateBar.Severity = severity;
            UpdateBar.Title = title;
            UpdateBar.Message = message;
            UpdateBar.IsClosable = closable;
            _updateAction = action;
            UpdateBarAction.Content = action switch
            {
                UpdateAction.Install => Loc.Get("General_RestartToUpdateButton"),
                UpdateAction.Download => Loc.Get("General_DownloadButton"),
                UpdateAction.Retry => Loc.Get("General_TryAgainButton"),
                _ => "",
            };
            UpdateBarAction.Visibility = action == UpdateAction.None ? Visibility.Collapsed : Visibility.Visible;
            UpdateBarAction.IsEnabled = true;
            UpdateProgress.Visibility = s.Stage == UpdateStage.Downloading ? Visibility.Visible : Visibility.Collapsed;
            UpdateProgress.IsIndeterminate = s.Stage == UpdateStage.Downloading && s.Progress <= 0;
            UpdateProgress.Value = s.Progress;
            UpdateBar.IsOpen = show;
        }

        /// <summary>3-part version for people (the package's 4th part is always 0).</summary>
        internal static string Short(Version v) => v.Revision > 0 ? v.ToString() : v.ToString(3);

        /// <summary>"Version 2.2.1", or "A new version" when the Store didn't name it.</summary>
        internal static string VersionName(Version? v) => v is { } known ? Loc.Format("General_Version", Short(known)) : Loc.Get("Updates_NewVersion");

        private async void UpdateBarAction_Click(object sender, RoutedEventArgs e)
        {
            if (Application.Current is not App app) return;
            UpdateBarAction.IsEnabled = false;
            switch (_updateAction)
            {
                case UpdateAction.Install: await app.InstallUpdateAsync(idle: false); break;
                case UpdateAction.Download: await app.DownloadUpdateAsync(); break;
                case UpdateAction.Retry: await app.CheckForUpdatesAsync(); break;
            }
            UpdateBarAction.IsEnabled = true;
        }

        // Closed for this stage and version: it comes back when either changes.
        private void UpdateBar_CloseButtonClick(InfoBar sender, object args)
        {
            if (Application.Current is App { Updates: { } updates })
                _updateBarClosedFor = (updates.Status.Stage, updates.Status.Version);
        }

        /// <summary>Shows a problem with the update in a dialog (Restart to update that didn't happen).</summary>
        public async void ReportUpdateProblem(string message) =>
            await ShowMessageAsync(Loc.Get("Updates_NotInstalledTitle"), Loc.Format("Updates_NotInstalledText", message));
    }
}
