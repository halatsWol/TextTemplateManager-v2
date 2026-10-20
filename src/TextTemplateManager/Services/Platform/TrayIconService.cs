using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System;

namespace TextTemplateManager.Services.Platform
{
    public sealed class TrayIconService : IDisposable
    {
        private readonly TaskbarIcon _trayIcon;
        private readonly DispatcherQueue _dispatcher;
        private readonly Action _showMainWindow;

        private readonly MenuFlyoutItem _updateItem;

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


            var menu = new MenuFlyout();

            menu.Items.Add(new MenuFlyoutItem
            {
                Text = "Open",
                Command = OpenCommand
            });

            _updateItem = new MenuFlyoutItem
            {
                Text = "Restart to update",
                Command = UpdateCommand,
                Visibility = Visibility.Collapsed,
            };
            menu.Items.Add(_updateItem);

            menu.Items.Add(new MenuFlyoutItem
            {
                Text = "Exit App",
                Command = ExitCommand
            });

            _trayIcon.ContextFlyout = menu;
            _trayIcon.ForceCreate();
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
            _dispatcher.TryEnqueue(() => _updateItem.Visibility = ready ? Visibility.Visible : Visibility.Collapsed);

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
