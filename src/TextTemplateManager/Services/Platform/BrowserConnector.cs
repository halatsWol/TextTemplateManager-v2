using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace TextTemplateManager.Services.Platform;

// ---- Wire DTOs (serialized as camelCase JSON) ----
public sealed record PasteModeDto(string Id, string Label);
public sealed record LanguageDto(string Id, string Language);
public sealed record ConnectorNodeDto(string Id, string Name, string Type, string? DefaultMode, string? Source, List<ConnectorNodeDto>? Children,
    string? Language = null, List<LanguageDto>? Languages = null);
public sealed record TemplateContentDto(string Id, string Name, string Mode, string ContentType, string Content,
    string? Language = null, string? LanguageId = null);
public sealed record CreatedTemplateDto(string Id, string Name, List<LanguageDto>? Languages = null);
public sealed record NewLanguageDto(string? Language, string? Content);
public sealed record NewTemplateDto(string Content, string? Name, string? Language = null, IReadOnlyList<NewLanguageDto>? Languages = null);
public sealed record LanguageWriteDto(string Id, string LanguageId, string Language, bool Added);

/// <summary>A request the data source turns down, answered with <see cref="Status"/> and {"error": message}.</summary>
public sealed class ConnectorRefusal(int status, string error) : Exception(error)
{
    public int Status { get; } = status;
}

/// <summary>Data the connector serves. The app implements this over its live templates; a test
/// harness can implement it with fixtures. Read methods run on the connector's background threads
/// (implementations read an immutable snapshot); the writes marshal to the UI thread themselves.
/// Replies carry the protocol 3 fields (languages); the connector drops them for older clients.</summary>
public interface IConnectorDataSource
{
    string AppVersion { get; }
    IReadOnlyList<PasteModeDto> PasteModes();
    IReadOnlyList<ConnectorNodeDto> Tree();
    /// <summary>Null when the id is unknown; a refusal (404) for a language the template doesn't paste.</summary>
    TemplateContentDto? Template(string id, string? mode, string? language = null, string? languageId = null);
    Task<CreatedTemplateDto> CreateTemplateAsync(NewTemplateDto template);   // adds a template to the local area
    /// <summary>Adds a language to a template or replaces the text of one of its other languages, never the default's.</summary>
    Task<LanguageWriteDto> SetLanguageAsync(string id, string? language, string? languageId, string content);
}

/// <summary>
/// A loopback (127.0.0.1) HTTP/1.1 server that browser extensions call to list templates and fetch
/// rendered content. Built on TcpListener (not HttpListener) so it needs no URL-ACL reservation and
/// runs under a non-admin per-user install. Security: loopback-only bind, a required token header,
/// and CORS limited to extension origins.
/// </summary>
public sealed class BrowserConnector : IDisposable
{
    // v2 adds POST /template (create), v3 a template's languages. A client asks for v3 with ProtocolHeader; any
    // other gets exactly the v2 replies, which the extensions released so far check for.
    public const int ProtocolVersion = 2;
    public const int MaxProtocolVersion = 3;
    public const string TokenHeader = "x-ttm-token";
    public const string ProtocolHeader = "x-ttm-protocol";

    private readonly IConnectorDataSource _data;
    private readonly TimeSpan _connectionTimeout;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private string _token = "";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Loopback JSON consumed by JSON.parse — relaxed escaping keeps HTML content readable.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <param name="connectionTimeout">How long one connection may take from accept to reply (default
    /// 10 s), so a client that connects and sends nothing can't hold it open.</param>
    public BrowserConnector(IConnectorDataSource data, TimeSpan? connectionTimeout = null)
    {
        _data = data;
        _connectionTimeout = connectionTimeout ?? TimeSpan.FromSeconds(10);
    }

    public bool IsRunning => _listener != null;

    /// <summary>Binds loopback:port and begins serving. Throws (SocketException) if the port is taken.</summary>
    public void Start(int port, string token)
    {
        Stop();
        _token = token ?? "";
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();   // throws before anything is kept, so a failed start leaves the connector stopped
        _listener = listener;
        _cts = new CancellationTokenSource();
        // Run the accept loop on the thread pool, never the caller's (UI) thread, so request handling
        // never ties up the UI thread.
        var ct = _cts.Token;
        _ = Task.Run(() => AcceptLoopAsync(listener, ct));
    }

    /// <summary>While set (the tutorial runs on sample data), every authorized request is answered 503 "busy".</summary>
    public bool Busy { get => _busy; set => _busy = value; }
    private volatile bool _busy;   // read by the connection threads

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        try { _listener?.Stop(); } catch { }
        _listener = null;
        _cts = null;
    }

    public void Dispose() => Stop();

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
    {
        while (true)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(ct); }
            catch (Exception ex)
            {
                // Stop() cancels the token; anything else means the listener is broken, so stop too.
                if (!ct.IsCancellationRequested)
                    System.Diagnostics.Debug.WriteLine($"[Connector] accept failed, stopping: {ex.Message}");
                return;
            }
            _ = HandleClientAsync(client, ct);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken stopping)
    {
        using (client)
        using (var cts = CancellationTokenSource.CreateLinkedTokenSource(stopping))
        {
            cts.CancelAfter(_connectionTimeout);
            var ct = cts.Token;
            try
            {
                client.NoDelay = true;
                var stream = client.GetStream();
                var req = await ReadRequestAsync(stream, ct);
                if (req == null) return;
                var res = await RouteAsync(req);
                await WriteResponseAsync(stream, res, ct);
            }
            catch { /* best effort — drop the connection */ }
        }
    }

    // ---- Request parsing ----
    private sealed record Request(string Method, string Path, Dictionary<string, string> Query, Dictionary<string, string> Headers, string Body);

    private static readonly byte[] HeaderEnd = { 0x0D, 0x0A, 0x0D, 0x0A };   // \r\n\r\n

    // A read that treats a client abort as a clean end-of-stream (0 bytes) rather than throwing — the
    // caller's read loops already stop on a non-positive count. Keeps the expected disconnect from
    // surfacing as a first-chance exception, same as WriteResponseAsync.
    private static async Task<int> ReadSafeAsync(NetworkStream stream, Memory<byte> buf, CancellationToken ct)
    {
        try { return await stream.ReadAsync(buf, ct); }
        catch (Exception ex) when (IsClientGone(ex)) { return 0; }
    }

    private static async Task<Request?> ReadRequestAsync(NetworkStream stream, CancellationToken ct)
    {
        var buf = new byte[8192];
        using var ms = new MemoryStream();
        int headEnd = -1;

        // Read raw bytes until the header block ends. Headers are ASCII, but a POST body (below) is
        // UTF-8, so the two can't be ASCII-decoded together.
        while (headEnd < 0)
        {
            int n = await ReadSafeAsync(stream, buf.AsMemory(0, buf.Length), ct);
            if (n <= 0) break;
            ms.Write(buf, 0, n);
            headEnd = IndexOf(ms.GetBuffer(), (int)ms.Length, HeaderEnd);
            if (ms.Length > 64 * 1024) break;   // guard against oversized headers
        }
        if (headEnd < 0) return null;

        byte[] all = ms.GetBuffer();
        int total = (int)ms.Length;

        var lines = Encoding.ASCII.GetString(all, 0, headEnd).Split("\r\n");
        var start = lines[0].Split(' ');
        if (start.Length < 2) return null;

        string method = start[0].ToUpperInvariant();
        string rawUrl = start[1];
        string path = rawUrl;
        var query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int q = rawUrl.IndexOf('?');
        if (q >= 0)
        {
            path = rawUrl.Substring(0, q);
            foreach (var pair in rawUrl.Substring(q + 1).Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                string k = eq >= 0 ? pair.Substring(0, eq) : pair;
                string v = eq >= 0 ? pair.Substring(eq + 1) : "";
                query[Uri.UnescapeDataString(k)] = Uri.UnescapeDataString(v);
            }
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i < lines.Length; i++)
        {
            int c = lines[i].IndexOf(':');
            if (c > 0) headers[lines[i].Substring(0, c).Trim()] = lines[i].Substring(c + 1).Trim();
        }

        // Body: read up to Content-Length bytes (capped), decoded as UTF-8. Some may already be buffered.
        string body = "";
        if (headers.TryGetValue("Content-Length", out var clStr) &&
            int.TryParse(clStr, out int contentLength) && contentLength > 0)
        {
            contentLength = Math.Min(contentLength, 1024 * 1024);   // cap at 1 MB
            int bodyStart = headEnd + HeaderEnd.Length;
            using var bodyMs = new MemoryStream();
            int have = total - bodyStart;
            if (have > 0) bodyMs.Write(all, bodyStart, Math.Min(have, contentLength));
            while (bodyMs.Length < contentLength)
            {
                int n = await ReadSafeAsync(stream, buf.AsMemory(0, buf.Length), ct);
                if (n <= 0) break;
                bodyMs.Write(buf, 0, Math.Min(n, contentLength - (int)bodyMs.Length));
            }
            body = Encoding.UTF8.GetString(bodyMs.GetBuffer(), 0, (int)bodyMs.Length);
        }

        string cleanPath = path.TrimEnd('/');
        return new Request(method, cleanPath.Length == 0 ? "/" : cleanPath, query, headers, body);
    }

    // First index of `pattern` within the first `len` bytes of `b`, or -1.
    private static int IndexOf(byte[] b, int len, byte[] pattern)
    {
        for (int i = 0; i + pattern.Length <= len; i++)
        {
            int j = 0;
            while (j < pattern.Length && b[i + j] == pattern[j]) j++;
            if (j == pattern.Length) return i;
        }
        return -1;
    }

    // ---- Routing + security ----
    private sealed record Response(int Status, string Body, string Origin);

    private static bool IsExtensionOrigin(string origin) =>
        origin.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase) ||
        origin.StartsWith("moz-extension://", StringComparison.OrdinalIgnoreCase) ||
        origin.StartsWith("safari-web-extension://", StringComparison.OrdinalIgnoreCase);

    // Constant time, so how long a refusal takes says nothing about how much of the token was right.
    private bool TokenMatches(string? token) =>
        _token.Length > 0 && token != null &&
        System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(_token));

    private async Task<Response> RouteAsync(Request req)
    {
        req.Headers.TryGetValue("Origin", out string? origin);
        origin ??= "";

        // CORS is echoed back only to extension origins; a website can neither read responses nor
        // (without the token) get a useful one.
        string corsOrigin = IsExtensionOrigin(origin) ? origin : "";

        if (req.Method == "OPTIONS") return new Response(204, "", corsOrigin);   // preflight

        // Reject a cross-origin request from a non-extension origin outright.
        if (origin.Length > 0 && corsOrigin.Length == 0)
            return new Response(403, "{\"error\":\"forbidden origin\"}", "");

        // Token gate.
        req.Headers.TryGetValue(TokenHeader, out string? token);
        if (!TokenMatches(token))
            return new Response(401, "{\"error\":\"unauthorized\"}", corsOrigin);
        if (Busy) return new Response(503, "{\"error\":\"busy\"}", corsOrigin);

        bool v3 = Protocol(req) >= 3;
        try
        {
            switch (req.Path)
            {
                case "/ping":
                    return Ok(new { app = "TextTemplateManager", version = _data.AppVersion, protocol = Protocol(req) }, corsOrigin);
                case "/pastemodes":
                    return Ok(_data.PasteModes(), corsOrigin);
                case "/tree":
                    return Ok(v3 ? _data.Tree() : WithoutLanguages(_data.Tree()), corsOrigin);
                case "/template" when req.Method == "POST":
                    return await CreateTemplateAsync(req, v3, corsOrigin);
                case "/template/language" when req.Method == "POST" && v3:
                    return await SetLanguageAsync(req, corsOrigin);
                case "/template":
                    if (!req.Query.TryGetValue("id", out string? id) || string.IsNullOrWhiteSpace(id))
                        return new Response(400, "{\"error\":\"missing id\"}", corsOrigin);
                    req.Query.TryGetValue("mode", out string? mode);
                    TemplateContentDto? tpl;
                    if (v3)
                    {
                        req.Query.TryGetValue("lang", out string? lang);
                        req.Query.TryGetValue("langId", out string? langId);
                        tpl = _data.Template(id, mode, NullIfBlank(lang), NullIfBlank(langId));
                    }
                    else tpl = _data.Template(id, mode) is { } full ? full with { Language = null, LanguageId = null } : null;
                    return tpl == null
                        ? new Response(404, "{\"error\":\"not found\"}", corsOrigin)
                        : Ok(tpl, corsOrigin);
                default:
                    return new Response(404, "{\"error\":\"unknown endpoint\"}", corsOrigin);
            }
        }
        catch (ConnectorRefusal refusal)
        {
            return Error(refusal.Status, refusal.Message, corsOrigin);
        }
        catch
        {
            return new Response(500, "{\"error\":\"internal\"}", corsOrigin);
        }
    }

    // The protocol the client asked for with ProtocolHeader, at most ours; the default without one.
    private static int Protocol(Request req) =>
        req.Headers.TryGetValue(ProtocolHeader, out var asked) && int.TryParse(asked, out int v) && v > ProtocolVersion
            ? Math.Min(v, MaxProtocolVersion) : ProtocolVersion;

    private static List<ConnectorNodeDto> WithoutLanguages(IEnumerable<ConnectorNodeDto> nodes) =>
        nodes.Select(n => n with { Language = null, Languages = null, Children = n.Children == null ? null : WithoutLanguages(n.Children) }).ToList();

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static JsonElement? ParseBody(Request req)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(req.Body) ? "{}" : req.Body);
            return doc.RootElement.ValueKind == JsonValueKind.Object ? doc.RootElement.Clone() : null;
        }
        catch (JsonException) { return null; }
    }

    private static string? StringOf(JsonElement body, string name) =>
        body.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    // POST /template — body {"content": "...", "name"?: "..."}, from v3 also "language"?: code of the content and
    // "languages"?: [{"language", "content"}]. Adds a template to the local area (name defaults to "New Template",
    // incremented on collision) and returns {"id","name"} (v3: plus "languages" for one in several languages).
    private async Task<Response> CreateTemplateAsync(Request req, bool v3, string corsOrigin)
    {
        if (ParseBody(req) is not { } body) return Error(400, "invalid json", corsOrigin);
        string? content = StringOf(body, "content");
        if (string.IsNullOrEmpty(content)) return Error(400, "missing content", corsOrigin);

        var template = new NewTemplateDto(content, StringOf(body, "name"));
        if (v3)
        {
            List<NewLanguageDto>? languages = null;
            if (body.TryGetProperty("languages", out var list) && list.ValueKind != JsonValueKind.Null)
            {
                if (list.ValueKind != JsonValueKind.Array) return Error(400, "invalid languages", corsOrigin);
                languages = new();
                foreach (var entry in list.EnumerateArray())
                {
                    if (entry.ValueKind != JsonValueKind.Object) return Error(400, "invalid languages", corsOrigin);
                    languages.Add(new NewLanguageDto(StringOf(entry, "language"), StringOf(entry, "content")));
                }
            }
            template = template with { Language = NullIfBlank(StringOf(body, "language")), Languages = languages };
        }

        var created = await _data.CreateTemplateAsync(template);
        return Ok(v3 ? created : created with { Languages = null }, corsOrigin);
    }

    // POST /template/language (v3) — body {"id", "language" or "langId", "content"}.
    private async Task<Response> SetLanguageAsync(Request req, string corsOrigin)
    {
        if (ParseBody(req) is not { } body) return Error(400, "invalid json", corsOrigin);
        string? id = NullIfBlank(StringOf(body, "id")), language = NullIfBlank(StringOf(body, "language")),
            languageId = NullIfBlank(StringOf(body, "langId")), content = StringOf(body, "content");
        if (id == null) return Error(400, "missing id", corsOrigin);
        if ((language == null) == (languageId == null)) return Error(400, "language or langId", corsOrigin);
        if (string.IsNullOrEmpty(content)) return Error(400, "missing content", corsOrigin);
        return Ok(await _data.SetLanguageAsync(id, language, languageId, content), corsOrigin);
    }

    private static Response Ok(object payload, string origin) =>
        new(200, JsonSerializer.Serialize(payload, Json), origin);

    private static Response Error(int status, string error, string origin) =>
        new(status, JsonSerializer.Serialize(new { error }, Json), origin);

    // The expected "the client hung up" exceptions: a reset/aborted socket, a disposed stream, or
    // cooperative cancellation on shutdown. A browser routinely aborts in-flight loopback requests
    // (the tab navigated, lost focus, or was throttled while another window took the foreground), so
    // these are normal, not errors.
    private static bool IsClientGone(Exception ex) =>
        ex is IOException or SocketException or ObjectDisposedException or OperationCanceledException;

    private static async Task WriteResponseAsync(NetworkStream stream, Response res, CancellationToken ct)
    {
        byte[] body = Encoding.UTF8.GetBytes(res.Body);
        var head = new StringBuilder();
        head.Append("HTTP/1.1 ").Append(res.Status).Append(' ').Append(Reason(res.Status)).Append("\r\n");
        head.Append("Content-Type: application/json; charset=utf-8\r\n");
        head.Append("Content-Length: ").Append(body.Length).Append("\r\n");
        if (res.Origin.Length > 0)
        {
            head.Append("Access-Control-Allow-Origin: ").Append(res.Origin).Append("\r\n");
            head.Append("Vary: Origin\r\n");
            head.Append("Access-Control-Allow-Headers: ").Append(TokenHeader).Append(", ").Append(ProtocolHeader).Append(", Content-Type\r\n");
            head.Append("Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n");
            head.Append("Access-Control-Max-Age: 600\r\n");
        }
        head.Append("Connection: close\r\n\r\n");

        byte[] headBytes = Encoding.ASCII.GetBytes(head.ToString());
        try
        {
            await stream.WriteAsync(headBytes, ct);
            if (body.Length > 0) await stream.WriteAsync(body, ct);
            await stream.FlushAsync(ct);
        }
        catch (Exception ex) when (IsClientGone(ex))
        {
            // The browser aborted the connection before we finished replying. Nothing to send; the
            // connection is dead. Swallowed here, at the throwing await, so it doesn't propagate up as
            // a first-chance exception the debugger stops on.
        }
    }

    private static string Reason(int code) => code switch
    {
        200 => "OK", 204 => "No Content", 400 => "Bad Request", 401 => "Unauthorized",
        403 => "Forbidden", 404 => "Not Found", 409 => "Conflict", 503 => "Service Unavailable", _ => "Error",
    };
}
