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

/// <summary>What a launch asks for: started by Windows at sign-in, and/or a .ttmdata to open.</summary>
public readonly record struct LaunchRequest(bool IsAutostart, string? File);

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

    /// <summary>What a later launch, redirected to this running instance, asks for.</summary>
    LaunchRequest ReadRedirected(AppActivationArguments activation);

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

    /// <summary>A launch from its command-line arguments. --hidden only counts in dev runs, where it stands in
    /// for Windows starting the app.</summary>
    internal static LaunchRequest FromArguments(IReadOnlyList<string> args, bool hiddenFlag) => new(
        hiddenFlag && args.Any(a => string.Equals(a, "--hidden", StringComparison.OrdinalIgnoreCase)),
        FirstTtmDataFile(args));

    internal static LaunchRequest FromActivation(AppActivationArguments activation, bool hiddenFlag) => activation.Kind switch
    {
        ExtendedActivationKind.StartupTask => new(true, null),
        ExtendedActivationKind.File when activation.Data is IFileActivatedEventArgs file =>
            new(false, FirstTtmDataFile(file.Files.Select(f => f.Path))),
        ExtendedActivationKind.Launch when activation.Data is ILaunchActivatedEventArgs launch =>
            FromArguments(SplitCommandLine(launch.Arguments), hiddenFlag),
        _ => default,
    };

    /// <summary>Splits a command line the way Windows passes it to Main.</summary>
    internal static string[] SplitCommandLine(string? commandLine)
    {
        // CommandLineToArgvW turns an empty string into the current exe's path.
        if (string.IsNullOrWhiteSpace(commandLine)) return [];
        IntPtr argv = CommandLineToArgvW(commandLine, out int count);
        if (argv == IntPtr.Zero) return [];
        try
        {
            var args = new string[count];
            for (int i = 0; i < count; i++)
                args[i] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size)) ?? "";
            return args;
        }
        finally { LocalFree(argv); }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CommandLineToArgvW(string lpCmdLine, out int pNumArgs);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    private sealed class Packaged : IAppEnvironment
    {
        private const string StartupTaskId = "TtmStartup";   // Package.appxmanifest

        private readonly LaunchRequest _launch =
            FromActivation(Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs(), hiddenFlag: false);

        public bool IsPackaged => true;

        // %LocalAppData%\Packages\<family name>\LocalState: wiped by Reset and uninstall.
        public string DataDirectory => Windows.Storage.ApplicationData.Current.LocalFolder.Path;

        public bool IsAutostartLaunch => _launch.IsAutostart;
        public string? OpenedFile => _launch.File;

        public LaunchRequest ReadRedirected(AppActivationArguments activation) => FromActivation(activation, hiddenFlag: false);

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

        private readonly LaunchRequest _launch = FromArguments(Environment.GetCommandLineArgs()[1..], hiddenFlag: true);

        public bool IsAutostartLaunch => _launch.IsAutostart;
        public string? OpenedFile => _launch.File;

        public LaunchRequest ReadRedirected(AppActivationArguments activation) => FromActivation(activation, hiddenFlag: true);

        public Task<AutostartState> GetAutostartAsync() => Task.FromResult(AutostartState.NotAvailable);
        public Task<AutostartState> SetAutostartAsync(bool on) => Task.FromResult(AutostartState.NotAvailable);
    }
}
