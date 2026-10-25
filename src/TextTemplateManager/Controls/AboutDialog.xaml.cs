using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using TextTemplateManager.Common;
using TextTemplateManager.Helpers;
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
        VersionText.Text = Loc.Format("General_Version", AppInfo.Version);
        ReleaseText.Text = AppInfo.ReleaseDate.Length > 0 ? Loc.Format("About_Released", AppInfo.ReleaseDate) : Loc.Get("About_DevBuild");
        ProtocolText.Text = Loc.Format("About_Protocol", BrowserConnector.ProtocolVersion);
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
