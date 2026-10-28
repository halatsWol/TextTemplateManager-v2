using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        InitLanguageUi();
        InitStartupUi();
        InitUpdatesUi();
        base.OnNavigatedTo(e);
    }

    // ---- Language ----
    public sealed record LanguageChoice(string Code, string Name);

    private bool _languageLoading;

    private void InitLanguageUi()
    {
        _languageLoading = true;
        var choices = new List<LanguageChoice> { new("", Loc.Get("General_LanguageWindows")) };
        choices.AddRange(UiLanguages.Available.Select(code => new LanguageChoice(code, UiLanguages.NameOf(code))));
#if DEBUG
        choices.Add(new(UiLanguages.Pseudo, UiLanguages.NameOf(UiLanguages.Pseudo)));
#endif
        LanguageCombo.ItemsSource = choices;
        string chosen = UiLanguages.Chosen;
        LanguageCombo.SelectedItem = choices.FirstOrDefault(c => string.Equals(c.Code, chosen, StringComparison.OrdinalIgnoreCase)) ?? choices[0];
        ShowLanguageRestart();
        _languageLoading = false;
    }

    private void Language_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_languageLoading || LanguageCombo.SelectedItem is not LanguageChoice choice) return;
        try { UiLanguages.Choose(choice.Code); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"[Language] choice not saved: {ex.Message}");
        }
        ShowLanguageRestart();
    }

    // The new language shows from the next start: until then the bar offers the restart.
    private void ShowLanguageRestart() =>
        LanguageRestartInfo.IsOpen = !string.Equals(UiLanguages.Chosen, UiLanguages.AtStart, StringComparison.OrdinalIgnoreCase);

    private async void LanguageRestart_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current is App app) await app.RestartAsync();
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
        UpdateVersionText.Text = Loc.Format("General_Version", Common.AppInfo.Version);
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
            UpdateStage.Unavailable => Loc.Get("General_UpdateUnavailable"),
            UpdateStage.DisabledByPolicy => Loc.Get("General_UpdateDisabledByPolicy"),
            UpdateStage.Checking => Loc.Get("General_UpdateChecking"),
            UpdateStage.Available when s.CanDownload => Loc.Format("General_UpdateAvailable", v, s.Message ?? Loc.Get("General_UpdateChooseDownload")),
            UpdateStage.Available => Loc.Format("General_UpdateAvailableManaged", v),
            UpdateStage.Downloading => Loc.Format("General_UpdateDownloading", (int)(s.Progress * 100)),
            UpdateStage.Ready => Loc.Format("General_UpdateReady", v),
            UpdateStage.Installing => Loc.Get("General_UpdateInstalling"),
            UpdateStage.Failed => s.Message ?? Loc.Get("General_UpdateFailed"),
            UpdateStage.ManualStepsNeeded => Loc.Format("General_UpdateManualSteps", v),
            _ => s.Message ?? (s.LastChecked is { } at ? Loc.Format("General_UpToDateCheckedAt", at) : Loc.Get("General_UpToDate")),
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
            UpdateAction.Install => Loc.Get("General_RestartToUpdateButton"),
            UpdateAction.Download => Loc.Get("General_DownloadButton"),
            UpdateAction.Retry => Loc.Get("General_TryAgainButton"),
            _ => Loc.Get("General_CheckUpdatesButton"),
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
        AutomaticUpdatesNote.Text = disabledByPolicy || available && policy.AutomaticUpdatesForced != null ? Loc.Get("General_SetByOrganization") : "";
        AutomaticUpdatesNote.Visibility = AutomaticUpdatesNote.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        IdleInstallToggle.IsOn = Updates?.IdleInstallAllowed(ViewModel.IdleInstall) ?? ViewModel.IdleInstall;
        IdleInstallToggle.IsEnabled = available && policy.Mode == InAppUpdatesMode.Full && policy.IdleInstallForced == null && automatic;
        IdleInstallNote.Text = disabledByPolicy || available && (policy.Mode != InAppUpdatesMode.Full || policy.IdleInstallForced != null)
            ? Loc.Get("General_SetByOrganization") : "";
        IdleInstallNote.Visibility = IdleInstallNote.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        StorePolicyNote.Text = policy.UpdatesDespiteStorePolicy
            ? Loc.Get("General_StorePolicyAppAsks") : Loc.Get("General_StorePolicyWindowsUpdate");
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
    private static string FormatHotkey(string? hotkey) => HotkeyText.For(hotkey);

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
            AutostartState.NotAvailable => Loc.Get("General_AutostartNotAvailable"),
            AutostartState.OffInWindows => Loc.Get("General_AutostartOffInWindows"),
            AutostartState.OnByPolicy or AutostartState.OffByPolicy => Loc.Get("General_SetByOrganization"),
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
            HotkeyTextBox.Text = HotkeyText.For(string.Join("+", mods));   // "Shift", "Shift+Alt", …
            _hotkeyCommitted = false;
            return;
        }

        if (mods.Count == 0) return;   // a bare key is not a valid global shortcut — ignore

        mods.Add(e.Key.ToString());
        string hotkey = string.Join("+", mods);
        HotkeyTextBox.Text = HotkeyText.For(hotkey);
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