using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.ComponentModel;
using TextTemplateManager.Models;

namespace TextTemplateManager.Controls;

/// <summary>The button beside a search box with the search options (search the template text too).
/// Reads and writes the saved settings, so every search box shows the same state.</summary>
public sealed partial class SearchOptionsButton : UserControl
{
    private AppSettings? _settings;

    public SearchOptionsButton()
    {
        InitializeComponent();
        Menu.Opening += (_, _) => MenuOpening?.Invoke(this, EventArgs.Empty);
        Menu.Closed += (_, _) => MenuClosed?.Invoke(this, EventArgs.Empty);
        // The checked state, not Click: UI Automation (screen readers) toggles without a click.
        ContentItem.RegisterPropertyChangedCallback(ToggleMenuFlyoutItem.IsCheckedProperty, (_, _) =>
        {
            if (_settings != null) _settings.SearchInContent = ContentItem.IsChecked;
        });
    }

    public event EventHandler? MenuOpening;
    public event EventHandler? MenuClosed;

    /// <summary>Lets the button take keyboard focus (off where focus elsewhere has a meaning).</summary>
    public bool TakesFocus
    {
        get => OptionsButton.IsTabStop;
        set { OptionsButton.IsTabStop = value; OptionsButton.AllowFocusOnInteraction = value; }
    }

    public AppSettings? Settings
    {
        get => _settings;
        set
        {
            if (_settings != null) _settings.PropertyChanged -= Settings_PropertyChanged;
            _settings = value;
            if (_settings != null) _settings.PropertyChanged += Settings_PropertyChanged;
            Show();
        }
    }

    private void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppSettings.SearchInContent)) Show();
    }

    private void Show()
    {
        bool on = _settings?.SearchInContent == true;
        ContentItem.IsChecked = on;
        ActiveDot.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        ToolTipService.SetToolTip(OptionsButton, on ? "Search options (searching template text too)" : "Search options");
    }
}
