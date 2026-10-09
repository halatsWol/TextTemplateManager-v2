# TextTemplateManager v2

A hotkey-driven text-template paste tool for Windows: organize reusable snippets in folders, give them shortcuts, and paste them into any application from a global Quick Paste window.

Version 2 is distributed through the Microsoft Store and updated by it.

> **Work in progress.** Until 2.0 is released, the current version is 1.x: [TextTemplateManager](https://github.com/halatsWol/TextTemplateManager).

## Repository layout

| Folder | Contents |
| --- | --- |
| [`src/TextTemplateManager/`](src/TextTemplateManager/) | Application source (WinUI 3) |
| [`src/editor/`](src/editor/) | TipTap editor sources, bundled into the app's assets |
| [`tests/`](tests/) | Automated tests (`tests/run-tests.ps1`) |
| [`tools/ManualGen/`](tools/ManualGen/) | Renders `docs/user/Manual.md` to the PDF shipped with the app |
| [`docs/user/`](docs/user/) | User manual, admin guide, browser connector API |
| [`docs/`](docs/) | Technical documentation: architecture, class diagrams |

## License

See [LICENSE](LICENSE).
