using System;
using System.IO;
using System.Runtime.InteropServices;

namespace TextTemplateManager.Services.Platform;

/// <summary>What differs between the Store package and an unpackaged dev run. Chosen once at startup
/// (<see cref="AppEnvironment.Detect"/>) and passed to whatever needs it.</summary>
public interface IAppEnvironment
{
    bool IsPackaged { get; }

    /// <summary>Where the live data (templates, settings, sync settings, crash log) lives, unless
    /// TTM_DATA_DIR redirects it. Never the 1.x data folder, which TTM 2 only ever reads.</summary>
    string DataDirectory { get; }
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
        public bool IsPackaged => true;

        // %LocalAppData%\Packages\<family name>\LocalState: wiped by Reset and uninstall.
        public string DataDirectory => Windows.Storage.ApplicationData.Current.LocalFolder.Path;
    }

    internal sealed class Unpackaged : IAppEnvironment
    {
        public bool IsPackaged => false;

        // Dev runs from bin\: their own folder, so they can't edit the 1.x data next door.
        public string DataDirectory { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Marflow Software", "TextTemplateManager 2 Dev");
    }
}
