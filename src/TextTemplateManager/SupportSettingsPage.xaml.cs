using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TextTemplateManager.Common;
using TextTemplateManager.Helpers;
using Windows.ApplicationModel.DataTransfer;

namespace TextTemplateManager;

/// <summary>Settings ▸ Support: the website's help, and how to report a problem, a feature request or a
/// vulnerability (e-mail with a prefilled form, or GitHub).</summary>
public sealed partial class SupportSettingsPage : Page
{
    public SupportSettingsPage()
    {
        InitializeComponent();
        HelpLink.NavigateUri = new Uri(SupportLinks.HelpUrl);
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
        MailHint.Text = $"If no e-mail window opened, write to {SupportLinks.Email}; the address is on the clipboard.";
        MailHint.Visibility = Visibility.Visible;
        ExternalLinks.Open(mailto);
    }
}
