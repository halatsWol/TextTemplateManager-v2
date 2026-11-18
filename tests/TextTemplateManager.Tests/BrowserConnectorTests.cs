using System.Net;
using System.Net.Sockets;
using System.Text;
using TextTemplateManager.Services.Platform;
using Xunit;

namespace TextTemplateManager.Tests;

/// <summary>The loopback connector over real HTTP: the token gate, CORS for extension origins only, the
/// endpoints, creating a template, and start/stop on the same port.</summary>
public sealed class BrowserConnectorTests : IDisposable
{
    private const string Token = "secret-token";

    private sealed class FakeData : IConnectorDataSource
    {
        public List<(string Content, string? Name)> Created { get; } = new();
        public List<NewTemplateDto> CreatedFull { get; } = new();
        public List<(string Id, string? Language, string? LanguageId, string Content)> Written { get; } = new();
        public string AppVersion => "9.9.9";
        public IReadOnlyList<PasteModeDto> PasteModes() => new[] { new PasteModeDto("Auto", "Auto") };
        public IReadOnlyList<ConnectorNodeDto> Tree() =>
            new[]
            {
                new ConnectorNodeDto("1", "Hello", "template", "Auto", "local", null),
                new ConnectorNodeDto("f", "Folder", "folder", null, "local",
                    [new ConnectorNodeDto("7", "Offer", "template", "Auto", "local", null, "en", [new("a1a1", "en"), new("b2b2", "de")])]),
            };
        public TemplateContentDto? Template(string id, string? mode, string? language = null, string? languageId = null) => id switch
        {
            "1" => new TemplateContentDto("1", "Hello", mode ?? "Auto", "text/html", "<p>hi</p>"),
            "7" when language == "de" || languageId == "b2b2" => new TemplateContentDto("7", "Offer", mode ?? "Auto", "text/html", "<p>Hallo</p>", "de", "b2b2"),
            "7" when language == null && languageId == null => new TemplateContentDto("7", "Offer", mode ?? "Auto", "text/html", "<p>Hello</p>", "en", "a1a1"),
            "7" => throw new ConnectorRefusal(404, "language not found"),
            _ => null,
        };
        public Task<CreatedTemplateDto> CreateTemplateAsync(NewTemplateDto template)
        {
            Created.Add((template.Content, template.Name));
            CreatedFull.Add(template);
            return Task.FromResult(new CreatedTemplateDto("new-id", template.Name ?? "New Template",
                template.Languages is { Count: > 0 } ? [new("a1a1", template.Language!), new("b2b2", template.Languages[0].Language!)] : null));
        }
        public Task<LanguageWriteDto> SetLanguageAsync(string id, string? language, string? languageId, string content)
        {
            if (id != "7") throw new ConnectorRefusal(404, "not found");
            Written.Add((id, language, languageId, content));
            return Task.FromResult(new LanguageWriteDto(id, languageId ?? "c3c3", language ?? "de", languageId == null));
        }
    }

    private readonly FakeData _data = new();
    private readonly BrowserConnector _connector;
    private readonly int _port = FreePort();
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public BrowserConnectorTests()
    {
        _connector = new BrowserConnector(_data);
        _connector.Start(_port, Token);
    }

    public void Dispose()
    {
        _connector.Dispose();
        _http.Dispose();
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, string? token = Token, string? origin = null, string? body = null,
        string? protocol = null)
    {
        var req = new HttpRequestMessage(method, $"http://127.0.0.1:{_port}{path}");
        if (token != null) req.Headers.TryAddWithoutValidation(BrowserConnector.TokenHeader, token);
        if (protocol != null) req.Headers.TryAddWithoutValidation(BrowserConnector.ProtocolHeader, protocol);
        if (origin != null) req.Headers.TryAddWithoutValidation("Origin", origin);
        if (body != null) req.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return await _http.SendAsync(req);
    }

    [Fact]
    public async Task Ping_answers_with_the_version_and_protocol()
    {
        var res = await Send(HttpMethod.Get, "/ping");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        string json = await res.Content.ReadAsStringAsync();
        Assert.Contains("\"version\":\"9.9.9\"", json);
        Assert.Contains($"\"protocol\":{BrowserConnector.ProtocolVersion}", json);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-token")]
    [InlineData("secret-toke")]
    [InlineData("secret-token-")]
    public async Task A_missing_or_wrong_token_is_unauthorized(string? token) =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(HttpMethod.Get, "/ping", token)).StatusCode);

    [Fact]
    public async Task A_website_origin_is_forbidden_and_gets_no_cors()
    {
        var res = await Send(HttpMethod.Get, "/tree", origin: "https://example.com");

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.False(res.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task An_extension_origin_gets_cors_and_a_preflight_answer()
    {
        const string origin = "chrome-extension://abcdef";

        var preflight = await Send(HttpMethod.Options, "/tree", token: null, origin: origin);
        var res = await Send(HttpMethod.Get, "/tree", origin: origin);

        Assert.Equal(HttpStatusCode.NoContent, preflight.StatusCode);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(origin, Assert.Single(res.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.Contains("\"name\":\"Hello\"", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_template_is_served_by_id()
    {
        Assert.Contains("<p>hi</p>", await (await Send(HttpMethod.Get, "/template?id=1&mode=HTML")).Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Get, "/template?id=2")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Get, "/template")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Get, "/nothing")).StatusCode);
    }

    [Fact]
    public async Task While_busy_every_authorized_request_is_turned_away_and_nothing_is_created()
    {
        _connector.Busy = true;

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Send(HttpMethod.Get, "/tree")).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Send(HttpMethod.Post, "/template", body: "{\"content\":\"x\"}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(HttpMethod.Get, "/tree", token: "wrong")).StatusCode);
        Assert.Empty(_data.Created);

        _connector.Busy = false;
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Get, "/tree")).StatusCode);
    }

    [Fact]
    public async Task Posting_content_creates_a_template()
    {
        var res = await Send(HttpMethod.Post, "/template", body: "{\"content\":\"<p>Grüße</p>\",\"name\":\"Clip\"}");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("\"id\":\"new-id\"", await res.Content.ReadAsStringAsync());
        Assert.Equal(("<p>Grüße</p>", "Clip"), Assert.Single(_data.Created));
    }

    [Fact]
    public async Task A_create_without_content_or_with_broken_json_is_refused()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Post, "/template", body: "{\"name\":\"x\"}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Post, "/template", body: "{nope")).StatusCode);
        Assert.Empty(_data.Created);
    }

    private async Task<string> Text(HttpResponseMessage res) => await res.Content.ReadAsStringAsync();

    [Theory]
    [InlineData(null, 2)]
    [InlineData("2", 2)]
    [InlineData("1", 2)]
    [InlineData("abc", 2)]
    [InlineData("3", 3)]
    [InlineData("7", 3)]
    public async Task Ping_answers_with_the_protocol_the_client_asked_for_and_v2_otherwise(string? asked, int expected) =>
        Assert.Contains($"\"protocol\":{expected}", await Text(await Send(HttpMethod.Get, "/ping", protocol: asked)));

    [Fact]
    public async Task A_v2_client_gets_the_v2_replies_without_languages()
    {
        string tree = await Text(await Send(HttpMethod.Get, "/tree"));
        string template = await Text(await Send(HttpMethod.Get, "/template?id=7&lang=de"));
        var create = await Send(HttpMethod.Post, "/template", body: "{\"content\":\"x\",\"language\":\"en\",\"languages\":[{\"language\":\"de\",\"content\":\"y\"}]}");
        var language = await Send(HttpMethod.Post, "/template/language", body: "{\"id\":\"7\",\"language\":\"fr\",\"content\":\"z\"}");

        Assert.Equal("[{\"id\":\"1\",\"name\":\"Hello\",\"type\":\"template\",\"defaultMode\":\"Auto\",\"source\":\"local\"},"
            + "{\"id\":\"f\",\"name\":\"Folder\",\"type\":\"folder\",\"source\":\"local\",\"children\":"
            + "[{\"id\":\"7\",\"name\":\"Offer\",\"type\":\"template\",\"defaultMode\":\"Auto\",\"source\":\"local\"}]}]", tree);
        Assert.Equal("{\"id\":\"7\",\"name\":\"Offer\",\"mode\":\"Auto\",\"contentType\":\"text/html\",\"content\":\"<p>Hello</p>\"}", template);
        Assert.Equal("{\"id\":\"new-id\",\"name\":\"New Template\"}", await Text(create));
        Assert.Null(Assert.Single(_data.CreatedFull).Language);
        Assert.Null(_data.CreatedFull[0].Languages);
        Assert.Equal(HttpStatusCode.NotFound, language.StatusCode);
        Assert.Empty(_data.Written);
    }

    [Fact]
    public async Task A_v3_client_gets_the_languages_and_can_ask_for_one()
    {
        string tree = await Text(await Send(HttpMethod.Get, "/tree", protocol: "3"));
        string byCode = await Text(await Send(HttpMethod.Get, "/template?id=7&lang=de", protocol: "3"));
        string byId = await Text(await Send(HttpMethod.Get, "/template?id=7&langId=b2b2&mode=HTML", protocol: "3"));
        var missing = await Send(HttpMethod.Get, "/template?id=7&lang=fr", protocol: "3");

        Assert.Contains("\"language\":\"en\",\"languages\":[{\"id\":\"a1a1\",\"language\":\"en\"},{\"id\":\"b2b2\",\"language\":\"de\"}]", tree);
        Assert.Contains("\"content\":\"<p>Hallo</p>\",\"language\":\"de\",\"languageId\":\"b2b2\"", byCode);
        Assert.Contains("\"mode\":\"HTML\"", byId);
        Assert.Contains("<p>Hallo</p>", byId);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("{\"error\":\"language not found\"}", await Text(missing));
    }

    [Fact]
    public async Task A_v3_create_passes_the_languages_on()
    {
        var res = await Send(HttpMethod.Post, "/template", protocol: "3",
            body: "{\"content\":\"<p>Hello</p>\",\"language\":\"en\",\"languages\":[{\"language\":\"de\",\"content\":\"<p>Hallo</p>\"}]}");

        Assert.Contains("\"languages\":[{\"id\":\"a1a1\",\"language\":\"en\"},{\"id\":\"b2b2\",\"language\":\"de\"}]", await Text(res));
        var created = Assert.Single(_data.CreatedFull);
        Assert.Equal("en", created.Language);
        Assert.Equal(new NewLanguageDto("de", "<p>Hallo</p>"), Assert.Single(created.Languages!));
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Post, "/template", protocol: "3", body: "{\"content\":\"x\",\"languages\":5}")).StatusCode);
    }

    [Fact]
    public async Task A_v3_language_write_is_passed_on_and_refusals_come_back_as_their_status()
    {
        var added = await Send(HttpMethod.Post, "/template/language", protocol: "3", body: "{\"id\":\"7\",\"language\":\"fr\",\"content\":\"<p>Bonjour</p>\"}");
        var unknown = await Send(HttpMethod.Post, "/template/language", protocol: "3", body: "{\"id\":\"9\",\"language\":\"fr\",\"content\":\"x\"}");

        Assert.Equal("{\"id\":\"7\",\"languageId\":\"c3c3\",\"language\":\"fr\",\"added\":true}", await Text(added));
        Assert.Equal(("7", "fr", (string?)null, "<p>Bonjour</p>"), Assert.Single(_data.Written));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("{\"error\":\"not found\"}", await Text(unknown));
    }

    [Theory]
    [InlineData("{\"language\":\"fr\",\"content\":\"x\"}")]
    [InlineData("{\"id\":\"7\",\"content\":\"x\"}")]
    [InlineData("{\"id\":\"7\",\"language\":\"fr\",\"langId\":\"b2b2\",\"content\":\"x\"}")]
    [InlineData("{\"id\":\"7\",\"language\":\"fr\"}")]
    [InlineData("{nope")]
    public async Task A_language_write_without_id_one_language_and_content_is_refused(string body)
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Post, "/template/language", protocol: "3", body: body)).StatusCode);
        Assert.Empty(_data.Written);
    }

    [Fact]
    public async Task The_protocol_header_is_allowed_for_extension_origins()
    {
        var preflight = await Send(HttpMethod.Options, "/tree", token: null, origin: "chrome-extension://abcdef");

        Assert.Contains(BrowserConnector.ProtocolHeader, Assert.Single(preflight.Headers.GetValues("Access-Control-Allow-Headers")));
    }

    [Fact]
    public async Task A_client_that_sends_nothing_is_dropped_after_the_timeout()
    {
        int port = FreePort();
        using var connector = new BrowserConnector(_data, TimeSpan.FromMilliseconds(300));
        connector.Start(port, Token);
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);

        var read = client.GetStream().ReadAsync(new byte[16]).AsTask();
        var finished = await Task.WhenAny(read, Task.Delay(5000));

        Assert.Same(read, finished);
        Assert.Equal(0, await read);   // closed by the connector, nothing sent
    }

    [Fact]
    public async Task Stopping_closes_the_port_and_starting_again_serves_on_it()
    {
        _connector.Stop();
        await Task.Delay(200);
        await Assert.ThrowsAsync<HttpRequestException>(() => Send(HttpMethod.Get, "/ping"));

        _connector.Start(_port, Token);
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Get, "/ping")).StatusCode);
    }

    [Fact]
    public void A_port_in_use_fails_the_start_and_leaves_the_connector_stopped()
    {
        using var other = new BrowserConnector(_data);

        Assert.ThrowsAny<SocketException>(() => other.Start(_port, Token));
        Assert.False(other.IsRunning);
    }
}
