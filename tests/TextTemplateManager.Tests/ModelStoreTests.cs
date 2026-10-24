using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TextTemplateManager.Services.Translation;
using Xunit;

namespace TextTemplateManager.Tests;

/// <summary>Installing translation models: Mozilla's files arrive zstd-compressed, are checked against both hashes,
/// and only a complete folder takes the place of an installed one. Export and import carry them to PCs offline.</summary>
public class ModelStoreTests
{
    private const string Base = "https://models.test/";

    private sealed class FakeServer : HttpMessageHandler
    {
        public Dictionary<string, byte[]> Files { get; } = new();
        public List<string> Requests { get; } = new();
        public TaskCompletionSource? Hold { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString();
            Requests.Add(url);
            if (Hold != null) await Hold.Task;
            return Files.TryGetValue(url, out var body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) }
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    private static string Sha(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    // A pair whose files the server holds compressed, as Mozilla publishes them.
    private static ModelPair Publish(FakeServer server, string from, string to, string version = "3.0", string content = "v1")
    {
        var files = new List<ModelFile>();
        foreach (var type in new[] { "lex", "model", "vocab" })
        {
            string name = type == "model" ? $"model.{from}{to}.intgemm.alphas.bin" : $"{type}.{from}{to}.bin";
            byte[] raw = Encoding.UTF8.GetBytes($"{type} {from}-{to} {content}");
            byte[] packed = new ZstdSharp.Compressor().Wrap(raw).ToArray();
            string location = $"main/{from}{to}-{type}-{content}.zst";
            server.Files[Base + location] = packed;
            files.Add(new ModelFile(type, name, location, Sha(packed).ToUpperInvariant(), packed.Length, Sha(raw), raw.Length));
        }
        return new ModelPair(from, to, version, "base-memory", files);
    }

    private static (ModelStore Store, FakeServer Server, HttpClient Http) Setup([System.Runtime.CompilerServices.CallerMemberName] string name = "")
    {
        var server = new FakeServer();
        return (new ModelStore(Path.Combine(TestEnvironment.NewScratchDir(name), "TranslationModels"), Base), server, new HttpClient(server));
    }

    [Fact]
    public async Task An_installed_pair_has_its_unpacked_files_and_a_model_json_the_translator_reads()
    {
        var (store, server, http) = Setup();
        var pair = Publish(server, "en", "de");
        long reported = 0;

        await store.InstallAsync(http, pair, new SyncProgress(n => reported = n));

        string dir = Path.Combine(store.Directory, "en-de");
        Assert.Equal("model en-de v1", File.ReadAllText(Path.Combine(dir, "model.ende.intgemm.alphas.bin")));
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, ModelStore.InfoFileName)));
        Assert.Equal("model.ende.intgemm.alphas.bin", json.RootElement.GetProperty("name").GetString());
        Assert.Equal("lex.ende.bin", json.RootElement.GetProperty("files").GetProperty("lex").GetString());
        Assert.Equal(pair.DownloadSize, reported);
        Assert.Equal(new[] { "en-de" }, Directory.GetFileSystemEntries(store.Directory).Select(Path.GetFileName));

        var installed = Assert.Single(store.Installed());
        Assert.Equal(("en", "de"), (installed.From, installed.To));
        Assert.True(installed.Matches(pair));
        Assert.Equal(pair.DiskSize, installed.Size);
    }

    [Fact]
    public async Task A_file_with_a_wrong_hash_installs_nothing()
    {
        var (store, server, http) = Setup();
        var pair = Publish(server, "en", "de");
        server.Files[Base + pair.Files[1].Location] = [1, 2, 3];

        await Assert.ThrowsAsync<InvalidDataException>(() => store.InstallAsync(http, pair));

        Assert.Empty(store.Installed());
        Assert.Empty(Directory.GetFileSystemEntries(store.Directory));
    }

    [Fact]
    public async Task A_failed_update_keeps_the_installed_version()
    {
        var (store, server, http) = Setup();
        await store.InstallAsync(http, Publish(server, "en", "de"));
        var newer = Publish(server, "en", "de", "3.1", "v2");
        server.Files.Remove(Base + newer.Files[2].Location);

        await Assert.ThrowsAsync<HttpRequestException>(() => store.InstallAsync(http, newer));

        var installed = Assert.Single(store.Installed());
        Assert.Equal("3.0", installed.Info.Version);
        Assert.False(installed.Matches(newer));
    }

    [Fact]
    public async Task An_update_replaces_the_installed_files()
    {
        var (store, server, http) = Setup();
        await store.InstallAsync(http, Publish(server, "en", "de"));
        var newer = Publish(server, "en", "de", "3.1", "v2");

        await store.InstallAsync(http, newer);

        var installed = Assert.Single(store.Installed());
        Assert.True(installed.Matches(newer));
        Assert.Equal("model en-de v2", File.ReadAllText(Path.Combine(installed.Directory, "model.ende.intgemm.alphas.bin")));
    }

    [Fact]
    public async Task A_cancelled_download_leaves_nothing_behind()
    {
        var (store, server, http) = Setup();
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.InstallAsync(http, Publish(server, "en", "de"), cancel: cancel.Token));

        Assert.Empty(Directory.GetFileSystemEntries(store.Directory));
    }

    // Exit asks while a download runs (only this class installs, and its tests run one after the other).
    [Fact]
    public async Task The_app_knows_while_a_download_runs()
    {
        var (store, server, http) = Setup();
        server.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var install = store.InstallAsync(http, Publish(server, "en", "de"));

        Assert.True(ModelStore.Busy);
        Assert.False(ModelStore.WhenIdle.IsCompleted);
        Assert.Equal(0, ModelStore.Progress);
        server.Hold.SetResult();
        await install;
        await ModelStore.WhenIdle.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(ModelStore.Busy);
        Assert.Equal(1, ModelStore.Progress);
    }

    [Fact]
    public async Task A_failed_download_still_lets_the_progress_reach_the_end()
    {
        var (store, server, http) = Setup();
        var pair = Publish(server, "en", "de");
        server.Files.Remove(Base + pair.Files[2].Location);

        await Assert.ThrowsAsync<HttpRequestException>(() => store.InstallAsync(http, pair));

        Assert.Equal(1, ModelStore.Progress);
    }

    [Fact]
    public async Task Remove_deletes_the_pair()
    {
        var (store, server, http) = Setup();
        await store.InstallAsync(http, Publish(server, "en", "de"));
        await store.InstallAsync(http, Publish(server, "de", "en"));

        store.Remove("en", "de");

        Assert.Equal(("de", "en"), (store.Installed().Single().From, store.Installed().Single().To));
    }

    [Fact]
    public async Task A_folder_with_a_missing_file_is_not_installed()
    {
        var (store, server, http) = Setup();
        await store.InstallAsync(http, Publish(server, "en", "de"));
        File.Delete(Path.Combine(store.Directory, "en-de", "lex.ende.bin"));

        Assert.Empty(store.Installed());
    }

    [Fact]
    public async Task Export_and_import_carry_the_pairs_to_another_pc()
    {
        var (store, server, http) = Setup();
        await store.InstallAsync(http, Publish(server, "en", "zh-Hans"));
        await store.InstallAsync(http, Publish(server, "zh-Hans", "en"));
        string zip = Path.Combine(TestEnvironment.NewScratchDir(), "languages.zip");
        store.Export(store.Installed(), zip);
        var other = new ModelStore(Path.Combine(TestEnvironment.NewScratchDir(), "TranslationModels"), Base);

        var imported = other.Import(zip);

        Assert.Equal(2, imported.Count);
        Assert.Equal(new[] { "en-zh-Hans", "zh-Hans-en" }, other.Installed().Select(p => Path.GetFileName(p.Directory)));
        Assert.Equal("model en-zh-Hans v1", File.ReadAllText(Path.Combine(other.Directory, "en-zh-Hans", "model.enzh-Hans.intgemm.alphas.bin")));
    }

    [Fact]
    public async Task A_damaged_file_in_an_import_is_refused()
    {
        var (store, server, http) = Setup();
        await store.InstallAsync(http, Publish(server, "en", "de"));
        File.WriteAllText(Path.Combine(store.Directory, "en-de", "vocab.ende.bin"), "changed");
        string zip = Path.Combine(TestEnvironment.NewScratchDir(), "languages.zip");
        store.Export(store.Installed(), zip);
        var other = new ModelStore(Path.Combine(TestEnvironment.NewScratchDir(), "TranslationModels"), Base);

        Assert.Throws<InvalidDataException>(() => other.Import(zip));

        Assert.Empty(other.Installed());
    }

    [Fact]
    public void A_zip_without_models_is_refused()
    {
        string zip = Path.Combine(TestEnvironment.NewScratchDir(), "other.zip");
        using (var archive = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Create))
            archive.CreateEntry("data.ttmdata");
        var store = new ModelStore(Path.Combine(TestEnvironment.NewScratchDir(), "TranslationModels"), Base);

        Assert.Throws<InvalidDataException>(() => store.Import(zip));
    }

    [Fact]
    public void A_model_json_naming_a_path_outside_its_folder_is_refused()
    {
        string zip = Path.Combine(TestEnvironment.NewScratchDir(), "evil.zip");
        using (var archive = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Create))
        {
            using (var w = new StreamWriter(archive.CreateEntry("en-de/model.json").Open()))
                w.Write("""{"from":"en","to":"de","name":"m","version":"3.0","architecture":"x","files":{"model":"../../x.bin"},"hashes":{"model":"00"}}""");
            archive.CreateEntry("x.bin");
        }
        var store = new ModelStore(Path.Combine(TestEnvironment.NewScratchDir(), "TranslationModels"), Base);

        Assert.Throws<InvalidDataException>(() => store.Import(zip));
        Assert.Empty(store.Installed());
    }

    private sealed class SyncProgress(Action<long> report) : IProgress<long>
    {
        public void Report(long value) => report(value);
    }
}
