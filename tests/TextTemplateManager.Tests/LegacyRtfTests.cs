using TextTemplateManager.Data;
using TextTemplateManager.Models;
using Xunit;

namespace TextTemplateManager.Tests;

/// <summary>Templates stored as RTF by early versions are HTML from the moment a file is read, whichever
/// way it is read (own data file, backup or import, sync file), and reading alone changes nothing on disk.</summary>
public class LegacyRtfTests
{
    private const string Rtf = @"{\rtf1\ansi\deff0{\fonttbl{\f0 Arial;}}\f0 Hello \b world\b0\par}";

    private static string WriteFile(string content)
    {
        string path = Path.Combine(TestEnvironment.NewScratchDir(), "data.ttmdata");
        var json = System.Text.Json.JsonSerializer.Serialize(content);
        File.WriteAllText(path,
            "{\"Id\":\"00000000-0000-0000-0000-000000000000\",\"Title\":\"Root\",\"LastChange\":\"20200101000000\",\"Children\":[" +
            "{\"$type\":\"folder\",\"Title\":\"Old\",\"Id\":\"aaaaaaaa-0000-0000-0000-000000000000\",\"LastChange\":\"20200101000000\",\"Children\":[" +
            "{\"$type\":\"template\",\"Title\":\"Letter\",\"Content\":" + json + ",\"Id\":\"11111111-1111-1111-1111-111111111111\",\"LastChange\":\"20200101000000\",\"Children\":[]}]}]}");
        return path;
    }

    private static Template Letter(Folder? root) =>
        Assert.IsType<Template>(Assert.Single(Assert.IsType<Folder>(Assert.Single(root!.Children)).Children));

    private static void AssertConverted(Template t)
    {
        Assert.DoesNotContain(@"\rtf1", t.Content);
        Assert.Contains("Hello", t.Content);
        Assert.Contains("<strong>world</strong>", t.Content);
        Assert.Equal("20200101000000", t.LastChange);
    }

    [Fact]
    public async Task The_own_data_file_loads_as_html()
    {
        string path = WriteFile(Rtf);

        AssertConverted(Letter(await new StorageService(TestEnvironment.NewScratchDir()).LoadOwnFileAsync<Folder>(path)));
        Assert.Contains(@"\rtf1", File.ReadAllText(path));
    }

    [Fact]
    public async Task A_backup_or_import_loads_as_html() =>
        AssertConverted(Letter(await StorageService.LoadRootAsync(WriteFile(Rtf))));

    [Fact]
    public async Task A_sync_file_loads_as_html() =>
        AssertConverted(Letter(await StorageService.LoadRootSharedAsync(WriteFile(Rtf))));

    [Fact]
    public async Task Html_content_is_left_alone()
    {
        var t = Letter(await StorageService.LoadRootAsync(WriteFile("<p>{\\rtf1 is just text here}</p>")));

        Assert.Equal("<p>{\\rtf1 is just text here}</p>", t.Content);
    }

    [Fact]
    public async Task Loading_does_not_save_and_the_next_save_writes_html()
    {
        var storage = new StorageService(TestEnvironment.NewScratchDir());
        File.Copy(WriteFile(Rtf), storage.GetDataPath());
        var node = new DataNode(storage);

        await node.InitializeAsync();
        await Task.Delay(1500);   // longer than the edit-save debounce
        Assert.Contains(@"\rtf1", File.ReadAllText(storage.GetDataPath()));

        await node.AddItemAsync(new Template { Title = "New" });
        string saved = File.ReadAllText(storage.GetDataPath());
        Assert.DoesNotContain(@"\\rtf1", saved);
        Assert.Contains("world", saved);
    }
}
