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

    // Search for the query as typed (whole words, * and . as wildcards) instead of the fuzzy search; both windows.
    [ObservableProperty] public partial bool StrictSearch { get; set; } = false;

    // When Windows starts the app at sign-in, stay in the tray instead of opening the window.
    [ObservableProperty] public partial bool StartHidden { get; set; } = true;

    // The one-time offer to remove a still installed TextTemplateManager 1 was made.
    [ObservableProperty] public partial bool LegacyRemovalOffered { get; set; } = false;

    // Edits to the selected item are saved as they are made. Off: they wait for Save (or the prompt on leaving the item).
    [ObservableProperty] public partial bool Autosave { get; set; } = false;

    // The guided tour was started, its offer answered with "Don't show again", or the first start restored a backup: the
    // tour isn't offered any more (see StartupGreeting).
    [ObservableProperty] public partial bool TutorialOfferDone { get; set; } = false;

    // The version that last ran (and showed its What's new): a newer one shows its What's new once.
    [ObservableProperty] public partial string LastRunVersion { get; set; } = "";

    // A downloaded update is installed while the app sits unused (window and Quick Paste closed, nothing unsaved, no input
    // for a while). Off: it waits for Restart to update. An InAppUpdates/IdleInstall policy overrides it. Written only when
    // on, so settings files without it keep their format.
    [ObservableProperty]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public partial bool IdleInstall { get; set; } = false;

    // Settings ▸ General ▸ Updates, "Check for and download updates automatically" switched off (stored inverted, so the
    // default isn't written).
    [ObservableProperty]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public partial bool NoAutomaticUpdates { get; set; } = false;
}
