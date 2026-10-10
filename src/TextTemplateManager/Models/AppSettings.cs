using CommunityToolkit.Mvvm.ComponentModel;
using TextTemplateManager.Common;

namespace TextTemplateManager.Models;

public partial class AppSettings : ObservableObject
{
    [ObservableProperty] private PasteMode _defaultPasteMode = PasteMode.Auto;
    [ObservableProperty] private string _pasteWindowHotkey = "Shift+Alt+Y";

    // When on, hides the dismissible cross-area shortcut warnings (the same shortcut used in local +
    // sync, or across sync folders). Same-area duplicate conflicts still block. Off by default.
    [ObservableProperty] private bool _hideCrossAreaShortcutWarnings = false;

    // Local loopback connector for browser extensions. Off by default (opens a 127.0.0.1 port);
    // the token is generated when first enabled and required on every request.
    [ObservableProperty] private bool _browserConnectorEnabled = false;
    [ObservableProperty] private int _browserConnectorPort = 47615;
    [ObservableProperty] private string _browserConnectorToken = "";
}

