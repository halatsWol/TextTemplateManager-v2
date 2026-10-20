## TextTemplateManager {{VERSION}}

A hotkey-driven text-template paste tool for Windows: organize reusable snippets in folders, give them shortcuts, and paste them into any application from a global Quick Paste window.

### Install

Get **TextTemplateManager** from the [Microsoft Store](https://apps.microsoft.com/detail/9NMBSZLLLN6V). The Store installs and updates it; no administrator rights are needed. In a company it can also be deployed with winget or Intune (see the Administrator & Deployment Guide below).

Coming from TextTemplateManager 1: your templates and settings are backed up at the first start and imported in one step if you choose so; version 1's data folder is left as it was.

> **Note — Remote Desktop (RDP):** If TextTemplateManager runs on both your local computer and a remote computer you connect to over RDP, the global Quick Paste hotkey always opens Quick Paste on the **local** computer — Windows delivers a registered global hotkey locally, so it never reaches the remote session. Give each machine a **different** Quick Paste hotkey, or run only one instance, to avoid the clash.

### What's new in this release

Updates install with one click instead of waiting until Windows gets round to it, and the main window works better from the keyboard.

#### New

- **Updates inside the app.** TextTemplateManager looks for a new version in the Store and downloads it in the background while you work. When it's ready, a notice above the template list (and the tray menu) offers **Restart to update**: your work is saved, the app closes, the new version is installed and the app is back within seconds, showing what's new. Not now? Quitting the app installs it too.
- **Update settings** in **Settings ▸ General ▸ Updates**: the version, **Check for updates**, a switch to update only when you ask, and an optional switch to install updates while the app isn't in use.
- **For administrators:** policy keys under `HKLM\SOFTWARE\Policies\Marflow Software\TextTemplateManager` turn in-app updates off or to notices only, lock the automatic updates and the idle install, and allow updating while automatic Store updates are turned off. See the Administrator & Deployment Guide.

#### Improved

- **Keyboard in the main window.** The toolbar above the tree and the editor toolbar are one **Tab** stop each, with the arrow keys moving along their buttons. **Tab** goes from the editor toolbar into the text and on to the template's shortcuts; in the text it only indents in lists, tables and code blocks (**Ctrl + Tab** always moves on).

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
