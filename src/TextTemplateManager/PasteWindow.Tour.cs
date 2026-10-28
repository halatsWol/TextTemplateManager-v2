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

        private string Hotkey => HotkeyText.For(_store.CurrentSettings.PasteWindowHotkey) is { Length: > 0 } key ? key : Loc.Tour.Get("HotkeyFallback");

        private IReadOnlyList<TourStep> TourSteps() => new List<TourStep>
        {
            new(Loc.Tour.Get("StepQuickPaste_Title"),
                Loc.Tour.Format("StepQuickPaste_Text", Hotkey),
                Enter: () => ShowTab(multi: false)),
            new(Loc.Tour.Get("StepSingleKey_Title"),
                Loc.Tour.Get("StepSingleKey_Text"),
                () => ShortcutPane, () => ShowTab(multi: false), Corner: TourCorner.TopRight),
            new(Loc.Tour.Get("StepMultiKey_Title"),
                Loc.Tour.Get("StepMultiKey_Text"),
                () => ShortcutPane, () => ShowTab(multi: true), Corner: TourCorner.TopRight),
            new(Loc.Tour.Get("StepPlainText_Title"),
                Loc.Tour.Get("StepPlainText_Text"),
                () => BufferContainer, () => ShowTab(multi: true)),
            new(Loc.Tour.Get("StepSearch_Title"),
                Loc.Tour.Get("StepSearch_Text"),
                () => TreePane, () => ShowTab(multi: false)),
            new(Loc.Tour.Get("StepBrowse_Title"),
                Loc.Tour.Get("StepBrowse_Text"),
                () => TemplateTree),
            new(Loc.Tour.Get("StepEnd_Title"),
                Loc.Tour.Get("StepEnd_Text")),
        };
    }
}
