# 12. Testing

## The test project

`tests/TextTemplateManager.Tests` (xUnit) references the app assembly. Run it with:

```bash
pwsh -File tests/run-tests.ps1
```

`run-tests.ps1` builds the app **unpackaged and self-contained** (`-p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true`). This isn't optional: the app assembly's module initializer boots the Windows App Runtime as soon as one of its types loads, and built the default way it fails with `REGDB_E_CLASSNOTREG` inside a test host, so every test would fail.

`TestEnvironment` sets `TTM_DATA_DIR` from a module initializer before any test runs, as a safety net, and gives each test its own scratch folder (`NewScratchDir()`); tests create their own `StorageService` / `DataNode` over it, so they run in parallel without sharing files, locks or write signals. It also points `Loc` at `ttm2.pri` next to the tests: the test host isn't `ttm2.exe`, so `ResourceLoader` finds no default file there.

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
| Translation | `TranslationChoicesTests` (the Translate menu's sources, targets and remembered languages), `TranslatedCopyTests` (title and multi-key shortcut of a translated copy), `TranslationUpdaterTests` (when the daily check runs, offline while off, notice or install, retries), `ModelCatalogTests` (which records are offered: versions, sizes, Android and Nightly filters, complete pairs, languages and names), `ModelStoreTests` (install from a fake server with both hashes checked, failed and cancelled downloads, updates, remove, export and import, damaged or unsafe zips, whether a download runs, for the exit question) |
| Connector | `BrowserConnectorTests` (routing, token, origins, busy, create) |
| Platform and 1.x | `AppEnvironmentTests`, `LegacyAppTests`, `LegacyAutostartTests`, `AppInfoTests`, `UnexpectedErrorHandlerTests` |
| Updates | `UpdateServiceTests` (check, background download, updates the source doesn't name (the Store's way: downloaded by the source's word, switch counted by a newer running version), download under the Store policy (on its own with automatic updates on, from Check for updates and from Download, over the app's window), a failed download and Try again without a new check, a known update downloaded inside the Store's 30 minutes, the Windows Update hint, a version Windows Update staged (ready without asking the Store), staged-version log texts (real AppX log lines), automatic updates off and the policy that locks them, notice only with an update already staged, a policy set before a switch, policy values, the switch and its attempt record, two failures, idle install conditions, the Store's check limits and the check times across restarts, a check the Store refused and its logged refusal (real log texts), the update log) over a stand-in Store and package switch |
| Tour and startup | `TutorialTests` (sample store, card placement, tour offer and What's new, every release's `release.md`, the notes for the app version) |
| UI text | `LocalizationTests` (every `x:Uid`, every `Loc` / `Loc.Tour` / `Loc.Editor` key and every `t` / `tp` key in the editor's script has en-US text in its file, the editor page gets its text, title and language ahead of its scripts, en-US has no unused keys, every other language has the same keys and placeholders and leaves no English text empty, `Loc`, `Loc.Plural` and `Loc.Tour` read the text, no literal UI text in XAML) |
| Smoke | `SmokeTests` |

The editor's JavaScript has its own tests (`src/editor/test`, Node's test runner), run from `src/editor`:

```bash
npm test
```

They cover the placeholder masking, the repair of tags the engine doubled, formal German (greetings in and out, which paragraphs are translated again, when the first translation stays), the language routing and the language detection. `engine.test.mjs` also runs the real engine from `Assets/translator` on English-German models when `TTM_TEST_MODELS` points to a folder laid out like `TranslationModels` with `en-de` and `de-en`; without it, that test is skipped.

Logic that needs no window lives outside the windows on purpose (`QuickPasteController`, `ItemDraft`, `MultiSelection`, `ConflictReport`, `StartupGreeting`, `WhatsNew`, `TutorialOverlay.Beside`), so it can be tested here.

## UI checks

What needs a running window (XAML rendering, focus, real keyboard and mouse input, WebView2, the packaged app) is checked by driving the app with UI Automation from PowerShell scripts against throwaway data (`TTM_DATA_DIR`), and the packaged app on a test VM. Those scripts are developer tooling and aren't part of this repository. The update switch itself (staging, the forced register, the restart, the watchdog) is checked there with sideloaded test-signed packages and `TTM_UPDATE_SOURCE`.

---

[← 11. Platform, build and release](11-platform-build-release.md) · [Contents](README.md) · [13. In-app updates →](13-updates.md)
