using System.IO.Compression;
using TextTemplateManager.Services.Platform;
using Xunit;

namespace TextTemplateManager.Tests;

/// <summary>Settings ▸ Support ▸ Export logs: the app's events with their operations, its log files but never its data,
/// and nothing that names the user.</summary>
public class LogExportTests
{
    private static readonly DateTime T0 = new(2026, 10, 22, 14, 0, 0, DateTimeKind.Local);

    private sealed class FakeEvents(Dictionary<string, IReadOnlyList<LogEvent>> logs) : IEventLogSource
    {
        public List<(string Log, IReadOnlyCollection<string>? Providers)> Reads { get; } = new();

        public IReadOnlyList<LogEvent> Read(string logName, DateTime sinceUtc, IReadOnlyCollection<string>? providers)
        {
            Reads.Add((logName, providers));
            return logs.TryGetValue(logName, out var events) ? events : throw new EventLogNotFound(logName);
        }
    }

    private sealed class EventLogNotFound(string log) : Exception($"The log {log} doesn't exist.");

    private static LogEvent Event(int minute, string? message, Guid? activity = null, string level = "Information") =>
        new(T0.AddMinutes(minute), level, 400, "Microsoft-Windows-AppXDeployment-Server", activity, message);

    [Fact]
    public void The_apps_events_come_with_the_other_events_of_their_operations_oldest_first()
    {
        var install = Guid.NewGuid();
        var other = Guid.NewGuid();
        var events = new[]
        {
            Event(5, "Deployment Register operation on Package MarflowSoftware.TextTemplateManager_2.4.0.0_x64__eb1r5ey0kd4ny finished.", install),
            Event(1, "Started deployment Stage operation on a package with main parameter 49408e78.", install),
            Event(2, "Some other app was updated.", other),
            Event(3, "ProductId = 9NMBSZLLLN6V queued."),
            Event(4, "No activity, no app.", Guid.Empty),
            Event(6, "Faulting application name: ttm2.exe, version: 2.4.0.0"),
        };

        var selected = LogExport.SelectAppEvents(events);

        Assert.Equal(new[] { 1, 3, 5, 6 }, selected.Select(e => e.Time.Minute));
    }

    [Fact]
    public void An_event_is_one_line_with_time_level_and_id()
    {
        string line = LogExport.Format(Event(0, "Error 0x80073D02:\r\n  the package is in use.\n", level: "Error"));

        Assert.Equal("2026-10-22 14:00:00.000  Error         400  Error 0x80073D02: | the package is in use.", line);
        Assert.EndsWith("(no message, provider Microsoft-Windows-AppXDeployment-Server)", LogExport.Format(Event(0, null)));
    }

    [Fact]
    public void An_events_file_says_how_many_concern_the_app_or_why_there_are_none()
    {
        var source = new FakeEvents(new()
        {
            ["Full"] = new[] { Event(0, "ttm2.exe stopped responding"), Event(1, "unrelated") },
            ["Other"] = new[] { Event(0, "unrelated") },
            ["Empty"] = Array.Empty<LogEvent>(),
        });

        Assert.StartsWith("1 of 2 events since 2026-10-08, oldest first:", LogExport.EventsText(source, "Full", T0.AddDays(-14), null));
        Assert.Equal("None of the 1 events since 2026-10-08 concern TextTemplateManager.", LogExport.EventsText(source, "Other", T0.AddDays(-14), null));
        Assert.Equal("No events since 2026-10-08.", LogExport.EventsText(source, "Empty", T0.AddDays(-14), null));
        Assert.Equal("Not readable: The log Missing doesn't exist.", LogExport.EventsText(source, "Missing", T0.AddDays(-14), null));
    }

    [Fact]
    public void The_user_profile_SIDs_user_and_computer_name_are_replaced()
    {
        var redactor = new Redactor(@"C:\Users\Jane.Doe", "Jane.Doe", "OFFICE-PC7");

        string text = redactor.Apply(@"Path c:\users\jane.doe\AppData\Local\x.log by JANE.DOE on office-pc7, SID S-1-5-21-1004336348-1177238915-682003330-1001."
            + " Words like Janet.Does or OFFICE-PC70 stay.");

        Assert.Equal(@"Path %USERPROFILE%\AppData\Local\x.log by <user> on <computer>, SID <sid>."
            + " Words like Janet.Does or OFFICE-PC70 stay.", text);
        Assert.Equal("Al and al stay", new Redactor("", "Al", "PC").Apply("Al and al stay"));
    }

    [Fact]
    public void Only_the_apps_log_files_are_collected_and_the_data_files_only_listed()
    {
        string dir = TestEnvironment.NewScratchDir();
        File.WriteAllText(Path.Combine(dir, "update.log"), "2026-10-22 check: up to date");
        File.WriteAllText(Path.Combine(dir, "crash.log"), "boom");
        File.WriteAllText(Path.Combine(dir, "data.ttmdata"), "SECRET TEMPLATE");
        File.WriteAllText(Path.Combine(dir, "settings.ttmsettings"), "SECRET SETTING");

        var entries = LogExport.AppFileEntries(dir).ToDictionary(e => e.Name, e => e.Text);

        Assert.Equal(new[] { "app-crash.log", "app-folder.txt", "app-update.log" }, entries.Keys.Order());
        Assert.Equal("2026-10-22 check: up to date", entries["app-update.log"]);
        Assert.Contains("data.ttmdata", entries["app-folder.txt"]);
        Assert.DoesNotContain(entries.Values, t => t.Contains("SECRET"));
        Assert.StartsWith("No app data folder", LogExport.AppFileEntries(Path.Combine(dir, "missing")).Single().Text);
    }

    [Fact]
    public void A_log_being_written_by_the_app_can_still_be_read()
    {
        string dir = TestEnvironment.NewScratchDir();
        string path = Path.Combine(dir, "update.log");
        using var writer = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        writer.Write("open"u8);
        writer.Flush();

        Assert.Equal("open", LogExport.AppFileEntries(dir).Single(e => e.Name == "app-update.log").Text);
    }

    [Fact]
    public void The_export_is_a_zip_of_plain_text_files_without_the_users_names()
    {
        string dir = TestEnvironment.NewScratchDir();
        File.WriteAllText(Path.Combine(dir, "update.log"), @"switch failed, see C:\Users\Jane.Doe\x");
        File.WriteAllText(Path.Combine(dir, "data.ttmdata"), "SECRET TEMPLATE");
        var source = new FakeEvents(new()
        {
            [LogExport.StoreLog] = new[] { Event(0, "ProductId = 9NMBSZLLLN6V for user Jane.Doe") },
            [LogExport.AppxLog] = Array.Empty<LogEvent>(),
        });
        string zipPath = Path.Combine(dir, "TTM-logs.zip");

        LogExport.Export(zipPath, dir, source, new Redactor(@"C:\Users\Jane.Doe", "Jane.Doe", "OFFICE-PC7"), T0);

        using var zip = ZipFile.OpenRead(zipPath);
        var texts = zip.Entries.ToDictionary(e => e.Name, e => new StreamReader(e.Open()).ReadToEnd());
        Assert.Equal(new[] { "app-folder.txt", "app-update.log", "events-app-errors.txt", "events-appx-deployment.txt", "events-store.txt", "system.txt" },
            texts.Keys.Order());
        Assert.Equal(@"switch failed, see %USERPROFILE%\x", texts["app-update.log"]);
        Assert.Contains("ProductId = 9NMBSZLLLN6V for user <user>", texts["events-store.txt"]);
        Assert.Equal("No events since 2026-10-08.", texts["events-appx-deployment.txt"].TrimEnd());
        Assert.StartsWith("Not readable:", texts["events-app-errors.txt"]);
        Assert.Contains("Windows:", texts["system.txt"]);
        Assert.DoesNotContain(texts.Values, t => t.Contains("SECRET") || t.Contains("Jane.Doe", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(LogExport.ErrorProviders, source.Reads.Single(r => r.Log == "Application").Providers);
        Assert.False(File.Exists(zipPath + ".partial"));
    }

    [Fact]
    public void The_Application_log_can_be_read_without_admin_rights()
    {
        var events = new WindowsEventLogSource().Read("Application", DateTime.UtcNow.AddDays(-1), LogExport.ErrorProviders);

        Assert.All(events, e => Assert.Contains(e.Provider, LogExport.ErrorProviders));
    }
}
