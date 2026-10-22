using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using TextTemplateManager.Helpers;
using TextTemplateManager.Models;
using TextTemplateManager.Services.Platform;
using TextTemplateManager.Services.Updates;
using Windows.System;
using Windows.UI.Core;

namespace TextTemplateManager;

public sealed partial class GeneralSettingsPage : Page
{
    public AppSettings ViewModel { get; set; } = null!;   // set in OnNavigatedTo

    public GeneralSettingsPage()
    {
        this.InitializeComponent();
        PasteModeCombo.ItemsSource = PasteModeLabel.DisplayOrder;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (e.Parameter is AppSettings settings)
        {
            ViewModel = settings;
        }
        HotkeyTextBox.Text = FormatHotkey(ViewModel?.PasteWindowHotkey);
        HotkeyUnavailableInfo.IsOpen = Application.Current is App { HotkeyRegistered: false };
        InitStartupUi();
        InitUpdatesUi();
        base.OnNavigatedTo(e);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        ApplyHotkey();   // safety: re-register the hotkey if the field never lost focus
        if (Updates is { } updates) updates.Changed -= OnUpdateChanged;
        base.OnNavigatedFrom(e);
    }

    // ---- Updates ----
    private static UpdateService? Updates => (Application.Current as App)?.Updates;
    private bool _updatesLoading;

    private void InitUpdatesUi()
    {
        UpdateVersionText.Text = $"Version {Common.AppInfo.Version}";
        if (Updates is not { } updates) { ShowUpdateStatus(new UpdateStatus(UpdateStage.Unavailable)); return; }
        updates.Changed += OnUpdateChanged;
        ShowUpdateStatus(updates.Status);
    }

    private void OnUpdateChanged(UpdateStatus status) => DispatcherQueue.TryEnqueue(() => ShowUpdateStatus(status));

    private void ShowUpdateStatus(UpdateStatus s)
    {
        string v = MainPage.VersionName(s.Version);
        UpdateStatusText.Text = s.Stage switch
        {
            UpdateStage.Unavailable => "This copy wasn't installed from the Microsoft Store, so it can't update itself.",
            UpdateStage.DisabledByPolicy => "Your organization manages updates for this app.",
            UpdateStage.Checking => "Checking for updates…",
            UpdateStage.Available when s.CanDownload => $"{v} is available. " + (s.Message ?? "Choose Download to get it."),
            UpdateStage.Available => $"{v} is available. Your organization manages updates on this PC.",
            UpdateStage.Downloading => $"Downloading the update… {(int)(s.Progress * 100)} %",
            UpdateStage.Ready => $"{v} is ready to install. Restart to update saves your work and opens the new version within a few seconds.",
            UpdateStage.Installing => "Installing the update…",
            UpdateStage.Failed => s.Message ?? "The last update step didn't work.",
            UpdateStage.ManualStepsNeeded => $"{v} couldn't be installed automatically. To finish, quit the app and start it again from the Start menu.",
            _ => s.Message ?? "Up to date" + (s.LastChecked is { } at ? $" (last checked at {at:t})." : "."),
        };
        // The same action as the notice in the main window, so nobody has to leave Settings for it.
        _updateAction = s.Stage switch
        {
            UpdateStage.Ready => UpdateAction.Install,
            UpdateStage.Available when s.CanDownload => UpdateAction.Download,
            UpdateStage.Failed => UpdateAction.Retry,
            _ => UpdateAction.Check,
        };
        CheckUpdatesButton.Content = _updateAction switch
        {
            UpdateAction.Install => "Restart to update",
            UpdateAction.Download => "Download",
            UpdateAction.Retry => "Try again",
            _ => "Check for updates",
        };
        CheckUpdatesButton.IsEnabled = s.Stage is UpdateStage.UpToDate or UpdateStage.Available or UpdateStage.Failed
            or UpdateStage.Ready or UpdateStage.ManualStepsNeeded;

        _updatesLoading = true;
        var policy = Updates?.Policy ?? UpdatePolicy.Default;
        bool available = s.Stage is not (UpdateStage.Unavailable or UpdateStage.DisabledByPolicy);
        // InAppUpdates = 0 (DisabledByPolicy) locks everything; the AutomaticUpdates and IdleInstall values lock their switch.
        bool disabledByPolicy = s.Stage == UpdateStage.DisabledByPolicy;
        bool automatic = policy.AutomaticUpdatesForced ?? !ViewModel.NoAutomaticUpdates;
        AutomaticUpdatesToggle.IsOn = automatic;
        AutomaticUpdatesToggle.IsEnabled = available && policy.AutomaticUpdatesForced == null;
        AutomaticUpdatesNote.Text = disabledByPolicy || available && policy.AutomaticUpdatesForced != null ? "Set by your organization." : "";
        AutomaticUpdatesNote.Visibility = AutomaticUpdatesNote.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        IdleInstallToggle.IsOn = Updates?.IdleInstallAllowed(ViewModel.IdleInstall) ?? ViewModel.IdleInstall;
        IdleInstallToggle.IsEnabled = available && policy.Mode == InAppUpdatesMode.Full && policy.IdleInstallForced == null && automatic;
        IdleInstallNote.Text = disabledByPolicy || available && (policy.Mode != InAppUpdatesMode.Full || policy.IdleInstallForced != null)
            ? "Set by your organization." : "";
        IdleInstallNote.Visibility = IdleInstallNote.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        StorePolicyNote.Text = "Automatic updates for Store apps are turned off on this PC. " + (policy.UpdatesDespiteStorePolicy
            ? "TextTemplateManager still asks the Microsoft Store for its own updates: on its own with this switch on, otherwise with Check for updates."
            : "An update that Windows Update brings is offered with Restart to update.");
        StorePolicyNote.Visibility = available && policy.Mode == InAppUpdatesMode.Full && policy.StoreBlocksAutomaticUpdates
            ? Visibility.Visible : Visibility.Collapsed;
        _updatesLoading = false;
    }

    private enum UpdateAction { Check, Download, Retry, Install }
    private UpdateAction _updateAction;

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current is not App app) return;
        switch (_updateAction)
        {
            case UpdateAction.Install: await app.InstallUpdateAsync(idle: false); break;
            case UpdateAction.Download: await app.DownloadUpdateAsync(); break;
            default: await app.CheckForUpdatesAsync(); break;
        }
    }

    private void AutomaticUpdates_Toggled(object sender, RoutedEventArgs e)
    {
        if (_updatesLoading) return;
        ViewModel.NoAutomaticUpdates = !AutomaticUpdatesToggle.IsOn;
        if (Updates is { } updates)
        {
            updates.CheckAutomatically = AutomaticUpdatesToggle.IsOn;
            ShowUpdateStatus(updates.Status);   // idle install follows
        }
    }

    private void IdleInstall_Toggled(object sender, RoutedEventArgs e)
    {
        if (_updatesLoading) return;
        ViewModel.IdleInstall = IdleInstallToggle.IsOn;
    }

    // "None"/empty means no hotkey — show a blank field (the placeholder shows through).
    private static string FormatHotkey(string? hotkey)
        => string.IsNullOrEmpty(hotkey) || hotkey == "None" ? "" : hotkey;

    // ---- Start with Windows (the package's startup task; Windows owns the on/off state) ----
    private bool _startupLoading;

    private static IAppEnvironment? HostEnvironment => (Application.Current as App)?.HostEnvironment;

    private async void InitStartupUi()
    {
        _startupLoading = true;
        StartHiddenToggle.IsOn = ViewModel.StartHidden;
        try { ShowAutostart(HostEnvironment is { } env ? await env.GetAutostartAsync() : AutostartState.NotAvailable); }
        finally { _startupLoading = false; }
    }

    private void ShowAutostart(AutostartState state)
    {
        StartupToggle.IsOn = state is AutostartState.On or AutostartState.OnByPolicy;
        StartupToggle.IsEnabled = state is AutostartState.On or AutostartState.Off;
        StartupNote.Text = state switch
        {
            AutostartState.NotAvailable => "Only the installed app can start with Windows.",
            AutostartState.OffInWindows => "Switched off in Windows Settings › Apps › Startup; switch it on there.",
            AutostartState.OnByPolicy or AutostartState.OffByPolicy => "Set by your organization.",
            _ => "",
        };
        StartupNote.Visibility = StartupNote.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void RunAtStartup_Toggled(object sender, RoutedEventArgs e)
    {
        if (_startupLoading || HostEnvironment is not { } env) return;
        _startupLoading = true;
        try { ShowAutostart(await env.SetAutostartAsync(StartupToggle.IsOn)); }
        finally { _startupLoading = false; }
    }

    private void StartHidden_Toggled(object sender, RoutedEventArgs e)
    {
        if (_startupLoading) return;
        ViewModel.StartHidden = StartHiddenToggle.IsOn;
    }

    private void HideCrossAreaWarnings_Toggled(object sender, RoutedEventArgs e)
    {
        if (ViewModel == null || sender is not ToggleSwitch ts) return;
        ViewModel.HideCrossAreaShortcutWarnings = ts.IsOn;
        // The conflict panel re-evaluates when Settings closes (MainPage.Settings_Closed).
    }

    // True once a full modifier(s)+key combo is committed; reset when a new bare modifier starts one.
    private bool _hotkeyCommitted = true;

    private void HotkeyTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        e.Handled = true;

        var mods = HeldModifiers();

        if (IsModifierKey(e.Key))
        {
            // Live feedback: show the modifiers held so far (not yet a valid shortcut).
            HotkeyTextBox.Text = string.Join("+", mods);   // "Shift", "Shift+Alt", …
            _hotkeyCommitted = false;
            return;
        }

        if (mods.Count == 0) return;   // a bare key is not a valid global shortcut — ignore

        mods.Add(e.Key.ToString());
        string hotkey = string.Join("+", mods);
        HotkeyTextBox.Text = hotkey;
        CommitHotkey(hotkey);
        _hotkeyCommitted = true;
    }

    private void HotkeyTextBox_KeyUp(object sender, KeyRoutedEventArgs e)
    {
        e.Handled = true;
        // Every key released without completing a valid shortcut → clear the field (and the hotkey).
        if (!_hotkeyCommitted && HeldModifiers().Count == 0)
        {
            HotkeyTextBox.Text = "";
            CommitHotkey("None");
        }
    }

    // Modifiers currently held, in a stable display order (Shift+Alt matches the default).
    private System.Collections.Generic.List<string> HeldModifiers()
    {
        var mods = new System.Collections.Generic.List<string>();
        if (IsKeyDown(VirtualKey.Control)) mods.Add("Ctrl");
        if (IsKeyDown(VirtualKey.Shift)) mods.Add("Shift");
        if (IsKeyDown(VirtualKey.Menu)) mods.Add("Alt");   // Alt = 'Menu' in Win32/WinUI
        return mods;
    }

    // Only sets the pending value; OS registration waits for focus loss, so the combo you are
    // pressing can't fire itself (opening Quick Paste and swallowing the final key).
    private void CommitHotkey(string hotkey) => ViewModel.PasteWindowHotkey = hotkey;

    private void HotkeyTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        // Suspend the global hotkey while capturing.
        if (Application.Current is App app) app.UpdateGlobalHotkey("None");
    }

    private void HotkeyTextBox_LostFocus(object sender, RoutedEventArgs e) => ApplyHotkey();

    private void ApplyHotkey()
    {
        if (ViewModel == null) return;
        if (Application.Current is App app)
            HotkeyUnavailableInfo.IsOpen = !app.UpdateGlobalHotkey(ViewModel.PasteWindowHotkey);
    }

    private bool IsModifierKey(VirtualKey key)
    {
        return key == VirtualKey.Control || key == VirtualKey.Menu ||
               key == VirtualKey.Shift || key == VirtualKey.LeftWindows ||
               key == VirtualKey.RightWindows;
    }

    private bool IsKeyDown(VirtualKey key)
    {
        var state = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key);
        return (state & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;
    }

    private void ClearHotkey_Click(object sender, RoutedEventArgs e)
    {
        HotkeyTextBox.Text = "";
        ViewModel.PasteWindowHotkey = "None";
        ApplyHotkey();
    }
}