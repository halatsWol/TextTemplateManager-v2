using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace TextTemplateManager.Services.Updates;

/// <summary>Whether the Store refused an update check because of its check limits. The Store API doesn't say so (it just
/// repeats its last answer), but its Operational log does, with the times of the app's earlier checks. The log text isn't
/// documented: when it isn't found or reads differently, nothing is known and the check counts as a normal one.</summary>
public static partial class StoreScanLog
{
    public const string LogName = "Microsoft-Windows-Store/Operational";

    // "Scan not allowed inside interval for <PFN>. Current last scan time cache: 2026-10-20T19:23:36+02:00;..."
    // "Online scan not allowed due to cooldown period for <PFN>. Current last scan time cache: ..."
    [GeneratedRegex(@"(?:Scan not allowed inside interval|Online scan not allowed due to cooldown period) for (?<pfn>[^\s.]+(?:\.[^\s.]+)*?)\.?\s+Current last scan time cache:\s*(?<times>[0-9T:;.+\-Z]+)")]
    private static partial Regex RefusalPattern();

    /// <summary>The app's logged scan times (UTC) when <paramref name="message"/> is a refusal for
    /// <paramref name="packageFamilyName"/>, else null.</summary>
    public static IReadOnlyList<DateTime>? ParseRefusal(string? message, string packageFamilyName)
    {
        if (string.IsNullOrEmpty(message)) return null;
        var match = RefusalPattern().Match(message);
        if (!match.Success || !string.Equals(match.Groups["pfn"].Value, packageFamilyName, StringComparison.OrdinalIgnoreCase)) return null;
        var scans = new List<DateTime>();
        foreach (string part in match.Groups["times"].Value.Split(';', StringSplitOptions.RemoveEmptyEntries))
            if (DateTimeOffset.TryParse(part, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)) scans.Add(at.UtcDateTime);
        return scans;
    }

    /// <summary>When the Store refused a check for the app since <paramref name="sinceUtc"/>: the earliest time (UTC) it
    /// checks again, by its documented limits applied to the logged scan times. Null when no refusal is logged or the log
    /// can't be read.</summary>
    public static DateTime? FindRefusal(string packageFamilyName, DateTime sinceUtc, DateTime utcNow)
    {
        try
        {
            string since = sinceUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
            string until = utcNow.AddSeconds(5).ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
            var query = new EventLogQuery(LogName, PathType.LogName,
                $"*[System[TimeCreated[@SystemTime>='{since}' and @SystemTime<='{until}']]]") { ReverseDirection = true };
            using var reader = new EventLogReader(query);
            for (var record = reader.ReadEvent(); record != null; record = reader.ReadEvent())
            {
                using (record)
                {
                    string? message;
                    try { message = record.FormatDescription(); }
                    catch { continue; }
                    if (ParseRefusal(message, packageFamilyName) is { } scans)
                        return UpdateCheckHistory.NextAllowed(scans, utcNow) ?? utcNow + UpdateCheckHistory.MinSpacing;
                }
            }
        }
        catch { }
        return null;
    }
}
