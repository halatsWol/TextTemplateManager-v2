# TextTemplateManager v2

A hotkey-driven text-template paste tool for Windows: organize reusable snippets in folders, give them shortcuts, and paste them into any application from a global Quick Paste window.

> Made by **Marflow Software**. Built with WinUI 3 (Windows App SDK) and .NET 8, distributed through the [Microsoft Store](https://apps.microsoft.com/detail/9NMBSZLLLN6V).

---

## Background

I started this project because I was never quite happy with the text-template tools I could find. Most had only minimalistic feature sets, felt outdated overall, or had dated, bare-bones designs, and the ones that came close usually only covered *some* of what I wanted. The biggest deal breaker was always the lack of **sync** and separation, so a team could share the same templates file but only use the templates they need.

This is my **first C# / WinUI 3 / .NET project**. It has been in the works since 2025-07-15 and was thrown away and rebuilt from scratch several times along the way. I'm not a professional software developer and don't aim to be: I work in IT, but got frustrated enough by the available tools that I took it on myself to solve this. AI was used occasionally (mainly for debugging complex issues, planning some structures and the like); any code written with or by AI was always reviewed and double-checked by me.

### Why version 2

Version 1 shipped as a per-user installer from GitHub with its own updater. Version 2 moves to the **Microsoft Store**: Windows installs and updates the app, no code-signing certificate of my own is needed, and companies can deploy it with their usual tools (winget, Intune). It lives in its own repository, so the code base could be cleaned up without carrying version 1's installer and updater around. Your templates come along: at its first start, version 2 backs up version 1's data and offers to import it.

> **Version 1** ([TextTemplateManager](https://github.com/halatsWol/TextTemplateManager)) has reached its end of life: it gets only important bug fixes. Its last release points to the Store and removes itself once version 2 is installed.

---

## Features

- **Rich templates**: formatted text (bold, colors, lists, tables, headings, code blocks with syntax highlighting, callout panels), saved when you click **Save**, or as you type with **Autosave**.
- **Quick Paste**: press a global hotkey anywhere; paste by **single key**, or hold **Alt** and type a **multi-key** shortcut.
- **Folders, drag and drop, multi-select**: organize templates, move or export several items at once; collapse or expand the whole tree.
- **Sync**: share a `.ttmdata` file through a cloud folder (e.g. OneDrive) or a network share; it shows as a pinned folder with per-source read-only and shortcut-prefix options. Opening a `.ttmdata` file adds it as a source. A synced folder whose file is gone keeps its last contents and can recreate the file.
- **Area-aware shortcuts**: the same shortcut can be reused in the local tree and in synced folders; local wins, then the synced folders in order, and Quick Paste lists every match.
- **Paste modes**: Auto, HTML/Jira, HTML, RTF, Markdown, Plaintext. Callout panels adapt to each mode: native panels in Jira, colored boxes in HTML/RTF, a labeled quote in Markdown. Pastes are kept out of the clipboard history.
- **Search**: fuzzy by default, or **strict** (exactly as typed, with `*` and `.` as wildcards); optionally also in the template text.
- **Automatic backups**: after changes, every two hours and daily; restore all or part of a backup, or export one to move to another PC.
- **Import and export** of `.ttmdata` files.
- **Guided tour** on sample templates, and an in-app **manual** (PDF).
- **Browser connector**: an opt-in local API for the companion extension [TTM-Connect](https://github.com/halatsWol/TTM-Connect) to list, paste and create templates ([API docs](docs/user/BrowserConnectorApi.md)); content going in and out is sanitized. Available on:
  - [Chrome Web Store](https://chrome.google.com/webstore/detail/jclopjpjdldbknjdhmjldehlkgbihlmi)
  - [Microsoft Edge Add-ons](https://microsoftedge.microsoft.com/addons/detail/ttm-connect/fbpmhopmnaoedcnbegdkpfblekhaindm)
  - [Firefox Add-ons](https://addons.mozilla.org/addon/ttm-connect/)
- **Start with Windows** (Windows' own startup setting, hidden in the system tray by default) and a configurable global hotkey.

---

## Install

Get **TextTemplateManager** from the [Microsoft Store](https://apps.microsoft.com/detail/9NMBSZLLLN6V). No administrator rights are needed; the Store keeps it up to date.

> **Administrators:** deployment with winget or Intune, updates under company policies, the app's data and removal are covered in the [Administrator & Deployment Guide](docs/user/AdminManual.md), also attached as a PDF to each release.

> **Note: Remote Desktop (RDP).** If TextTemplateManager runs on both your local computer and a remote computer you connect to over RDP, the global Quick Paste hotkey always opens Quick Paste on the **local** computer: Windows delivers a registered global hotkey locally, so it never reaches the remote session. Give each machine a **different** Quick Paste hotkey, or run only one instance.

---

## Documentation

- [User manual](docs/user/Manual.md) (also in the app under **Help ▸ Open Manual**)
- [Administrator & Deployment Guide](docs/user/AdminManual.md)
- [Browser connector API](docs/user/BrowserConnectorApi.md)
- [Architecture](docs/architecture/README.md): classes, relations and runtime flows, with diagrams
- [Publishing a release](docs/PUBLISHING.md)

---

## Build from source

Prerequisites:

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (the app targets .NET 8 and uses C# 14)
- Windows 10 19041+ / Windows 11
- [Node.js](https://nodejs.org/), only to rebuild the editor after changing `src/editor` (the bundle in the app's assets is committed)

```powershell
# Build (x64 is required); a debug build runs unpackaged from the bin folder
dotnet build src\TextTemplateManager\TextTemplateManager.csproj -c Debug -p:Platform=x64

# Run the tests
pwsh -File tests/run-tests.ps1
```

Set `TTM_DATA_DIR` to a folder of your choice to run a build against throwaway data instead of your own templates.

The manuals in `docs/user/` are rendered to PDF by `tools/ManualGen` during the build. The app icon and tile images are rendered from [`design/logo/logo.svg`](design/logo/logo.svg) by `design/logo/Render-Assets.ps1` (needs ImageMagick).

---

## Releasing

Pushing a version tag runs the release workflow ([`.github/workflows/release.yml`](.github/workflows/release.yml)): tests, the x64 Store package, the manuals as PDFs, a draft GitHub release with notes rendered from [`release.md`](release.md) (published by hand), and the Store submission including its "What's new" text. The tag (without the leading `v`) is the single source of the version. Details, including how to skip the Store submission, are in [Publishing a release](docs/PUBLISHING.md).

---

## Repository layout

| Folder | Contents |
| --- | --- |
| [`src/TextTemplateManager/`](src/TextTemplateManager/) | Application source (WinUI 3) |
| [`src/editor/`](src/editor/) | TipTap editor sources, bundled into the app's assets |
| [`tests/`](tests/) | Automated tests (`tests/run-tests.ps1`) |
| [`tools/ManualGen/`](tools/ManualGen/) | Renders the manuals in `docs/user/` to PDF |
| [`build/`](build/) | Release build and release-notes scripts used by the workflow |
| [`design/logo/`](design/logo/) | The logo as SVG and the script that renders the app's icon and tile images |
| [`.github/workflows/`](.github/workflows/) | Release workflow |
| [`docs/user/`](docs/user/) | User manual, admin guide, browser connector API |
| [`docs/architecture/`](docs/architecture/) | Technical documentation |

---

## Tech stack

- **UI**: WinUI 3 / Windows App SDK 1.8, MVVM (CommunityToolkit.Mvvm)
- **Editor**: TipTap / ProseMirror, bundled with esbuild, hosted in WebView2
- **Storage**: `System.Text.Json` (`.ttmdata` files), zip backups with checksums
- **Manual**: Markdig + QuestPDF (`tools/ManualGen`)
- **Packaging**: MSIX through the Microsoft Store

---

## License

Source-available under a custom license, see [LICENSE](LICENSE). In short: free to use, run, copy and modify for any purpose that complies with the terms (including inside a company), but **not** for sale and **not** to be provided as, or as part of, a service, directly or indirectly, and not by modifying it first.

Copyright © 2026 Marflow Software.

Privacy: the app collects and sends nothing, see [PRIVACY.md](PRIVACY.md).
