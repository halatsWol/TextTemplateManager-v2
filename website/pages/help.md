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

### Where is the update log?

The app records every update step, with the full error messages, in `%LocalAppData%\Packages\MarflowSoftware.TextTemplateManager_eb1r5ey0kd4ny\LocalState\update.log`. To report an update problem, use the [log collector](#collecting-the-logs): it includes this file together with the Microsoft Store's and Windows' logs. See [Reporting a problem](#reporting-a-problem).

## Quick Paste and hotkeys

### The Quick Paste hotkey opens on the wrong computer over Remote Desktop

If TextTemplateManager runs both on your computer and on a remote computer you connect to over Remote Desktop, the hotkey always opens Quick Paste on your **local** computer: Windows delivers a registered hotkey locally. Give each computer a **different** Quick Paste hotkey (**Settings ▸ General**), or run the app on only one of them.

## Data

### Moving my templates to another PC

On the old PC, **Settings ▸ Backups ▸ Export backup…** saves everything (templates, settings, sync settings) as one `.zip` file. On the new PC, **Settings ▸ Backups ▸ Restore from file…** picks that file and restores it. Shared sync files stay where they are; if one isn't reachable from the new PC, the restore asks what to do. Details in the manual under [Restoring a backup](manual.md#restoring-a-backup).

### Resetting or removing the app

Windows **Settings ▸ Apps ▸ Installed apps ▸ TextTemplateManager ▸ …**: **Advanced options ▸ Reset** starts the app as on the first day, **Uninstall** removes it. Both delete the app's data folder but keep the backups in `Documents\TextTemplateManager\Backups`. Details in the manual under [Resetting or removing the app](manual.md#resetting-or-removing-the-app).

## Reporting a problem

Report it by e-mail to [contact@kmarflow.com](mailto:contact@kmarflow.com), or as an issue on GitHub if you have a GitHub account. Both buttons open a form with the questions below; GitHub issues are public, so don't post private text there. For a security vulnerability, see [Reporting a security vulnerability](#reporting-a-security-vulnerability) instead.

[:material-email-outline: Report by e-mail][mail-problem]{ .md-button .md-button--primary } [:fontawesome-brands-github: Report on GitHub](https://github.com/halatsWol/TextTemplateManager-v2/issues/new?template=bug_report.yml){ .md-button }

The more exactly you describe what happened, the faster it can be found and fixed:

- **When it happened**: the date and time, as exactly as you know them, and whether it happens every time, sometimes or happened once. The logs are sorted by time, so this is how the right entries are found.
- **What you did**, step by step, up to the problem: which window, menu, button or hotkey, which template, and the app you pasted into. For example: "In Outlook, opened Quick Paste with the hotkey, held Alt and typed SIG, released Alt: nothing was pasted."
- **What you expected** and **what happened instead**.
- **Any message**, with its exact text or a screenshot (error codes such as `0x80244007` matter).
- **The app version** (**Help ▸ About**), and whether it's a company PC that manages updates or the Microsoft Store.
- **The logs** for problems with installing, updating or starting the app, or when the app closes unexpectedly: see below.

### Collecting the logs

The logs of the app, the Microsoft Store and Windows together tell the most. If the app starts, the quickest way is **Settings ▸ Support ▸ Export logs…** in the app. If it doesn't, or for more than 14 days, the log collector below gathers the same into one `.zip` file that you attach to your e-mail or GitHub issue. It needs no administrator rights and changes nothing on your PC.

1. Download the log collector, `Collect-TTMLogs.cmd`. It goes to your **Downloads** folder. If your browser asks whether to keep the file, choose **Keep**.

    [:material-download: Download log collector](downloads/Collect-TTMLogs.cmd){ .md-button .md-button--primary download="Collect-TTMLogs.cmd" }

2. Double-click `Collect-TTMLogs.cmd` in your **Downloads** folder. If Windows shows "Windows protected your PC", click **More info** and then **Run anyway**: the file comes from the internet and isn't signed. A window shows the progress; reading the event logs can take a minute.
3. File Explorer opens with `TTM-logs-<date>-<time>.zip` selected on your desktop. Attach it to your report, then press any key to close the window.

The `.zip` file contains the app's `update.log` and `crash.log`, the installed version, update policies, and the last 14 days of the Microsoft Store, app installation and app error events that concern TextTemplateManager. It doesn't contain your templates, settings or sync files. Your user name, computer name and account ID are replaced with placeholders, and every file in it is plain text, so you can read it before you send it.

If the problem started more than 14 days ago, use the PowerShell version below with `-Days`.

??? note "Running it from PowerShell, or for more than 14 days"

    The same collector as a PowerShell script: [Collect-TTMLogs.ps1](downloads/Collect-TTMLogs.ps1){ download="Collect-TTMLogs.ps1" }. Run it from **Windows PowerShell**; `-ExecutionPolicy Bypass` lets this one run start the downloaded script and doesn't change your PC's settings. `-Days 30` reaches further back, `-OutputPath` saves the `.zip` somewhere other than the desktop.

    ```powershell
    powershell -ExecutionPolicy Bypass -File "$env:USERPROFILE\Downloads\Collect-TTMLogs.ps1" -Days 30
    ```

## Suggesting a feature

Ideas are welcome, by e-mail or, with a GitHub account, on GitHub. Both buttons open a form that asks what you'd like to get done, how you do it today, and how it could work.

[:material-email-outline: Suggest by e-mail][mail-feature]{ .md-button .md-button--primary } [:fontawesome-brands-github: Suggest on GitHub](https://github.com/halatsWol/TextTemplateManager-v2/issues/new?template=feature_request.yml){ .md-button }

## Reporting a security vulnerability

Report a vulnerability privately, never as a public issue: by e-mail (no account needed), or on GitHub with a GitHub account through the repository's private vulnerability reporting. Include the app version, what is affected and how to reproduce it. A fix ships as a Microsoft Store update; details are in the [security policy](https://github.com/halatsWol/TextTemplateManager-v2/security/policy).

[:material-email-outline: Report by e-mail][mail-security]{ .md-button .md-button--primary } [:fontawesome-brands-github: Report on GitHub](https://github.com/halatsWol/TextTemplateManager-v2/security/advisories/new){ .md-button }

[mail-problem]: mailto:contact@kmarflow.com?subject=TextTemplateManager%3A%20problem%20report&body=App%20version%20%28Help%20%3E%20About%29%3A%0D%0AWindows%20version%3A%0D%0ACompany%20PC%20that%20manages%20updates%20or%20the%20Microsoft%20Store%20%28yes%20%2F%20no%20%2F%20don%27t%20know%29%3A%0D%0A%0D%0AWhen%20did%20it%20happen%20%28date%2C%20time%2C%20time%20zone%29%3F%0D%0A%0D%0AHow%20often%20%28every%20time%20%2F%20sometimes%20%2F%20once%29%3F%0D%0A%0D%0AWhat%20did%20you%20do%20right%20before%2C%20step%20by%20step%3F%0D%0A1.%0D%0A2.%0D%0A3.%0D%0A%0D%0AWhat%20did%20you%20expect%3F%0D%0A%0D%0AWhat%20happened%20instead%20%28exact%20message%20text%2C%20error%20code%29%3F%0D%0A%0D%0APlease%20attach%20screenshots%20and%2C%20for%20install%2C%20update%20or%20start%20problems%2C%20the%20logs%3A%20Settings%20%3E%20Support%20%3E%20Export%20logs%0D%0A%28if%20the%20app%20doesn%27t%20start%3A%20https%3A%2F%2Fsoftware.kmarflow.com%2FTextTemplateManager-v2%2Fhelp%2F%23collecting-the-logs%29.%0D%0A
[mail-feature]: mailto:contact@kmarflow.com?subject=TextTemplateManager%3A%20feature%20request&body=What%20would%20you%20like%20to%20do%20that%20the%20app%20doesn%27t%20support%20today%2C%20or%20makes%20hard%3F%0D%0A%0D%0AHow%20do%20you%20do%20it%20today%20%28workaround%29%3F%0D%0A%0D%0AHow%20could%20it%20work%20%28idea%2C%20example%2C%20how%20another%20app%20does%20it%29%3F%0D%0A%0D%0AHow%20often%20would%20you%20use%20it%20%28several%20times%20a%20day%20%2F%20daily%20%2F%20weekly%20%2F%20rarely%29%3F%0D%0A%0D%0AApp%20version%20%28Help%20%3E%20About%29%3A%0D%0A
[mail-security]: mailto:contact@kmarflow.com?subject=TextTemplateManager%3A%20security%20vulnerability&body=App%20version%20%28Help%20%3E%20About%29%3A%0D%0AWindows%20version%3A%0D%0A%0D%0AWhat%20is%20affected%3F%0D%0A%0D%0AHow%20to%20reproduce%20it%2C%20step%20by%20step%3A%0D%0A1.%0D%0A2.%0D%0A3.%0D%0A%0D%0AWhat%20can%20an%20attacker%20do%20with%20it%3F%0D%0A
