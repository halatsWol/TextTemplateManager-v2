using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Linq;
using TextTemplateManager.Data;

namespace TextTemplateManager.Controls;

public enum SettingsPage { General, Integrations, Sync, Backups }

/// <summary>The in-window Settings: a page list on the left and the selected page on the right.</summary>
public sealed partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();

    /// <summary>The store the pages edit. Set once by the page hosting this view.</summary>
    public DataNode? Store { get; set; }

    public bool IsOpen => Visibility == Visibility.Visible;

    /// <summary>Where the selected page shows (the guided tour highlights it).</summary>
    public FrameworkElement PageArea => PageFrame;

    /// <summary>Settings were left (back arrow, or <see cref="Close"/>).</summary>
    public event EventHandler? Closed;

    private object? _parameter;

    /// <summary>Shows <paramref name="page"/>; <paramref name="parameter"/> goes to the page (see
    /// <see cref="BackupsSettingsPage.Request"/>).</summary>
    public void Open(SettingsPage page = SettingsPage.General, object? parameter = null)
    {
        _parameter = parameter;
        Visibility = Visibility.Visible;
        var item = Nav.MenuItems.OfType<NavigationViewItem>().First(i => (string)i.Tag == page.ToString());
        if (ReferenceEquals(Nav.SelectedItem, item))
            Navigate(page);                 // already selected -> navigate manually
        else
            Nav.SelectedItem = item;        // fires SelectionChanged -> navigate
    }

    public void Close()
    {
        Visibility = Visibility.Collapsed;
        Closed?.Invoke(this, EventArgs.Empty);
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem { Tag: string tag } && Enum.TryParse(tag, out SettingsPage page))
            Navigate(page);
    }

    private void Nav_BackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args) => Close();

    private void Navigate(SettingsPage page)
    {
        if (Store is not { } store) return;
        var parameter = _parameter;   // for this navigation only, not when the page is picked again later
        _parameter = null;
        switch (page)
        {
            case SettingsPage.General:
                PageFrame.Navigate(typeof(GeneralSettingsPage), store.CurrentSettings);
                break;
            case SettingsPage.Integrations:
                PageFrame.Navigate(typeof(IntegrationsSettingsPage), store.CurrentSettings);
                break;
            case SettingsPage.Sync:
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
                PageFrame.Navigate(typeof(SyncSettingsPage), new SyncSettingsPage.Parameters(store, hwnd));
                break;
            case SettingsPage.Backups:
                PageFrame.Navigate(typeof(BackupsSettingsPage),
                    new BackupsSettingsPage.Parameters(new BackupService(store.Storage), parameter as BackupsSettingsPage.Request));
                break;
        }
    }
}
