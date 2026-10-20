# 11. Platform, build and release

## Packaged and dev runs

`AppEnvironment.Detect()` checks for a package identity and returns one of two `IAppEnvironment`s; everything that differs between the Store package and a dev run from `bin\` goes through it.

| | Packaged (Store) | Unpackaged (dev run) |
| --- | --- | --- |
| Data folder | Package `LocalState` | `%LocalAppData%\Marflow Software\TextTemplateManager 2 Dev` |
| Backups | `Documents\TextTemplateManager\Backups` | `Documents\TextTemplateManager\Backups (Dev)` |
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

The project is unpackaged by default (`WindowsPackageType=None`); `StoreBuild=true` switches to MSIX with the Windows App SDK as a framework dependency and an unsigned bundle (the Store signs it). The XAML compiler fails silently near 260-character paths, so release builds run from a short path. The editor bundle under `Assets/editor` is committed, so the app builds without Node; after changing `src/editor`, run `npm run build` there. `Manual.pdf` is generated from `docs/user/Manual.md` by `tools/ManualGen` during the build. The icon and tile PNGs and `AppIcon.ico` under `Assets` are committed too; they are rendered from `design/logo/logo.svg` by `design/logo/Render-Assets.ps1` (ImageMagick), so after changing the logo run that script. The Store listing images (screenshots in two colour schemes, `amber` and `slate`, and the 300x300 tile) are in `design/store`. They are built in Photoshop: `Make-StoreTemplates.jsx` makes the layered PSD templates in `templates` (live text layers, a smart object for the screenshot), and `Make-StoreImages.jsx` fills them with the captions and the window captures in `captures` (72 dpi, or Replace Contents scales them) and saves the PNGs. `Make-StoreArt.ps1` renders the poster, box and super hero art from the logo (ImageMagick).

## Checks and dependency updates

- `tests.yml` runs the test suite on every push to main (except docs-only changes) and on every pull request.
- `codeql.yml` scans C#, the editor's JavaScript and the workflows with CodeQL (build-mode none) on pushes, pull requests and weekly. It skips the committed editor bundle, which is minified third-party code.
- `dependabot.yml` opens grouped update pull requests monthly for NuGet, npm (`src/editor`) and the GitHub Actions; security updates come as soon as an advisory is out. An npm update changes the app only after `npm ci` and `npm run build` in `src/editor` and a commit of the rebuilt bundle; TipTap major versions are ignored.
- GitHub's automatic dependency submission restores the projects on Linux to list the transitive NuGet packages too; `EnableWindowsTargeting` in the app and test projects allows that (it has no effect on Windows).
- Vulnerabilities are reported privately through the repository's Security tab (`SECURITY.md`).
- Workflow inputs and secrets reach scripts through environment variables, never pasted into the script text.

## Release

![Release workflow](diagrams/act-release.svg)

([source](diagrams/act-release.puml))

The git tag is the only version source. The tag message picks the Store submission: `publish: store` (default), `publish: draft` (uploaded, not committed) or `publish: none`. The `microsoft-store` job uploads the package with the Microsoft Store Developer CLI and does the access check, "What's new" and the commit through the Partner Center submission API (`build/Store-Api.ps1`); a manual run with **Check Store access** runs only that check. How to publish, the secrets and their renewal are in [`../PUBLISHING.md`](../PUBLISHING.md); `release.md` is the release-notes template (its "What's new" section is also the in-app What's new); `store.md` holds the Store texts, its "What's new" block is sent with the submission.

---

[← 10. Guided tour](10-guided-tour.md) · [Contents](README.md) · [12. Testing →](12-testing.md)
