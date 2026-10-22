# Help & troubleshooting

Answers to common questions. The [user manual](manual.md) has the full details; the [administrator guide](admin.md) covers company PCs and policies.

## Updates

### "The Microsoft Store allows another check at … or later"

The Microsoft Store answers each app only one update check every 30 minutes and ten a day; inside that limit it repeats its last answer. TextTemplateManager keeps to these limits, also across restarts, and tells you when the next check is possible. Checks by others count too (for example the Microsoft Store app's product page), so the time can be later than you expect. An update the app has already found is downloaded right away, without waiting for the next check.

### "The update couldn't be downloaded (Microsoft Store, error 0x80244007)"

A temporary error from the Microsoft Store's download servers, seen most often in the first hours after a new version is released. Click **Try again** after a few minutes; the app downloads the update again without a new check.

### A new version is out, but the app says it's up to date

New versions take up to a few hours after their release to reach every PC. If it takes longer, see the limits [above](#the-microsoft-store-allows-another-check-at-or-later), and check **Help ▸ About** for the version that runs.

### "Update couldn't be installed" or Restart to update did nothing

1. Quit the app completely: tray icon ▸ **Exit App**, or **File ▸ Exit** (closing the window only hides it).
2. Start TextTemplateManager again from the Start menu. A downloaded update is installed at this start, and the window opens with what's new.
3. If **Help ▸ About** still shows the old version, open **Library** in the Microsoft Store app and click **Update** next to TextTemplateManager, then start the app again.

More in the manual under [Updates](manual.md#updates).

### Automatic Store updates are turned off on my PC (company PC)

TextTemplateManager still asks the Microsoft Store for its own updates, unless your organization doesn't allow it: on its own with **Check for and download updates automatically** on, otherwise with **Check for updates**. Windows Update often brings the update on such PCs as well; as soon as it's downloaded, the app offers **Restart to update**. Administrators: see the [administrator guide](admin.md).

### Sending the update log

The app records every update step in `update.log`, which has the full error messages:

`%LocalAppData%\Packages\MarflowSoftware.TextTemplateManager_eb1r5ey0kd4ny\LocalState\update.log`

Paste the path into File Explorer's address bar, and attach the file when you report an update problem. The [log collector](#collecting-logs-for-a-problem-report) includes it, together with the Microsoft Store's and Windows' logs.

## Quick Paste and hotkeys

### The Quick Paste hotkey opens on the wrong computer over Remote Desktop

If TextTemplateManager runs both on your computer and on a remote computer you connect to over Remote Desktop, the hotkey always opens Quick Paste on your **local** computer: Windows delivers a registered hotkey locally. Give each computer a **different** Quick Paste hotkey (**Settings ▸ General**), or run the app on only one of them.

## Data

### Moving my templates to another PC

On the old PC, **Settings ▸ Backups ▸ Export backup…** saves everything (templates, settings, sync settings) as one `.zip` file. On the new PC, **Settings ▸ Backups ▸ Restore from file…** picks that file and restores it. Shared sync files stay where they are; if one isn't reachable from the new PC, the restore asks what to do. Details in the manual under [Restoring a backup](manual.md#restoring-a-backup).

### Resetting or removing the app

Windows **Settings ▸ Apps ▸ Installed apps ▸ TextTemplateManager ▸ …**: **Advanced options ▸ Reset** starts the app as on the first day, **Uninstall** removes it. Both delete the app's data folder but keep the backups in `Documents\TextTemplateManager\Backups`. Details in the manual under [Resetting or removing the app](manual.md#resetting-or-removing-the-app).

## Collecting logs for a problem report

For a problem with installing, updating or starting the app, the logs of the app, the Microsoft Store and Windows together tell the most. The log collector, a PowerShell script, gathers them into one `.zip` file that you attach to your e-mail or GitHub issue. It needs no administrator rights and changes nothing on your PC.

1. Download [Collect-TTMLogs.ps1](downloads/Collect-TTMLogs.ps1){ download="Collect-TTMLogs.ps1" }. It goes to your **Downloads** folder.
2. Open **Windows PowerShell** (Start menu, type `PowerShell`) and run:

    ```powershell
    powershell -ExecutionPolicy Bypass -File "$env:USERPROFILE\Downloads\Collect-TTMLogs.ps1"
    ```

    `-ExecutionPolicy Bypass` lets this one run start the downloaded script; it doesn't change your PC's settings. Reading the event logs can take a minute.

3. File Explorer opens with `TTM-logs-<date>-<time>.zip` selected on your desktop. Attach it to your report.

The `.zip` file contains the app's `update.log` and `crash.log`, the installed version, update policies, and the last 14 days of the Microsoft Store, app installation and app error events that concern TextTemplateManager. It doesn't contain your templates, settings or sync files. Your user name, computer name and account ID are replaced with placeholders, and every file in it is plain text, so you can read it before you send it. For a problem that started earlier, add `-Days 30` at the end of the command.

## Still stuck?

Write to `contact@kmarflow.com`, or open an issue on [GitHub](https://github.com/halatsWol/TextTemplateManager-v2/issues).
