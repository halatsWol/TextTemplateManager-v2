using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using TextTemplateManager.Common;
using TextTemplateManager.Services.Platform;
using Windows.ApplicationModel.DataTransfer;

namespace TextTemplateManager.Controls;

/// <summary>Help ▸ About. The primary button ("View License") and the secondary one ("Third-party notices") are
/// answered by the caller.</summary>
public sealed partial class AboutDialog : ContentDialog
{
    private readonly string _email;

    public AboutDialog(string websiteUrl, string email)
    {
        InitializeComponent();
        _email = email;
        VersionText.Text = $"Version {AppInfo.Version}";
        ReleaseText.Text = AppInfo.ReleaseDate.Length > 0 ? $"Released {AppInfo.ReleaseDate}" : "Released: dev build";
        ProtocolText.Text = $"Connector API protocol {BrowserConnector.ProtocolVersion}";
        CopyrightText.Text = $"© {DateTime.Now.Year} Marflow Software";
        WebsiteLink.NavigateUri = new Uri(websiteUrl);
        EmailLink.Content = email;
    }

    private async void EmailLink_Click(object sender, RoutedEventArgs e)
    {
        var package = new DataPackage();
        package.SetText(_email);
        Clipboard.SetContent(package);
        CopiedHint.Visibility = Visibility.Visible;
        try { await Windows.System.Launcher.LaunchUriAsync(new Uri($"mailto:{_email}")); } catch { }
    }
}
