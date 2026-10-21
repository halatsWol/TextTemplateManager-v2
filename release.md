## TextTemplateManager {{VERSION}}

A hotkey-driven text-template paste tool for Windows: organize reusable snippets in folders, give them shortcuts, and paste them into any application from a global Quick Paste window.

### Install

Get **TextTemplateManager** from the [Microsoft Store](https://apps.microsoft.com/detail/9NMBSZLLLN6V). The Store installs and updates it; no administrator rights are needed. In a company it can also be deployed with winget or Intune (see the Administrator & Deployment Guide below).

Coming from TextTemplateManager 1: your templates and settings are backed up at the first start and imported in one step if you choose so; version 1's data folder is left as it was.

> **Note — Remote Desktop (RDP):** If TextTemplateManager runs on both your local computer and a remote computer you connect to over RDP, the global Quick Paste hotkey always opens Quick Paste on the **local** computer — Windows delivers a registered global hotkey locally, so it never reaches the remote session. Give each machine a **different** Quick Paste hotkey, or run only one instance, to avoid the clash.

### What's new in this release

In-app updates that arrive reliably, also on PCs where automatic Store updates are turned off.

#### Fixed

- **Check for updates could say "Up to date" although a new version was out.** The Microsoft Store answers an app only one update check every 30 minutes and ten a day, and otherwise repeats its last answer. TextTemplateManager now keeps to these limits, also across restarts, and when you check sooner, **Settings ▸ General ▸ Updates** says when the next check is possible. Checks by others (the Microsoft Store app, winget) count too: when the Store refuses a check for that reason, TextTemplateManager now says so instead of "Up to date".
- **On PCs with automatic Store updates turned off, a new version didn't show up as ready.** Windows Update often downloads it there anyway, but TextTemplateManager didn't notice and tried winget instead, which can't update it, and then said the download had finished. It now finds a version Windows has downloaded and offers **Restart to update**; winget is no longer used.
- **While downloading, Settings showed "Downloading version Version ...".** It now says "Downloading the update".

#### Improved

- **Check for updates downloads a new version right away.** Where automatic Store updates are turned off, the Microsoft Store asks once first; if it can't download on your PC, TextTemplateManager says so and the update comes through Windows Update.
- **Clearer update texts** in **Settings ▸ General ▸ Updates**, in the update notice in the main window and in the tray notice.

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
Source: https://github.com/halatsWol/TextTemplateManager-v2
