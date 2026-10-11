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
        public string AppVersion => "9.9.9";
        public IReadOnlyList<PasteModeDto> PasteModes() => new[] { new PasteModeDto("Auto", "Auto") };
        public IReadOnlyList<ConnectorNodeDto> Tree() =>
            new[] { new ConnectorNodeDto("1", "Hello", "template", "Auto", "local", null) };
        public TemplateContentDto? Template(string id, string? mode) =>
            id == "1" ? new TemplateContentDto("1", "Hello", mode ?? "Auto", "text/html", "<p>hi</p>") : null;
        public Task<CreatedTemplateDto> CreateTemplateAsync(string content, string? name)
        {
            Created.Add((content, name));
            return Task.FromResult(new CreatedTemplateDto("new-id", name ?? "New Template"));
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

    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, string? token = Token, string? origin = null, string? body = null)
    {
        var req = new HttpRequestMessage(method, $"http://127.0.0.1:{_port}{path}");
        if (token != null) req.Headers.TryAddWithoutValidation(BrowserConnector.TokenHeader, token);
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
}
