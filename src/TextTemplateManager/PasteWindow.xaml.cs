using H.NotifyIcon;   // Window.Show()/Hide() extension methods (same as the main window uses)
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using TextTemplateManager.Common;
using TextTemplateManager.Data;
using TextTemplateManager.Helpers;
using TextTemplateManager.Models;
using TextTemplateManager.Services.Pasting;
using TextTemplateManager.ViewModels;
using static TextTemplateManager.Helpers.SearchNavigation;
using Windows.System;
using Windows.UI.Core;

namespace TextTemplateManager
{
    public sealed partial class PasteWindow : Window
    {
        private bool _isProcessing = false;
        private bool _closingForPaste = false;   // a paste is destroying the window (let the real close through)
        private IntPtr _inputSiteHwnd = IntPtr.Zero;   // WinUI content-input child, subclassed for the Alt-beep
        private IntPtr _hwnd = IntPtr.Zero;

        private readonly DataNode _store;
        private readonly QuickPasteController _entry;

        private readonly bool _tour;   // opened by the guided tour: shows its sample data, never pastes

        public PasteWindow(DataNode store, bool tour = false)
        {
            _store = store;
            _tour = tour;
            _entry = new QuickPasteController(store.ShortcutEntries, () => store.CurrentSyncSettings.Sources, store.ResolveSingleKey);
            this.InitializeComponent();
            _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            TreeViewNames.NameForScreenReaders(TemplateTree, Loc.Get("Main_TreeName"));

            this.Activated += (s, e) =>
            {
                if (e.WindowActivationState != WindowActivationState.Deactivated)
                {
                    this.DispatcherQueue.TryEnqueue(() =>
                    {
                        // A paste is in flight (a double-click closes the window, then HandlePaste
                        // brings the TARGET app to the front): don't yank Quick Paste back to the
                        // front here, or the Ctrl+V lands in this window's search box and it gets
                        // stuck. On a real open _hasExecuted is false (reset in ShowForPaste).
                        if (_hasExecuted) return;

                        WindowHelper.ForceWindowToFront(_hwnd);

                        // Subclass the input child so the Alt-key WM_SYSCHAR ding is swallowed
                        // there. Retries on later activations if not yet realized.
                        if (_inputSiteHwnd == IntPtr.Zero)
                            _inputSiteHwnd = WindowHelper.SuppressAltMenuBeepOnChild(_hwnd);

                        // Open in "shortcut mode" (focus the root, not search) so a single-key
                        // shortcut pastes immediately; a letter/digit that isn't a shortcut beeps, and
                        // Tab or a click moves into search. In the tour the keyboard stays on its card.
                        if (!_tour) RootGrid.Focus(FocusState.Programmatic);
                    });
                }
                else if (!_hasExecuted)
                {
                    // Lost focus mid-entry (e.g. clicked another window): abandon the multi-key
                    // entry so returning doesn't resume with a stale buffer or a stuck ALT.
                    CancelMultiKeyEntry();
                }
            };

            this.AppWindow.Resize(new Windows.Graphics.SizeInt32(780, 500));
            ConfigureWindow();

            this.DispatcherQueue.TryEnqueue(() => LoadInitialData());

            // handledEventsToo: see keys even after SearchBox handles them.
            this.RootGrid.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnKeyDown), true);
            this.RootGrid.AddHandler(UIElement.KeyUpEvent, new KeyEventHandler(OnKeyUp), true);

            // Focus landing anywhere in the window drives the shortcut-list highlight (see
            // SyncShortcutHighlight): visible in shortcut mode, cleared while search or the tree has focus.
            // GotFocus bubbles, so subscribing on RootGrid catches focus reaching any descendant.
            this.RootGrid.GotFocus += OnContentGotFocus;

            SearchOptions.Settings = store.CurrentSettings;
            store.CurrentSettings.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(AppSettings.SearchInContent) or nameof(AppSettings.StrictSearch) && AppWindow.IsVisible)
                    UpdateAllFilters(SearchBox.Text);
            };
        }

        private void LoadInitialData() => UpdateAllFilters("");

        /// <summary>Reveal the window for a hotkey press. Resets per-open state, arms the Alt+Esc hook,
        /// then shows + foregrounds + focuses shortcut mode. The instance is reused (hidden between
        /// uses), so the tree and WebView preview stay built and the window is instantly ready for a
        /// single-key press instead of leaking that key to the app underneath while it cold-loads.</summary>
        public void ShowForPaste()
        {
            ResetForShow();
            InstallAltEscHook();
            this.Show();
            WindowHelper.SetForegroundWindow(_hwnd);   // input recipient before Activate
            this.Activate();                           // Activated -> ForceWindowToFront + focus RootGrid
        }

        // Hide (not close) so the instance stays warm for the next open; drop the global hook while idle.
        private void Dismiss()
        {
            RemoveAltEscHook();
            this.Hide();
        }

        /// <summary>Warm the preview WebView ahead of first use (called shortly after app launch) so the
        /// first hotkey open isn't cold. Safe before the window is ever shown; if the WebView can't
        /// initialize until then, the Loaded handler picks it up and the setup still runs exactly once.</summary>
        public void Prewarm() => _ = EnsurePreviewAsync();

        // Clear whatever the previous session left so the window opens in the default shortcut mode.
        private void ResetForShow()
        {
            _hasExecuted = false;
            _entry.Open();

            _isProcessing = true;              // don't let clearing the box re-run the filter
            SearchBox.Text = "";
            _isProcessing = false;

            UpdateAllFilters("");              // unfiltered tree + cleared preview
            RootGrid.Focus(FocusState.Programmatic);   // shortcut mode: a single key pastes immediately
        }

        private void ConfigureWindow()
        {
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);

            appWindow.SetIcon("Assets/AppIcon.ico");

            // The X hides the window (keeping it warm) instead of destroying it, like the main window.
            // A paste sets _closingForPaste so the real close goes through (App then rebuilds a fresh
            // warm one). Quitting the app (App.Shutdown) ends it without going through this.
            appWindow.Closing += (s, e) =>
            {
                if (_closingForPaste) return;
                e.Cancel = true;
                if (_tour) TourWindowClosed?.Invoke(this, EventArgs.Empty);   // the X ends the tour
                else Dismiss();
            };

            if (appWindow?.Presenter is Microsoft.UI.Windowing.OverlappedPresenter p)
            {
                p.IsResizable = true;
                p.IsMaximizable = false;   // quick-paste window shouldn't maximize
            }

            WindowHelper.SetWindowMinSize(hWnd, 520, 400);   // small floor, below the main window

            this.Closed += (s, e) =>
            {
                WindowHelper.RemoveWindowHook(hWnd);
                if (_inputSiteHwnd != IntPtr.Zero) WindowHelper.RemoveWindowHook(_inputSiteHwnd);
                RemoveAltEscHook();
            };
        }

        #region Search & Filtering
        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isProcessing) return;
            try
            {
                _isProcessing = true;
                UpdateAllFilters(SearchBox.Text);
            }
            finally { _isProcessing = false; }
        }

        // The options menu takes focus while open; give it back to where it was (search box, tree or
        // shortcut mode), or a letter typed next would land in shortcut mode and paste.
        private UIElement? _focusBeforeMenu;

        private void SearchOptions_MenuOpening(object? sender, EventArgs e) =>
            _focusBeforeMenu = FocusManager.GetFocusedElement(Content.XamlRoot) as UIElement;

        private void SearchOptions_MenuClosed(object? sender, EventArgs e)
        {
            (_focusBeforeMenu ?? RootGrid).Focus(FocusState.Programmatic);
            _focusBeforeMenu = null;
        }

        private void UpdateAllFilters(string searchFilter)
        {
            TemplateTree.RootNodes.Clear();
            ClearPreview();

            if (string.IsNullOrWhiteSpace(searchFilter))
            {
                foreach (var item in _store.LocalItems)
                    TemplateTree.RootNodes.Add(CreateNode(item));
            }
            else
            {
                foreach (var node in TemplateSearch.Prune<TreeViewNode>(_store.LocalItems, searchFilter, _store.CurrentSettings.SearchInContent, FilteredNode, _store.CurrentSettings.StrictSearch))
                    TemplateTree.RootNodes.Add(node);
            }

            _entry.SetSearch(searchFilter, _store.CurrentSettings.SearchInContent, _store.CurrentSettings.StrictSearch);
            Render();
        }

        private bool _rendering;

        // Shows the shortcut entry's state: both lists with their highlighted row, the tab and the typed key.
        private void Render()
        {
            _rendering = true;   // the list and tab events below come from here, not from the user
            try
            {
                if (!ReferenceEquals(SingleKeyList.ItemsSource, _entry.SingleRows)) SingleKeyList.ItemsSource = _entry.SingleRows;
                if (!ReferenceEquals(MultiKeyList.ItemsSource, _entry.MultiRows)) MultiKeyList.ItemsSource = _entry.MultiRows;
                ShowHighlight(SingleKeyList, _entry.SingleIndex);
                ShowHighlight(MultiKeyList, _entry.MultiIndex);

                bool multi = _entry.Tab == ShortcutTab.Multi;
                ShortcutTabs.SelectedItem = multi ? TabMulti : TabSingle;
                SingleKeyList.Visibility = multi ? Visibility.Collapsed : Visibility.Visible;
                MultiKeyList.Visibility = multi ? Visibility.Visible : Visibility.Collapsed;
            }
            finally { _rendering = false; }
            UpdateBufferUI();
        }

        private static void ShowHighlight(ListView list, int index)
        {
            if (list.SelectedIndex == index) return;
            list.SelectedIndex = index;
            if (list.SelectedItem != null) list.ScrollIntoView(list.SelectedItem);
        }
        #endregion

        #region TreeView Helpers
        // Quick Paste owns its own node tree, so its expansion is independent of the main window's.
        private TreeViewNode CreateNode(BaseItem item)
        {
            var node = new TreeViewNode() { Content = item, IsExpanded = false };
            if (item.Children != null)
                foreach (var child in item.Children) node.Children.Add(CreateNode(child));
            return node;
        }

        // A search result's node: only its remaining children, expanded when it has any.
        private static TreeViewNode FilteredNode(BaseItem item, List<TreeViewNode> children)
        {
            var node = new TreeViewNode { Content = item, IsExpanded = children.Count > 0 };
            foreach (var child in children) node.Children.Add(child);
            return node;
        }
        #endregion

        #region Input & Paste Logic
        private void OnKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (_tour) return;   // the tour's card has the keyboard; a key must never paste or close here
            // ALT via event args — more reliable than InputKeyboardSource here.
            bool altIsDown = e.KeyStatus.IsMenuKeyDown || e.Key == VirtualKey.Menu;

            if (e.Key == VirtualKey.Escape)
            {
                // Help tip open → Esc closes it first, not the window. Also swallow an Esc the
                // light-dismiss layer already used.
                if (HelpTip.IsOpen || e.Handled)
                {
                    HelpTip.IsOpen = false;
                    e.Handled = true;
                    return;
                }

                if (altIsDown || _entry.AltHeld) return;   // Alt+Esc handled by the low-level hook

                // Esc steps focus back toward shortcut mode before it ever closes the window:
                // tree → search box → shortcut mode → close (so from the tree the 3rd Esc closes).
                if (IsTreeFocused())
                {
                    // Back to the search box with the filter intact and the caret at the end, so one
                    // more Down steps straight back into the tree (a clean browse/return round trip).
                    e.Handled = true;
                    SearchBox.Focus(FocusState.Programmatic);
                    SearchBox.SelectionLength = 0;
                    SearchBox.SelectionStart = SearchBox.Text.Length;
                    return;
                }

                var escFocus = FocusManager.GetFocusedElement(this.Content.XamlRoot);
                if (ReferenceEquals(escFocus, SearchBox))
                {
                    // Leave the search box (clearing any filter) so single/multi-key and list
                    // navigation work again; a further Esc, already in shortcut mode, closes.
                    e.Handled = true;
                    SearchBox.Text = "";
                    RootGrid.Focus(FocusState.Programmatic);
                    return;
                }

                e.Handled = true;
                Dismiss();   // shortcut mode already -> Esc hides (keep warm for next open)
                return;
            }

            if (e.Key == VirtualKey.Menu)
            {
                // Holding ALT shows the multi-key list (the tab before it comes back on release).
                _entry.AltDown();
                Render();
                // Pull focus off the tree so arrows/typing drive the multi-key entry, not the tree.
                if (IsTreeFocused()) RootGrid.Focus(FocusState.Programmatic);
                e.Handled = true;
                return;
            }

            // Multi-key (ALT + key) — must precede the TextBox passthrough below.
            if (altIsDown || _entry.AltHeld)
            {
                e.Handled = true;
                HandleMultiKeyInput(e.Key);
                return;
            }

            // Ctrl+F focuses the search box from anywhere (shortcut mode, tree, or search), selecting any
            // existing text to type over. Without this a bare 'F' in shortcut mode would just beep as a
            // non-shortcut key.
            if (e.Key == VirtualKey.F && IsCtrlDown())
            {
                e.Handled = true;
                SearchBox.Focus(FocusState.Programmatic);
                SearchBox.SelectAll();
                return;
            }

            // Three explicit focus states drive the rest: the TREE (owns its own arrows/Enter), the
            // SEARCH box (arrows move the caret and can step down into the tree; letters filter), and
            // SHORTCUT mode (RootGrid focused — single keys paste and arrows browse the visible list).
            // Keeping them separate stops a key inheriting a neighbour's behaviour, the way the search
            // box used to pick up the shortcut-list arrows by accident.
            var focused = FocusManager.GetFocusedElement(this.Content.XamlRoot);
            bool searchFocused = focused is TextBox;
            bool treeFocused = IsTreeFocused();

            // Tab / Shift+Tab cycle deterministically between the three surfaces, handled here so the
            // framework's own tab navigation (which kept pulling focus onto a display-only shortcut-list
            // row) never runs. Forward order is shortcut mode -> search -> tree -> shortcut mode; Shift
            // reverses it. An empty tree is skipped so focus never lands on nothing.
            if (e.Key == VirtualKey.Tab)
            {
                e.Handled = true;
                bool back = IsShiftDown();
                if (searchFocused)
                {
                    if (back) FocusShortcutMode();
                    else if (!TryFocusTree()) FocusShortcutMode();
                }
                else if (treeFocused)
                {
                    if (back) FocusSearch(); else FocusShortcutMode();
                }
                else   // shortcut mode
                {
                    if (back) { if (!TryFocusTree()) FocusSearch(); }
                    else FocusSearch();
                }
                return;
            }

            // ---- Tree ----
            if (treeFocused)
            {
                // Enter/Space paste the selected template, or expand/collapse a folder.
                if (e.Key is VirtualKey.Enter or VirtualKey.Space)
                {
                    e.Handled = true;
                    if (TemplateTree.SelectedItem is TreeViewNode node)
                    {
                        if (node.Content is Template t) ExecutePaste(t, false);
                        else node.IsExpanded = !node.IsExpanded;
                    }
                    return;
                }
                // The TreeView handles the arrows itself (up/down move, left/right collapse/expand).
                if (e.Key is VirtualKey.Up or VirtualKey.Down or VirtualKey.Left or VirtualKey.Right)
                    return;
                // A printable key hands the tree back to the search box and keeps typing there, rather
                // than dead-ending with a beep. (Space is taken above; '-'/'.' aren't letters/digits,
                // so they fall through and are ignored, as before.)
                if (TryGetCharKey(e.Key, out char intoSearch))
                {
                    e.Handled = true;
                    FocusSearchWith(intoSearch);
                }
                return;
            }

            // ---- Search box ----
            if (searchFocused)
            {
                // Up: caret (collapsing any selection) to the very start — nothing sits above the box,
                // so it never leaves. Down: caret to the end; a second Down from the end steps into the
                // tree (when it has rows — otherwise it stays put).
                if (e.Key == VirtualKey.Up)
                {
                    e.Handled = true;
                    SearchBox.SelectionLength = 0;
                    SearchBox.SelectionStart = 0;
                    return;
                }
                if (e.Key == VirtualKey.Down)
                {
                    e.Handled = true;
                    if (DecideSearchDown(SearchBox.SelectionStart, SearchBox.SelectionLength,
                                         SearchBox.Text.Length, TemplateTree.RootNodes.Count) == SearchDown.EnterTree)
                        TryFocusTree();
                    else { SearchBox.SelectionLength = 0; SearchBox.SelectionStart = SearchBox.Text.Length; }
                    return;
                }
                // Enter here is deliberately inert for now (the shortcut list isn't the focus).
                // Swallowed so a later binding has a clean home. Alt+Enter never reaches this branch —
                // the ALT block above commits the highlighted multi-key row first.
                if (e.Key == VirtualKey.Enter)
                {
                    e.Handled = true;
                    return;
                }
                return;   // every other key types into the box normally
            }

            // ---- Shortcut mode (RootGrid focused) ----
            // Arrow up/down browse whichever shortcut tab is showing; Enter pastes the highlighted row.
            // Both tabs behave the same — the multi-key list is reachable this way as well as via ALT.
            var list = IsSingleTabActive() ? SingleKeyList : MultiKeyList;
            if (e.Key == VirtualKey.Up || e.Key == VirtualKey.Down)
            {
                if (!ReferenceEquals(focused, list))   // if the list itself is focused, its own nav handles it
                {
                    _entry.MoveHighlight(e.Key == VirtualKey.Down ? 1 : -1);
                    Render();
                    e.Handled = true;
                }
                return;
            }
            if (e.Key == VirtualKey.Enter)
            {
                if (_entry.CommitHighlighted() is { } highlighted) { e.Handled = true; ExecutePaste(highlighted); }
                return;
            }

            // A single-key shortcut pastes immediately in shortcut mode. (A SearchBox.Text check isn't
            // enough to gate this — the text is still empty on the FIRST keystroke into search, so that
            // first letter would be hijacked into a paste; the focus split above is what gates it.)
            if (TryGetCharKey(e.Key, out char keyChar) && _entry.SingleKey(keyChar) is { } match)
            {
                e.Handled = true;
                ExecutePaste(match);
                return;
            }

            // Not a matching shortcut: in shortcut mode the keyboard is for shortcuts, so a stray letter
            // or digit just beeps rather than dropping into search. That keeps behaviour steady whether
            // or not a key happens to be a shortcut today — a key you're used to doing nothing can't
            // start pasting later just because a shortcut got assigned to it.
            if (TryGetCharKey(e.Key, out _))
            {
                e.Handled = true;
                PlayNoMatchBeep();
            }
            // Everything else — Tab, Shift, Caps Lock, other modifiers, '-'/'.'/Space — is left unhandled
            // so nothing drags focus out of shortcut mode: Tab lets the framework move to the search box in
            // one clean step (focusing it here as well would skip search and land on the tree), and a bare
            // modifier does nothing. Search is reached by Tab or a click.
        }

        private void HandleMultiKeyInput(VirtualKey key)
        {
            // Arrow up/down move the highlight without changing the typed key (so typing can continue
            // where it left off). Enter commits the highlighted row even with nothing typed.
            if (key == VirtualKey.Up || key == VirtualKey.Down)
                _entry.MoveMultiHighlight(key == VirtualKey.Down ? 1 : -1);
            else if (key == VirtualKey.Enter)
            {
                if (_entry.CommitMulti(requireBuffer: false) is { } paste) ExecutePaste(paste);
                return;
            }
            else if (key == VirtualKey.Back)
                _entry.Backspace();
            else if ((TryGetSpecialChar(key, out char c) || TryGetCharKey(key, out c)) && !_entry.TypeMulti(c))
                PlayNoMatchBeep();   // would no longer begin any shortcut
            Render();
        }

        private void OnKeyUp(object sender, KeyRoutedEventArgs e)
        {
            if (_tour) return;
            if (_hasExecuted) return;   // already pasted (e.g. via Enter) — window is closing
            if (e.Key == VirtualKey.Menu)
            {
                // Release pastes the highlighted row if something is typed (trailing '_' = plaintext);
                // otherwise the entry is cleared and the tab from before ALT comes back.
                if (_entry.AltUp() is { } paste) { ExecutePaste(paste); return; }
                Render();
            }
        }
        private const uint MB_OK = 0x00000000;   // "Default Beep" — the Explorer no-match ding
        [DllImport("user32.dll")] private static extern bool MessageBeep(uint uType);
        private static void PlayNoMatchBeep() => MessageBeep(MB_OK);

        private static bool IsShiftDown()
            => (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift) & CoreVirtualKeyStates.Down)
               == CoreVirtualKeyStates.Down;

        private static bool IsCtrlDown()
            => (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) & CoreVirtualKeyStates.Down)
               == CoreVirtualKeyStates.Down;


        // Maps the '-'/'_' and '.' keys to their character (Shift+'-' = '_').
        private static bool TryGetSpecialChar(VirtualKey key, out char c)
        {
            c = '\0';
            switch (key)
            {
                case (VirtualKey)189:      // OEM_MINUS
                case VirtualKey.Subtract:  // numpad '-'
                    c = IsShiftDown() ? '_' : '-';
                    return true;
                case (VirtualKey)190:      // OEM_PERIOD
                case VirtualKey.Decimal:   // numpad '.'
                    c = '.';
                    return true;
                default:
                    return false;
            }
        }

        private bool IsSingleTabActive() => _entry.Tab == ShortcutTab.Single;

        // True while focus is inside the template tree (the TreeViewItem is focused, not the TreeView).
        private bool IsTreeFocused()
        {
            var node = FocusManager.GetFocusedElement(this.Content.XamlRoot) as DependencyObject;
            while (node != null)
            {
                if (ReferenceEquals(node, TemplateTree)) return true;
                node = VisualTreeHelper.GetParent(node);
            }
            return false;
        }

        // Shortcut mode = the keyboard is on the shortcut lists: focus is neither the search box nor the
        // tree. Single keys paste and the arrows browse the visible list in this state. Before the window
        // is shown (no XamlRoot yet) it opens straight into shortcut mode, so default to true.
        private bool InShortcutMode()
        {
            var root = this.Content?.XamlRoot;
            if (root is null) return true;
            return FocusManager.GetFocusedElement(root) is not TextBox && !IsTreeFocused();
        }

        // Focus moved: the visible shortcut list carries a highlighted row only while shortcut mode is
        // active, so it never looks live while you type in search or browse the tree. Leaving clears both
        // lists; coming back (Esc, Tab, or a click that didn't land on a row) re-selects the first row.
        // A window being closed (a paste, or the tour ending) can still get a late focus event; its content is gone.
        private void OnContentGotFocus(object sender, RoutedEventArgs e)
        {
            if (!_closingForPaste) SyncShortcutHighlight();
        }

        private void SyncShortcutHighlight()
        {
            _entry.SetShortcutMode(InShortcutMode());
            Render();
        }

        // Move focus into the search box and append a character, caret at the end — the shared path for a
        // printable key pressed in shortcut mode or while the tree has focus.
        private void FocusSearchWith(char typed)
        {
            SearchBox.Focus(FocusState.Programmatic);
            SearchBox.Text += typed;
            SearchBox.SelectionStart = SearchBox.Text.Length;
        }

        // Move focus into the tree, seeding a selection so Enter has a target and the preview fills (the
        // tree drives Enter off its SELECTED node, not the focused one, and nothing else seeds one).
        // Returns false when the tree is empty, so callers can route focus elsewhere.
        private bool TryFocusTree()
        {
            if (TemplateTree.RootNodes.Count == 0) return false;
            TemplateTree.SelectedNode ??= TemplateTree.RootNodes[0];
            if (TemplateTree.ContainerFromNode(TemplateTree.SelectedNode) is TreeViewItem container)
                container.Focus(FocusState.Programmatic);
            else
                TemplateTree.Focus(FocusState.Programmatic);
            return true;
        }

        private void FocusSearch() => SearchBox.Focus(FocusState.Programmatic);

        // Return to shortcut mode: focus the root grid (single keys paste, the arrows drive the list).
        private void FocusShortcutMode() => RootGrid.Focus(FocusState.Programmatic);

        private bool _hasExecuted = false;

        private void ExecutePaste(PasteRequest paste) => ExecutePaste(paste.Template, paste.PlainText);

        private void ExecutePaste(Template item, bool forcePlain)
            => ExecutePaste(item, forcePlain ? PasteMode.Plaintext : item.DefaultPasteMode);

        private void ExecutePaste(Template item, PasteMode mode)
        {
            if (_hasExecuted || _tour) return;   // guard double-trigger; the tour never pastes
            _hasExecuted = true;

            string content = item.Content;   // capture before the window is torn down

            // Defer out of the current input event so a mouse double-click finishes settling, then
            // CLOSE (destroy) the window rather than hide it: a merely-hidden window can still be
            // reactivated and swallow the Ctrl+V into its own search box — a closed one cannot. App
            // rebuilds a fresh, prewarmed instance on Closed for the next hotkey.
            DispatcherQueue.TryEnqueue(() =>
            {
                RemoveAltEscHook();
                _closingForPaste = true;
                this.Close();
                _ = PasteService.HandlePaste(content, mode);
            });
        }

        // Clears the in-progress multi-key entry. On a full reset (window lost focus) the held-ALT
        // state is cleared and the tab restored too, since the ALT key-up will never arrive.
        private void CancelMultiKeyEntry(bool fullReset = true)
        {
            _entry.CancelEntry(altReleased: fullReset);
            Render();
        }
        #endregion

        #region Alt+Esc hook
        // Alt+Esc is an OS window-switch shortcut, so it never reaches WinUI's KeyDown. A low-level
        // keyboard hook lets us swallow it while Quick Paste is foreground and cancel the entry
        // instead of letting Windows send the window to the back (which left ALT / the buffer stuck).
        private IntPtr _keyboardHook = IntPtr.Zero;
        private LowLevelKeyboardProc? _keyboardProc;

        private void InstallAltEscHook()
        {
            if (_keyboardHook != IntPtr.Zero) return;
            _keyboardProc = KeyboardHookProc;   // keep the delegate alive against GC
            _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, GetModuleHandle(null), 0);
        }

        private void RemoveAltEscHook()
        {
            if (_keyboardHook == IntPtr.Zero) return;
            UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
            _keyboardProc = null;
        }

        private IntPtr KeyboardHookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && ((int)wParam == WM_KEYDOWN || (int)wParam == WM_SYSKEYDOWN))
            {
                var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                bool altDown = (data.flags & LLKHF_ALTDOWN) != 0 || (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
                if (data.vkCode == VK_ESCAPE && altDown && GetForegroundWindow() == _hwnd)
                {
                    // ALT is still physically held, so keep the ALT state and just drop the buffer.
                    DispatcherQueue.TryEnqueue(() => CancelMultiKeyEntry(fullReset: false));
                    return (IntPtr)1;   // swallow Alt+Esc
                }
            }
            return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104;
        private const int VK_ESCAPE = 0x1B, VK_MENU = 0x12;
        private const uint LLKHF_ALTDOWN = 0x20;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);
        #endregion

        #region UI Updates & Preview

        // The preview reuses the editor's rendering (same HTML + editor.css) in a read-only
        // WebView, so it's pixel-accurate (colors, highlights, tables) and consistent.
        private bool _previewReady;
        private string _pendingPreviewHtml = "<p></p>";

        private bool _previewConfigured;

        private async void PreviewWebView_Loaded(object sender, RoutedEventArgs e) => await EnsurePreviewAsync();

        // Idempotent: driven by the Loaded event and by Prewarm (which may run before the window is ever
        // shown). EnsureCoreWebView2Async is safe to call repeatedly; the one-time setup + navigation
        // guards on _previewConfigured so it runs exactly once whichever call reaches it first.
        private async System.Threading.Tasks.Task EnsurePreviewAsync()
        {
            try
            {
                await PreviewWebView.EnsureCoreWebView2Async();
                if (_previewConfigured) return;
                _previewConfigured = true;

                var core = PreviewWebView.CoreWebView2;
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.IsStatusBarEnabled = false;
                core.WebMessageReceived += Preview_WebMessageReceived;

                PreviewWebView.NavigateToString(EditorPages.Preview);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Preview] init failed: {ex.Message}");
            }
        }

        private void Preview_WebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            try
            {
                using var d = System.Text.Json.JsonDocument.Parse(args.TryGetWebMessageAsString());
                if (d.RootElement.TryGetProperty("type", out var t) && t.GetString() == "ready")
                {
                    _previewReady = true;
                    ApplyPreviewTheme();
                    PushPreview(_pendingPreviewHtml);
                }
            }
            catch { }
        }

        private void ApplyPreviewTheme()
        {
            if (PreviewWebView?.CoreWebView2 == null) return;
            bool dark = RootGrid.ActualTheme == ElementTheme.Dark;
            _ = PreviewWebView.CoreWebView2.ExecuteScriptAsync(
                $"window.previewApi && window.previewApi.setTheme({(dark ? "true" : "false")})");
        }

        private async void PushPreview(string html)
        {
            if (!_previewReady || PreviewWebView?.CoreWebView2 == null) return;
            try
            {
                string arg = System.Text.Json.JsonSerializer.Serialize(html ?? string.Empty);
                await PreviewWebView.CoreWebView2.ExecuteScriptAsync($"window.previewApi.setContent({arg})");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Preview] setContent failed: {ex.Message}");
            }
        }

        private void ClearPreview()
        {
            _pendingPreviewHtml = "<p></p>";
            PushPreview("<p></p>");
        }

        private void UpdatePreview(string content)
        {
            string html = content ?? string.Empty;
            _pendingPreviewHtml = html;
            PushPreview(html);
        }

        private void UpdateBufferUI()
        {
            Brush GetThemeBrush(string key, Windows.UI.Color fallback) =>
                (Application.Current.Resources.TryGetValue(key, out object val) && val is Brush b) ? b : new SolidColorBrush(fallback);

            if (_entry.Buffer.Length == 0)
            {
                BufferDisplay.Text = "---";
                BufferDisplay.Opacity = 0.5;
                PlaintextBadge.Visibility = Visibility.Collapsed;
            }
            else
            {
                BufferDisplay.Text = _entry.BufferKey.ToUpper();
                BufferDisplay.Opacity = 1.0;
                BufferDisplay.Foreground = _entry.BufferMatches ? GetThemeBrush("AccentTextFillColorPrimaryBrush", Colors.DeepSkyBlue) : new SolidColorBrush(Colors.Crimson);
                PlaintextBadge.Visibility = _entry.IsPlainText ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void TemplateTree_SelectionChanged(TreeView sender, object args)
        {
            if (args is TreeViewSelectionChangedEventArgs selection && selection.AddedItems.Count > 0)
            {
                var item = selection.AddedItems[0];
                if (item is TreeViewNode node && node.Content is Template t) UpdatePreview(t.Content);
                else if (item is Template temp) UpdatePreview(temp.Content);
            }
            else ClearPreview();
        }

        private void HelpButton_Click(object sender, RoutedEventArgs e) => HelpTip.IsOpen = true;

        // ---- Tree double-click / right-click ----
        private Template? _contextTemplate;

        private void Tree_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is TreeViewNode node && node.Content is Template t)
                ExecutePaste(t, false);   // default paste — same as pressing the shortcut
        }

        private void Tree_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            // Capture the right-clicked template so the context-menu handlers know the target.
            _contextTemplate = (sender is FrameworkElement fe && fe.DataContext is TreeViewNode node)
                ? node.Content as Template
                : null;
        }

        private void CtxPaste_Click(object sender, RoutedEventArgs e)
        {
            if (_contextTemplate != null) ExecutePaste(_contextTemplate, false);
        }

        private void CtxPasteAs_Click(object sender, RoutedEventArgs e)
        {
            if (_contextTemplate != null && sender is MenuFlyoutItem mi && mi.Tag is string tag
                && Enum.TryParse<PasteMode>(tag, out var mode))
                ExecutePaste(_contextTemplate, mode);
        }

        // ---- Single Key / Multi Key tabs ----
        // The newly visible list highlights its first row so the arrows and Enter have somewhere to start
        // — but only in shortcut mode, so switching tabs never lights a list up while focus is in the
        // search box or the tree.
        private void ShortcutTabs_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
        {
            if (_rendering) return;
            _entry.SelectTab(ReferenceEquals(sender.SelectedItem, TabMulti) ? ShortcutTab.Multi : ShortcutTab.Single);
            Render();
        }

        // A clicked row becomes the highlighted one (what Enter pastes).
        private void SingleKeyList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_rendering) _entry.Select(ShortcutTab.Single, SingleKeyList.SelectedIndex);
        }

        private void MultiKeyList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_rendering) _entry.Select(ShortcutTab.Multi, MultiKeyList.SelectedIndex);
        }

        private void ShortcutItem_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is ShortcutRow row)
                ExecutePaste(row.Template, false);   // default paste — same as pressing the shortcut
        }
        #endregion
    }
}