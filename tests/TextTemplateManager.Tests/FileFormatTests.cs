using System.Runtime.CompilerServices;
using TextTemplateManager.Common;
using TextTemplateManager.Data;
using TextTemplateManager.Models;
using Xunit;

namespace TextTemplateManager.Tests;

/// <summary>The files must stay byte-identical: 1.x clients share the sync files, and an unchanged tree
/// that serialises differently is rewritten (churn, cloud conflict copies). The expected files in
/// Fixtures/ were produced by the reflection-based serializer of the 1.9.2 code base.</summary>
public class FileFormatTests
{
    private readonly StorageService _storage = new(TestEnvironment.NewScratchDir());

    private static string Fixture(string name, [CallerFilePath] string here = "") =>
        Path.Combine(Path.GetDirectoryName(here)!, "Fixtures", name);

    internal static Folder SampleTree()
    {
        var root = new Folder { Id = new Guid("00000000-0000-0000-0000-000000000000"), Title = "Root", LastChange = "20260101120000" };
        var folder = new Folder
        {
            Id = new Guid("11111111-1111-1111-1111-111111111111"),
            ParentId = root.Id,
            SyncId = new Guid("99999999-9999-9999-9999-999999999999"),
            Title = "Folder <A> & \"B\"",
            LastChange = "20260101120001",
            IsExpanded = true,          // never persisted
            IsSyncRoot = true,          // never persisted
        };
        folder.Children.Add(new Template
        {
            Id = new Guid("22222222-2222-2222-2222-222222222222"),
            ParentId = folder.Id,
            Title = "Greeting",
            Content = "<p>Hello <strong>world</strong> äöü €</p>",
            SingleKeyShortcut = "g",
            MultiKeyShortcut = "msg",
            TagsCsv = "mail, hello",
            DefaultPasteMode = PasteMode.RTF,
            LastChange = "20260101120002",
            EffectiveMultiKey = "x-msg",   // never persisted
            SourceLabel = "local",         // never persisted
        });
        root.Children.Add(folder);
        root.Children.Add(new Template
        {
            Id = new Guid("33333333-3333-3333-3333-333333333333"),
            ParentId = root.Id,
            Title = "Plain",
            LastChange = "20260101120003",
        });
        return root;
    }

    [Fact]
    public async Task Data_file_format_is_unchanged()
    {
        string path = Path.Combine(TestEnvironment.NewScratchDir(), "data.ttmdata");
        await _storage.SaveAsync(path, SampleTree());

        Assert.Equal(await File.ReadAllTextAsync(Fixture("format-data.ttmdata")), await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task Settings_file_format_is_unchanged()
    {
        await _storage.SaveSettingsAsync(new AppSettings
        {
            DefaultPasteMode = PasteMode.Jira,
            PasteWindowHotkey = "Ctrl+Alt+Q",
            HideCrossAreaShortcutWarnings = true,
            BrowserConnectorEnabled = true,
            BrowserConnectorPort = 47700,
            BrowserConnectorToken = "token",
        });

        Assert.Equal(await File.ReadAllTextAsync(Fixture("format-settings.ttmsettings")), await File.ReadAllTextAsync(_storage.GetSettingsPath()));
    }

    [Fact]
    public async Task Sync_settings_file_format_is_unchanged()
    {
        var sync = new SyncSettings { Separator = "." };
        sync.Sources.Add(new SyncSource
        {
            Id = new Guid("44444444-4444-4444-4444-444444444444"),
            Name = "Team",
            Path = @"C:\Shared\team.ttmdata",
            IsActive = true,
            AllowSave = true,
            ShortcutPrefix = "tm",
            CanMoveUp = false,   // never persisted
        });
        await _storage.SaveSyncSettingsAsync(sync);

        Assert.Equal(await File.ReadAllTextAsync(Fixture("format-sync.ttmsettings")), await File.ReadAllTextAsync(_storage.GetSyncSettingsPath()));
    }

    [Fact]
    public async Task Data_file_round_trips_to_the_same_bytes()
    {
        string first = Path.Combine(TestEnvironment.NewScratchDir(), "a.ttmdata");
        string second = Path.Combine(TestEnvironment.NewScratchDir(), "b.ttmdata");
        await _storage.SaveAsync(first, SampleTree());

        await _storage.SaveAsync(second, (await StorageService.LoadRootAsync(first))!);

        Assert.Equal(await File.ReadAllTextAsync(first), await File.ReadAllTextAsync(second));
    }
}
