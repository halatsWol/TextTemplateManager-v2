# 11. Platform, build and release

## Packaged and dev runs

`AppEnvironment.Detect()` checks for a package identity and returns one of two `IAppEnvironment`s; everything that differs between the Store package and a dev run from `bin\` goes through it.

| | Packaged (Store) | Unpackaged (dev run) |
| --- | --- | --- |
| Data folder | Package `LocalState` | `%LocalAppData%\Marflow Software\TextTemplateManager 2 Dev` |
| Backups | `Documents\TextTemplateManager\Backups` | `Documents\TextTemplateManager\Backups (Dev)` |
| Translation models | Package `LocalCache\TranslationModels` | `TranslationModels` in the data folder |
| Start with Windows | Startup task `TtmStartup` (Windows Settings > Apps > Startup) | Not available |
| Started at sign-in | Startup-task activation | `--hidden` stands in for it |
| Opened file | File activation | First `.ttmdata` command-line argument |
| Closing a running 1.x, removal offer | Yes | No (a dev run leaves an installed 1.x alone) |
| Tour offer at startup | Yes | Only with `--offer-tour` (UI test scripts start on fresh data) |
| In-app updates | From the Store (Store-signed package); a sideloaded test package from the `TTM_UPDATE_SOURCE` folder | None |

`TTM_DATA_DIR` overrides the data folder in both (then backups and the 1.x folder are looked for inside it). The test suite sets it for every test, and UI checks run with it, so no test ever touches real data. The 1.x data folder (`%LocalAppData%\Marflow Software\TextTemplateManager`) is only ever read.

## Package manifest

`src/TextTemplateManager/Package.appxmanifest`: identity `MarflowSoftware.TextTemplateManager` with the Partner Center publisher, display name TextTemplateManager, `runFullTrust`, a startup task (`TtmStartup`, enabled), the `.ttmdata` file type association, minimum Windows 10 2004 (19041). The version is stamped from the build (`-p:Version=X.Y.Z` becomes `X.Y.Z.0`).

## Build

| Build | Command | Result |
| --- | --- | --- |
| Dev | `dotnet build src\TextTemplateManager\TextTemplateManager.csproj -c Debug -p:Platform=x64` | Unpackaged `ttm2.exe` in `bin\x64\Debug\...\win-x64` |
| Tests | `pwsh -File tests/run-tests.ps1` | Builds the app unpackaged and self-contained, then runs xUnit |
| Store | `build/Build-Release.ps1 -Version X.Y.Z` (from a short path) | `TextTemplateManager_X.Y.Z.0_x64_bundle.msixupload` (`-p:StoreBuild=true`), manual PDFs |

The project is unpackaged by default (`WindowsPackageType=None`); `StoreBuild=true` switches to MSIX with the Windows App SDK as a framework dependency and an unsigned bundle (the Store signs it). The XAML compiler fails silently near 260-character paths, so release builds run from a short path. The editor bundle under `Assets/editor` and the translator worker under `Assets/translator` are committed, so the app builds without Node; after changing `src/editor`, run `npm run build` there. The build writes all three files with LF line endings, and `.gitattributes` checks them out as LF, so a rebuild changes only what really changed. The Bergamot engine next to the worker (`bergamot-translator.js` and `.wasm`) is Mozilla's: the JavaScript from Firefox's `toolkit/components/translations/bergamot-translator`, the wasm from Firefox's Remote Settings (`translations-wasm-v2`, checked against its `decompressedHash`); it ships in the package because Store apps may not download code. `Manual.pdf` is generated from `docs/user/Manual.md` by `tools/ManualGen` during the build. The icon and tile PNGs and `AppIcon.ico` under `Assets` are committed too; they are rendered from `design/logo/logo.svg` by `design/logo/Render-Assets.ps1` (ImageMagick), so after changing the logo run that script. The Store listing images (screenshots in two colour schemes, `amber` and `slate`, and the 300x300 tile) are in `design/store`. They are built in Photoshop: `Make-StoreTemplates.jsx` makes the layered PSD templates in `templates` (live text layers, a smart object for the screenshot), and `Make-StoreImages.jsx` fills them with the captions and the window captures in `captures` (72 dpi, or Replace Contents scales them) and saves the PNGs. `Make-StoreArt.ps1` renders the poster, box and super hero art from the logo (ImageMagick).

## UI text

The UI text is in `Strings/<language>/Resources.resw`, en-US being the source; the guided tour's steps and sample templates are in `Tour.resw` next to it, read with `Loc.Tour.Get` / `Loc.Tour.Format` (the PRI's `Tour` resource map), and the editor's and preview's own text in `Editor.resw`. `Loc.Editor.All()` reads that whole map; `EditorPages` writes it into the page as `window.ttmStrings`, ahead of the page's scripts, and sets its `<title>` and `lang` (screen readers read both). `main.js` looks texts up with `t('Key', ...)` and `tp('Key', n, ...)`. XAML takes it through `x:Uid` (key `Uid.Property`, for example `SupportPage_Title.Text`, or `Uid.[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name` for a Narrator name); code through `Helpers/Loc` (`Loc.Get` / `Loc.Format`, key `Area_Purpose`), which reads it with the Windows App SDK's `ResourceLoader`. Sentences are whole strings with `{0}` placeholders, never put together in code; keys are always written out in the `Loc` call, so the tests find them. A count takes `Loc.Plural(key, count, ...)`, which picks `key_One` or `key_Other` with the count as `{0}`; a count phrase inside a sentence ("in 3 folders") is its own key for that sentence, since languages decline it differently. A list of facts (a backup's or a language's details) is joined from separately translated items. Paste mode names are translated in the app (`PasteModeLabel.For`), the browser connector keeps the English ones (`PasteModeLabel.Invariant`). Only what the user reads in the UI is translated; logs, the log export and support details stay English, and dates and numbers keep their current formats. All UI text reads from there (a test fails on literal UI text in XAML; product and format names such as TextTemplateManager or HTML/Jira stay literal, as do the editor's code-language names).

Settings ▸ General ▸ Language offers "Use the Windows language" and the languages in `UiLanguages.Available` (a test keeps that list equal to the `Strings` folders; Debug builds add the pseudo-language). The choice is `ui-language.txt` in the data folder, so it stays on the device and out of settings, sync files and backups; it applies from the next start, when `UiLanguages.Apply` sets it as `ApplicationLanguages.PrimaryLanguageOverride`. Windows itself keeps that override only for a packaged app (an unpackaged run forgot it), so the file is the record. `TTM_UI_LANGUAGE` overrides it for a run without saving it.

The package's languages come from the language folders (`x-generate` in the manifest): a folder added under `Strings` makes the Store expect a listing in that language.

Debug builds also carry the pseudo-language `qps-ploc`: the `GeneratePseudoLoc` target runs `build/New-PseudoLoc.ps1` over en-US into `obj\...\PseudoLoc\Resources.lang-qps-ploc.resw` (the language is in the file name there). Every letter gets an accent and every text is wrapped in `[ ]` and padded by about 40%, so text that is still plain English wasn't moved, and text that loses its `]` gets cut off. Release and Store builds never generate it, so the package never lists it. A run with `TTM_UI_LANGUAGE=qps-ploc` shows it, and a Debug build's language switch offers it.

## Checks and dependency updates

- `tests.yml` runs the test suite on every push to main (except docs-only changes) and on every pull request.
- `codeql.yml` scans C#, the editor's JavaScript and the workflows with CodeQL (build-mode none) on pushes, pull requests and weekly. It skips the committed editor bundle, which is minified third-party code.
- `bergamot-check.yml` runs `build/Check-Bergamot.ps1` every Monday (and by hand): Dependabot can't see the Bergamot engine, which Firefox loads from Remote Settings. It compares the engine record (`translations-wasm-v2`), the loader's revision in Firefox's `moz.yaml` and the model formats on offer (`translations-models-v2`) with `build/bergamot.json`, and opens an issue, or updates the open one, when Mozilla ships a newer engine, a newer model format, or no more models in the format the shipped engine reads. Scheduled runs happen on the default branch only.
- `dependabot.yml` opens grouped update pull requests monthly for NuGet, npm (`src/editor`) and the GitHub Actions; security updates come as soon as an advisory is out. An npm update changes the app only after `npm ci` and `npm run build` in `src/editor` and a commit of the rebuilt bundle; TipTap major versions are ignored.
- GitHub's automatic dependency submission restores the projects on Linux to list the transitive NuGet packages too; `EnableWindowsTargeting` in the app and test projects allows that (it has no effect on Windows).
- Vulnerabilities are reported privately through the repository's Security tab (`SECURITY.md`).
- Workflow inputs and secrets reach scripts through environment variables, never pasted into the script text.

## Release

![Release workflow](diagrams/act-release.svg)

([source](diagrams/act-release.puml))

The git tag is the only version source. The tag message picks the Store submission: `publish: store` (default), `publish: draft` (uploaded, not committed) or `publish: none`. The `microsoft-store` job uploads the package with the Microsoft Store Developer CLI and does the access check, "What's new" and the commit through the Partner Center submission API (`build/Store-Api.ps1`); a manual run with **Check Store access** runs only that check, and one with **Commit the pending Store submission** commits a `publish: draft` submission (Partner Center shows the package only after the commit). After a committed submission the `publish-when-live` job (or, after the manual commit, `publish-when-live-after-commit`) starts `.github/workflows/publish-when-live.yml`, which runs `build/Publish-WhenLive.ps1` on Ubuntu: it reads the live package version from the Store's public catalog (`displaycatalog.mp.microsoft.com`, market US, no secrets) every 10 minutes and publishes the draft GitHub release once that version or a newer one is live. A job is limited to 6 hours and certification often takes longer, so each run waits about 5.5 hours and then starts the next run itself (`workflow_dispatch`, which the workflow token may trigger), until 7 days after the first one. How to publish, the secrets and their renewal are in [`../PUBLISHING.md`](../PUBLISHING.md); `release.md` is the release-notes template (its "What's new" section is also the in-app What's new); `store.md` holds the Store texts, its "What's new" block is sent with the submission.

The documentation website (`.github/workflows/pages.yml`, GitHub Pages) is built by `build/Build-Website.ps1` with MkDocs (Material theme, pinned below MkDocs 2.0): it stages `website/pages`, the manuals from `docs/user`, `PRIVACY.md` and the manual's screenshots (rewriting their links from `Assets/ManualImages` to `images/`) and builds with `--strict`, so a broken page or section link fails it. The look follows the slate Store art: colors and the home page's hero, feature cards and gallery are in `website/pages/stylesheets/ttm.css` and `website/overrides/home.html`, the hero and gallery images are copied from `design/store/slate`. `website/overrides/partials/header.html` (Material's header plus a "Marflow Software ›" link) leads back to the account's landing page, `software.kmarflow.com`; it is a copy of the pinned Material version's partial, so a Material update needs it compared again. The help page offers `website/pages/downloads/Collect-TTMLogs.ps1`, a log collector for problem reports (Windows PowerShell 5.1, no admin rights, read-only): the app's logs from LocalState (not its data), package, policy and watchdog state, and the Store, AppX deployment and application error events of the app, each with the other events of the same operations (ActivityId); user name, computer name and SIDs are replaced. The page's download is `Collect-TTMLogs.cmd`, which the build script makes from the `.ps1`: a batch/PowerShell hybrid that runs the script's text through `powershell -Command`, so a double-click works whatever opens `.ps1` files and whatever the execution policy, and the window stays open at the end; it passes no arguments (the `.ps1` takes them). The app's **Settings ▸ Support ▸ Export logs** (`LogExport`) writes the same files with the same event selection; the script is for an app that doesn't start, so changes to what is collected go into both. Problem reports and feature requests use the GitHub issue forms in `.github/ISSUE_TEMPLATE` (`bug_report.yml`, `feature_request.yml`; blank issues off); the site's e-mail links (help and home page) carry the same questions as a `mailto:` subject and body. It runs on pushes to `main` that touch those sources. The site is served under the account's Pages domain, `software.kmarflow.com/TextTemplateManager-v2/`.

---

[← 10. Guided tour](10-guided-tour.md) · [Contents](README.md) · [12. Testing →](12-testing.md)
