## TextTemplateManager {{VERSION}}

A hotkey-driven text-template paste tool for Windows: organize reusable snippets in folders, give them shortcuts, and paste them into any application from a global Quick Paste window.

### Install

Get **TextTemplateManager** from the [Microsoft Store](https://apps.microsoft.com/detail/9NMBSZLLLN6V). The Store installs and updates it; no administrator rights are needed. In a company it can also be deployed with winget or Intune (see the Administrator & Deployment Guide below).

Coming from TextTemplateManager 1: your templates and settings are backed up at the first start and imported in one step if you choose so; version 1's data folder is left as it was.

> **Note — Remote Desktop (RDP):** If TextTemplateManager runs on both your local computer and a remote computer you connect to over RDP, the global Quick Paste hotkey always opens Quick Paste on the **local** computer — Windows delivers a registered global hotkey locally, so it never reaches the remote session. Give each machine a **different** Quick Paste hotkey, or run only one instance, to avoid the clash.

### What's new in this release

Updates that download on their own, also where automatic Store updates are turned off.

#### Fixed

- **Check for updates didn't download an update it had already found** when you clicked it within 30 minutes of the last check; it now downloads it right away.
- **The tray menu showed Restart to update without an update ready**, and clicking it did nothing. It now appears only with an update ready.
- **A download that failed once said the Store couldn't download the update at all** and pointed to Windows Update. It now shows the error with **Try again**, which downloads again straight away.

#### Improved

- **Settings ▸ General ▸ Updates has the update's next step itself**: its button becomes **Download**, **Try again** or **Restart to update**, so you don't need to go back to the main window.
- **Automatic updates also download where automatic Store updates are turned off**, with **Check for and download updates automatically** on (unless your organization doesn't allow it). Until now you had to click **Download**.

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
