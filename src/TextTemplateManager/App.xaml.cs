using H.NotifyIcon;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TextTemplateManager.Common;
using TextTemplateManager.Data;
using TextTemplateManager.Helpers;
using TextTemplateManager.Services.Platform;
using WinRT.Interop;


namespace TextTemplateManager
{
    public partial class App : Application
    {
        public static Window MainWindow { get; private set; } = null!;
        private static bool _isClosingFromTray = false;

        // Keep references to prevent Garbage Collection
        private HotkeyListener _hotkeyListener = null!;   // set in OnLaunched
        private PasteWindow? _pasteWindow;
        private TrayIconService _trayService = null!;     // set in OnLaunched
        private DispatcherQueue _uiDispatcher = null!;    // set in OnLaunched
        private BrowserConnector? _connector;
        private AppConnectorData? _connectorData;
        private BackupScheduler? _backupScheduler;
        private readonly DataNode _store;
        private readonly IAppEnvironment _environment = AppEnvironment.Detect();   // packaged or a dev run

        public IAppEnvironment HostEnvironment => _environment;

        // Dev runs (and the UI test scripts) start on fresh data again and again: --offer-tour asks for the tour offer there.
        private static bool OfferTourInDevRun { get; } =
            Environment.GetCommandLineArgs().Contains("--offer-tour", StringComparer.OrdinalIgnoreCase);

        // Single-instance key (Windows App SDK). 1.x uses its own mutex and pipe, so neither version takes
        // over the other's launches.
        private const string InstanceKey = "main";

        private readonly UnexpectedErrorHandler _errors;

        public App()
        {
            InitializeComponent();
            _store = new DataNode(StorageService.CreateDefault(_environment));
            _errors = new UnexpectedErrorHandler(ex => LogCrash("UnhandledException", ex),
                PersistPendingAsync, AskQuitAfterErrorAsync, ExitAsync);
            UnhandledException += OnUnhandledException;
        }

        protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            // Single instance: a later launch (a .ttmdata opened from Explorer, the Start menu) hands its
            // activation to the running instance and exits rather than run twice.
            _uiDispatcher = DispatcherQueue.GetForCurrentThread();
            string? fileArg = _environment.OpenedFile;
            var instance = AppInstance.FindOrRegisterForKey(InstanceKey);
            if (!instance.IsCurrent)
            {
                // A redundant hidden autostart (no file) shouldn't surface the running window — just exit.
                if (fileArg != null || !_environment.IsAutostartLaunch)
                {
                    try { await instance.RedirectActivationToAsync(AppInstance.GetCurrent().GetActivatedEventArgs()); }
                    catch { /* the running instance may be shutting down — nothing we can do */ }
                }
                Environment.Exit(0);
                return;
            }
            instance.Activated += OnRedirected;

            // A running 1.x would keep the Quick Paste shortcut and the connector port. Dev runs leave it alone.
            if (_environment.IsPackaged)
                await LegacyApp.CloseRunningAsync(TimeSpan.FromSeconds(5));

            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

            // A restore cut off by a crash is undone before anything is loaded.
            try { new RestoreService(_store.Storage).RecoverInterrupted(); }
            catch (Exception ex) { LogCrash("RestoreRecovery", ex); }

            // The 1.x data (1.x is closed by now) gets its permanent backup once, before anything is imported from it.
            BackupResult? v1Backup = null;
            try { v1Backup = await Task.Run(Backups.EnsureV1BackupAsync); }
            catch (Exception ex) { LogCrash("V1Backup", ex); }

            bool firstStart = !System.IO.File.Exists(_store.Storage.GetDataPath());
            await _store.InitializeAsync();
            var unreadableFiles = _store.Storage.TakeUnreadableFiles();

            // 1.x found for the first time: its start-with-Windows choice carries over.
            if (v1Backup?.Outcome == BackupOutcome.Created)
            {
                try { await LegacyAutostart.CarryOverAsync(LegacyAutostart.Read(), _environment, _store.CurrentSettings); }
                catch (Exception ex) { LogCrash("AutostartCarryOver", ex); }
            }

            _backupScheduler = new BackupScheduler(Backups, _store.Storage);
            _backupScheduler.Start();

            _hotkeyListener = new HotkeyListener();
            _hotkeyListener.HotkeyPressed += OnGlobalHotkeyPressed;
            UpdateGlobalHotkey(_store.CurrentSettings.PasteWindowHotkey);

            MainWindow = new Window();
            MainWindow.Content = new MainPage(_store);
            MainWindow.Title = "TextTemplateManager 2";

            IntPtr hwnd = WindowNative.GetWindowHandle(MainWindow);
            var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            appWindow.SetIcon("Assets/AppIcon.ico");

            WindowHelper.SetWindowMinSize(hwnd, 720, 560);   // don't clip the panes
            // Windows' default size is too small for the settings pages (Sync rows, Backups).
            var work = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary).WorkArea;
            var bounds = WindowHelper.CenteredBounds(work.X, work.Y, work.Width, work.Height,
                WindowHelper.GetDpiForWindow(hwnd) / 96.0, 1200, 820);
            appWindow.MoveAndResize(new Windows.Graphics.RectInt32(bounds.X, bounds.Y, bounds.Width, bounds.Height));
            WindowHelper.OnSessionEnd(hwnd, OnQueryEndSession, OnEndSession);


            _trayService = new TrayIconService(ShowMainWindow);

            ApplyBrowserConnectorSettings();   // start the loopback connector if enabled



            // "X" hides instead of closing, after asking about unsaved edits (Cancel keeps the window open).
            appWindow.Closing += async (s, e) =>
            {
                if (!_isClosingFromTray)
                {
                    e.Cancel = true;
                    EndTutorial();
                    if (MainWindow.Content is MainPage page && !await page.ResolveUnsavedEditsAsync()) return;
                    MainWindow.Hide();
                    // Reopen in a clean state rather than wherever the window was left.
                    (MainWindow.Content as MainPage)?.ClearSearch();
                    (MainWindow.Content as MainPage)?.CloseSettingsIfOpen();
                }
            };

            // Started by Windows at sign-in with "start hidden" on: stay in the tray, never show the window.
            // A .ttmdata to open, or a data file that couldn't be read, overrides this.
            bool startHidden = _environment.IsAutostartLaunch && _store.CurrentSettings.StartHidden
                && fileArg == null && unreadableFiles.Count == 0;
            if (!startHidden)
                MainWindow.Activate();

            if (unreadableFiles.Count > 0)
                (MainWindow.Content as MainPage)?.ReportUnreadableFiles(unreadableFiles);

            var greeting = Tutorial.StartupGreeting.For(_store.CurrentSettings, AppInfo.Version);
            if (greeting == Tutorial.Greeting.TourOffer && !_environment.IsPackaged && !OfferTourInDevRun)
                greeting = Tutorial.Greeting.None;
            if (greeting != Tutorial.Greeting.WhatsNew) _store.CurrentSettings.LastRunVersion = AppInfo.Version;
            bool offerTour = greeting == Tutorial.Greeting.TourOffer && !startHidden;

            bool offeredFirstStart = false;
            if (firstStart && !startHidden)
            {
                var offer = FirstStartOffer.From(await Task.Run(Backups.List));
                offeredFirstStart = offer.HasChoice;
                if (offeredFirstStart) (MainWindow.Content as MainPage)?.OfferFirstStart(offer.V1, offer.Newest, offerTour);
            }

            // 1.x still installed: offered once, not on top of the first-start choice (an import restarts the app,
            // so it follows right after). Dev runs leave an installed 1.x alone.
            if (_environment.IsPackaged && !startHidden && !offeredFirstStart && !_store.CurrentSettings.LegacyRemovalOffered
                && LegacyApp.FindUninstaller() != null)
                (MainWindow.Content as MainPage)?.OfferLegacyRemoval();

            if (!HotkeyRegistered)
                ReportHotkeyUnavailable(startHidden);
            if (ConnectorUnavailable)
                ReportStartupProblem("Browser connector unavailable",
                    $"Port {_store.CurrentSettings.BrowserConnectorPort} can't be used, so the browser extension can't connect. " +
                    "Another app is using it (for example an older TextTemplateManager that is still running) or Windows has reserved it. " +
                    "Choose a different port in Settings › Integrations and set the same port in the extension.", startHidden);

            // A .ttmdata passed to this (first) launch: add it as a sync source once the UI is up.
            if (fileArg != null)
                (MainWindow.Content as MainPage)?.HandleOpenTtmDataFile(fileArg);

            // Last of the startup questions; the first-start choice offers the tour itself when starting fresh. What's
            // new waits for the window when the app started hidden.
            if (offerTour && !offeredFirstStart)
                (MainWindow.Content as MainPage)?.OfferTour();
            if (greeting == Tutorial.Greeting.WhatsNew)
            {
                if (startHidden) _onFirstShow = ShowWhatsNew;
                else ShowWhatsNew();
            }

            // Prewarm the Quick Paste window while idle so the first hotkey open isn't cold (builds its
            // UI + tree and starts the WebView2 preview). It stays hidden until the hotkey is pressed.
            _uiDispatcher.TryEnqueue(DispatcherQueuePriority.Low, () => EnsurePasteWindow().Prewarm());        }

        // Another launch reached the running instance: surface the window and open its file (if any).
        // Raised on a background thread.
        private void OnRedirected(object? sender, AppActivationArguments activation)
        {
            var request = _environment.ReadRedirected(activation);
            if (request.IsAutostart && request.File == null) return;
            _uiDispatcher?.TryEnqueue(() =>
            {
                ShowMainWindow();
                if (request.File != null)
                    (MainWindow.Content as MainPage)?.HandleOpenTtmDataFile(request.File);
            });
        }

        /// <summary>A notice from the tray icon, for when the app started hidden and has no window to show it in.</summary>
        public void ShowTrayBalloon(string title, string message) => _trayService?.ShowBalloon(title, message);

        // Restore + foreground the main window (it may be hidden in the tray); also the tray's Open.
        private static void ShowMainWindow()
        {
            IntPtr hWnd = WindowNative.GetWindowHandle(MainWindow);
            // Show() (H.NotifyIcon) brings a maximized window back at normal size, and SW_RESTORE would too:
            // maximize again, restore only if minimized.
            bool wasMaximized = WindowHelper.IsZoomed(hWnd);
            MainWindow.Show();
            if (wasMaximized)
                WindowHelper.ShowWindow(hWnd, WindowHelper.SW_MAXIMIZE);
            else if (WindowHelper.IsIconic(hWnd))
                WindowHelper.ShowWindow(hWnd, WindowHelper.SW_RESTORE);
            WindowHelper.SetForegroundWindow(hWnd);
            MainWindow.Activate();
            var onFirstShow = _onFirstShow;
            _onFirstShow = null;
            onFirstShow?.Invoke();
        }

        private static Action? _onFirstShow;

        // Shown once: the page records the version when the dialog was shown.
        private void ShowWhatsNew()
        {
            string notes = "";
            try { notes = System.IO.File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "release.md")); }
            catch { }
            var blocks = Tutorial.WhatsNew.Parse(notes);
            if (blocks.Count == 0) _store.CurrentSettings.LastRunVersion = AppInfo.Version;
            else (_pageBeforeTour ?? MainWindow.Content as MainPage)?.ShowWhatsNew(AppInfo.Version, blocks);   // the real page: during a tour it waits until the tour ends
        }

        private void OnGlobalHotkeyPressed()
        {
            WindowHelper.CaptureTargetContext();

            MainWindow.DispatcherQueue.TryEnqueue(() =>
            {
                // Reuse the warm instance (hidden between uses) so reopen is instant and immediately
                // ready for a single-key press — no cold rebuild of the tree / WebView preview.
                EnsurePasteWindow().ShowForPaste();
            });
        }

        // The Quick Paste window, kept warm (hidden) between uses. A paste destroys it (so it can't
        // steal the Ctrl+V) and OnPasteWindowClosed rebuilds a fresh prewarmed one; the X/Esc only hide.
        private PasteWindow EnsurePasteWindow()
        {
            if (_pasteWindow == null)
            {
                _pasteWindow = new PasteWindow(_store);
                _pasteWindow.Closed += OnPasteWindowClosed;
            }
            return _pasteWindow;
        }

        // A paste closed (destroyed) the window: rebuild a fresh hidden, prewarmed instance for the next
        // hotkey, mirroring the launch prewarm. Low priority so it never contends with the paste's
        // foreground handoff; if the user reopens first, EnsurePasteWindow just prewarms that instance.
        private void OnPasteWindowClosed(object sender, WindowEventArgs e)
        {
            _pasteWindow = null;
            _uiDispatcher.TryEnqueue(DispatcherQueuePriority.Low, () => EnsurePasteWindow().Prewarm());
        }

        /// <summary>False when the configured hotkey couldn't be registered (another app owns it).</summary>
        public bool HotkeyRegistered { get; private set; } = true;

        public bool UpdateGlobalHotkey(string hotkey)
        {
            HotkeyRegistered = _hotkeyListener?.Register(hotkey) ?? true;
            return HotkeyRegistered;
        }

        private void ReportHotkeyUnavailable(bool startedHidden)
        {
            string hotkey = _store.CurrentSettings.PasteWindowHotkey;
            ReportStartupProblem("Quick Paste shortcut unavailable",
                $"The Quick Paste shortcut {hotkey} is already used by another app, so it won't open Quick Paste. " +
                "Choose a different shortcut in Settings › General.", startedHidden);
        }

        // At startup: a dialog when the window is shown, a tray notice when started hidden.
        private void ReportStartupProblem(string title, string message, bool startedHidden)
        {
            if (startedHidden) ShowTrayBalloon(title, message);
            else (MainWindow.Content as MainPage)?.ReportStartupProblem(title, message);
        }

        /// <summary>True when the connector is enabled but couldn't listen on its port (taken or reserved).</summary>
        public bool ConnectorUnavailable { get; private set; }

        /// <summary>Starts/stops/restarts the browser connector to match the current settings.
        /// Safe to call at launch and whenever the connector settings change.</summary>
        public void ApplyBrowserConnectorSettings()
        {
            var s = _store.CurrentSettings;
            ConnectorUnavailable = false;
            if (!s.BrowserConnectorEnabled) { _connector?.Stop(); return; }

            // Generate the shared token on first enable (saved with the other settings changes).
            if (string.IsNullOrEmpty(s.BrowserConnectorToken))
                s.BrowserConnectorToken = Guid.NewGuid().ToString("N");

            if (_connectorData == null)
            {
                _connectorData = new AppConnectorData(_store);
                _store.TreeChanged += RebuildConnectorSnapshot;
                _store.DataSaved += RebuildConnectorSnapshot;
            }
            _connectorData.Rebuild();   // on the UI thread here

            _connector ??= new BrowserConnector(_connectorData);
            try { _connector.Start(s.BrowserConnectorPort, s.BrowserConnectorToken); }
            catch (Exception ex)
            {
                ConnectorUnavailable = true;
                System.Diagnostics.Debug.WriteLine($"[Connector] start failed: {ex.Message}");
            }
        }

        // TreeChanged/DataSaved can fire off the UI thread; marshal the snapshot rebuild back.
        private void RebuildConnectorSnapshot() =>
            _uiDispatcher?.TryEnqueue(() => { try { _connectorData?.Rebuild(); } catch { } });

        // Last-resort net for UI-thread exceptions (async-void handlers make this easy to trip). Handled=true
        // stops the immediate termination so pending work can be saved; the user then decides whether to
        // keep running in a possibly inconsistent state (see UnexpectedErrorHandler).
        private async void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            e.Handled = true;
            await _errors.HandleAsync(e.Exception);
        }

        private async Task<bool> AskQuitAfterErrorAsync(bool saved)
        {
            if (MainWindow?.Content is not MainPage page) return false;
            ShowMainWindow();
            return await page.AskQuitAfterErrorAsync(saved, _store.Storage.GetCrashLogPath());
        }

        private void LogCrash(string source, Exception? ex)
        {
            System.Diagnostics.Debug.WriteLine($"[{source}] {ex}");
            try
            {
                System.IO.File.AppendAllText(
                    _store.Storage.GetCrashLogPath(),
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{source}] {ex}{Environment.NewLine}{Environment.NewLine}");
            }
            catch { /* logging must never throw */ }
        }

        // ---- Exit: persist everything pending first ----

        private readonly List<Func<Task>> _exitFlushes = new();
        private Task? _sessionEndSave;

        /// <summary>Registers work that must be persisted before the app quits (e.g. the editor's not yet
        /// reported content). Runs in registration order, before the pending data save.</summary>
        public void AddExitFlush(Func<Task> flush) => _exitFlushes.Add(flush);

        /// <summary>Writes everything pending: registered flushes, the debounced data save, any write still in
        /// progress, and a recent backup of what isn't backed up yet. A failing step is logged and doesn't stop
        /// the others.</summary>
        public async Task PersistPendingAsync()
        {
            foreach (var flush in _exitFlushes)
            {
                try { await flush(); }
                catch (Exception ex) { LogCrash("ExitFlush", ex); }
            }
            try { await _store.FlushPendingSaveAsync(); }
            catch (Exception ex) { LogCrash("ExitFlush", ex); }
            await _store.Storage.WaitForWritesAsync();

            // Changes since the last recent backup get one before the app goes.
            try { if (_backupScheduler != null) await _backupScheduler.FlushAsync(TimeSpan.FromSeconds(5)); }
            catch (Exception ex) { LogCrash("ExitBackup", ex); }
        }

        /// <summary>The backups the Backups settings page lists and restores from.</summary>
        public BackupService Backups => _backups ??= new BackupService(_store.Storage);
        private BackupService? _backups;

        /// <summary>Restores <paramref name="parts"/> of a backup while running: saves and backs up the current state
        /// first, stops everything that writes, swaps the files in and restarts the app to load them. Returns only
        /// when the restore failed (nothing changed, the app carries on) or the restart did.</summary>
        public async Task<RestoreOutcome> RestoreFromBackupAsync(string backupPath, RestoreParts parts,
            IReadOnlyCollection<SyncFilePlacement>? placements = null)
        {
            await PersistPendingAsync();   // also a recent backup of anything not backed up yet
            StopAcceptingWork();
            if (_backupScheduler != null) await _backupScheduler.StopAsync();
            await _store.Storage.SuspendWritesAsync();

            var result = await new RestoreService(_store.Storage).RestoreAsync(backupPath, parts, placements);
            if (!result.Succeeded)
            {
                _store.Storage.ResumeWrites();
                UpdateGlobalHotkey(_store.CurrentSettings.PasteWindowHotkey);
                ApplyBrowserConnectorSettings();
                _backupScheduler = new BackupScheduler(Backups, _store.Storage);
                _backupScheduler.Start();
                return new RestoreOutcome(false, result.Problem);
            }

            // The new instance must not redirect to this one, and the tray icon goes with it.
            try { _trayService?.Dispose(); } catch { }
            try { AppInstance.GetCurrent().UnregisterKey(); } catch { }
            AppInstance.Restart(OfferTourInDevRun ? "--offer-tour" : "");   // ends this process; returns only if the restart failed
            return new RestoreOutcome(true, "The backup was restored, but the app couldn't restart itself. Start it again to load the restored data.");
        }

        public sealed record RestoreOutcome(bool Restored, string? Problem);

        /// <summary>Where the synced folders of a backup would go when restored without its sync settings.</summary>
        public IReadOnlyList<SyncedFolderTarget> SyncedFolderTargets(string backupPath) =>
            new RestoreService(_store.Storage).FindSyncedFolderTargets(backupPath);

        /// <summary>Writes a full backup zip to <paramref name="path"/>, after saving any pending edits so it holds
        /// what the user sees.</summary>
        public async Task<BackupResult> ExportBackupAsync(string path)
        {
            await _store.FlushPendingSaveAsync();
            await _store.Storage.WaitForWritesAsync();
            return await Backups.CreateAsync(BackupTier.Exported, BackupSource.Live(_store.Storage), quietMs: 500,
                quietTimeout: TimeSpan.FromSeconds(10), targetPath: path);
        }

        private void StopAcceptingWork()
        {
            _hotkeyListener?.Register("None");
            try { _connector?.Stop(); } catch { }
        }

        /// <summary>Quit from the menu or the tray: persist everything, then exit. Shows progress when that
        /// takes more than a second and asks before giving up on a save that hangs.</summary>
        public async Task ExitAsync()
        {
            EndTutorial();
            if (MainWindow.Content is MainPage editing)
            {
                if (editing.ViewModel.HasUnsavedChanges) ShowMainWindow();   // the question needs the window
                if (!await editing.ResolveUnsavedEditsAsync()) return;
            }
            StopAcceptingWork();
            var save = PersistPendingAsync();
            if (await Task.WhenAny(save, Task.Delay(TimeSpan.FromSeconds(1))) != save
                && MainWindow.Content is MainPage page)
            {
                ShowMainWindow();
                page.ShowExitProgress();
                while (await Task.WhenAny(save, Task.Delay(TimeSpan.FromSeconds(15))) != save)
                {
                    await page.HideExitProgressAsync();
                    if (!await page.AskKeepWaitingForSaveAsync()) break;
                    page.ShowExitProgress();
                }
                await page.HideExitProgressAsync();
            }
            Shutdown();
        }

        // ---- Guided tour: the main window shows a sample store while it runs ----

        private Tutorial.TourSample? _tour;
        private MainPage? _tourPage, _pageBeforeTour;
        private PasteWindow? _tourQuickPaste;

        /// <summary>Runs the guided tour on sample data: the main window shows a page over a throwaway store, the
        /// global hotkey is off and the browser connector answers "busy"; everything is put back at the end.</summary>
        public async Task StartTutorialAsync()
        {
            // Not under an open dialog (What's new, a question): swapping the page would leave it over the tour.
            if (_tour != null || MainWindow.Content is not MainPage page || page.IsDialogOpen) return;
            if (!await page.ResolveUnsavedEditsAsync()) return;

            _store.CurrentSettings.TutorialOfferDone = true;
            _tour = await Tutorial.TourSample.CreateAsync(_store.CurrentSettings);
            _pageBeforeTour = page;
            _tourPage = new MainPage(_tour.Store, tour: true);
            _hotkeyListener?.Register("None");
            if (_connector != null) _connector.Busy = true;
            MainWindow.Content = _tourPage;
            ShowMainWindow();

            var outcome = Tutorial.TourOutcome.Skipped;
            try
            {
                if (!_tourPage.IsLoaded)
                {
                    var loaded = new TaskCompletionSource();
                    _tourPage.Loaded += (_, _) => loaded.TrySetResult();
                    await loaded.Task;
                }
                // The main window's part, then Quick Paste's (opened by the tour); Back from Quick Paste's first step
                // returns to the main window's last one.
                _tourQuickPaste = new PasteWindow(_tour.Store, tour: true);
                _tourQuickPaste.TourWindowClosed += (_, _) => EndTutorial();
                int mainSteps = _tourPage.TourStepCount, startAt = 0;
                while (_tourPage is { } tourPage && _tourQuickPaste != null)
                {
                    outcome = await tourPage.RunTourAsync(startAt, mainSteps + _tourQuickPaste.TourStepCount);
                    if (outcome != Tutorial.TourOutcome.Done || _tourQuickPaste == null) break;

                    _tourQuickPaste.ShowForTour();
                    await Task.Delay(400);   // shown and laid out
                    outcome = await _tourQuickPaste.RunTourAsync(mainSteps + 1, mainSteps + _tourQuickPaste.TourStepCount);
                    if (outcome != Tutorial.TourOutcome.BackFromFirst) break;
                    _tourQuickPaste?.Hide();
                    MainWindow.Activate();
                    startAt = mainSteps - 1;
                }
            }
            finally { EndTutorial(); }
            if (outcome != Tutorial.TourOutcome.Done) page.ShowTourHint();
        }

        /// <summary>Puts the real page back and turns input on again; nothing when no tour runs.</summary>
        public void EndTutorial()
        {
            if (_tourPage is not { } tourPage || _pageBeforeTour is not { } page) return;
            _tourPage = null;
            _pageBeforeTour = null;
            _tourQuickPaste?.CloseForTour();
            _tourQuickPaste = null;
            tourPage.EndTour();
            MainWindow.Content = page;
            tourPage.ViewModel.StopBackgroundWork();
            _tour?.Dispose();
            _tour = null;
            UpdateGlobalHotkey(_store.CurrentSettings.PasteWindowHotkey);
            if (_connector != null) _connector.Busy = false;
        }

        // Sign-out/shutdown announced: start saving right away; the session may still be cancelled.
        private void OnQueryEndSession() => _sessionEndSave ??= SaveForSessionEndAsync();

        private async Task SaveForSessionEndAsync()
        {
            try { if (MainWindow.Content is MainPage page) await page.KeepUnsavedEditsAsync(); }
            catch (Exception ex) { LogCrash("SessionEndEdits", ex); }
            await PersistPendingAsync();
        }

        // Final: Windows ends the process as soon as this returns, so wait here (keeping the UI thread's
        // messages flowing for the saves) within the few seconds Windows allows.
        private void OnEndSession()
        {
            StopAcceptingWork();
            var save = _sessionEndSave ??= SaveForSessionEndAsync();
            WindowHelper.PumpMessagesUntil(() => save.IsCompleted, TimeSpan.FromSeconds(4));
            Shutdown();
        }

        /// <summary>Quit: remove the tray icon, then exit normally. Persist pending state before calling
        /// (<see cref="ExitAsync"/> does).</summary>
        public void Shutdown()
        {
            _isClosingFromTray = true;
            try { _connector?.Stop(); } catch { }
            try { _backupScheduler?.Dispose(); } catch { }
            // From here a new launch starts fresh instead of redirecting to this exiting instance.
            try { AppInstance.GetCurrent().UnregisterKey(); } catch { }
            try { _trayService?.Dispose(); } catch { }

            // 1.x hard-exited because Exit()'s teardown threw (0xc000027b); that no longer reproduces.
            // Should the teardown ever hang, a quit still ends the process.
            new System.Threading.Thread(() => { System.Threading.Thread.Sleep(5000); Environment.Exit(0); }) { IsBackground = true }.Start();
            Exit();
        }
    }
}