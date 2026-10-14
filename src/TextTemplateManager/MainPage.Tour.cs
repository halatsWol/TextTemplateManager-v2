using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TextTemplateManager.Models;
using TextTemplateManager.Tutorial;

namespace TextTemplateManager
{
    // The guided tour's part in the main window (the window then shows the tour's sample data, see App.StartTutorialAsync).
    public sealed partial class MainPage
    {
        /// <summary>Shows the tour over this window. True when it was finished, false when skipped or ended.</summary>
        public Task<bool> RunTourAsync() => Tour.RunAsync(TourSteps());

        public void EndTour() => Tour.End();

        /// <summary>After a skipped tour: where to start it again.</summary>
        public void ShowTourHint() => TourHintTip.IsOpen = true;

        private Task SelectSample(System.Guid id)
        {
            ViewModel.SetAllExpanded(true);
            ViewModel.SelectedItem = _store.LocalItems.Descendants().FirstOrDefault(i => i.Id == id);
            return Task.Delay(400);   // the editor loads the template
        }

        private IReadOnlyList<TourStep> TourSteps() => new List<TourStep>
        {
            new("Welcome to TextTemplateManager",
                "Keep reusable text (replies, snippets, signatures) and insert it into any app with a keystroke. This short tour shows the main parts using sample templates; your own templates aren't touched. Next continues, Esc skips.",
                Enter: () => { ViewModel.SetAllExpanded(true); return Task.CompletedTask; }),
            new("Templates and folders",
                "Your templates, in folders. Synced folders (with the badge) come from a shared file and stay at the top. The search box filters the tree; Ctrl+F jumps to it, and ↓ moves on into the tree.",
                () => LeftPane),
            new("Organizing",
                "Add a template or a folder (into the selected folder), delete the selection, and collapse or expand all folders. Drag and drop moves items; Ctrl+click and Shift+click select several.",
                () => TreeToolbar),
            new("The editor",
                "Format text with headings, lists, tables, code blocks with syntax highlighting, and callout panels like the Info panel here.",
                () => EditorCard, () => SelectSample(TourSample.ReleaseNotesId)),
            new("Shortcuts and tags",
                "Single Key and Multi Key are the template's shortcuts in Quick Paste, tags help finding it, and the default paste mode decides the format it is pasted in.",
                () => TemplateMetaPanel),
            new("Paste modes",
                "Auto picks the best format for the app you paste into; HTML/Jira keeps callout panels for Jira; HTML, RTF, Markdown and Plaintext paste just that. Right-click a template to copy it in another mode.",
                () => PasteModeBox),
            new("Saving",
                "Changes to a template or folder wait for Save here (or Ctrl+S), and you're asked before leaving unsaved changes. To save as you type, turn on Autosave in Settings ▸ General.",
                () => TopRightActions),
            new("Import and export",
                "File ▸ Export saves the selected items, or all of them, to a .ttmdata file; File ▸ Import adds such a file to your tree.",
                () => FileMenu),
            new("Always at hand",
                "Closing the window keeps TextTemplateManager running in the system tray, so Quick Paste is always ready. To quit, use File ▸ Exit or the tray icon."),
            new("Help",
                "The manual, and this tour again: Help ▸ Start tutorial.",
                () => HelpMenu),
        };
    }
}
