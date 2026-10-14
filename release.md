## TextTemplateManager {{VERSION}}

A hotkey-driven text-template paste tool for Windows: organize reusable snippets in folders, give them shortcuts, and paste them into any application from a global Quick Paste window.

### Install

Get **TextTemplateManager** from the [Microsoft Store](https://apps.microsoft.com/detail/9NMBSZLLLN6V). The Store installs and updates it; no administrator rights are needed. In a company it can also be deployed with winget or Intune (see the Administrator & Deployment Guide below).

Coming from TextTemplateManager 1: your templates and settings are backed up at the first start and offered for import; version 1's data folder is left as it was.

> **Note — Remote Desktop (RDP):** If TextTemplateManager runs on both your local computer and a remote computer you connect to over RDP, the global Quick Paste hotkey always opens Quick Paste on the **local** computer — Windows delivers a registered global hotkey locally, so it never reaches the remote session. Give each machine a **different** Quick Paste hotkey, or run only one instance, to avoid the clash.

### What's new in this release

TextTemplateManager 2 comes from the Microsoft Store, which installs and updates it.

#### New

- **Moving from version 1.** At the first start, version 1's templates, settings and sync settings are backed up once and offered for import, and its start-with-Windows choice carries over. A version 1 that is still installed can be removed from here.
- **Automatic backups.** After your changes, every two hours and daily, in `Documents\TextTemplateManager\Backups`. **Settings ▸ Backups** restores all or part of a backup and exports one to move to another PC.
- **Save when you're ready.** Changes to a template or folder wait for **Save** (or **Ctrl+S**), and you're asked before leaving unsaved changes. **Autosave** in **Settings ▸ General** saves as you type instead.
- **Import and export.** **File ▸ Import…** merges a `.ttmdata` file into your tree; **File ▸ Export…** saves the selection or everything.
- **Several items at once.** **Ctrl**+click and **Shift**+click select several items to move, export or delete together.
- **Strict search.** Finds exactly what you type, with `*` and `.` as wildcards; tags match in any order. Search can also look in the template text.
- **Missing sync files.** A synced folder whose file is gone keeps showing its last contents and can recreate the file.
- **Guided tour.** **Help ▸ Start tutorial** shows the main window, the settings and Quick Paste on sample templates.
- **Collapse all and expand all** in the tree toolbar.
- **Keyboard.** **Tab** goes round the search box, the tree and the selected item's fields; typing in the tree carries on in the search box.

#### Changed

- Updates come from the Microsoft Store; the app's own updater is gone. Start with Windows is now Windows' own startup setting (**Settings ▸ Apps ▸ Startup**), on and starting hidden by default.
- After an unexpected error, your changes are saved and you can choose to quit or keep running.
- Pastes are kept out of the Windows clipboard history.

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
