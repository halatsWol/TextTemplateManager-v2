## Overview

This guide covers the administrator- and deployment-facing details of TextTemplateManager 2 that everyday users don't need: deployment, updates under company policies, security, moving from version 1, start with Windows, the browser connector, removal, and where the app keeps its files. End-user documentation is in the **User Manual** (in the app under **Help ▸ Open Manual**).

TextTemplateManager 2 is a Microsoft Store app (MSIX package `MarflowSoftware.TextTemplateManager`, Store ID `9NMBSZLLLN6V`, x64). It is installed and registered **per user**, needs no administrator rights, writes nothing to the registry itself, and is updated by the Store. The .NET runtime is part of the package; the Windows App SDK runtime is a Store framework package that Windows installs and services alongside it. The editor and preview use the Microsoft Edge WebView2 Runtime, which Windows 11 and current Windows 10 include.

Requirements: Windows 10 version 2004 (build 19041) or later, x64.

---

## Deployment

### Microsoft Store

Users install it from the Store app or from [its Store page](https://apps.microsoft.com/detail/9NMBSZLLLN6V).

### winget

The Store catalog is available to winget as the `msstore` source:

    winget install --id 9NMBSZLLLN6V --source msstore --accept-package-agreements --accept-source-agreements

Run it in the context of the user who will use the app (the package is registered per user). It needs no Microsoft account on the device: on a managed Windows Enterprise PC without one, the Store's first licence request fails (`0x80070525` in its log), is retried and succeeds. `winget upgrade --id 9NMBSZLLLN6V --source msstore` installs an available update.

### Microsoft Intune

Add it as **Apps ▸ Windows ▸ Add ▸ Microsoft Store app (new)**, search for *TextTemplateManager*, and choose the install behavior (User or System). Intune installs and updates it through winget's `msstore` source, so keep that source enabled (see *Updates*).

### Command-line options

The app takes no deployment options of its own: there are no installer switches, and nothing needs to be preconfigured. Every user starts with the first-start choice described in the User Manual (import from version 1, restore a backup, or start fresh).

---

## Moving from TextTemplateManager 1

TextTemplateManager 2 is a separate app; version 1 stays installed until it is removed. At every start, version 2 closes a running version 1 (within a few seconds), so the two never compete for the Quick Paste shortcut or the browser connector port.

The first time version 2 finds version 1's data (`%LocalAppData%\Marflow Software\TextTemplateManager`), it:

- writes a permanent backup of it (`ttm-backup-v1-<date-time>.zip` in the backups folder, never pruned) and only ever **reads** the version 1 folder, never writes to it;
- takes over version 1's start-with-Windows and start-hidden choices;
- offers the user to import version 1's templates, settings and sync settings (also possible later in **Settings ▸ Backups ▸ Import from TextTemplateManager 1**).

If version 1 is still installed, the user is asked once whether to remove it; **Remove TextTemplateManager 1** runs version 1's own uninstaller, which leaves its data folder in place.

Version 1 and version 2 can share the same sync files at the same time; the file format is the same.

The last version 1 release (1.10.0) points its users to the Store and removes itself once version 2 is installed. For a damaged or half-removed version 1 install, the **TextTemplateManager-Support-Cleanup** utility is still available from the [version 1 releases](https://github.com/halatsWol/TextTemplateManager/releases); it only handles version 1.

---

## Updates

The Microsoft Store updates TextTemplateManager like any other Store app. There is no built-in updater and no update policy of the app's own; version 1's `allowUpdate` registry value has no effect on version 2. Updates are controlled with the Windows and Store policies below.

### When an update installs

An update is installed in two steps: the Store downloads and **stages** the new version next to the installed one, then Windows **registers** it, which switches the app over. Windows never switches a package while the app is running and doesn't close it for a Store update.

- **Automatic updates** come from the Store's background check, which runs on Windows' own schedule (typically every few hours, not when a new version is published). It only gets through when TextTemplateManager isn't running at that moment.
- TextTemplateManager **starts with Windows and runs in the system tray all day**, so that moment can be a long time coming: a machine can stay on an older version for days. Signing out or restarting doesn't reliably help, because the app starts again right at logon.
- When the switch fails because the app is running (`0x80073D02` in the log), the new version stays staged and Windows marks it as waiting. The next time the app is **started from the Start menu while it isn't running**, Windows registers the waiting version first, and the new version starts. A start while the app is still running doesn't count (it only brings up the running window), and a version that is only staged, without a failed switch, isn't picked up at start.
- **Opening the Store app** also checks for updates, and the Store then shows **Update**. It doesn't install by itself; clicking **Update** does.

To bring a machine up to date on purpose:

1. Quit the app completely: tray icon ▸ **Exit App**, or **File ▸ Exit** (closing the window only hides it).
2. Start TextTemplateManager again from the Start menu. If the update was already downloaded and a switch had failed, it's installed now.
3. If **Help ▸ About** still shows the old version, quit the app again, run `winget upgrade --id 9NMBSZLLLN6V --source msstore` or click **Update** in the Store app (**Library**, or the app's Store page), then start the app again.

With Intune, an assigned *Microsoft Store app (new)* is brought up to date by Intune's own schedule, under the same rule: the switch happens while the app isn't running.

### Store restricted by policy

- **"Turn off the Store application"** (Administrative Templates ▸ Windows Components ▸ Store) only blocks the Store app's window; according to Microsoft's documentation, Store apps keep updating automatically, and Intune and winget can still install them. Reports differ on whether this holds when the policy is set at computer level and on Windows 11 24H2 and later, so verify it on a pilot device in your configuration.
- **"Turn off Automatic Download and Install of updates"** (registry: `HKLM\SOFTWARE\Policies\Microsoft\WindowsStore`, `AutoDownload` = 4; 2 = always on) stops automatic updates of all Store apps, TextTemplateManager included. The background check still finds the update but only parks it; nothing is downloaded. Update it then through Intune, `winget upgrade` or **Update** in the Store app, which this policy doesn't block.
- **"Only display the private store within the Microsoft Store app"** / *Require private store only* is reported to block deliveries of public Store apps, including Intune's *Microsoft Store app (new)* deployments.
- **Disabling winget's `msstore` source** (Desktop App Installer policy *Enable App Installer Microsoft Store Source*) also breaks Intune's *Microsoft Store app (new)* deployments and `winget upgrade`.
- **Network:** the Store's download endpoints (and Delivery Optimization) must be reachable; SSL inspection of Microsoft endpoints commonly breaks Store downloads.
- **Windows LTSC / without the Store:** deploy and update through Intune or winget.

### Checking a machine

Installed version, and whether the app is running (and from which version):

    (Get-AppxPackage MarflowSoftware.TextTemplateManager).Version
    Get-Process ttm2 -ErrorAction Ignore | Select-Object StartTime, Path

The Store and Windows log every step of an install or update under **Event Viewer ▸ Applications and Services Logs ▸ Microsoft ▸ Windows**, in **Store ▸ Operational** (search, download, pause, cancel, and who started it: the background check, the Store app or winget) and **AppXDeploymentServer ▸ Operational** (stage, register, remove). Filter for `TextTemplateManager` or `9NMBSZLLLN6V`:

    $log = 'Microsoft-Windows-AppXDeploymentServer/Operational'
    Get-WinEvent -LogName $log -MaxEvents 500 |
        Where-Object Message -match 'TextTemplateManager'

Codes you may meet there:

| Code | Meaning |
| --- | --- |
| `0x80073D02` | The package is in use: the app was running when Windows tried to switch versions. The update now waits; quit the app and start it from the Start menu. |
| `0x80073CF1` | Remove of a package that isn't installed for this user. Harmless; Windows tidies up a leftover or staged copy. |
| `0x80070525` | No Microsoft account for a licence request; the Store retries without one. Harmless if the install completes. |

---

## Security

- **Package:** the Microsoft Store certifies every release and signs the package; Windows installs it per user in its own package folder, without administrator rights. The app collects and sends no data (see the privacy policy, `PRIVACY.md` in the repository).
- **Network:** the app listens only when a user turns on the browser connector, and then only on the local loopback address, with pairing (see *Browser connector*).
- **Source checks:** the public source code is scanned with GitHub CodeQL on every change and weekly, the test suite runs on every change, and Dependabot reports dependencies (NuGet, npm, GitHub Actions) with known vulnerabilities and proposes the fixes. Secret scanning keeps credentials out of the repository.
- **Fixes** ship as a regular Store update (see *Updates*); a published security advisory names the fixed version. Only the current version gets fixes.
- **Reporting a vulnerability:** privately through **Report a vulnerability** on the repository's **Security** tab ([github.com/halatsWol/TextTemplateManager-v2](https://github.com/halatsWol/TextTemplateManager-v2)), not as a public issue.

---

## Start with Windows

The package declares a Windows startup task (`TtmStartup`). It is **on** by default from the first time the user opens the app, and **Start hidden in the system tray** (in the app's **Settings ▸ General**) is on by default too: started at sign-in, the app stays in the tray with the Quick Paste shortcut active. Starting it by hand always opens the window.

The startup task is the same switch as the app's entry in Windows **Settings ▸ Apps ▸ Startup** and on the **Startup** tab of Task Manager. When the user switches it off there, Windows only allows switching it back on there; when a policy enables or disables it, the app's switch is locked and says *Set by your organization*. Windows keeps the state per user (read-only reference) under `HKCU\Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData\MarflowSoftware.TextTemplateManager_eb1r5ey0kd4ny\TtmStartup`, value `State`: `0` off, `1` off by the user in Windows, `2` on.

---

## File association

The package registers the `.ttmdata` file type; Windows adds and removes the association with the package. Opening a `.ttmdata` file starts the app (or brings the running one forward) and adds the file as a sync source. If another program took the file type over, users choose TextTemplateManager again in **Settings ▸ Apps ▸ Default apps**.

---

## Browser connector

The connector for the TTM-Connect browser extension is **off** by default. When a user turns it on (**Settings ▸ Integrations**), the app listens on `127.0.0.1` only (loopback, never on a network interface; no firewall rule is needed), port `47615` by default, and every request must carry the pairing token shown in the settings. If the port is taken or reserved, the app reports it at startup and the user can choose another port. The API is described in `BrowserConnectorApi.md` in the repository.

---

## Reset and removal

Per user, in **Settings ▸ Apps ▸ Installed apps ▸ TextTemplateManager ▸ …**:

- **Advanced options ▸ Reset** deletes the app's data (templates, settings, sync settings); the next start is a first start that offers to restore a backup. **Repair** keeps the data.
- **Uninstall** removes the app and its data folder.

With PowerShell, for the current user:

    Get-AppxPackage MarflowSoftware.TextTemplateManager | Remove-AppxPackage

As an administrator, for every user of the machine (and, if the package was provisioned for new users, its provisioning too):

    Get-AppxPackage -AllUsers MarflowSoftware.TextTemplateManager | Remove-AppxPackage -AllUsers
    Get-AppxProvisionedPackage -Online | Where-Object DisplayName -eq 'MarflowSoftware.TextTemplateManager' | Remove-AppxProvisionedPackage -Online

None of these remove the backups (`Documents\TextTemplateManager\Backups`) or shared sync files; delete them by hand if they are no longer needed. Version 1's data folder is never touched by version 2.

---

## File-system and registry reference

| Path | Contents |
| --- | --- |
| `%LocalAppData%\Packages\MarflowSoftware.TextTemplateManager_eb1r5ey0kd4ny\LocalState` | User data: `data.ttmdata` (templates), `settings.ttmsettings`, `sync.ttmsettings`, `crash.log`, and any `*.broken-<date-time>` copy of a file that couldn't be read at startup. Removed by Reset and Uninstall. |
| `Documents\TextTemplateManager\Backups` | Automatic backups (follows a redirected Documents folder, e.g. OneDrive folder backup). Kept on Reset and Uninstall. |
| Wherever the user puts them | Shared sync files (`.ttmdata`), typically in OneDrive or on a share. |
| `%LocalAppData%\Marflow Software\TextTemplateManager` | Version 1's data, read only by version 2. |

Backups are `.zip` files named `ttm-backup-<kind>-<yyyyMMdd-HHmmss>.zip`, each holding the templates, settings, sync settings, a copy of every shared sync file and a manifest with checksums:

| Kind | Made | Kept |
| --- | --- | --- |
| `recent` | 2 minutes after the last change, and on exit with changes not yet backed up | last 3 |
| `2h` | every 2 hours while the app runs, if something changed | last 5 |
| `daily` | at startup and at the first change after midnight | last 5 |
| `v1` | once, from version 1's data | never pruned |

A backup is written under a temporary name and only appears once it is complete and checked. Damaged backups don't count toward these numbers and are never deleted automatically.

Registry: the app itself writes nothing. Windows keeps the package registration, the file association and the startup task state. Of version 1's keys, only `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, `...\Explorer\StartupApproved\Run` (to carry over the start-with-Windows choice) and its uninstall entry (to find its uninstaller) are read.
