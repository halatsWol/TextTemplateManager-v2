using System;
using System.IO;
using System.Threading.Tasks;
using TextTemplateManager.Common;
using TextTemplateManager.Data;
using TextTemplateManager.Helpers;
using TextTemplateManager.Models;

namespace TextTemplateManager.Tutorial;

/// <summary>The tour's own data: a store over a throwaway folder with sample templates, a sample synced folder and
/// a copy of the real settings (so the tour shows the user's own hotkey). The real store is never touched.</summary>
public sealed class TourSample : IDisposable
{
    private TourSample(string directory, DataNode store)
    {
        Directory = directory;
        Store = store;
    }

    public string Directory { get; }
    public DataNode Store { get; }

    public static async Task<TourSample> CreateAsync(AppSettings realSettings)
    {
        string dir = Path.Combine(Path.GetTempPath(), "TextTemplateManager-tour-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        var storage = new StorageService(dir, Path.Combine(dir, "Backups"), Path.Combine(dir, "no-1.x"));

        var settings = StorageService.ParseSettings(StorageService.SerializeSettings(realSettings)) ?? new AppSettings();
        await storage.SaveSettingsAsync(settings);
        await storage.SaveAsync(storage.GetDataPath(), LocalItems());

        string teamFile = Path.Combine(dir, "team.ttmdata");
        await storage.SaveSharedAsync(teamFile, TeamItems());
        await storage.SaveSyncSettingsAsync(new SyncSettings
        {
            Sources = { new SyncSource { Id = TeamId, Name = Loc.Tour.Get("SampleTeamName"), Path = teamFile, ShortcutPrefix = "team" } },
        });

        // Two backups for the Backups page: daily (yesterday) and recent (today).
        var now = DateTime.Now;
        foreach (var (tier, at) in new[] { (BackupTier.Daily, now.AddDays(-1).Date.AddHours(9)), (BackupTier.Recent, now.AddMinutes(-10)) })
            await new BackupService(storage, () => at).CreateAsync(tier, BackupSource.Live(storage), quietMs: 0);

        var store = new DataNode(storage);
        await store.InitializeAsync();
        return new TourSample(dir, store);
    }

    public static readonly Guid TeamId = new("7e4d0a63-5b1f-4c3e-9a77-2f1c6b8d9e01");
    public static readonly Guid ReleaseNotesId = new("7e4d0a63-5b1f-4c3e-9a77-2f1c6b8d9e02");

    private static Folder LocalItems() => new()
    {
        Id = Guid.Empty,
        Title = "Root",
        Children =
        {
            new Folder
            {
                Title = Loc.Tour.Get("SampleRepliesFolder"),
                Children =
                {
                    new Template
                    {
                        Title = Loc.Tour.Get("SampleThanksTitle"), TagsCsv = Loc.Tour.Get("SampleThanksTags"), SingleKeyShortcut = "T", MultiKeyShortcut = "THX",
                        Content = Loc.Tour.Get("SampleThanksContent"),
                    },
                    new Template
                    {
                        Id = ReleaseNotesId,
                        Title = Loc.Tour.Get("SampleReleaseNotesTitle"), TagsCsv = Loc.Tour.Get("SampleReleaseNotesTags"), MultiKeyShortcut = "REL", DefaultPasteMode = PasteMode.Jira,
                        Content = Loc.Tour.Get("SampleReleaseNotesContent"),
                    },
                },
            },
            new Folder
            {
                Title = Loc.Tour.Get("SampleCodeFolder"),
                Children =
                {
                    new Template
                    {
                        Title = Loc.Tour.Get("SampleServicesTitle"), TagsCsv = "powershell", SingleKeyShortcut = "P", MultiKeyShortcut = "SVC", DefaultPasteMode = PasteMode.Plaintext,
                        Content = "<pre><code class=\"language-powershell\">Get-Service | Where-Object Status -eq 'Running'</code></pre>",
                    },
                },
            },
            new Template
            {
                Title = Loc.Tour.Get("SampleSignatureTitle"), SingleKeyShortcut = "S", MultiKeyShortcut = "SIG",
                Content = Loc.Tour.Get("SampleSignatureContent"),
            },
        },
    };

    private static Folder TeamItems() => new()
    {
        Id = Guid.Empty,
        Title = "Root",
        Children =
        {
            new Template
            {
                Title = Loc.Tour.Get("SampleOutOfOfficeTitle"), MultiKeyShortcut = "OOO",
                Content = Loc.Tour.Get("SampleOutOfOfficeContent"),
            },
        },
    };

    public void Dispose()
    {
        try { System.IO.Directory.Delete(Directory, recursive: true); } catch { }
    }
}
