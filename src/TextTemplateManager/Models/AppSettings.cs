using CommunityToolkit.Mvvm.ComponentModel;
using TextTemplateManager.Common;

namespace TextTemplateManager.Models;

public partial class AppSettings : ObservableObject
{
    [ObservableProperty] public partial PasteMode DefaultPasteMode { get; set; } = PasteMode.Auto;
    [ObservableProperty] public partial string PasteWindowHotkey { get; set; } = "Shift+Alt+Y";

    // When on, hides the dismissible cross-area shortcut warnings (the same shortcut used in local +
    // sync, or across sync folders). Same-area duplicate conflicts still block. Off by default.
    [ObservableProperty] public partial bool HideCrossAreaShortcutWarnings { get; set; } = false;

    // Local loopback connector for browser extensions. Off by default (opens a 127.0.0.1 port);
    // the token is generated when first enabled and required on every request.
    [ObservableProperty] public partial bool BrowserConnectorEnabled { get; set; } = false;
    [ObservableProperty] public partial int BrowserConnectorPort { get; set; } = 47615;
    [ObservableProperty] public partial string BrowserConnectorToken { get; set; } = "";

    // Search also looks in the template text (both windows), not only titles and tags.
    [ObservableProperty] public partial bool SearchInContent { get; set; } = false;

    // When Windows starts the app at sign-in, stay in the tray instead of opening the window.
    [ObservableProperty] public partial bool StartHidden { get; set; } = true;

    // The one-time offer to remove a still installed TextTemplateManager 1 was made.
    [ObservableProperty] public partial bool LegacyRemovalOffered { get; set; } = false;
}
