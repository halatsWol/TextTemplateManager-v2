using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using TextTemplateManager.Common;
using TextTemplateManager.Helpers;
using TextTemplateManager.Services.Platform;
using Windows.ApplicationModel.DataTransfer;

namespace TextTemplateManager;

/// <summary>Settings ▸ Support: the website's help, the log export, and how to report a problem, a feature request or
/// a vulnerability (e-mail with a prefilled form, or GitHub). The navigation parameter is the app's data folder.</summary>
public sealed partial class SupportSettingsPage : Page
{
    private string _dataDirectory = "";
    private string? _exported;

    public SupportSettingsPage()
    {
        InitializeComponent();
        HelpLink.NavigateUri = new Uri(SupportLinks.HelpUrl);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _dataDirectory = e.Parameter as string ?? "";
        base.OnNavigatedTo(e);
    }

    private async void ExportLogs_Click(object sender, RoutedEventArgs e)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        if (await DataFilePicker.PickLogsToSaveAsync(hwnd, LogExport.SuggestedFileName(DateTime.Now)) is not { } path) return;

        ExportLogsButton.IsEnabled = false;
        ExportLogsProgress.IsActive = true;
        ExportLogsStatus.Visibility = Visibility.Visible;
        ExportLogsInfo.IsOpen = false;
        string dataDirectory = _dataDirectory;
        string? problem = await Task.Run(() =>
        {
            try
            {
                LogExport.Export(path, dataDirectory, new WindowsEventLogSource(), Redactor.ForCurrentUser(), DateTime.Now);
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        });
        ExportLogsButton.IsEnabled = true;
        ExportLogsProgress.IsActive = false;
        ExportLogsStatus.Visibility = Visibility.Collapsed;

        _exported = problem is null ? path : null;
        (ExportLogsInfo.Severity, ExportLogsInfo.Message) = problem is null
            ? (InfoBarSeverity.Success, Loc.Format("Support_LogsSaved", path))
            : (InfoBarSeverity.Error, Loc.Format("Support_LogsSaveFailed", problem));
        ShowLogsButton.Visibility = problem is null ? Visibility.Visible : Visibility.Collapsed;
        ExportLogsInfo.IsOpen = true;
    }

    private void ShowLogs_Click(object sender, RoutedEventArgs e)
    {
        if (_exported is null) return;
        try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{_exported}\""); }
        catch { }
    }

    private void ProblemMail_Click(object sender, RoutedEventArgs e) =>
        OpenMail(SupportLinks.ProblemMail(AppInfo.Version, SupportLinks.WindowsVersion()));

    private void FeatureMail_Click(object sender, RoutedEventArgs e) => OpenMail(SupportLinks.FeatureMail(AppInfo.Version));

    private void SecurityMail_Click(object sender, RoutedEventArgs e) =>
        OpenMail(SupportLinks.SecurityMail(AppInfo.Version, SupportLinks.WindowsVersion()));

    private void ProblemGitHub_Click(object sender, RoutedEventArgs e) => ExternalLinks.Open(SupportLinks.GitHubProblemUrl);

    private void FeatureGitHub_Click(object sender, RoutedEventArgs e) => ExternalLinks.Open(SupportLinks.GitHubFeatureUrl);

    private void SecurityGitHub_Click(object sender, RoutedEventArgs e) => ExternalLinks.Open(SupportLinks.GitHubSecurityUrl);

    // A mailto: link does nothing when no mail app is registered, so the address is also copied.
    private void OpenMail(string mailto)
    {
        var package = new DataPackage();
        package.SetText(SupportLinks.Email);
        Clipboard.SetContent(package);
        MailHint.Text = Loc.Format("Support_MailHint", SupportLinks.Email);
        MailHint.Visibility = Visibility.Visible;
        ExternalLinks.Open(mailto);
    }
}
