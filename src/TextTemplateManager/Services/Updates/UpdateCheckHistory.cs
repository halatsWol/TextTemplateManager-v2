using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace TextTemplateManager.Services.Updates;

/// <summary>When TTM asked the source for updates, kept across restarts. The Store answers an app at most one check per
/// 30 minutes and ten per 24 hours; beyond that it repeats its last answer (StoreContext.GetAppAndOptionalStorePackageUpdatesAsync,
/// "Detection frequency"), which looks like "up to date". Other callers with the app's identity count too.</summary>
public sealed class UpdateCheckHistory
{
    public static readonly TimeSpan MinSpacing = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan Window = TimeSpan.FromHours(24);
    public const int MaxPerWindow = 10;

    private readonly string _path;
    private readonly List<DateTime> _checks = [];

    public UpdateCheckHistory(string path)
    {
        _path = path;
        try
        {
            if (File.Exists(path))
                foreach (string line in File.ReadAllLines(path))
                    if (DateTime.TryParse(line, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at))
                        _checks.Add(at.ToUniversalTime());
        }
        catch { }
        _checks.Sort();
    }

    /// <summary>The last check (UTC), or null.</summary>
    public DateTime? Last => _checks.Count > 0 ? _checks[^1] : null;

    public void Record(DateTime utcNow)
    {
        _checks.Add(utcNow);
        _checks.RemoveAll(c => c < utcNow - Window);
        try { File.WriteAllLines(_path, _checks.Select(c => c.ToString("o", CultureInfo.InvariantCulture))); }
        catch { }
    }

    /// <summary>The earliest time (UTC) the Store answers a new check, or null when it does now.</summary>
    public DateTime? NextAllowed(DateTime utcNow) => NextAllowed(_checks, utcNow);

    /// <summary>The same for any list of checks (UTC), such as the one the Store logs.</summary>
    public static DateTime? NextAllowed(IEnumerable<DateTime> checksUtc, DateTime utcNow)
    {
        var recent = checksUtc.Where(c => c > utcNow - Window).OrderBy(c => c).ToList();
        DateTime? next = null;
        if (recent.Count > 0 && recent[^1] + MinSpacing > utcNow) next = recent[^1] + MinSpacing;
        if (recent.Count >= MaxPerWindow)
        {
            var freed = recent[recent.Count - MaxPerWindow] + Window;
            if (next == null || freed > next) next = freed;
        }
        return next;
    }
}
