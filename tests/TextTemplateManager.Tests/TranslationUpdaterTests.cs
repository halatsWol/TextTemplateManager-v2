using System.Text.Json;
using TextTemplateManager.Models;
using TextTemplateManager.Services.Translation;
using Xunit;

namespace TextTemplateManager.Tests;

/// <summary>Settings ▸ Translation's daily update check: when it runs, what it updates, and that it stays offline when off.</summary>
public class TranslationUpdaterTests
{
    private static readonly DateTime Now = new(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);

    private static ModelFile File(string type, string hash) => new(type, $"{type}.bin", $"loc/{type}", "h", 10, hash, 20);

    private static ModelPair Pair(string from, string to, string version, string hash) =>
        new(from, to, version, "base-memory", [File("lex", hash), File("model", hash), File("vocab", hash)]);

    // An installed direction as ModelStore writes it: the files and a model.json with their hashes.
    private static void Install(string dir, ModelPair pair)
    {
        string folder = Path.Combine(dir, $"{pair.From}-{pair.To}");
        Directory.CreateDirectory(folder);
        foreach (var f in pair.Files) System.IO.File.WriteAllText(Path.Combine(folder, f.Name), "x");
        var info = new ModelInfo(pair.From, pair.To, "model.bin", pair.Version, pair.Architecture,
            pair.Files.ToDictionary(f => f.Type, f => f.Name), pair.Files.ToDictionary(f => f.Type, f => f.DecompressedHash));
        System.IO.File.WriteAllText(Path.Combine(folder, ModelStore.InfoFileName),
            JsonSerializer.Serialize(info, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }

    private sealed class Setup
    {
        public AppSettings Settings { get; } = new() { TranslationAutoCheck = true };
        public List<ModelPair> Catalog { get; } = new();
        public List<ModelPair> InstallCalls { get; } = new();
        public List<string> Notices { get; } = new();
        public int CatalogLoads { get; private set; }
        public bool CatalogFails { get; set; }
        public ModelStore Store { get; }
        public TranslationUpdater Updater { get; }

        public Setup(string dir)
        {
            Store = new ModelStore(dir);
            Updater = new TranslationUpdater(Settings, Store,
                _ => { CatalogLoads++; return CatalogFails ? throw new HttpRequestException("offline") : Task.FromResult<IReadOnlyList<ModelPair>>(Catalog); },
                (pair, _) => { InstallCalls.Add(pair); return Task.CompletedTask; },
                () => Now, (title, message) => Notices.Add(message));
        }
    }

    [Fact]
    public void A_check_is_due_after_a_day_or_without_one()
    {
        Assert.True(TranslationUpdater.IsDue(null, Now));
        Assert.False(TranslationUpdater.IsDue(Now.AddHours(-23), Now));
        Assert.True(TranslationUpdater.IsDue(Now.AddHours(-24), Now));
        Assert.True(TranslationUpdater.IsDue(Now.AddDays(2), Now));   // a clock set back
    }

    [Fact]
    public async Task Nothing_goes_online_while_the_option_is_off()
    {
        var s = new Setup(TestEnvironment.NewScratchDir());
        Install(s.Store.Directory, Pair("en", "de", "3.0", "a"));
        s.Settings.TranslationAutoCheck = false;

        Assert.Null(await s.Updater.TickAsync());
        Assert.Equal(0, s.CatalogLoads);
    }

    [Fact]
    public async Task Without_installed_languages_there_is_nothing_to_check()
    {
        var s = new Setup(TestEnvironment.NewScratchDir());

        Assert.Null(await s.Updater.TickAsync());
        Assert.Equal(0, s.CatalogLoads);
    }

    [Fact]
    public async Task A_newer_version_is_announced_without_automatic_install()
    {
        var s = new Setup(TestEnvironment.NewScratchDir());
        Install(s.Store.Directory, Pair("en", "de", "3.0", "a"));
        s.Catalog.Add(Pair("en", "de", "3.1", "b"));
        s.Catalog.Add(Pair("en", "fr", "3.1", "c"));   // not installed: no update

        var result = await s.Updater.TickAsync();

        Assert.Equal(new[] { "en-de" }, result!.Outdated.Select(p => p.Key));
        Assert.Empty(s.InstallCalls);
        Assert.Contains("German", Assert.Single(s.Notices));
        Assert.Equal(Now, TranslationUpdater.ParseLastCheck(s.Settings.TranslationLastCheck));
    }

    [Fact]
    public async Task With_automatic_install_the_newer_version_is_installed()
    {
        var s = new Setup(TestEnvironment.NewScratchDir());
        s.Settings.TranslationAutoInstall = true;
        Install(s.Store.Directory, Pair("en", "de", "3.0", "a"));
        Install(s.Store.Directory, Pair("de", "en", "3.0", "a"));
        s.Catalog.Add(Pair("en", "de", "3.1", "b"));
        s.Catalog.Add(Pair("de", "en", "3.0", "a"));   // current

        var result = await s.Updater.TickAsync();

        Assert.Equal(new[] { "en-de" }, s.InstallCalls.Select(p => p.Key));
        Assert.Equal(new[] { "en-de" }, result!.Installed.Select(p => p.Key));
        Assert.Empty(s.Notices);
    }

    [Fact]
    public async Task Up_to_date_languages_leave_nothing_to_do()
    {
        var s = new Setup(TestEnvironment.NewScratchDir());
        Install(s.Store.Directory, Pair("en", "de", "3.0", "a"));
        s.Catalog.Add(Pair("en", "de", "3.0", "a"));

        var result = await s.Updater.TickAsync();

        Assert.Empty(result!.Outdated);
        Assert.Empty(s.Notices);
    }

    [Fact]
    public async Task A_check_runs_once_a_day()
    {
        var s = new Setup(TestEnvironment.NewScratchDir());
        Install(s.Store.Directory, Pair("en", "de", "3.0", "a"));

        await s.Updater.TickAsync();
        await s.Updater.TickAsync();

        Assert.Equal(1, s.CatalogLoads);
    }

    [Fact]
    public async Task An_unreachable_server_is_tried_again_at_the_next_tick()
    {
        var s = new Setup(TestEnvironment.NewScratchDir());
        Install(s.Store.Directory, Pair("en", "de", "3.0", "a"));
        s.CatalogFails = true;

        var result = await s.Updater.TickAsync();

        Assert.Equal("offline", result!.Problem);
        Assert.Null(s.Settings.TranslationLastCheck);
        s.CatalogFails = false;
        await s.Updater.TickAsync();
        Assert.Equal(2, s.CatalogLoads);
    }
}
