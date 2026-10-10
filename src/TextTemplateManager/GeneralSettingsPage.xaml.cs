using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using TextTemplateManager.Helpers;
using TextTemplateManager.Models;
using TextTemplateManager.Services.Platform;
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
        base.OnNavigatedTo(e);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        ApplyHotkey();   // safety: re-register the hotkey if the field never lost focus
        base.OnNavigatedFrom(e);
    }

    // "None"/empty means no hotkey — show a blank field (the placeholder shows through).
    private static string FormatHotkey(string? hotkey)
        => string.IsNullOrEmpty(hotkey) || hotkey == "None" ? "" : hotkey;

    // ---- Autostart (registry-only; see StartupManager) ----
    private bool _startupLoading;

    private void InitStartupUi()
    {
        _startupLoading = true;
        StartupToggle.IsOn = StartupManager.IsEnabled();
        StartHiddenToggle.IsOn = StartupManager.IsHiddenEnabled();
        StartHiddenToggle.IsEnabled = StartupToggle.IsOn;   // "start hidden" is meaningless without autostart
        _startupLoading = false;
    }

    private void RunAtStartup_Toggled(object sender, RoutedEventArgs e)
    {
        if (_startupLoading) return;
        bool on = StartupToggle.IsOn;
        StartupManager.SetEnabled(on, StartHiddenToggle.IsOn);
        StartHiddenToggle.IsEnabled = on;
    }

    private void StartHidden_Toggled(object sender, RoutedEventArgs e)
    {
        // Only reachable while autostart is on — rewrite the Run entry with/without the hidden flag.
        if (_startupLoading) return;
        StartupManager.SetEnabled(true, StartHiddenToggle.IsOn);
    }

    private void HideCrossAreaWarnings_Toggled(object sender, RoutedEventArgs e)
    {
        if (ViewModel == null || sender is not ToggleSwitch ts) return;
        ViewModel.HideCrossAreaShortcutWarnings = ts.IsOn;
        _ = TextTemplateManager.Data.StorageService.SaveSettingsAsync(ViewModel);
        // The conflict panel re-evaluates when Settings closes (MainPage.CloseSettings).
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
        _ = TextTemplateManager.Data.StorageService.SaveSettingsAsync(ViewModel);
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