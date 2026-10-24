## TextTemplateManager {{VERSION}}

A hotkey-driven text-template paste tool for Windows: organize reusable snippets in folders, give them shortcuts, and paste them into any application from a global Quick Paste window.

### Install

Get **TextTemplateManager** from the [Microsoft Store](https://apps.microsoft.com/detail/9NMBSZLLLN6V). The Store installs and updates it; no administrator rights are needed. In a company it can also be deployed with winget or Intune (see the Administrator & Deployment Guide below).

Coming from TextTemplateManager 1: your templates and settings are backed up at the first start and imported in one step if you choose so; version 1's data folder is left as it was.

> **Note — Remote Desktop (RDP):** If TextTemplateManager runs on both your local computer and a remote computer you connect to over RDP, the global Quick Paste hotkey always opens Quick Paste on the **local** computer — Windows delivers a registered global hotkey locally, so it never reaches the remote session. Give each machine a **different** Quick Paste hotkey, or run only one instance, to avoid the clash.

### What's new in this release

Translate templates on your PC, offline, in over 50 languages (experimental).

#### New

- **Translate** (the button at the right end of the editor toolbar): translate a template or the selected text. **From** detects the language or takes the one you pick, **To** is remembered. **Replace template** and **Replace selection** translate in place; **Create new template** makes a translated copy in the same folder, with the language on the title and the multi-key shortcut (`ANDMSG` becomes `ANDMSGDE`) and no single key. For German, **Formal** picks Sie or du (du is best effort). Placeholders and formatting stay as they were. Machine translation makes mistakes: check every translation.
- **Settings ▸ Translation** (experimental): download a language once from Mozilla's servers (the Firefox Translations models); from then on translating runs offline, and the text you translate never leaves your PC. Languages can be updated, removed, and exported and imported for a PC without internet access. Optionally the app checks once a day for newer versions of the installed languages and downloads them on its own; otherwise **Update** on a language or **Download all updates** gets them.

#### Improved

- The editor's toolbar menus open right below their own button, also when the toolbar wraps onto a second line.
- **Help ▸ About ▸ Third-party notices** lists all bundled components.

#### Fixed

- The app could crash right after its window was closed while it was waiting to install an update in idle time.
- After a version switch, an update could show as ready when it wasn't, and **Restart to update** then installed the version already running; **Download** and **Check for updates** now ask the Store again.

{{CHANGELOG}}

### Uninstall / reset

In Windows **Settings ▸ Apps ▸ Installed apps ▸ TextTemplateManager ▸ …**: **Advanced options** offers **Reset** (deletes the app's data) and **Repair**; **Uninstall** removes the app and its data. With PowerShell:

    Get-AppxPackage *TextTemplateManager* | Remove-AppxPackage

The backups in `Documents\TextTemplateManager\Backups` stay; delete them by hand if you no longer need them.

### Browser extension

Pair TextTemplateManager with the companion **TTM-Connect** extension to list and paste your templates from your browser:

- [Chrome Web Store](https://chrome.google.com/webstore/detail/jclopjpjdldbknjdhmjldehlkgbihlmi)
- [Microsoft Edge Add-ons](https://microsoftedge.microsoft.com/addons/detail/ttm-connect/fbpmhopmnaoedcnbegdkpfblekhaindm)
- [Firefox Add-ons](https://addons.mozilla.org/addon/ttm-connect/)

---

Full documentation is available in the app under **Help ▸ Open Manual**, or download **`TextTemplateManager-Manual.pdf`** from the assets below; deployment, updates under company policies and removal are in **`TextTemplateManager-AdminManual.pdf`**.
Website, help and problem reports: https://software.kmarflow.com/TextTemplateManager-v2/ · Source: https://github.com/halatsWol/TextTemplateManager-v2
