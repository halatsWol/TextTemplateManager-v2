using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Text.RegularExpressions;

namespace TextTemplateManager.Services.Updates;

/// <summary>Which versions of the app Windows staged, from the AppXDeploymentServer log (readable without admin rights).
/// Needed because a version staged by someone else than the Store queue (Windows Update stages Store apps too, seen
/// 2026-10-21 with automatic Store updates off) never shows in the queue item, and the Store never names the version.</summary>
public static partial class AppxStageLog
{
    public const string LogName = "Microsoft-Windows-AppXDeploymentServer/Operational";

    // "Deployment Stage operation with target volume C: on Package <Name>_2.3.2.0_x64__<PublisherId> from: (...) finished successfully."
    [GeneratedRegex(@"Deployment Stage operation .*? on Package (?<name>[^_\s]+)_(?<version>\d+\.\d+\.\d+\.\d+)_[^_\s]*__(?<publisher>[a-z0-9]+)\b.*finished successfully", RegexOptions.Singleline)]
    private static partial Regex StagedPattern();

    /// <summary>The version a log message says was staged for the package <paramref name="name"/> of
    /// <paramref name="publisherId"/>, else null.</summary>
    public static Version? ParseStaged(string? message, string name, string publisherId)
    {
        if (string.IsNullOrEmpty(message)) return null;
        var match = StagedPattern().Match(message);
        return match.Success
            && string.Equals(match.Groups["name"].Value, name, StringComparison.OrdinalIgnoreCase)
            && string.Equals(match.Groups["publisher"].Value, publisherId, StringComparison.OrdinalIgnoreCase)
            && Version.TryParse(match.Groups["version"].Value, out var version) ? version : null;
    }

    /// <summary>The versions above <paramref name="running"/> the log says were staged, newest first. Whether each is
    /// still there is the caller's to check. Empty when the log can't be read.</summary>
    public static IReadOnlyList<Version> FindStaged(string name, string publisherId, Version running)
    {
        var found = new SortedSet<Version>(Comparer<Version>.Create((a, b) => b.CompareTo(a)));
        try
        {
            // 400 and 478: a stage or register operation that finished (bundle and architecture package).
            var query = new EventLogQuery(LogName, PathType.LogName, "*[System[(EventID=400 or EventID=478)]]");
            using var reader = new EventLogReader(query);
            for (var record = reader.ReadEvent(); record != null; record = reader.ReadEvent())
            {
                using (record)
                {
                    string? message;
                    try { message = record.FormatDescription(); }
                    catch { continue; }
                    if (ParseStaged(message, name, publisherId) is { } version && version > running) found.Add(version);
                }
            }
        }
        catch { }
        return [.. found];
    }
}
