# TextTemplateManager privacy policy

TextTemplateManager does not collect, transmit or share any personal information.

Your templates, settings and backups are stored only on your PC: the app's data folder, and backups in `Documents\TextTemplateManager\Backups`. Shared template files (`.ttmdata`) are read from and written to the folders you choose, for example a OneDrive folder or a network share; their syncing is done by that service, not by the app.

The app sends no telemetry or usage data and makes no internet connections of its own, except asking the Microsoft Store, through Windows, for updates to TextTemplateManager itself (Settings > General > Updates; an organization can turn this off), and the translation downloads below. The optional browser connector listens only on this PC (localhost), is protected by a pairing token, and only answers the TTM-Connect browser extension you pair it with.

Translation runs on your PC. Only Settings > Translation goes online: when you open it, the app loads the list of languages from Mozilla's servers (firefox.settings.services.mozilla.com), and when you download a language, it downloads that language's files from Mozilla's download server (firefox-settings-attachments.cdn.mozilla.net). If you turn on the daily update check there (off by default), the app also loads that list once a day and, if you allow it, downloads newer versions of the languages you installed. These requests carry the app's name and version, as any download does, and nothing else; the text you translate never leaves your PC. Mozilla's privacy policy applies to its servers.

Settings > Support > Export logs saves the app's logs and the Windows events about it as a `.zip` file where you choose, for you to attach to a problem report; the app doesn't send it anywhere. Your user name and computer name are replaced in it.

Links in the app (manual, website, browser extension stores, e-mail) open in your browser or mail app.

Questions: Marflow Software, [contact@kmarflow.com](mailto:contact@kmarflow.com), [software.kmarflow.com/TextTemplateManager-v2](https://software.kmarflow.com/TextTemplateManager-v2/)
