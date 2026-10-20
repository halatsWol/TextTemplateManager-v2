# 12. Testing

## The test project

`tests/TextTemplateManager.Tests` (xUnit) references the app assembly. Run it with:

```bash
pwsh -File tests/run-tests.ps1
```

`run-tests.ps1` builds the app **unpackaged and self-contained** (`-p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true`). This isn't optional: the app assembly's module initializer boots the Windows App Runtime as soon as one of its types loads, and built the default way it fails with `REGDB_E_CLASSNOTREG` inside a test host, so every test would fail.

`TestEnvironment` sets `TTM_DATA_DIR` from a module initializer before any test runs, as a safety net, and gives each test its own scratch folder (`NewScratchDir()`); tests create their own `StorageService` / `DataNode` over it, so they run in parallel without sharing files, locks or write signals.

## What is covered

| Area | Tests |
| --- | --- |
| File formats | `FileFormatTests` (data, settings and sync files byte-for-byte against fixtures), `StorageServiceTests` (atomic writes, unchanged skips, unreadable files set aside, write signals) |
| Store | `DataNodeTests` (add, delete, move, import, export, save suppression, shortcut clash resolution), `TreeWalkTests`, `ItemTitlesTests` |
| Sync | `SyncEngineTests` (merge rules, identity reuse, order) |
| Shortcuts and search | `ShortcutValidatorTests`, `TemplateShortcutTests`, `ShortcutRowTests`, `ConflictReportTests`, `TemplateSearchTests` |
| Backups and restore | `BackupServiceTests`, `BackupSchedulerTests` (with an injected clock), `RestoreServiceTests` (parts, 1.x settings, sync file placements, rollback, recovery) |
| Editing | `ItemDraftTests`, `MultiSelectionTests` |
| Quick Paste | `QuickPasteControllerTests`, `PasteWindowNavigationTests`, `PasteClipboardTests`, `SendInputTests`, `HotkeyListenerTests` |
| Conversions | `HtmlToRtfTests`, `LegacyRtfTests` |
| Connector | `BrowserConnectorTests` (routing, token, origins, busy, create) |
| Platform and 1.x | `AppEnvironmentTests`, `LegacyAppTests`, `LegacyAutostartTests`, `AppInfoTests`, `UnexpectedErrorHandlerTests` |
| Updates | `UpdateServiceTests` (check, background download, consent download under the Store policy, the opt-in to keep updating despite it, policy values, the switch and its attempt record, two failures, idle install conditions, the update log) over a stand-in Store and package switch |
| Tour and startup | `TutorialTests` (sample store, card placement, tour offer and What's new, the shipped `release.md`) |
| Smoke | `SmokeTests` |

Logic that needs no window lives outside the windows on purpose (`QuickPasteController`, `ItemDraft`, `MultiSelection`, `ConflictReport`, `StartupGreeting`, `WhatsNew`, `TutorialOverlay.Beside`), so it can be tested here.

## UI checks

What needs a running window (XAML rendering, focus, real keyboard and mouse input, WebView2, the packaged app) is checked by driving the app with UI Automation from PowerShell scripts against throwaway data (`TTM_DATA_DIR`), and the packaged app on a test VM. Those scripts are developer tooling and aren't part of this repository. The update switch itself (staging, the forced register, the restart, the watchdog) is checked there with sideloaded test-signed packages and `TTM_UPDATE_SOURCE`.

---

[← 11. Platform, build and release](11-platform-build-release.md) · [Contents](README.md) · [13. In-app updates →](13-updates.md)
