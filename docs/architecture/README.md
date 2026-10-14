# Architecture

Technical documentation of TextTemplateManager 2: which classes exist, how they relate, and what happens at runtime. It is written for developers working on the code; user-facing documentation is in [`../user/`](../user/).

| Page | Covers |
| --- | --- |
| [1. Overview](01-overview.md) | Purpose, technology, solution layout, layers, components, deployment |
| [2. Data model and files](02-data-model-and-files.md) | Models, identities, areas and shortcuts, the file formats |
| [3. Storage and saving](03-storage-and-saving.md) | `StorageService`, `DataNode`, loading, change tracking, the save path |
| [4. Sync](04-sync.md) | Sync sources, merging, write-back, polling, missing files |
| [5. Backups and restore](05-backups-and-restore.md) | Backup zips, the scheduler, restore with journal and recovery |
| [6. Main window](06-main-window.md) | `MainPage`, `MainViewModel`, tree, editing and Save, the editor bridge, settings, dialogs |
| [7. Quick Paste and pasting](07-quick-paste-and-pasting.md) | Hotkey, `PasteWindow`, `QuickPasteController`, `PasteService`, clipboard formats |
| [8. Browser connector](08-browser-connector.md) | The loopback HTTP server, snapshot, security |
| [9. Application lifecycle](09-lifecycle.md) | Launch, single instance, startup questions, exit and sign-out, unexpected errors |
| [10. Guided tour](10-guided-tour.md) | Sample store, overlay, steps, input suspension |
| [11. Platform, build and release](11-platform-build-release.md) | Packaged vs. dev runs, manifest, build properties, release workflow |
| [12. Testing](12-testing.md) | The test project, what it covers, UI checks |

## Diagrams

Every diagram is a PlantUML source in [`diagrams/`](diagrams/) (`*.puml`, shared look in `_style.iuml`) with the rendered SVG next to it; the pages show the SVGs. After changing a diagram, render the SVGs again and commit both:

```bash
java -jar plantuml.jar -tsvg -charset UTF-8 docs/architecture/diagrams/*.puml
```

Class diagrams need Graphviz (`dot`) on the PATH or in `GRAPHVIZ_DOT`.

Diagram names start with their kind: `class-`, `component-`, `deployment`, `seq-` (sequence), `act-` (activity), `state-`.

## Keeping it current

These pages describe the code as of TextTemplateManager 2.0. When a change moves responsibilities between classes, adds a file or setting, or changes a runtime flow shown here, update the page and its diagram in the same change.
