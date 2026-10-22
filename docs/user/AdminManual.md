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

With TextTemplateManager 1 still installed, winget takes it for the same app and stops with "No available upgrade found" (exit code `0x8A15002B`). Add `--force` to install version 2 anyway (it then offers to remove version 1 itself), or remove version 1 first.

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

Updates come from the Microsoft Store. Windows' own Store updater can't switch the app while it runs (see below), so the app has its own updater on top: it checks the Store every 6 hours (the first time a few minutes after it starts, unless it checked less than 6 hours ago), downloads a new version in the background, and switches to it when the user clicks **Restart to update**, quits the app, or (optional) while the app isn't in use. Version 1's `allowUpdate` registry value has no effect on version 2; use the policy keys below.

### The in-app updater

- **Download**: the Store stages the new version while the app keeps running (`StoreContext`, download only). With automatic Store updates off (policy below, or the user's Store setting), the app asks the Store for its own update through the Store's request route (`RequestDownloadStorePackageUpdatesAsync`), unless `AllowUpdateDespiteStorePolicy` = 0: on its own with **Check for and download updates automatically** on, otherwise with **Check for updates** or **Download** on the notice. On a Windows 11 PC with `AutoDownload` = 4 and without the Store app this resumed the Store's waiting download without any dialog (2026-10-22). A download error shows its code with **Try again**, which downloads again without a new check.
- **Staged by Windows Update**: Windows Update stages Store app updates too, also with automatic Store updates off and without the Store app (seen 2026-10-21, caller `MoUsoCoreWorker`); its own install step fails while the app runs (`0x80073D02`), and Windows installs the staged version at the next start of the app. The app finds such a version in the AppX deployment log (`Microsoft-Windows-AppXDeploymentServer/Operational`, "Deployment Stage operation … finished successfully", readable without admin rights), checks that the package is still there, and offers **Restart to update** without asking the Store. winget's `msstore` source can't update the app (it lists its version as *Unknown*), so the app doesn't use winget.
- **Switch**: the app saves everything, asks Windows to register the staged version with "force target application shutdown", and registers itself for a restart. Windows closes the app, switches, and starts the new version, usually within seconds to a minute or two. If Windows didn't, a one-time task in the user's Task Scheduler (*TextTemplateManager update watchdog*, deleted after it ran) starts it from the Start menu after two minutes. If the switch fails twice for one version, the app stops trying and shows the manual steps below.
- **Users can turn automatic updates off** (Settings ▸ General ▸ Updates, *Check for and download updates automatically*): the app then only checks when they click **Check for updates**, and installs only with **Restart to update**. `AutomaticUpdates` below locks that switch on or off; to prevent in-app updates altogether, use `InAppUpdates`.
- **Idle install** (Settings ▸ General ▸ Updates, off by default): the switch runs when the window and Quick Paste are closed, nothing is unsaved and the session has had no input for 10 minutes; the app then opens with what's new.
- The Store answers each app at most one update check every 30 minutes and ten in 24 hours; beyond that it repeats its last answer, which looks like "up to date". Every caller with the app's identity counts. The app keeps track of its own checks (`update-checks.txt`) and stays within these limits; a **Check for updates** inside them says when the Store allows the next one. Checks by others (the Store app, winget) also count: when the Store refuses a check, it says so in its own event log (`Microsoft-Windows-Store/Operational`, "Scan not allowed inside interval"), and the app reads that entry to tell the user and to wait. New versions also take up to a few hours after publication to reach the check.
- Every step goes to `update.log` in the app's data folder (`%LocalAppData%\Packages\MarflowSoftware.TextTemplateManager_eb1r5ey0kd4ny\LocalState`), including checks without an update. When a check, download or switch fails, the app shows the error code and its source (for example *Microsoft Store, error 0x80244007*); `update.log` has the code with the original error message.

### Update policy

DWORD values under `HKLM\SOFTWARE\Policies\Marflow Software\TextTemplateManager`:

| Value | Meaning |
| --- | --- |
| `InAppUpdates` | `0`: the app never checks for or offers updates (the Store and Intune still update it the usual way). `1`: it shows that an update is available, nothing else. `2` or not set: download, Restart to update and idle install as the user sets them. |
| `AllowUpdateDespiteStorePolicy` | `0`: with "Turn off Automatic Download and Install of updates" (below) the app offers no **Download** and doesn't ask the Store from **Check for updates** either; an update Windows Update stages is still offered with **Restart to update** (use `InAppUpdates` = 1 to prevent that). `1` or not set: the app downloads its own updates through the Store's request route, on its own with **Check for and download updates automatically** on (`AutomaticUpdates` = 0 leaves only **Check for updates** and **Download**). |
| `AutomaticUpdates` | `1`: the app checks for and downloads updates on its own for every user; `0`: only when the user clicks **Check for updates** (no idle install, no install on quit). The switch *Check for and download updates automatically* is locked. Not set: the user decides. |
| `IdleInstall` | `1`: idle install on, `0`: off, for every user; the switch in Settings is locked. Not set: the user decides. |

Settings set by a policy show "Set by your organization." The app reads the policy again before every check and right before it switches to a new version, so a changed policy applies without restarting the app; with `InAppUpdates` = 1 an update is only ever announced, even one Windows has already downloaded. For example, notice only and idle install off:

    Windows Registry Editor Version 5.00

    [HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Marflow Software\TextTemplateManager]
    "InAppUpdates"=dword:00000001
    "IdleInstall"=dword:00000000

### When an update installs

An update is installed in two steps: the Store downloads and **stages** the new version next to the installed one, then Windows **registers** it, which switches the app over. Windows never switches a package while the app is running and doesn't close it for a Store update.

- **Automatic updates** come from the Store's background check, which runs on Windows' own schedule (typically every few hours, not when a new version is published). It only gets through when TextTemplateManager isn't running at that moment.
- TextTemplateManager **starts with Windows and runs in the system tray all day**, so without the in-app updater that moment can be a long time coming: a machine could stay on an older version for days. With it, the update waits only until the user restarts or quits the app, or the idle install runs.
- When the switch fails because the app is running (`0x80073D02` in the log), the new version stays staged and Windows marks it as waiting. The next time the app is **started from the Start menu while it isn't running**, Windows registers the waiting version first, and the new version starts. A start while the app is still running doesn't count (it only brings up the running window), and a version that is only staged, without a failed switch, isn't picked up at start.
- **Opening the Store app** also checks for updates, and the Store then shows **Update**. It doesn't install by itself; clicking **Update** does.

To bring a machine up to date by hand (in-app updates off by policy, or a switch that failed):

1. Quit the app completely: tray icon ▸ **Exit App**, or **File ▸ Exit** (closing the window only hides it).
2. Start TextTemplateManager again from the Start menu. If the update was already downloaded and a switch had failed, it's installed now.
3. If **Help ▸ About** still shows the old version, quit the app again, run `winget upgrade --id 9NMBSZLLLN6V --source msstore` or click **Update** in the Store app (**Library**, or the app's Store page), then start the app again.

With Intune, an assigned *Microsoft Store app (new)* is brought up to date by Intune's own schedule, under the same rule: the switch happens while the app isn't running.

### Store restricted by policy

- **"Turn off the Store application"** (Administrative Templates ▸ Windows Components ▸ Store) only blocks the Store app's window; according to Microsoft's documentation, Store apps keep updating automatically, and Intune and winget can still install them. Reports differ on whether this holds when the policy is set at computer level and on Windows 11 24H2 and later, so verify it on a pilot device in your configuration.
- **"Turn off Automatic Download and Install of updates"** (registry: `HKLM\SOFTWARE\Policies\Microsoft\WindowsStore`, `AutoDownload` = 4; 2 = always on) stops automatic updates of all Store apps, TextTemplateManager included. The Store's background check still finds the update but only parks it. Windows Update may still stage it (seen on a Windows 11 PC with this policy and without the Store app); the app then offers **Restart to update**. Otherwise update it through Intune or **Update** in the Store app. The app's own updater offers **Download** through the Store's consent dialog instead of downloading on its own, unless `AllowUpdateDespiteStorePolicy` = 0 (see *Update policy*).
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
| `%LocalAppData%\Packages\MarflowSoftware.TextTemplateManager_eb1r5ey0kd4ny\LocalState` | User data: `data.ttmdata` (templates), `settings.ttmsettings`, `sync.ttmsettings`, `crash.log`, `update.log` (the in-app updater's steps), `update-checks.txt` (when the app last checked the Store, for its check limits), `update-attempt.json` (a switch in progress), `update-watchdog.ps1` / `.xml` (the watchdog task's script and definition), and any `*.broken-<date-time>` copy of a file that couldn't be read at startup. Removed by Reset and Uninstall. |
| `Documents\TextTemplateManager\Backups` | Automatic backups (follows a redirected Documents folder, e.g. OneDrive folder backup). Kept on Reset and Uninstall. |
| Wherever the user puts them | Shared sync files (`.ttmdata`), typically in OneDrive or on a share. |
| `%LocalAppData%\Marflow Software\TextTemplateManager` | Version 1's data, read only by version 2. |
| `HKLM\SOFTWARE\Policies\Marflow Software\TextTemplateManager` | Update policy values (`InAppUpdates`, `AllowUpdateDespiteStorePolicy`, `IdleInstall`), see *Update policy*. Read only. |

Backups are `.zip` files named `ttm-backup-<kind>-<yyyyMMdd-HHmmss>.zip`, each holding the templates, settings, sync settings, a copy of every shared sync file and a manifest with checksums:

| Kind | Made | Kept |
| --- | --- | --- |
| `recent` | 2 minutes after the last change, and on exit with changes not yet backed up | last 3 |
| `2h` | every 2 hours while the app runs, if something changed | last 5 |
| `daily` | at startup and at the first change after midnight | last 5 |
| `v1` | once, from version 1's data | never pruned |

A backup is written under a temporary name and only appears once it is complete and checked. Damaged backups don't count toward these numbers and are never deleted automatically.

Registry: the app itself writes nothing. Windows keeps the package registration, the file association and the startup task state. Of version 1's keys, only `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, `...\Explorer\StartupApproved\Run` (to carry over the start-with-Windows choice) and its uninstall entry (to find its uninstaller) are read.
