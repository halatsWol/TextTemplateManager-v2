# Microsoft Store texts

Texts for the Partner Center submission, to copy as they are (plain text; the Store fields take no Markdown, HTML or links in the description). The release workflow also sends the "What's new" text with an automatic submission (`build/Release-Notes.ps1 -StoreOut`); it must stay within the Store's 1500 characters.

Limits (Partner Center, 2026): description 10,000 characters; short description 1,000 (only the first 270 show in some views); up to 20 product features of 200 characters each; "What's new" 1,500.

---

## Store listing (English)

### Product name

```text
TextTemplateManager
```

### Short description

```text
Reusable text, one keystroke away. Keep replies, snippets and signatures in folders, give them shortcuts, and paste them into any app from Quick Paste. Share templates with your team through a synced file.
```

### Description

```text
TextTemplateManager keeps the text you write again and again (replies, snippets, signatures, code) in one place and puts it into any application with a keystroke.

Press the Quick Paste hotkey wherever you are typing: a small window opens with your templates. Press a template's single key, or hold Alt and type its multi-key shortcut, and the text is pasted where you were, formatted to fit the app you paste into.

Built for teams
Share templates through a .ttmdata file in a cloud folder such as OneDrive or on a network share. Each shared file shows as a pinned folder; it can be read-only for some people, and a shortcut prefix keeps everyone's shortcuts apart. Everyone uses only the shared folders they need, next to their own templates.

Rich templates
Write with headings, lists, tables, colors, code blocks with syntax highlighting and callout panels. Paste modes decide the format: Auto picks the best one for the target app; HTML/Jira keeps callout panels for Jira; HTML, RTF, Markdown and plain text paste just that.

Find and organize
Folders with drag and drop, several items at once, collapse and expand all. Search the tree as you type, fuzzy or strict with wildcards, by title, tags or the template text.

Safe with your data
Changes are saved when you click Save, or as you type with Autosave. Automatic backups are made after your changes, every two hours and daily, in your Documents folder; restore all or part of one, or export a backup to move to another PC. Everything stays on your PC and in the shared files you choose; nothing is sent anywhere.

More
- Starts with Windows, hidden in the system tray, if you like.
- A guided tour on sample templates and a built-in manual.
- Import and export of .ttmdata files.
- An optional local connector for the TTM-Connect browser extension (Chrome, Edge, Firefox) to list, paste and create templates from your browser.

Coming from TextTemplateManager 1? At the first start, your templates, settings and sync settings are backed up and offered for import, and version 1 can be removed for you.
```

### Product features

One per field in Partner Center (no bullets).

```text
Quick Paste from any app with a global hotkey
Paste by single key, or hold Alt and type a multi-key shortcut
Rich text: headings, lists, tables, colors, code blocks and callout panels
Paste modes: Auto, HTML/Jira, HTML, RTF, Markdown and plain text
Share templates with your team through a synced file on OneDrive or a network share
Read-only shared folders and shortcut prefixes per shared file
Folders, drag and drop, and several items at once
Fuzzy or strict search by title, tags or template text
Save when you're ready, or Autosave as you type
Automatic backups with restore and export
Import and export of .ttmdata files
Starts with Windows, hidden in the system tray
Guided tour and built-in manual
Optional local connector for the TTM-Connect browser extension
Imports your templates from TextTemplateManager 1
```

### Search terms

One per field, at most 7, each under 30 characters, 21 words in all; no brand names of other products.

```text
text templates
text snippets
quick paste
canned responses
reply templates
email templates
snippet manager
```

### Screenshots and logo

From `design/store`: one colour scheme, `amber/` or `slate/` (2560x1440 PNG), uploaded in this order; the caption field (optional, up to 200 characters) takes the text below. The Store tile is `StoreTile-300.png` (Store logos, 1:1 app tile icon, 300x300). From the same scheme folder: `poster-art-<scheme>.png` for the 2:3 poster art (1440x2160), `box-art-<scheme>.png` for the 1:1 box art (2160x2160) and `super-hero-art-<scheme>.png` for the 16:9 super hero art (3840x2160, no text, as required).

```text
01  Your text templates, in one place: folders, rich text, tags and shortcuts for every reply you write again and again.
02  Paste any template with a single key: Shift+Alt+Y opens Quick Paste in any app.
03  Or hold Alt and type a short code: multi-key shortcuts narrow the list as you type.
04  Pasted in the format the app needs: Auto, HTML/Jira, HTML, RTF, Markdown or plain text.
05  Find any template as you type, by title, tags or the text itself, fuzzy or strict.
06  Share templates with your team through a synced file on OneDrive or a network share.
07  Automatic backups you can restore: after your changes, every two hours and daily.
08  A guided tour gets you started, on sample templates; yours stay untouched.
```

### Copyright and trademark info

```text
Copyright © 2026 Marflow Software
```

---

## What's new in this version

This block is the Store's "What's new" (Microsoft asks for it on updates only; it stayed empty for 2.0.0). It lists only what changed since the previous release; keep it in step with `release.md`.

```text
In-app updates find Store updates again, and one switch for automatic updates.

- Fixed: Check for updates said Up to date although a newer version was out. TextTemplateManager now downloads updates in the background and shows Update ready as intended. This update itself still arrives the old way (the Store, or quitting the app and starting it again).
- Settings > General > Updates: Check for and download updates automatically now also covers PCs where automatic Store updates are turned off. Where the organization allows it, TextTemplateManager still downloads its own updates there, and a note under the switch says so. The separate Keep updating switch is gone.
- For administrators: on PCs with automatic Store updates turned off, the app now keeps updating itself by default. To keep the Store policy final for this app, set the AllowUpdateDespiteStorePolicy policy to 0; see the Administrator & Deployment Guide.
```

---

## Properties

- **Category:** Productivity. Secondary category (optional): Utilities & tools.
- **Personal information:** No. The app doesn't access, collect or transmit personal information.
- **System requirements:** minimum: Keyboard only. Recommended: Keyboard and Mouse. Leave memory, DirectX and the rest unset. Windows version and x64 come from the package.

### Privacy policy

Partner Center requires a URL, because the package declares restricted capabilities (`runFullTrust`). The policy is [`PRIVACY.md`](PRIVACY.md) in this repository; keep it and the text below in step.

```text
https://github.com/halatsWol/TextTemplateManager-v2/blob/main/PRIVACY.md
```

The policy text:

```text
TextTemplateManager does not collect, transmit or share any personal information.

Your templates, settings and backups are stored only on your PC: the app's data folder, and backups in Documents\TextTemplateManager\Backups. Shared template files (.ttmdata) are read from and written to the folders you choose, for example a OneDrive folder or a network share; their syncing is done by that service, not by the app.

The app sends no telemetry or usage data and makes no internet connections of its own, except asking the Microsoft Store, through Windows, for updates to TextTemplateManager itself (Settings > General > Updates; an organization can turn this off). The optional browser connector listens only on this PC (localhost), is protected by a pairing token, and only answers the TTM-Connect browser extension you pair it with.

Links in the app (manual, website, browser extension stores) open in your browser.

Questions: Marflow Software, https://github.com/halatsWol/TextTemplateManager-v2/issues
```

### Support info

- **Website:** https://github.com/halatsWol/TextTemplateManager-v2
- **Support contact info:** https://github.com/halatsWol/TextTemplateManager-v2/issues

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
Notes from 2026-10-17. Thank you for testing TextTemplateManager.

No account, sign-in, service or internet connection is needed; everything runs and is stored locally.

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
- Settings: File > Settings (General, Integrations, Sync, Backups).
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
