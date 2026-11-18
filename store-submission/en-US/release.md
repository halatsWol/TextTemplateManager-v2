## TextTemplateManager {{VERSION}}

A hotkey-driven text-template paste tool for Windows: organize reusable snippets in folders, give them shortcuts, and paste them into any application from a global Quick Paste window.

### Install

Get **TextTemplateManager** from the [Microsoft Store](https://apps.microsoft.com/detail/9NMBSZLLLN6V). The Store installs and updates it; no administrator rights are needed. In a company it can also be deployed with winget or Intune (see the Administrator & Deployment Guide below).

Coming from TextTemplateManager 1: your templates and settings are backed up at the first start and imported in one step if you choose so; version 1's data folder is left as it was.

> **Note — Remote Desktop (RDP):** If TextTemplateManager runs on both your local computer and a remote computer you connect to over RDP, the global Quick Paste hotkey always opens Quick Paste on the **local** computer — Windows delivers a registered global hotkey locally, so it never reaches the remote session. Give each machine a **different** Quick Paste hotkey, or run only one instance, to avoid the clash.

### What's new in this release

#### New

- **Templates in several languages:** a template can now hold its text in more than one language, one tab each above the editor's toolbar, with one title and one set of shortcuts. **+** adds a language (recognized from the text where possible), and **Translate ▸ Add as language** translates into a new tab. The first tab is the default that is pasted; **Copy Language** in the tree's right-click menu, **Paste Language** in Quick Paste and **← / →** on Quick Paste's Multi Key list pick another one. Search looks through every language.
- **Browser extension:** with TTM-Connect 1.2 or newer you can also paste a template's other languages and save a selection as a language of a template. Older versions of the extension keep working as before.
- **Synced folders:** templates in several languages need TextTemplateManager 2.7 or newer on every PC that saves a shared file; TextTemplateManager 1 and 2.6 or older drop the other languages when they save it.

#### Improved

- **Runs on .NET 10:** TextTemplateManager now runs on .NET 10, which Microsoft supplies with security updates until November 2028. .NET 8, used by earlier versions, no longer gets any since November 10, 2026. Your templates, settings and the way you work stay the same.
- **Clearer icons:** folders and templates are easier to tell apart in the tree and on the buttons that add them.

#### Fixed

- **Shared files:** saving to a synced file no longer overwrites changes another PC made to it since it was read; they are merged at the next check instead.
- **Exit:** quitting now waits for a settings save that is already in progress.

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
