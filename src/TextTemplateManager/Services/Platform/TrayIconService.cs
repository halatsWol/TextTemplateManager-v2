using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using TextTemplateManager.Helpers;

namespace TextTemplateManager.Services.Platform
{
    public sealed class TrayIconService : IDisposable
    {
        private readonly TaskbarIcon _trayIcon;
        private readonly DispatcherQueue _dispatcher;
        private readonly Action _showMainWindow;

        private bool _updateReady;

        public IRelayCommand OpenCommand { get; }
        public IRelayCommand ExitCommand { get; }
        public IRelayCommand UpdateCommand { get; }


        /// <param name="showMainWindow">Brings the main window back; the tray's Open and double-click.</param>
        /// <param name="installUpdate">Restart to update, shown in the menu while an update is ready.</param>
        public TrayIconService(Action showMainWindow, Func<System.Threading.Tasks.Task> installUpdate)
        {
            _showMainWindow = showMainWindow;
            _dispatcher = DispatcherQueue.GetForCurrentThread();

            OpenCommand = new RelayCommand(OpenWindow);
            ExitCommand = new RelayCommand(ExitApp);
            UpdateCommand = new RelayCommand(() => _dispatcher.TryEnqueue(async () => await installUpdate()));



            _trayIcon = new TaskbarIcon
            {
                IconSource = new BitmapImage(
                    new Uri("ms-appx:///Assets/AppIcon.ico")
                ),
                ToolTipText = "TextTemplateManager",
                DoubleClickCommand = OpenCommand,   // double-click the tray icon to open the window
            };


            _trayIcon.ContextFlyout = BuildMenu();
            _trayIcon.ForceCreate();
        }

        // The tray shows the flyout as a native popup menu, which ignores an item's Visibility (2.3.3 showed Restart
        // to update with nothing ready), so the menu is rebuilt with or without the item.
        private MenuFlyout BuildMenu()
        {
            var menu = new MenuFlyout();
            menu.Items.Add(new MenuFlyoutItem { Text = Loc.Get("Tray_OpenItem"), Command = OpenCommand });
            if (_updateReady) menu.Items.Add(new MenuFlyoutItem { Text = Loc.Get("General_RestartToUpdateButton"), Command = UpdateCommand });
            menu.Items.Add(new MenuFlyoutItem { Text = Loc.Get("Tray_ExitItem"), Command = ExitCommand });
            return menu;
        }

        private void OpenWindow() => _dispatcher.TryEnqueue(() => _showMainWindow());

        private void ExitApp()
        {
            _dispatcher.TryEnqueue(async () =>
            {
                if (Application.Current is TextTemplateManager.App app) await app.ExitAsync();
            });
        }

        /// <summary>Shows Restart to update in the menu while an update is downloaded and ready.</summary>
        public void SetUpdateReady(bool ready) =>
            _dispatcher.TryEnqueue(() =>
            {
                if (_updateReady == ready) return;
                _updateReady = ready;
                _trayIcon.ContextFlyout = BuildMenu();
            });

        /// <summary>Balloon notice through Shell_NotifyIcon; needs no notification registration.</summary>
        public void ShowBalloon(string title, string message)
        {
            _dispatcher.TryEnqueue(() =>
            {
                try { _trayIcon.ShowNotification(title, message, H.NotifyIcon.Core.NotificationIcon.Info); }
                catch { /* notifications are never worth breaking the app over */ }
            });
        }

        public void Dispose()
        {
            _trayIcon?.Dispose();
        }
    }
}
