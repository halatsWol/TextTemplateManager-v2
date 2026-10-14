using System;
using System.IO;
using System.Threading.Tasks;
using TextTemplateManager.Common;
using TextTemplateManager.Data;
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
            Sources = { new SyncSource { Id = TeamId, Name = "Team (sample)", Path = teamFile, ShortcutPrefix = "team" } },
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
                Title = "Customer replies",
                Children =
                {
                    new Template
                    {
                        Title = "Thanks for your message", TagsCsv = "support, reply", SingleKeyShortcut = "T", MultiKeyShortcut = "THX",
                        Content = "<p>Hello,</p><p>thank you for your message. We will get back to you <strong>within one working day</strong>.</p><p>Kind regards</p>",
                    },
                    new Template
                    {
                        Id = ReleaseNotesId,
                        Title = "Release notes", TagsCsv = "release", MultiKeyShortcut = "REL", DefaultPasteMode = PasteMode.Jira,
                        Content = "<h2>What's new</h2><div class=\"ttm-panel ak-editor-panel\" data-panel-type=\"info\"><p>This update is installed automatically.</p></div>"
                                + "<ul><li><p>Faster search</p></li><li><p>New export options</p></li></ul>",
                    },
                },
            },
            new Folder
            {
                Title = "Code snippets",
                Children =
                {
                    new Template
                    {
                        Title = "List running services", TagsCsv = "powershell", SingleKeyShortcut = "P", MultiKeyShortcut = "SVC", DefaultPasteMode = PasteMode.Plaintext,
                        Content = "<pre><code class=\"language-powershell\">Get-Service | Where-Object Status -eq 'Running'</code></pre>",
                    },
                },
            },
            new Template
            {
                Title = "Signature", SingleKeyShortcut = "S", MultiKeyShortcut = "SIG",
                Content = "<p>Best regards<br>Alex Example</p>",
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
                Title = "Out of office", MultiKeyShortcut = "OOO",
                Content = "<p>I'm out of the office until Monday and will reply then.</p>",
            },
        },
    };

    public void Dispose()
    {
        try { System.IO.Directory.Delete(Directory, recursive: true); } catch { }
    }
}
