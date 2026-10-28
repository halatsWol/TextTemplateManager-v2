using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TextTemplateManager.Common;
using TextTemplateManager.Helpers;
using Windows.ApplicationModel.DataTransfer;

namespace TextTemplateManager.Controls;

/// <summary>Shown after an e-mail button: a mailto: link does nothing when Windows has no app for e-mail links, so the
/// address goes to the clipboard, and the hint points to Windows' default apps and the website's step-by-step help.</summary>
public sealed partial class MailFallbackHint : UserControl
{
    public MailFallbackHint() => InitializeComponent();

    /// <summary>Copies the support address and shows the hint; call it right before opening the mailto: link.</summary>
    public void Show()
    {
        var package = new DataPackage();
        package.SetText(SupportLinks.Email);
        Clipboard.SetContent(package);
        HintText.Text = Loc.Format("Support_MailHint", SupportLinks.Email);
        Visibility = Visibility.Visible;
    }

    private void DefaultApps_Click(object sender, RoutedEventArgs e) => ExternalLinks.Open("ms-settings:defaultapps");

    private void Help_Click(object sender, RoutedEventArgs e) => ExternalLinks.Open(SupportLinks.MailSetupUrl);
}
