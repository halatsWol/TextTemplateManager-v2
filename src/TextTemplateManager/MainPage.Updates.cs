using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
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
                UpdateStage.Downloading => (InfoBarSeverity.Informational, "Downloading update",
                    "The update is downloading in the background. You can keep working.", UpdateAction.None, true),
                UpdateStage.Ready => (InfoBarSeverity.Success, "Update ready",
                    $"The update is ready to install. Restart to update saves your work and opens the new version within a few seconds.{extra}", UpdateAction.Install, true),
                UpdateStage.Installing => (InfoBarSeverity.Informational, "Installing update",
                    $"TextTemplateManager restarts with {(s.Version is { } target ? "version " + Short(target) : "the new version")} in a moment.", UpdateAction.None, false),
                UpdateStage.Available when s.CanDownload => (InfoBarSeverity.Informational, "Update available",
                    $"{v} is available." + (extra.Length > 0 ? extra
                        : s.StoreBlocked ? " Automatic updates for Store apps are turned off on this PC, so choose Download to get it." : ""),
                    UpdateAction.Download, true),
                UpdateStage.Available => (InfoBarSeverity.Informational, "Update available",
                    $"{v} is available. Your organization manages updates on this PC: ask your IT department, or update the app in the Microsoft Store if you're allowed to.",
                    UpdateAction.None, true),
                UpdateStage.Failed => (InfoBarSeverity.Warning, "Update problem", s.Message ?? "The update didn't work.", UpdateAction.Retry, true),
                UpdateStage.ManualStepsNeeded => (InfoBarSeverity.Warning, "Update couldn't be installed",
                    "The update is downloaded, but the app couldn't switch to it. Quit the app (File ▸ Exit) and start it again from the Start menu. " +
                    "If Help ▸ About still shows the old version, update the app in the Microsoft Store.", UpdateAction.None, true),
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
                UpdateAction.Install => "Restart to update",
                UpdateAction.Download => "Download",
                UpdateAction.Retry => "Try again",
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
        internal static string VersionName(Version? v) => v is { } known ? "Version " + Short(known) : "A new version";

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
            await ShowMessageAsync("Update not installed", message + "\n\nYour templates weren't affected. The update stays ready; you can try again.");
    }
}
