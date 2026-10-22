using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using TextTemplateManager.Common;

namespace TextTemplateManager.Services.Platform;

/// <summary>One event log entry, as far as the export needs it.</summary>
public sealed record LogEvent(DateTime Time, string Level, int Id, string Provider, Guid? ActivityId, string? Message);

public interface IEventLogSource
{
    /// <summary>The events of <paramref name="logName"/> since <paramref name="sinceUtc"/>, from the given providers
    /// (all when null). Throws when the log can't be read.</summary>
    IReadOnlyList<LogEvent> Read(string logName, DateTime sinceUtc, IReadOnlyCollection<string>? providers);
}

/// <summary>Settings ▸ Support ▸ Export logs: the app's logs, its package and policies, and the Microsoft Store,
/// AppX deployment and application error events about it in one .zip for a problem report. The same files as the
/// website's log collector (Collect-TTMLogs.ps1), which covers an app that doesn't start. Templates, settings and
/// sync files stay out; user name, computer name, profile path and SIDs are replaced.</summary>
public static partial class LogExport
{
    public const int Days = 14;
    public const string StoreLog = "Microsoft-Windows-Store/Operational";
    public const string AppxLog = "Microsoft-Windows-AppXDeploymentServer/Operational";
    public static readonly string[] ErrorProviders = ["Application Error", "Application Hang", ".NET Runtime", "Windows Error Reporting"];
    public static readonly string[] AppFiles = ["update.log", "crash.log", "update-checks.txt", "update-attempt.json"];

    [GeneratedRegex(@"MarflowSoftware\.TextTemplateManager|9NMBSZLLLN6V|ttm2\.exe", RegexOptions.IgnoreCase)]
    private static partial Regex AppPattern();

    public static string SuggestedFileName(DateTime now) => $"TTM-logs-{now:yyyyMMdd-HHmmss}";

    /// <summary>Collects everything and writes the .zip to <paramref name="zipPath"/>. Slow (reads the event logs):
    /// call it off the UI thread.</summary>
    public static void Export(string zipPath, string dataDirectory, IEventLogSource events, Redactor redactor, DateTime now)
    {
        var entries = new List<(string Name, string Text)> { ("system.txt", SystemInfo(now)) };
        entries.AddRange(AppFileEntries(dataDirectory));
        var since = now.AddDays(-Days);
        entries.Add(("events-store.txt", EventsText(events, StoreLog, since, null)));
        entries.Add(("events-appx-deployment.txt", EventsText(events, AppxLog, since, null)));
        entries.Add(("events-app-errors.txt", EventsText(events, "Application", since, ErrorProviders)));
        WriteZip(zipPath, entries.Select(e => (e.Name, redactor.Apply(e.Text))));
    }

    public static IEnumerable<(string Name, string Text)> AppFileEntries(string dataDirectory)
    {
        if (!Directory.Exists(dataDirectory))
        {
            yield return ("app-folder.txt", $"No app data folder ({dataDirectory}).");
            yield break;
        }
        var listing = new StringBuilder();
        foreach (var entry in new DirectoryInfo(dataDirectory).EnumerateFileSystemInfos().OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
        {
            string size = entry is FileInfo file ? file.Length.ToString(CultureInfo.InvariantCulture) : "<folder>";
            listing.AppendLine(CultureInfo.InvariantCulture, $"{entry.Name,-28} {size,10}  {entry.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
        }
        yield return ("app-folder.txt", listing.ToString());
        foreach (string name in AppFiles)
        {
            string path = Path.Combine(dataDirectory, name);
            if (File.Exists(path)) yield return ($"app-{name}", ReadShared(path));
        }
    }

    // The app may be writing update.log at the same time.
    private static string ReadShared(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception ex) { return $"Not readable: {ex.Message}"; }
    }

    public static string EventsText(IEventLogSource source, string logName, DateTime since, IReadOnlyCollection<string>? providers)
    {
        IReadOnlyList<LogEvent> events;
        try { events = source.Read(logName, since.ToUniversalTime(), providers); }
        catch (Exception ex) { return $"Not readable: {ex.Message}"; }
        string day = since.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (events.Count == 0) return $"No events since {day}.";
        var selected = SelectAppEvents(events);
        if (selected.Count == 0) return $"None of the {events.Count} events since {day} concern TextTemplateManager.";
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"{selected.Count} of {events.Count} events since {day}, oldest first:").AppendLine();
        foreach (var e in selected) text.AppendLine(Format(e));
        return text.ToString();
    }

    /// <summary>The app's events plus every event of the same operations (ActivityId): an install or update logs most of
    /// its steps without the package name. Oldest first.</summary>
    public static IReadOnlyList<LogEvent> SelectAppEvents(IEnumerable<LogEvent> events)
    {
        var all = events.ToList();
        var activities = all.Where(e => e.ActivityId is { } id && id != Guid.Empty && Concerns(e)).Select(e => e.ActivityId!.Value).ToHashSet();
        return all.Where(e => Concerns(e) || e.ActivityId is { } id && activities.Contains(id)).OrderBy(e => e.Time).ToList();
    }

    private static bool Concerns(LogEvent e) => e.Message is { } m && AppPattern().IsMatch(m);

    public static string Format(LogEvent e)
    {
        string message = string.IsNullOrWhiteSpace(e.Message) ? $"(no message, provider {e.Provider})"
            : Regex.Replace(e.Message.Trim(), @"\s*\r?\n\s*", " | ");
        return string.Format(CultureInfo.InvariantCulture, "{0:yyyy-MM-dd HH:mm:ss.fff}  {1,-11} {2,5}  {3}", e.Time, e.Level, e.Id, message);
    }

    private static string SystemInfo(DateTime now)
    {
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"Collected:   {now:yyyy-MM-dd HH:mm:ss zzz} (Settings > Support > Export logs)");
        text.AppendLine($"Windows:     {SupportLinks.WindowsVersion()}");
        text.AppendLine($"Language:    {CultureInfo.CurrentCulture.Name}, display {CultureInfo.CurrentUICulture.Name}");
        text.AppendLine($"App:         {AppInfo.Version}{(AppInfo.ReleaseDate.Length > 0 ? $", released {AppInfo.ReleaseDate}" : ", dev build")}");
        text.AppendLine($"Package:     {PackageName() ?? "none (not running as the Store package)"}");
        text.AppendLine().AppendLine("---- Policies ----");
        foreach (string key in new[] { @"SOFTWARE\Policies\Marflow Software\TextTemplateManager", @"SOFTWARE\Policies\Microsoft\WindowsStore" })
        {
            text.AppendLine(@"HKLM\" + key);
            try
            {
                using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                using var values = hklm.OpenSubKey(key);
                if (values is null || values.ValueCount == 0) text.AppendLine("  not set");
                else foreach (string name in values.GetValueNames()) text.AppendLine($"  {name} = {values.GetValue(name)}");
            }
            catch (Exception ex) { text.AppendLine($"  not readable: {ex.Message}"); }
        }
        return text.ToString();
    }

    private static string? PackageName()
    {
        try { return Windows.ApplicationModel.Package.Current.Id.FullName; }
        catch { return null; }
    }

    public static void WriteZip(string path, IEnumerable<(string Name, string Text)> entries)
    {
        string temp = path + ".partial";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            foreach (var (name, text) in entries)
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(true));
                writer.Write(text.ReplaceLineEndings("\r\n"));
            }
        }
        File.Move(temp, path, overwrite: true);
    }
}

/// <summary>Replaces what identifies the user in exported text: the profile path, SIDs, and the user and computer
/// name (as whole words; names under 3 characters would also hit ordinary words).</summary>
public sealed class Redactor(string profilePath, string userName, string computerName)
{
    public static Redactor ForCurrentUser() => new(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.UserName, Environment.MachineName);

    public string Apply(string text)
    {
        if (profilePath.Length > 0) text = Regex.Replace(text, Regex.Escape(profilePath), "%USERPROFILE%", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"S-1-5-21-[\d-]+", "<sid>");
        if (userName.Length >= 3) text = Regex.Replace(text, $@"\b{Regex.Escape(userName)}\b", "<user>", RegexOptions.IgnoreCase);
        if (computerName.Length >= 3) text = Regex.Replace(text, $@"\b{Regex.Escape(computerName)}\b", "<computer>", RegexOptions.IgnoreCase);
        return text;
    }
}

/// <summary>Reads Windows event logs; the Store and AppX deployment logs and the Application log need no admin rights.</summary>
public sealed class WindowsEventLogSource : IEventLogSource
{
    public IReadOnlyList<LogEvent> Read(string logName, DateTime sinceUtc, IReadOnlyCollection<string>? providers)
    {
        string time = $"TimeCreated[@SystemTime>='{sinceUtc:yyyy-MM-ddTHH:mm:ss.fffZ}']";
        string filter = providers is { Count: > 0 }
            ? $"{time} and Provider[{string.Join(" or ", providers.Select(p => $"@Name='{p}'"))}]"
            : time;
        var events = new List<LogEvent>();
        using var reader = new EventLogReader(new EventLogQuery(logName, PathType.LogName, $"*[System[{filter}]]"));
        for (var record = reader.ReadEvent(); record != null; record = reader.ReadEvent())
        {
            using (record)
            {
                string? message, level;
                try { message = record.FormatDescription(); } catch { message = null; }
                try { level = record.LevelDisplayName; } catch { level = null; }
                events.Add(new LogEvent(record.TimeCreated ?? DateTime.MinValue, level ?? record.Level?.ToString(CultureInfo.InvariantCulture) ?? "",
                    record.Id, record.ProviderName ?? "", record.ActivityId, message));
            }
        }
        return events;
    }
}
