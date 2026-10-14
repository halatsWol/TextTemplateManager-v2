using H.NotifyIcon;   // Window.Show()/Hide()
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TextTemplateManager.Helpers;
using TextTemplateManager.Tutorial;

namespace TextTemplateManager
{
    // The guided tour's Quick Paste part: the tour opens its own Quick Paste window on the sample data, which never
    // pastes and takes no keys (see _tour), and shows these steps over it.
    public sealed partial class PasteWindow
    {
        /// <summary>The tour window's X was clicked: the tour ends.</summary>
        public event EventHandler? TourWindowClosed;

        public void ShowForTour()
        {
            ResetForShow();
            this.Show();
            WindowHelper.SetForegroundWindow(_hwnd);
            this.Activate();
        }

        /// <summary>Quick Paste's part of the tour, numbered from <paramref name="numberFrom"/> of <paramref name="total"/>;
        /// Back on its first step returns to the main window's part.</summary>
        public Task<TourOutcome> RunTourAsync(int numberFrom, int total) =>
            Tour.RunAsync(TourSteps(), numberFrom: numberFrom, total: total, canGoBackFromFirst: true);

        public int TourStepCount => TourSteps().Count;

        public void EndTour() => Tour.End();

        /// <summary>Destroys the tour's window (it isn't kept warm like the real one).</summary>
        public void CloseForTour()
        {
            _closingForPaste = true;
            Tour.End();
            this.Close();
        }

        private Task ShowTab(bool multi)
        {
            ShortcutTabs.SelectedItem = multi ? TabMulti : TabSingle;
            return Task.Delay(200);
        }

        private string Hotkey => _store.CurrentSettings.PasteWindowHotkey is { Length: > 0 } key && key != "None" ? key : "the global shortcut (Settings ▸ General)";

        private IReadOnlyList<TourStep> TourSteps() => new List<TourStep>
        {
            new("Quick Paste",
                $"Press {Hotkey} in any app: Quick Paste opens on top, and the template you pick is pasted into that app. For the tour it's open already, with the sample templates, and pastes nothing.",
                Enter: () => ShowTab(multi: false)),
            new("Single-key shortcuts",
                "Right after opening, press a template's single key (T, S or P in the samples): it's pasted at once. You can also select a row in this list with ↑ and ↓; Enter pastes the selected one.",
                () => ShortcutPane, () => ShowTab(multi: false), Corner: TourCorner.TopRight),
            new("Multi-key shortcuts",
                "Hold Alt and type a multi key (Alt + T, H, X): the list narrows as you type, and releasing Alt pastes the highlighted shortcut. Enter also pastes the row selected in this list. Alt + Backspace corrects, Alt + Esc cancels.",
                () => ShortcutPane, () => ShowTab(multi: true), Corner: TourCorner.TopRight),
            new("As plain text",
                "Add _ (Alt + Shift + -) while typing a multi key to paste the template as plain text; this bar shows what you typed and marks it.",
                () => BufferContainer, () => ShowTab(multi: true)),
            new("Search",
                "Tab or Ctrl + F moves into the search box: it filters the tree and the lists by title and tags, or the text too with the filter button. ↓ moves on into the tree.",
                () => TreePane, () => ShowTab(multi: false)),
            new("Browse and paste",
                "Pick a template in the tree to see it in the preview; Enter or a double-click pastes it. Right-click for Paste As, to paste it in another paste mode. Esc closes Quick Paste.",
                () => TemplateTree),
            new("That's it",
                "You're ready to go. Quick Paste closes now; start this tour again any time from Help ▸ Start tutorial."),
        };
    }
}
