using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.ApplicationModel;

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

    private sealed class Packaged : IAppEnvironment
    {
        private const string StartupTaskId = "TtmStartup";   // Package.appxmanifest

        public bool IsPackaged => true;

        // %LocalAppData%\Packages\<family name>\LocalState: wiped by Reset and uninstall.
        public string DataDirectory => Windows.Storage.ApplicationData.Current.LocalFolder.Path;

        public bool IsAutostartLaunch { get; } =
            Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs().Kind
                == Microsoft.Windows.AppLifecycle.ExtendedActivationKind.StartupTask;

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

        public Task<AutostartState> GetAutostartAsync() => Task.FromResult(AutostartState.NotAvailable);
        public Task<AutostartState> SetAutostartAsync(bool on) => Task.FromResult(AutostartState.NotAvailable);
    }
}
