# Microsoft Store texts

Texts for the Partner Center submission, to copy as they are. The release workflow also sends the "What's new" text with an automatic submission (`build/Release-Notes.ps1 -StoreOut`); it must stay within the Store's 1500 characters.

## What's new in this version

```text
TextTemplateManager 2 now comes from the Microsoft Store, which installs and updates it.

- Moving from version 1: at the first start, your templates, settings and sync settings are backed up and offered for import; version 1 can be removed from here.
- Automatic backups after your changes, every two hours and daily. Settings > Backups restores all or part of a backup or exports one.
- Save when you're ready: changes wait for Save (Ctrl+S), and you're asked before leaving unsaved changes. Autosave saves as you type instead.
- Import and export of .ttmdata files, and several items at once with Ctrl+click and Shift+click.
- Strict search with * and . as wildcards; search can also look in the template text.
- Synced folders keep their contents when their file is missing and can recreate it.
- A guided tour: Help > Start tutorial.
- Collapse all and expand all in the tree toolbar, and better keyboard navigation.
- Start with Windows is now Windows' own startup setting.
- Pastes are kept out of the Windows clipboard history.
- A new app icon.
```
