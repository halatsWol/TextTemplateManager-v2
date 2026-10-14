# 8. Browser connector

The connector lets the TTM-Connect browser extension list templates, fetch them rendered in a paste mode, and create templates from a page selection. The HTTP API (endpoints, payloads, errors) is documented for extension developers in [`../user/BrowserConnectorApi.md`](../user/BrowserConnectorApi.md); this page covers how it is built.

## Structure

- `BrowserConnector`: a minimal HTTP/1.1 server on `TcpListener` bound to `127.0.0.1` (not `HttpListener`, so no URL ACL reservation and no admin rights are needed). The accept loop and each connection run on the thread pool; each connection has 10 seconds from accept to reply; headers are capped at 64 KB and bodies at 1 MB.
- `IConnectorDataSource`: what the server serves. `AppConnectorData` implements it over the store; tests implement it with fixtures.
- `AppConnectorData` keeps an **immutable snapshot** of the tree (names, ids, contents, default modes), rebuilt on the UI thread whenever the store raises `TreeChanged` or `DataSaved`. Reads from connection threads only ever see a complete snapshot. Rendering (`PasteService.RenderForMode`) is a pure function and runs off the UI thread.
- `App.ApplyBrowserConnectorSettings()` starts, restarts or stops the server to match the settings (off by default, port 47615, a token generated on first enable). A port that can't be used is reported at startup and in Settings > Integrations.

![A request](diagrams/act-connector-request.svg)

([source](diagrams/act-connector-request.puml))

## Security

- Bound to loopback only; nothing listens on a network interface.
- Every request must carry the pairing token in `x-ttm-token`, compared in constant time.
- CORS is answered only for extension origins (`chrome-extension://`, `moz-extension://`, `safari-web-extension://`); a request from any other web origin gets 403, so a web page can't use the connector even if it guessed the port.
- Content is sanitized by `HtmlSanitizer` in both directions: a selection sent to `POST /template` before it becomes a template, and every template before it is served (an allow-list of formatting elements and attributes, then a deny-list pass: scripts, styles, frames, objects, images and media, forms, SVG/MathML, comments, event handlers, `javascript:`/`vbscript:`/`data:` links and active CSS removed; code blocks kept as text).
- During the guided tour `Busy` is set and every authorized request is answered 503, so the extension can't see or change the sample data.
- A created template is added on the UI thread to the local root with a unique title and the default paste mode, saved, and the tree refreshed (`NotifyTreeChanged`); the request waits at most 10 seconds for that.
