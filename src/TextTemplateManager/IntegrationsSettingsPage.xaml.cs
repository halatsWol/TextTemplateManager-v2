using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using TextTemplateManager.Common;
using TextTemplateManager.Models;

namespace TextTemplateManager;

public sealed partial class IntegrationsSettingsPage : Page
{
    public AppSettings ViewModel { get; set; } = null!;   // set in OnNavigatedTo

    public IntegrationsSettingsPage()
    {
        this.InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (e.Parameter is AppSettings settings)
        {
            ViewModel = settings;
        }
        RefreshConnectorUi();
        ApiDocLink.NavigateUri = new Uri(ApiDocUrl());   // point at the doc for the installed version
        base.OnNavigatedTo(e);
    }

    // Links to the API doc at the tag matching the installed version (v<version>), so it always
    // matches the connector the user is running; dev/local builds fall back to the main branch.
    private static string ApiDocUrl()
    {
        const string blob = "https://github.com/halatsWol/TextTemplateManager/blob";
        const string path = "docs/BrowserConnectorApi.md";
        string v = AppInfo.Version;
        string reference = string.IsNullOrEmpty(v) || v.StartsWith("0.0.0") || v.Contains("dev", StringComparison.OrdinalIgnoreCase)
            ? "main" : "v" + v;
        return $"{blob}/{reference}/{path}";
    }


    // ---- Browser connector ----
    private bool _connectorLoading;

    private void RefreshConnectorUi()
    {
        if (ViewModel == null) return;
        _connectorLoading = true;
        ConnectorToggle.IsOn = ViewModel.BrowserConnectorEnabled;
        ConnectorPortBox.Text = ViewModel.BrowserConnectorPort.ToString();
        ConnectorTokenBox.Text = ViewModel.BrowserConnectorToken;
        ConnectorDetails.Visibility = ViewModel.BrowserConnectorEnabled ? Visibility.Visible : Visibility.Collapsed;
        _connectorLoading = false;
    }

    private void Connector_Toggled(object sender, RoutedEventArgs e)
    {
        if (_connectorLoading || ViewModel == null) return;
        ViewModel.BrowserConnectorEnabled = ConnectorToggle.IsOn;
        (Application.Current as App)?.ApplyBrowserConnectorSettings();   // start/stop; generates the token on first enable
        RefreshConnectorUi();                                           // reflect the generated token + visibility
    }

    private void ConnectorPort_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_connectorLoading || ViewModel == null) return;
        if (int.TryParse(ConnectorPortBox.Text, out int port) && port is > 1024 and < 65536)
        {
            if (port != ViewModel.BrowserConnectorPort)
            {
                ViewModel.BrowserConnectorPort = port;
                if (ViewModel.BrowserConnectorEnabled) (Application.Current as App)?.ApplyBrowserConnectorSettings();
            }
        }
        else ConnectorPortBox.Text = ViewModel.BrowserConnectorPort.ToString();   // revert an invalid entry
    }

    private void CopyToken_Click(object sender, RoutedEventArgs e)
    {
        var dp = new global::Windows.ApplicationModel.DataTransfer.DataPackage();
        dp.SetText(ViewModel?.BrowserConnectorToken ?? "");
        global::Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
    }

    private void RegenerateToken_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;
        ViewModel.BrowserConnectorToken = Guid.NewGuid().ToString("N");
        if (ViewModel.BrowserConnectorEnabled) (Application.Current as App)?.ApplyBrowserConnectorSettings();
        RefreshConnectorUi();
    }
}
