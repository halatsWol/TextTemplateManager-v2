using System;
using System.Linq;
using System.Reflection;

namespace TextTemplateManager.Common;

/// <summary>The running app's version and release date, as stamped into the assembly by the build.</summary>
public static class AppInfo
{
    private static readonly Assembly App = typeof(AppInfo).Assembly;

    /// <summary>The release version from the tag-driven InformationalVersion (e.g. "1.9.2"), or the numeric
    /// assembly version for a local dev build.</summary>
    public static string Version { get; } = ReadVersion();

    /// <summary>The release date (yyyy-MM-dd), or "" for a dev build.</summary>
    public static string ReleaseDate { get; } =
        App.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(m => m.Key == "ReleaseDate" && !string.IsNullOrWhiteSpace(m.Value))?.Value ?? "";

    private static string ReadVersion()
    {
        var info = App.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            int plus = info.IndexOf('+');   // strip any +<git-hash> the SDK may append
            return plus >= 0 ? info[..plus] : info;
        }
        var v = App.GetName().Version ?? new Version(0, 0, 0);
        return $"{v.Major}.{v.Minor}.{v.Build}";
    }
}
