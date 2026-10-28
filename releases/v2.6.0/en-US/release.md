## TextTemplateManager {{VERSION}}

A hotkey-driven text-template paste tool for Windows: organize reusable snippets in folders, give them shortcuts, and paste them into any application from a global Quick Paste window.

### Install

Get **TextTemplateManager** from the [Microsoft Store](https://apps.microsoft.com/detail/9NMBSZLLLN6V). The Store installs and updates it; no administrator rights are needed. In a company it can also be deployed with winget or Intune (see the Administrator & Deployment Guide below).

Coming from TextTemplateManager 1: your templates and settings are backed up at the first start and imported in one step if you choose so; version 1's data folder is left as it was.

> **Note — Remote Desktop (RDP):** If TextTemplateManager runs on both your local computer and a remote computer you connect to over RDP, the global Quick Paste hotkey always opens Quick Paste on the **local** computer — Windows delivers a registered global hotkey locally, so it never reaches the remote session. Give each machine a **different** Quick Paste hotkey, or run only one instance, to avoid the clash.

### What's new in this release

TextTemplateManager now speaks German, Hungarian, Italian, French and Spanish.

#### New

- **The app in six languages:** English, German, Hungarian, Italian, French and Spanish (one Spanish for Spain and Latin America). It starts in your Windows display language, or English if that isn't one of them; **Settings ▸ General ▸ Language** picks another one for this PC, and **Restart now** switches to it. Windows, menus, messages, the guided tour and its sample templates, and the editor are translated; your templates aren't changed.
- Hungarian, Italian, French and Spanish were translated with the help of AI and haven't been proofread yet. **Settings ▸ General ▸ Report a translation problem** opens a prepared e-mail; we're grateful for every report.

#### Improved

- Dates and weekday names (backups, sync, updates) follow the app's language; numbers keep your region's format.
- Key names in shortcuts follow the app's language (for example Strg in German), and so do the language names under **Translate** and in **Settings ▸ Translation**.
- If no e-mail window opens after an e-mail button in **Settings ▸ Support**, the app says what to check: **Open Default apps** goes to the Windows setting for e-mail links, **Step-by-step help** to the instructions on the website. The address is on the clipboard either way.

#### Fixed

- The app could crash when it was started a second time (for example from the Start menu or a `.ttmdata` file) while its first start was still under way.

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
