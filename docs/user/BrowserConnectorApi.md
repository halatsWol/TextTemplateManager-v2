# Browser Connector API

TextTemplateManager exposes a small local HTTP API that a browser extension (Chrome / Edge /
Firefox) can call to list templates, fetch rendered content, and create new templates from selected
text. It is meant for building a context-menu "insert template" extension.

## Enabling & pairing

- The user enables it in **Settings ▸ Integrations ▸ Browser extension** (off by default).
- On first enable, the app generates a random **token** and shows it in settings. The user copies
  it into the extension. Every request must send it as the `x-ttm-token` header.
- The server binds **loopback only** (`127.0.0.1`), default port **47615** (configurable in settings).
- CORS is granted only to `chrome-extension://`, `moz-extension://`, and `safari-web-extension://`
  origins. A web page cannot use the API (wrong origin, and it can't know the token).
  Microsoft Edge is Chromium-based and uses the `chrome-extension://` scheme, so Edge extensions are
  covered by the same origin (there is no separate `edge-extension://`).

The app must be running for the API to answer. If it isn't (or the connector is disabled), requests
fail to connect.

## Base URL

```
http://127.0.0.1:<port>/
```

All responses are `application/json; charset=utf-8`.

## Auth & headers

| Header | Required | Value |
| --- | --- | --- |
| `x-ttm-token` | yes | the token from the app's settings |
| `x-ttm-protocol` | no | the protocol version your client speaks, e.g. `3` (see [Protocol versions](#protocol-versions)) |
| `Origin` | yes (browsers send it) | must be an extension origin |

Errors: `401` (missing/wrong token), `403` (non-extension origin, or a read-only template), `400` (bad request),
`404` (unknown endpoint, template id or language), `409` (the change can't be made now, see `POST /template/language`),
`500` (internal), `503` (busy: the app's guided tour is running). Error bodies are `{ "error": "<reason>" }`.

## Protocol versions

The API speaks the protocol your client asks for, so older extensions keep working unchanged:

- Send `x-ttm-protocol: 3` on every request to use protocol 3 (template languages). A higher number gets the highest version the app has; `/ping` tells you which one you got.
- Without the header (or with `1`, `2` or anything unreadable) you get protocol 2, exactly as before: no language fields in the replies, the language parameters are ignored and `POST /template/language` is `404`.
- An app older than TextTemplateManager 2.8 ignores the header and answers with protocol 2, so a protocol 3 client should check `protocol` in `/ping` and fall back to the protocol 2 calls when it reads 2.

| protocol | adds |
| --- | --- |
| 1 | `/ping`, `/pastemodes`, `/tree`, `GET /template` |
| 2 | `POST /template` (create) |
| 3 | a template's languages: `language`/`languages` in `/tree`, `lang`/`langId` on `GET /template`, languages on `POST /template`, `POST /template/language` |

## Endpoints

### `GET /ping`
Connectivity + version check. `protocol` is the version this request was answered with: `2`, or `3` when you sent `x-ttm-protocol: 3` to an app that has it.
```json
{ "app": "TextTemplateManager", "version": "1.0.1", "protocol": 2 }
```

### `GET /pastemodes`
The paste modes, in display order. `id` is what you pass to `/template?mode=`.
```json
[ { "id": "Auto", "label": "Auto" },
  { "id": "Jira", "label": "HTML/Jira" },
  { "id": "HTML", "label": "HTML" },
  { "id": "RTF", "label": "RTF" },
  { "id": "Markdown", "label": "Markdown" },
  { "id": "Plaintext", "label": "Plaintext" } ]
```

### `GET /tree`
The folder/template tree (names + ids only — no content). Build your menu from this.
```json
[ { "id": "<guid>", "name": "Greetings", "type": "folder", "source": "local",
    "children": [
      { "id": "<guid>", "name": "Kind Regards", "type": "template",
        "defaultMode": "Auto", "source": "local" } ] } ]
```
- `type` is `"folder"` or `"template"`.
- `defaultMode` (templates only) is the template's own default paste mode.
- `source` is the sync folder's name, or `"local"`.

Protocol 3 adds to templates:
```json
{ "id": "<guid>", "name": "Offer", "type": "template", "defaultMode": "Auto", "source": "local",
  "language": "en",
  "languages": [ { "id": "a1b2", "language": "en" }, { "id": "c3d4", "language": "de" } ] }
```
- `language` is the language of the template's default text, an ISO 639-1 code (`zh-Hans`/`zh-Hant` for Chinese); missing when the template has none set.
- `languages` is there only for a template in more than one language: every language that is pasted, the default first. `id` names the language within this template (4 hex characters, unique only inside it); use it with `langId`. A language the user hasn't set, or a second tab with the same language, isn't listed.

### `GET /template?id=<guid>&mode=<mode>`
Rendered content for one template. `mode` is a `/pastemodes` id, or `default` / omitted to use the
template's own default.
```json
{ "id": "<guid>", "name": "Kind Regards", "mode": "HTML",
  "contentType": "text/html", "content": "<p>Kind regards</p>" }
```
Protocol 3 adds `lang=<code>` or `langId=<id>` to pick one of the template's languages (without either you get the default), and the reply names the language served:
```json
{ "id": "<guid>", "name": "Offer", "mode": "HTML", "contentType": "text/html",
  "content": "<p>Hallo</p>", "language": "de", "languageId": "c3d4" }
```
`languageId` is there only for a template in more than one language. A language the template doesn't paste is `404` `language not found`.
`contentType` tells you how to use `content`:

| mode | contentType | how to insert |
| --- | --- | --- |
| Auto / HTML / Jira | `text/html` | insert as HTML (e.g. into a `contenteditable`, or write `text/html` to the clipboard) |
| Markdown | `text/markdown` | insert the markdown source as text |
| Plaintext | `text/plain` | insert as plain text |
| RTF | `application/rtf` | RTF source (mainly useful for clipboard) |

### `POST /template`
Create a new template in the **local** area (e.g. from the page selection). Send a JSON body with
`Content-Type: application/json`:
```json
{ "content": "<p>selected text</p>", "name": "optional title" }
```
- `content` (**required**) — the template body. Templates hold HTML (the same format the in-app editor
  saves), so send HTML; plain text is stored verbatim. The content is **sanitized** before it is stored
  (see [Content sanitizing](#content-sanitizing)), so active/script markup is stripped — send the
  formatting you want kept and don't rely on anything scriptable surviving.
- `name` (optional) — the title. Defaults to `New Template`. A name that already exists gets an
  incrementing suffix (`New Template 2`, …), so the returned title may differ from what you sent.

Returns the created template's id and final name:
```json
{ "id": "<guid>", "name": "New Template 2" }
```
`400` if `content` is missing or empty. The template appears in the app immediately and in the next
`/tree`. (Available from `protocol` 2.)

Protocol 3 also takes the template's languages:
```json
{ "content": "<p>Hello</p>", "name": "Offer", "language": "en",
  "languages": [ { "language": "de", "content": "<p>Hallo</p>" } ] }
```
- `language` (optional): the language of `content`.
- `languages` (optional): more languages, each with its own `content` (sanitized like `content`). Needs `language`.
- Codes are ISO 639-1 (`zh-Hans`/`zh-Hant` for Chinese), case doesn't matter. `400` for an unknown code (`unknown language`), a language given twice (`duplicate language`), an entry without content (`missing content`) or `languages` without `language` (`languages need language`); nothing is created then.

The reply then also lists the languages, as in `/tree`:
```json
{ "id": "<guid>", "name": "Offer",
  "languages": [ { "id": "a1b2", "language": "en" }, { "id": "c3d4", "language": "de" } ] }
```

### `POST /template/language` (protocol 3)
Adds a language to a template, or replaces the text of one of its other languages, e.g. to save a translation made in the browser. The default text is never changed.
```json
{ "id": "<guid>", "language": "fr", "content": "<p>Bonjour</p>" }
```
- `id` (**required**): the template.
- `language` or `langId` (exactly one): `language` adds that language, or replaces its text when the template already pastes it; `langId` replaces the text of that language.
- `content` (**required**): the text, sanitized like `POST /template`.

Returns the template, the language's id and code, and whether it was added:
```json
{ "id": "<guid>", "languageId": "e5f6", "language": "fr", "added": true }
```

| status | error | when |
| --- | --- | --- |
| `400` | `missing id`, `language or langId`, `missing content`, `unknown language` | a field is missing, both or neither of `language`/`langId` are given, or the code is unknown |
| `403` | `read only` | the template is in a synced folder that is read-only on this PC |
| `404` | `not found`, `language not found` | unknown template, or no language with that `langId` |
| `409` | `default language` | the language (or `langId`) is the template's default; its text is only changed in the app |
| `409` | `no default language` | the template has no language set yet; the user sets it in the app first |
| `409` | `unsaved edits` | the template is open in the app with changes not saved yet (autosave off) |
| `409` | `sync file missing` | the template is in a synced folder whose file can't be found |

The change is saved right away and shows in the app's editor if the template is open there; what the user typed is kept.

## Example (extension background)

```js
const BASE = "http://127.0.0.1:47615";
const TOKEN = "<paired token>";
const h = { "x-ttm-token": TOKEN };

const tree  = await (await fetch(`${BASE}/tree`,  { headers: h })).json();
const tpl   = await (await fetch(`${BASE}/template?id=${id}&mode=default`, { headers: h })).json();
// tpl.content + tpl.contentType -> insert into the page

// Create a template from the current selection:
const created = await (await fetch(`${BASE}/template`, {
  method: "POST",
  headers: { ...h, "Content-Type": "application/json" },
  body: JSON.stringify({ content: selectedHtml, name: "From browser" }),
})).json();
// created.id, created.name

// Protocol 3: ask for it, fall back when the app is older.
const h3 = { ...h, "x-ttm-protocol": "3" };
const { protocol } = await (await fetch(`${BASE}/ping`, { headers: h3 })).json();
if (protocol >= 3) {
  const german = await (await fetch(`${BASE}/template?id=${id}&lang=de`, { headers: h3 })).json();
  await fetch(`${BASE}/template/language`, {
    method: "POST",
    headers: { ...h3, "Content-Type": "application/json" },
    body: JSON.stringify({ id, language: "fr", content: translatedHtml }),
  });
}
```

`manifest.json` needs host permission for the loopback origin:
```json
"host_permissions": ["http://127.0.0.1:47615/*"]
```
(Loopback is a secure context, so this works from an HTTPS page's extension without mixed-content
issues. Match the port to the app setting.)

## Content sanitizing

Template HTML is untrusted (a page selection can carry attacker-controlled markup), so it is sanitized
**both directions** — when a template is created via `POST /template` and when content is served by
`GET /template`. Sanitizing is a two-stage, DOM-based process: a strict **allow-list**, then a
**deny-list** hardening pass that also re-parses the result to catch anything a serialization round-trip
could resurrect (mutation-XSS). Text nodes are never altered, so **code blocks pass through verbatim** —
code in any language, including HTML/JS shown as escaped text inside `<pre>`/`<code>`, is preserved
(only a *real* `<script>` element is dropped, not the string `<script>` shown as code).

**Kept**

- Block/inline formatting: `p`, `h1`–`h6`, `strong`/`b`, `em`/`i`, `u`, `s`, `del`, `ins`, `mark`,
  `sub`, `sup`, `small`, `code`, `pre`, `blockquote`, `br`, `hr`, `ul`/`ol`/`li`, and tables
  (`table`/`thead`/`tbody`/`tr`/`td`/`th`/`caption`/`colgroup`/`col`).
- Links: `<a>` with `href` limited to `http`, `https`, `mailto`, `tel`, or relative/anchor URLs. A link
  with a `target` gets `rel="noopener noreferrer"` added.
- Inline `style` limited to inert properties (`color`, `background-color`, `text-align`, `width`,
  `height`, `font-weight`, `font-style`, `text-decoration`, `vertical-align`).
- Inert `class` and `data-*` attributes (so code-block language classes and panel markers round-trip).

**Removed**

- Active / remote-loading / legacy elements — removed **with their contents**: `script`, `style`,
  `iframe`, `frame`, `object`, `embed`, `applet`, `form` (and inputs), `svg`, `math`, `img`/`picture`,
  `video`/`audio`/`source`/`track`, `link`, `meta`, `base`, `noscript`, `template`, and parser-mode
  legacy tags (`xmp`, `plaintext`, `listing`, `marquee`, …). **Images are not kept.**
- HTML comments (a conditional-comment mutation-XSS vector).
- Event-handler attributes (`onclick`, `onerror`, `onload`, … — anything starting with `on`).
- Dangerous URL schemes anywhere a URL is expected: `javascript:`, `vbscript:`, `data:` and similar,
  including entity-/whitespace-obfuscated forms (`jav&#x09;ascript:`).
- Script-capable or remote CSS: `expression()`, `-moz-binding`, `behavior:`, `@import`, and any
  `url(...)` — so inline styles can't fetch remotely (tracking) or execute.
- Any other unrecognised tag is **unwrapped**: the tag is dropped but its text content is kept.

The receiving extension should still insert content safely (e.g. as text, or through its own sanitizer)
as defense in depth — a server-side sanitizer can't perfectly mirror the browser's HTML parser.

## Notes

- `protocol` in `/ping` is the API version the request was answered with; see [Protocol versions](#protocol-versions).
- The tree is served from an in-memory snapshot refreshed when templates change, so it stays current
  without you polling aggressively — re-fetch `/tree` when you open your menu.
- One request per connection (`Connection: close`). A connection that hasn't sent its request and received the reply within 10 seconds is closed.
