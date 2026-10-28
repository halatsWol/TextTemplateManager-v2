# Microsoft Store submission

The Partner Center settings that don't depend on the language, to copy as they are. The Store listing, "What's new" and screenshots of each language are in that release's folder, `releases/vX.Y.Z/<language>/` (`store.md`, `screenshots/`).

---

## Properties

- **Category:** Productivity. Secondary category (optional): Utilities & tools.
- **Personal information:** No. The app doesn't access, collect or transmit personal information.
- **System requirements:** minimum: Keyboard only. Recommended: Keyboard and Mouse. Leave memory, DirectX and the rest unset. Windows version and x64 come from the package.

### Privacy policy

Partner Center requires a URL, because the package declares restricted capabilities (`runFullTrust`). The policy is [`PRIVACY.md`](PRIVACY.md) in this repository, published on the website (its Privacy page); keep it and the text below in step.

```text
https://software.kmarflow.com/TextTemplateManager-v2/privacy/
```

The policy text:

```text
TextTemplateManager does not collect, transmit or share any personal information.

Your templates, settings and backups are stored only on your PC: the app's data folder, and backups in Documents\TextTemplateManager\Backups. Shared template files (.ttmdata) are read from and written to the folders you choose, for example a OneDrive folder or a network share; their syncing is done by that service, not by the app.

The app sends no telemetry or usage data and makes no internet connections of its own, except asking the Microsoft Store, through Windows, for updates to TextTemplateManager itself (Settings > General > Updates; an organization can turn this off), and the translation downloads below. The optional browser connector listens only on this PC (localhost), is protected by a pairing token, and only answers the TTM-Connect browser extension you pair it with.

Translation runs on your PC. Only Settings > Translation goes online: when you open it, the app loads the list of languages from Mozilla's servers (firefox.settings.services.mozilla.com), and when you download a language, it downloads that language's files from Mozilla's download server (firefox-settings-attachments.cdn.mozilla.net). If you turn on the daily update check there (off by default), the app also loads that list once a day and, if you allow it, downloads newer versions of the languages you installed. These requests carry the app's name and version, as any download does, and nothing else; the text you translate never leaves your PC. Mozilla's privacy policy applies to its servers.

Settings > Support > Export logs saves the app's logs and the Windows events about it as a .zip file where you choose, for you to attach to a problem report; the app doesn't send it anywhere. Your user name and computer name are replaced in it.

Links in the app (manual, website, browser extension stores, e-mail) open in your browser or mail app.

Questions: Marflow Software, contact@kmarflow.com, https://software.kmarflow.com/TextTemplateManager-v2/
```

### Support info

- **Website:** https://software.kmarflow.com/TextTemplateManager-v2/
- **Support contact info:** contact@kmarflow.com

---

## Pricing and availability, age rating, packages

Suggested answers; the decisions are yours.

- **Pricing and availability:** Free, no free trial, no in-app purchases. Markets: all. Visibility: public, available in the Store. Publish as soon as certified (or set a date).
- **Age rating (IARC questionnaire):** category Productivity/Utility. No violence, sexual content, bad language, drugs, gambling or scary content. Users don't interact or share content with other users through the app (shared sync files are the user's own files in their own storage), no location sharing, no digital purchases, no unrestricted web browsing (links open the system browser). Expected result: suitable for all ages.
- **Packages:** the `.msixupload` from the release run's `TextTemplateManager-<version>` artifact. Remove any test package of the same version from the submission first. Device family: Windows 10/11 desktop (minimum 10.0.19041, from the package).

---

## Submission options: notes for certification

Update the date with each submission.

```text
Notes from 2026-10-29. Thank you for testing TextTemplateManager.

No account, sign-in, service or internet connection is needed; everything runs and is stored locally. Only the optional translation goes online, to download language files from Mozilla's servers (Settings > Translation).

Getting started
1. Start the app. On a PC without earlier data it offers a guided tour (Take the tour) that shows the main window, the settings and Quick Paste on sample templates.
2. To try it with your own text: click Add Template in the toolbar, enter a title and some text, set a Single Key (e.g. A) and click Save.

Quick Paste (the main feature)
3. Open Notepad and click into it.
4. Press Shift+Alt+Y. The Quick Paste window opens.
5. Press the template's Single Key (A): the text is pasted into Notepad. Multi-key shortcuts work by holding Alt and typing the shortcut.

Things that may not be obvious
- Closing the main window keeps the app running in the system tray (notification area), so the hotkey keeps working. Quit with File > Exit or the tray icon's menu.
- The app registers a startup task: it starts with Windows, hidden in the tray. Windows Settings > Apps > Startup turns it off.
- Settings: File > Settings (General, Integrations, Sync, Backups, Translation, Support).
- The app is in English, German, Hungarian, Italian, French and Spanish. It starts in the Windows display language (otherwise English); Settings > General > Language switches it after a restart.
- Settings > Support > Export logs reads the app's own log files and the Microsoft Store, app installation and application error event logs (no admin rights needed) and saves them as a .zip where the user chooses; nothing is sent. The e-mail buttons there open the default mail app with a prefilled message; the GitHub buttons open the browser.
- Sharing templates: Settings > Sync > Create makes a shared .ttmdata file in a folder of your choice; it appears as a pinned folder in the tree.
- Backups are written to Documents\TextTemplateManager\Backups; Settings > Backups restores them.
- The browser connector (Settings > Integrations) is off by default. It listens only on localhost and is used by our separate TTM-Connect browser extension; it is not needed to test the app.
- Importing from TextTemplateManager 1 is only offered when data of that older, separately installed version exists on the PC.
```

---

## Submission options: restricted capabilities

Partner Center asks why the package declares `runFullTrust`:

```text
TextTemplateManager is a desktop app (WinUI 3, Windows App SDK) that runs as a full-trust process. It needs this to register a system-wide hotkey that opens its Quick Paste window from any application, to paste the chosen template into the window the user was working in (clipboard and simulated Ctrl+V), to keep running in the system tray, and to read and write the shared template files the user picks in any folder, such as OneDrive or a network share.
```
