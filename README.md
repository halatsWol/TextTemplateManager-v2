# TextTemplateManager v2

A hotkey-driven text-template paste tool for Windows: organize reusable snippets in folders, give them shortcuts, and paste them into any application from a global Quick Paste window.

Version 2 is distributed through the [Microsoft Store](https://apps.microsoft.com/detail/9NMBSZLLLN6V), which installs and updates it. Companies can deploy it with winget or Intune; see the [Administrator & Deployment Guide](docs/user/AdminManual.md).

> **Version 1** ([TextTemplateManager](https://github.com/halatsWol/TextTemplateManager)) has reached its end of life: it gets only important bug fixes. Its last release points to the Store and removes itself once version 2 is installed; version 2 backs up version 1's templates and offers to import them at its first start.

## Documentation

- [User manual](docs/user/Manual.md) (also in the app under **Help ▸ Open Manual**)
- [Administrator & Deployment Guide](docs/user/AdminManual.md)
- [Browser connector API](docs/user/BrowserConnectorApi.md), for the [TTM-Connect](https://github.com/halatsWol/TTM-Connect) browser extension
- [Publishing a release](docs/PUBLISHING.md)

## Repository layout

| Folder | Contents |
| --- | --- |
| [`src/TextTemplateManager/`](src/TextTemplateManager/) | Application source (WinUI 3) |
| [`src/editor/`](src/editor/) | TipTap editor sources, bundled into the app's assets |
| [`tests/`](tests/) | Automated tests (`tests/run-tests.ps1`) |
| [`tools/ManualGen/`](tools/ManualGen/) | Renders the manuals in `docs/user/` to PDF |
| [`build/`](build/) | Release build and release-notes scripts used by the workflow |
| [`.github/workflows/`](.github/workflows/) | Release workflow: tests, Store package, GitHub release, Store submission |
| [`docs/user/`](docs/user/) | User manual, admin guide, browser connector API |
| [`docs/`](docs/) | Technical documentation and publishing |

## License

See [LICENSE](LICENSE).
