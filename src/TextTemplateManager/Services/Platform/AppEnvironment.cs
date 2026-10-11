using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;

namespace TextTemplateManager.Services.Platform;

/// <summary>Start with Windows, as Windows reports it.</summary>
public enum AutostartState
{
    /// <summary>Dev run: only the installed package can start with Windows.</summary>
    NotAvailable,
    Off,
    On,
    /// <summary>Switched off in Windows Settings › Apps › Startup; only the user can switch it on there.</summary>
    OffInWindows,
    OnByPolicy,
    OffByPolicy,
}

/// <summary>What differs between the Store package and an unpackaged dev run. Chosen once at startup
/// (<see cref="AppEnvironment.Detect"/>) and passed to whatever needs it.</summary>
public interface IAppEnvironment
{
    bool IsPackaged { get; }

    /// <summary>Where the live data (templates, settings, sync settings, crash log) lives, unless
    /// TTM_DATA_DIR redirects it. Never the 1.x data folder, which TTM 2 only ever reads.</summary>
    string DataDirectory { get; }

    /// <summary>This launch is Windows starting the app at sign-in (the only case where "start hidden"
    /// applies).</summary>
    bool IsAutostartLaunch { get; }

    /// <summary>The .ttmdata this launch was asked to open (double-click in Explorer, Open with), or null.</summary>
    string? OpenedFile { get; }

    Task<AutostartState> GetAutostartAsync();

    /// <summary>Turns starting with Windows on or off and returns the state Windows then reports.</summary>
    Task<AutostartState> SetAutostartAsync(bool on);
}

public static class AppEnvironment
{
    private const int APPMODEL_ERROR_NO_PACKAGE = 15700;

    /// <summary>The 1.x data folder (%LocalAppData%\Marflow Software\TextTemplateManager): read-only for TTM 2.
    /// Nothing in MSIX keeps a package from writing there, so no data path may point into it.</summary>
    public static string LegacyDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Marflow Software", "TextTemplateManager");

    public static IAppEnvironment Detect() => HasPackageIdentity() ? new Packaged() : new Unpackaged();

    private static bool HasPackageIdentity()
    {
        int length = 0;
        return GetCurrentPackageFullName(ref length, IntPtr.Zero) != APPMODEL_ERROR_NO_PACKAGE;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, IntPtr packageFullName);

    /// <summary>The first existing .ttmdata among the given paths, as a full path.</summary>
    internal static string? FirstTtmDataFile(IEnumerable<string> paths)
    {
        foreach (var p in paths)
        {
            if (!p.EndsWith(".ttmdata", StringComparison.OrdinalIgnoreCase)) continue;
            try { if (File.Exists(p)) return Path.GetFullPath(p); }
            catch { }
        }
        return null;
    }

    private sealed class Packaged : IAppEnvironment
    {
        private const string StartupTaskId = "TtmStartup";   // Package.appxmanifest

        private readonly AppActivationArguments _activation = Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs();

        public Packaged()
        {
            IsAutostartLaunch = _activation.Kind == ExtendedActivationKind.StartupTask;
            OpenedFile = _activation.Kind == ExtendedActivationKind.File && _activation.Data is IFileActivatedEventArgs file
                ? FirstTtmDataFile(file.Files.Select(f => f.Path))
                : FirstTtmDataFile(Environment.GetCommandLineArgs().Skip(1));
        }

        public bool IsPackaged => true;

        // %LocalAppData%\Packages\<family name>\LocalState: wiped by Reset and uninstall.
        public string DataDirectory => Windows.Storage.ApplicationData.Current.LocalFolder.Path;

        public bool IsAutostartLaunch { get; }
        public string? OpenedFile { get; }

        public async Task<AutostartState> GetAutostartAsync() => Map((await StartupTask.GetAsync(StartupTaskId)).State);

        public async Task<AutostartState> SetAutostartAsync(bool on)
        {
            var task = await StartupTask.GetAsync(StartupTaskId);
            if (on) return Map(await task.RequestEnableAsync());   // stays off if the user switched it off in Windows
            if (task.State == StartupTaskState.Enabled) task.Disable();
            return Map(task.State);
        }

        private static AutostartState Map(StartupTaskState state) => state switch
        {
            StartupTaskState.Enabled => AutostartState.On,
            StartupTaskState.DisabledByUser => AutostartState.OffInWindows,
            StartupTaskState.EnabledByPolicy => AutostartState.OnByPolicy,
            StartupTaskState.DisabledByPolicy => AutostartState.OffByPolicy,
            _ => AutostartState.Off,
        };
    }

    internal sealed class Unpackaged : IAppEnvironment
    {
        public bool IsPackaged => false;

        // Dev runs from bin\: their own folder, so they can't edit the 1.x data next door.
        public string DataDirectory { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Marflow Software", "TextTemplateManager 2 Dev");

        // A dev run can't be started by Windows; --hidden stands in for that when testing hidden start.
        public bool IsAutostartLaunch { get; } =
            Environment.GetCommandLineArgs().Skip(1).Any(a => string.Equals(a, "--hidden", StringComparison.OrdinalIgnoreCase));

        public string? OpenedFile { get; } = FirstTtmDataFile(Environment.GetCommandLineArgs().Skip(1));

        public Task<AutostartState> GetAutostartAsync() => Task.FromResult(AutostartState.NotAvailable);
        public Task<AutostartState> SetAutostartAsync(bool on) => Task.FromResult(AutostartState.NotAvailable);
    }
}
