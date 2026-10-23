# 6. Main window

![Presentation layer](diagrams/class-ui.svg)

([source](diagrams/class-ui.puml))

`App.MainWindow` is a plain `Window` whose `Content` is a `MainPage`. The page is built over a `DataNode` (`new MainPage(store, tour: false)`); the guided tour builds a second page over sample data and swaps it in (see [10](10-guided-tour.md)).

## MainPage

`MainPage` (`MainPage.xaml.cs`, tour steps in `MainPage.Tour.cs`) is the view: menu bar, tree, search box, toolbar, the right pane with `TemplateEditor` and the item's fields, the `ConflictPanel`, the in-window `SettingsView`, and the `TutorialOverlay`. It handles what needs the view: drag and drop on the tree, keyboard navigation, context menus, file pickers, and every dialog.

**Dialogs.** Only one `ContentDialog` may be open at a time, and startup notices arrive on their own schedule, so every dialog goes through `ShowDialogAsync`: it opens nothing while another dialog is open (returns `None`), and startup messages wait until the page is loaded and no dialog is open (`WaitUntilDialogCanOpenAsync`). Startup offers that must not be lost retry when another dialog got there first (`ShowWhenFreeAsync`). The 1.x removal offer keeps the slot while the uninstaller runs, so its result message isn't pushed out by the waiting tour offer.

## MainViewModel

`MainViewModel` holds the window's state over the store:

- **Tree projection.** The tree binds to `RootNodes`, a tree of `TreeNode`s (one node per item for as long as the item exists, `NodeFor`). Nodes carry `IsExpanded` and `IsMultiSelected`; their `Children` are all children, or only the search matches. `ApplyFilter()` updates the nodes in place (`SyncCollection`: add, remove, move), so `TreeView` containers and expansion survive; `ReloadTree()` rebinds a new collection when the top-level order changed.
- **Selection.** `SelectedItem` / `SelectedNode`, plus `MultiSelection` for Ctrl/Shift-click, Shift+Up/Down (`Extend`) and Ctrl+A (`SelectAll`, visible rows) (Explorer rules: the anchor is the last click without Shift; the focused item is the last one). Delete and Export act on `MultiSelection.TopMost` (selected items without those inside another selected folder); `CanExportSelection` disables File ▸ Export and the right-click entry for a selection across areas (label "(not across areas)", since a disabled menu item shows no tooltip).
- **Commands.** Add template / folder (into the selected folder, or the selected template's folder, or the root; refused for read-only synced folders), clone, delete, move (`DropDestination` turns a drop position into folder and index), import and export.
- **Search.** `SearchText` with the shared settings `SearchInContent` and `StrictSearch` (see [2](02-data-model-and-files.md)).
- **Validation.** `ValidateAllShortcuts()` runs `ShortcutValidator` once per batch of changes (`QueueValidation`, on the UI thread) and raises `ConflictsChanged`; the page shows a `ConflictReport` in the `ConflictPanel` (conflicts can't be dismissed, cross-area notes can, until they change).
- **Sync polling.** A 4-second `DispatcherQueueTimer` (see [4](04-sync.md)); `StopBackgroundWork()` stops it for the tour's page.

## Editing and Save

`EditedItem` is what the right pane shows and edits:

- **Autosave on**: the selected item itself. Every edit takes the store's debounced save path.
- **Autosave off** (default): an `ItemDraft`, a copy of the editable fields (title; a template's content, tags, shortcuts, paste mode). The copy is edited; `IsDirty` compares it with the original; `Commit()` copies the changed fields into the tree item, which the store then saves like any edit.

![Edit states](diagrams/state-edit.svg)

([source](diagrams/state-edit.puml))

Details that keep this correct:

- **Editor baseline.** TipTap normalizes HTML when it loads a template, so the loaded text can differ from the stored text without any edit. The editor reports what it shows (`ContentLoaded`), and the draft takes that as its unedited content.
- **Leaving an item** is decided at low dispatcher priority (`CheckSwitch`), after the selection has settled: a tree refresh can pass through no selection and back, which isn't leaving.
- **Sync refresh.** When a sync re-apply replaces the edited item, the draft moves its edits onto the replacement (`RebaseOnto`); when the item was deleted, the edits go with it.
- **Before anything that ends editing** (Save, selecting another item, closing the window, exit) the page first pulls the editor's latest content (`TemplateEditor.FlushAsync`), because the editor reports changes with a 250 ms delay. A read that fails is ignored, never taken as empty: a script that can't run returns JSON null instead of throwing, and an empty editor still returns `<p></p>` (`ContentFromScriptResult`). Taking that null as "" once emptied a synced template at exit.

## TemplateEditor: the WebView2 bridge

The editor is TipTap running in a WebView2 (`src/editor/src/main.js`, built into `Assets/editor/editor.html` with CSS and script inlined; the page is loaded with `NavigateToString`, so nothing is fetched and nothing can be cached stale). The preview in Quick Paste uses `preview.html` the same way. Both pages need their `<title>`: Chromium names its hosting window after it, and without one that name is the whole page as a `data:` URL, which a screen reader reads out when the focus enters the editor. The editing area is a named `role="textbox"`, and toolbar buttons are named by their tooltip (`aria-label`; their content is an icon or a glyph), with `aria-pressed` / `aria-haspopup` / `aria-expanded` for state. Keyboard: the main window's Tab cycle (`MainPage.TabCycle`) is tree toolbar → search → tree → title → editor → shortcuts, tags, paste mode (a folder: its title). The tree toolbar is one stop (Left/Right between enabled buttons, `SearchNavigation.NextInRow`). The editor takes its own keys while it has the focus: its toolbar is one stop with a roving tabindex, Tab from there goes to the end of the text (`editorApi.focusToolbar` / `focusTextEnd`, called through `TemplateEditor.FocusPart`); in the text Tab is left to lists, tables and code blocks, elsewhere the page posts `tabOut` and the host (`TemplateEditor.TabOut`) moves on. The popups work from the keyboard: colour swatches are buttons, the table-size grid takes the arrow keys and Enter (size announced through an `aria-live` label), and Esc closes a popup and returns the focus to its button. On the XAML side, icon-only buttons and controls labelled by a separate TextBlock carry an AutomationProperties.Name; a TreeView reaches screen readers as its inner list, which Helpers/TreeViewNames names (main window and Quick Paste); items shown through ToString() (Quick Paste rows, sync sources) return their title or name.

| Direction | Message | Purpose |
| --- | --- | --- |
| page to app | `{type:"ready"}` | Editor loaded: apply theme, show the requested template |
| page to app | `{type:"change", html}` | Content changed (debounced 250 ms); written to the shown template (by reference, not by selection) |
| page to app | `{type:"save"}` | Ctrl+S in the editor |
| page to app | `{type:"focusSearch"}` | Ctrl+F in the editor |
| page to app | `{type:"openLink", href}` | Ctrl+click on a link, opened in the default browser |
| app to page | `editorApi.setContent(html)` | Show a template (resets undo history per template) |
| app to page | `editorApi.getContent()` | Read the current HTML |
| app to page | `editorApi.setEditable(bool)` | Read-only for synced folders with saving off |
| app to page | `editorApi.setTheme(dark)` | Light/dark |

The WebView's context menu is trimmed to editing items; dev tools, the status bar and browser accelerator keys are off.

### Translator

Translation runs offline in the editor's WebView2 with Bergamot, the engine of Firefox Translations (`Assets/translator/bergamot-translator.js` and `.wasm`, v0.6.0, MPL-2.0, checked in as Mozilla publishes them). `editorApi.translateHtml(html, from, to)` (`src/editor/src/translator/client.js`) hands the HTML to a worker (`translator-worker.js`, built from `worker.js`), started on first use and stopped after five minutes without requests, which frees the engine and its models. The editor page is an opaque origin, so the worker is a blob that loads the real one from the virtual host `ttm-translator.example` (mapped to `Assets\translator`); the models come from `ttm-models.example`, mapped to `StorageService.TranslationModelDirectory`. `TemplateEditor` maps both before navigating, with cross-origin reads allowed. A models folder holds one subfolder per direction (`en-de`) with the model, lexical shortlist and vocabulary files and a `model.json` naming them and the registry name of the model (its suffix tells the gemm precision). Pairs exist only to and from English, so other pairs go through English (`engine.route`). The engine settings are Firefox's (`engine.js`). The engine translates HTML with markup kept. Placeholders (`$[DATE]`, `$[TIME(HH:mm)]`, ...) would have their names translated (`$[TIME]` to `$[ZEIT]`), so `mask.js` replaces them with numbered braces (`{0}`, numbered past any braces already in the text), which the models keep, and puts them back afterwards; one the model dropped is appended to the last paragraph. A missing pair rejects with `missingPair` set.

Settings ▸ Translation (`TranslationSettingsPage`) installs the models (`Services/Translation`). `ModelCatalog` reads Mozilla's Remote Settings collection `translations-models-v2`, the list Firefox downloads its models from, and keeps per direction the newest release version of the model format the bundled engine reads (major version 3), skipping records for Android or Nightly only; between sizes of the same version it takes the better one (base, then base-memory, then tiny). `ModelCatalog.Languages` turns the directions into languages, each with its model to and from English, so the page offers languages, not pairs. `ModelStore.InstallAsync` downloads a direction's files from Mozilla's attachment CDN into a `.partial-` folder, checks the SHA-256 of each download and of its unpacked content (zstd, `ZstdSharp.Port`), writes `model.json` with the file names and hashes, and only then swaps the folder in; a failed or cancelled download leaves the installed version as it was. A language whose installed hashes differ from the catalog's shows Update. Downloads outlive the page (a static list keeps the running rows). Export writes the installed folders to a zip and Import installs a zip's folders, each only if its files match the hashes in its `model.json` and its file names stay inside the folder, for PCs without internet access. Without the catalog (offline), installed languages are listed from disk and can still be removed and exported.

## Settings

`SettingsView` is an in-window view (not a separate window) with six pages: General (hotkey, default paste mode, start with Windows, start hidden, Autosave, updates: version, Check for updates, automatic updates on/off, with a note where it updates around the Store policy, idle install; cross-area warnings), Integrations (browser connector, extension links, file association), Sync (sources, separator, missing files), Backups (list, export, restore, import from 1.x), Translation (the languages for the editor's translator, see below), Support (website help; **Export logs**, `Services/Platform/LogExport.cs`: the same .zip as the website's log collector, from `IEventLogSource` with `WindowsEventLogSource` reading the Store, AppX deployment and Application logs without admin rights, the app's log files from the data folder (never its data files), `Redactor` replacing profile path, SIDs, user and computer name, run off the UI thread; problem reports, feature requests and vulnerabilities by e-mail or GitHub, links and prefilled `mailto:` forms with app and Windows version from `Common/SupportLinks`, which also holds the website address for Help ▸ Go to website and About). Pages edit `DataNode.CurrentSettings` / `CurrentSyncSettings` directly; the store saves settings on its own, and pages call `App` for side effects (`UpdateGlobalHotkey`, `ApplyBrowserConnectorSettings`, `RestoreFromBackupAsync`). Settings covers the menu bar while it is open.

The update notice (`UpdateBar`, `MainPage.Updates.cs`) sits between the menu bar and the panes; `App` sends it the updater's status (never to the tour's page). See [13](13-updates.md).

---

[← 5. Backups and restore](05-backups-and-restore.md) · [Contents](README.md) · [7. Quick Paste and pasting →](07-quick-paste-and-pasting.md)
