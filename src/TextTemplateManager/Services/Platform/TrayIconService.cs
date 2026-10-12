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

        public IRelayCommand OpenCommand { get; }
        public IRelayCommand ExitCommand { get; }


        /// <param name="showMainWindow">Brings the main window back; the tray's Open and double-click.</param>
        public TrayIconService(Action showMainWindow)
        {
            _showMainWindow = showMainWindow;
            _dispatcher = DispatcherQueue.GetForCurrentThread();

            OpenCommand = new RelayCommand(OpenWindow);
            ExitCommand = new RelayCommand(ExitApp);



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
