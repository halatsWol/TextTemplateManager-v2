# Microsoft Store texts: English (en-US), 2.6.0

The Store listing and "What's new" for this release, to copy into Partner Center as they are (plain text; the Store fields take no Markdown, HTML or links in the description). The release workflow also sends the "What's new" text with an automatic submission (`build/Release-Notes.ps1 -StoreOut`); it must stay within the Store's 1500 characters. The submission settings that don't depend on the language are in [`store.md`](../../../store.md) at the repository root.

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

Translate, offline (experimental)
Translate a template or a selection into over 50 languages, in place or as a translated copy, with formal or informal German. Download a language once in Settings; the translation then runs on your PC, and your text never leaves it.

Safe with your data
Changes are saved when you click Save, or as you type with Autosave. Automatic backups are made after your changes, every two hours and daily, in your Documents folder; restore all or part of one, or export a backup to move to another PC. Everything stays on your PC and in the shared files you choose; nothing is sent anywhere. Only the language downloads for translation come from Mozilla's servers.

More
- Starts with Windows, hidden in the system tray, if you like.
- A guided tour on sample templates and a built-in manual.
- Import and export of .ttmdata files.
- An optional local connector for the TTM-Connect browser extension (Chrome, Edge, Firefox) to list, paste and create templates from your browser.
- In English, German, Hungarian, Italian, French and Spanish.

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
Offline translation in over 50 languages (experimental)
Save when you're ready, or Autosave as you type
Automatic backups with restore and export
Import and export of .ttmdata files
Starts with Windows, hidden in the system tray
Guided tour and built-in manual
Optional local connector for the TTM-Connect browser extension
Imports your templates from TextTemplateManager 1
In English, German, Hungarian, Italian, French and Spanish
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

From `screenshots/` in this folder (2560x1440 PNG, one colour scheme), uploaded in this order; the caption field (optional, up to 200 characters) takes the text below. They are made with the scripts in [`design/store`](../../../design/store). The images without text are the same in every language and stay in `design/store`: the Store tile `StoreTile-300.png` (Store logos, 1:1 app tile icon, 300x300) and, from one scheme folder (`amber/` or `slate/`), `poster-art-<scheme>.png` for the 2:3 poster art (1440x2160), `box-art-<scheme>.png` for the 1:1 box art (2160x2160) and `super-hero-art-<scheme>.png` for the 16:9 super hero art (3840x2160, no text, as required).

```text
01  Your text templates, in one place: folders, rich text, tags and shortcuts for every reply you write again and again.
02  Paste any template with a single key: Shift+Alt+Y opens Quick Paste in any app.
03  Or hold Alt and type a short code: multi-key shortcuts narrow the list as you type.
04  Pasted in the format the app needs: Auto, HTML/Jira, HTML, RTF, Markdown or plain text.
05  Find any template as you type, by title, tags or the text itself, fuzzy or strict.
06  Share templates with your team through a synced file on OneDrive or a network share.
07  Automatic backups you can restore: after your changes, every two hours and daily.
08  A guided tour gets you started, on sample templates; yours stay untouched.
09  Translate templates on your PC, offline in over 50 languages, formal German included (experimental).
```

### Copyright and trademark info

```text
Copyright © 2026 Marflow Software
```

---

## What's new in this version

This block is the Store's "What's new" (Microsoft asks for it on updates only). It lists only what changed since the previous release; keep it in step with `release.md` in this folder.

```text
TextTemplateManager now speaks German, Hungarian, Italian, French and Spanish.

- New: the app in six languages: English, German, Hungarian, Italian, French and Spanish (one Spanish for Spain and Latin America). It starts in your Windows display language, or English; Settings > General > Language picks another one. Windows, menus, messages, the guided tour and the editor are translated; your templates aren't changed.
- Hungarian, Italian, French and Spanish were translated with the help of AI and haven't been proofread yet: Settings > General > Report a translation problem.
- Improved: dates, weekday names, key names and language names follow the app's language.
- Improved: if no e-mail window opens, the app points to the Windows setting for e-mail links and to step-by-step help.
- Fixed: a crash when the app was started a second time while its first start was still under way.
```
