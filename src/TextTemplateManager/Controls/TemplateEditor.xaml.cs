using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using TextTemplateManager.Helpers;
using TextTemplateManager.Models;
using TextTemplateManager.Services.Translation;

namespace TextTemplateManager.Controls;

/// <summary>The rich-text editor (TipTap in a WebView2) with a tab per language above it. It shows one tab of one
/// template at a time and writes the edits back to that tab; the store saves them. Must not live in a DataTemplate.</summary>
public sealed partial class TemplateEditor : UserControl
{
    private bool _initStarted;
    private bool _ready;
    // Template and tab loaded in the editor (the template by reference), so edits save to the right one.
    private Template? _current;
    private string? _currentTab;
    private string? _currentKey;
    private Template? _requested;
    private string? _requestedTab;
    private bool _requestedEditable = true;
    // The tabs as last built (ids, languages, editable), so typing in a tab doesn't rebuild them.
    private string? _tabsShown;
    private bool _buildingTabs;
    // Language detections the page hasn't answered yet, by request id.
    private readonly Dictionary<int, TaskCompletionSource<LanguageGuess?>> _detections = [];
    private int _lastDetection;
    private bool _tabsQueued;

    /// <summary>The editor asked to move focus back to the search box.</summary>
    public event EventHandler? FocusSearchRequested;

    /// <summary>Ctrl+S was pressed in the editor.</summary>
    public event EventHandler? SaveRequested;

    /// <summary>Tab left the editor: forward from its text (true = Shift+Tab, back from its toolbar).</summary>
    public event Action<bool>? TabOut;

    /// <summary>A tab of a template was loaded (template, tab id, null for the first); its content as the editor
    /// shows it (normalised HTML, which can differ from the stored text without any edit).</summary>
    public event Action<Template, string?, string>? ContentLoaded;

    /// <summary>The Translate menu's "Manage languages…" was chosen.</summary>
    public event EventHandler? TranslationSettingsRequested;

    /// <summary>The Translate menu's "Create new template": the translated HTML, the source and the target language.</summary>
    public event Action<string, string, string>? TranslatedCopyRequested;

    /// <summary>The installed translation models, served to the editor's translator. Set before the editor loads.</summary>
    public string? TranslationModelDirectory { get; set; }

    /// <summary>The app settings, where the Translate menu's last languages are kept.</summary>
    public AppSettings? Settings { get; set; }

    /// <summary>Shows a dialog (the language picker, the delete question); None when none can be shown.</summary>
    public Func<ContentDialog, Task<ContentDialogResult>>? ShowDialog { get; set; }

    /// <summary>All templates, whose languages a tab's text is detected among too.</summary>
    public Func<IEnumerable<Template>>? Templates { get; set; }

    /// <summary>The language tabs, a stop of their own in the page's Tab order, before the editor.</summary>
    public FrameworkElement LanguageTabStrip => LanguageTabs;

    public TemplateEditor() => InitializeComponent();

    /// <summary>Shows <paramref name="template"/> (null: an empty editor) at its first tab (another template) or the
    /// tab it was on (the same one again), read-only unless
    /// <paramref name="editable"/>. Before the editor is ready this is remembered and shown once it is.</summary>
    public void Show(Template? template, bool editable)
    {
        if (!ReferenceEquals(_requested, template))
        {
            if (_requested != null) _requested.PropertyChanged -= Requested_PropertyChanged;
            if (template != null) template.PropertyChanged += Requested_PropertyChanged;
            // A fresh copy of the same template (new draft, sync refresh) stays on the tab it was on.
            if (template?.Id != _requested?.Id) _requestedTab = null;
        }
        _requested = template;
        _requestedEditable = editable;
        BuildTabs();
        LoadRequested();
    }

    /// <summary>Shows the template again after a change made outside the editor, without first saving the editor's
    /// text over it (flush before the change).</summary>
    public void Reload()
    {
        _current = null;
        BuildTabs();
        LoadRequested();
    }

    /// <summary>Puts the keyboard focus on the selected language tab.</summary>
    public void FocusTabs()
    {
        if (LanguageTabs.ContainerFromIndex(Math.Max(0, LanguageTabs.SelectedIndex)) is Control tab) tab.Focus(FocusState.Keyboard);
        else LanguageTabs.Focus(FocusState.Keyboard);
    }

    /// <summary>Moves the keyboard focus into the editor: its toolbar, or its text with the caret at the end (the
    /// text when the toolbar is hidden for a read-only template).</summary>
    public void FocusPart(bool text)
    {
        if (!_ready || EditorWebView?.CoreWebView2 == null) return;
        EditorWebView.Focus(FocusState.Keyboard);
        _ = EditorWebView.CoreWebView2.ExecuteScriptAsync(text ? "window.editorApi.focusTextEnd()" : "window.editorApi.focusToolbar()");
    }

    /// <summary>Reads the editor's HTML and saves it to the template tab it belongs to.</summary>
    public async Task FlushAsync()
    {
        var t = _current;
        string? tab = _currentTab;
        if (t == null || EditorWebView?.CoreWebView2 == null) return;
        if (await GetHtmlAsync() is { } html) TemplateTabs.SetContent(t, tab, html);
    }

    private async Task<string?> GetHtmlAsync()
    {
        try
        {
            // ExecuteScriptAsync returns the result JSON-encoded (a quoted string).
            string result = await EditorWebView.CoreWebView2.ExecuteScriptAsync("window.editorApi.getContent()");
            return ContentFromScriptResult(result);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Editor] reading the content failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>The editor's HTML from getContent()'s JSON result; null when it couldn't be read. A script that fails
    /// (page gone while the app closes, not loaded yet) yields JSON null rather than an exception, and an empty editor
    /// still returns "&lt;p&gt;&lt;/p&gt;", so null or "" must never be saved as the template's content.</summary>
    internal static string? ContentFromScriptResult(string? result)
    {
        if (string.IsNullOrWhiteSpace(result)) return null;
        try
        {
            string? html = System.Text.Json.JsonSerializer.Deserialize<string>(result);
            return string.IsNullOrEmpty(html) ? null : html;
        }
        catch (System.Text.Json.JsonException) { return null; }
    }

    private async void EditorWebView_Loaded(object sender, RoutedEventArgs e)
    {
        if (_initStarted) return;
        _initStarted = true;
        try
        {
            await EditorWebView.EnsureCoreWebView2Async(await WebViewEnvironment.GetAsync());
            var core = EditorWebView.CoreWebView2;

            core.WebMessageReceived += Editor_WebMessageReceived;

            // Standard editing context menu (cut / copy / paste / spell-check suggestions).
            // Editor_ContextMenuRequested trims it to editing items; dev tools stay off so
            // there is no "Inspect".
            core.Settings.AreDefaultContextMenusEnabled = true;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.ContextMenuRequested += Editor_ContextMenuRequested;
            MapTranslatorHosts(core);

            EditorWebView.NavigateToString(EditorPages.Editor);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Editor] init failed: {ex.Message}");
        }
    }

    // The translator's worker and engine (Assets\translator) and the models it reads. The editor page is an
    // opaque origin, so both hosts must allow cross-origin reads.
    private void MapTranslatorHosts(CoreWebView2 core)
    {
        try
        {
            core.SetVirtualHostNameToFolderMapping("ttm-translator.example",
                System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "translator"), CoreWebView2HostResourceAccessKind.Allow);
            if (TranslationModelDirectory is { } models)
            {
                System.IO.Directory.CreateDirectory(models);
                core.SetVirtualHostNameToFolderMapping("ttm-models.example", models, CoreWebView2HostResourceAccessKind.Allow);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Editor] translator hosts failed: {ex.Message}");
        }
    }

    // Navigation / page / developer context-menu entries to drop, leaving the editing and
    // spell-check items (whose names are dynamic and kept by default).
    private static readonly HashSet<string> _hiddenContextItems = new()
    {
        "back", "forward", "reload", "reloadFrame", "saveAs", "savePageAs", "print",
        "createQrCode", "inspectElement", "viewSource", "viewPageSource", "webCapture",
        "share", "webSelect", "translate", "saveImageAs", "copyImage", "copyImageLink",
        "openImageInNewTab", "saveLinkAs", "copyLinkToText",
    };

    // "Send tab to your devices" has no stable Name; matched by its label, which older WebView2 versions show in
    // English whatever the language.
    private static readonly string[] _hiddenContextLabels = { "to your devices" };

    // The "Writing direction" submenu has no stable Name and a label in the app's language: a nameless submenu of
    // checkboxes (Default / Left to right / Right to left).
    private static bool IsWritingDirection(CoreWebView2ContextMenuItem item) =>
        item.Kind == CoreWebView2ContextMenuItemKind.Submenu && item.Name == "other" && item.Children.Count > 0
        && item.Children.All(c => c.Kind == CoreWebView2ContextMenuItemKind.CheckBox);

    private void Editor_ContextMenuRequested(CoreWebView2 sender, CoreWebView2ContextMenuRequestedEventArgs args)
    {
        var items = args.MenuItems;
        for (int i = items.Count - 1; i >= 0; i--)
            if (_hiddenContextItems.Contains(items[i].Name) || HasHiddenLabel(items[i].Label) || IsWritingDirection(items[i]))
                items.RemoveAt(i);

        // Drop separators left dangling at the menu edges or doubled after the removals.
        for (int i = items.Count - 1; i > 0; i--)
            if (items[i].Kind == CoreWebView2ContextMenuItemKind.Separator && items[i - 1].Kind == CoreWebView2ContextMenuItemKind.Separator)
                items.RemoveAt(i);
        while (items.Count > 0 && items[0].Kind == CoreWebView2ContextMenuItemKind.Separator)
            items.RemoveAt(0);
        while (items.Count > 0 && items[^1].Kind == CoreWebView2ContextMenuItemKind.Separator)
            items.RemoveAt(items.Count - 1);
    }

    private static bool HasHiddenLabel(string label)
    {
        if (string.IsNullOrEmpty(label)) return false;
        string s = label.Replace("&", "").ToLowerInvariant();
        foreach (var frag in _hiddenContextLabels)
            if (s.Contains(frag)) return true;
        return false;
    }

    private void Editor_WebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        string json;
        try { json = args.TryGetWebMessageAsString(); }
        catch { return; }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var type = doc.RootElement.GetProperty("type").GetString();

            if (type == "ready")
            {
                _ready = true;
                ApplyTheme();
                LoadRequested();
            }
            else if (type == "change")
            {
                var html = doc.RootElement.TryGetProperty("html", out var h) ? h.GetString() : null;
                var key = doc.RootElement.TryGetProperty("key", out var k) ? k.GetString() : null;
                // A change typed just before a switch can arrive after it: it belongs to the tab it was typed in.
                if (!string.IsNullOrEmpty(html) && key == _currentKey) OnContentChanged(html);
            }
            else if (type == "detected")
            {
                if (doc.RootElement.TryGetProperty("id", out var di) && di.TryGetInt32(out int id) && _detections.TryGetValue(id, out var answer))
                    answer.TrySetResult(doc.RootElement.TryGetProperty("code", out var dc) && TemplateLanguages.NormalizeCode(dc.ValueKind == System.Text.Json.JsonValueKind.String ? dc.GetString() : null) is { } code
                        ? new LanguageGuess(code, doc.RootElement.TryGetProperty("sure", out var ds) && ds.ValueKind == System.Text.Json.JsonValueKind.True)
                        : null);
            }
            else if (type == "switchTab")
            {
                SelectNeighbourTab(doc.RootElement.TryGetProperty("step", out var st) && st.TryGetInt32(out int step) ? step : 1);
            }
            else if (type == "tabMenu")
            {
                var root = doc.RootElement;
                ShowTabMenu(root.TryGetProperty("x", out var x) ? x.GetDouble() : 0, root.TryGetProperty("y", out var y) ? y.GetDouble() : 0);
            }
            else if (type == "openLink")
            {
                var href = doc.RootElement.TryGetProperty("href", out var u) ? u.GetString() : null;
                ExternalLinks.OpenWebLink(href);
            }
            else if (type == "focusSearch")
            {
                FocusSearchRequested?.Invoke(this, EventArgs.Empty);
            }
            else if (type == "save")
            {
                SaveRequested?.Invoke(this, EventArgs.Empty);
            }
            else if (type == "tabOut")
            {
                TabOut?.Invoke(doc.RootElement.TryGetProperty("back", out var b) && b.GetBoolean());
            }
            else if (type == "translationRefresh")
            {
                PushTranslationChoices();
            }
            else if (type == "translationChoice" && Settings is { } settings)
            {
                if (doc.RootElement.TryGetProperty("from", out var f) && f.GetString() is { Length: > 0 } from) settings.TranslateFrom = from;
                if (doc.RootElement.TryGetProperty("to", out var t) && t.GetString() is { Length: > 0 } to) settings.TranslateTo = to;
                if (doc.RootElement.TryGetProperty("formal", out var fo) && fo.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False)
                    settings.TranslateFormal = fo.GetBoolean();
            }
            else if (type == "translationSettings")
            {
                TranslationSettingsRequested?.Invoke(this, EventArgs.Empty);
            }
            else if (type == "translationCreate")
            {
                var root = doc.RootElement;
                if (root.TryGetProperty("html", out var h) && h.GetString() is { Length: > 0 } html
                    && root.TryGetProperty("from", out var f) && f.GetString() is { Length: > 0 } from
                    && root.TryGetProperty("to", out var t) && t.GetString() is { Length: > 0 } to)
                    TranslatedCopyRequested?.Invoke(html, from, to);
            }
            else if (type == "translationAddLanguage")
            {
                var root = doc.RootElement;
                if (root.TryGetProperty("html", out var h) && h.GetString() is { Length: > 0 } html
                    && root.TryGetProperty("from", out var f) && f.GetString() is { Length: > 0 } from
                    && root.TryGetProperty("to", out var t) && t.GetString() is { Length: > 0 } to
                    && root.TryGetProperty("key", out var k) && k.GetString() is { Length: > 0 } key)
                    AddTranslatedLanguage(html, from, to, key);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Editor] message parse error: {ex.Message}");
        }
    }

    /// <summary>Loads the requested template's HTML into the editor.</summary>
    private async void LoadRequested()
    {
        if (!_ready || EditorWebView?.CoreWebView2 == null) return;

        // Flush the previous template's debounced edits before swapping content.
        await FlushAsync();

        if (_requested is not Template t)
        {
            _current = null;
            _currentTab = null;
            _currentKey = null;
            await SetHtmlAsync("<p></p>", null);
            try { await EditorWebView.CoreWebView2.ExecuteScriptAsync("window.editorApi.setTabMenu(false)"); }
            catch { }
            return;
        }

        string? tab = TemplateTabs.IndexOf(t, _requestedTab) > 0 ? _requestedTab : t.LanguageId;
        // By its id from here on: a tab moved away from the front stays the selected one.
        _requestedTab = tab;
        _current = t;
        _currentTab = tab;
        _currentKey = $"{t.Id}/{tab ?? "first"}";
        await SetHtmlAsync(GetHtmlForEditing(TemplateTabs.ContentOf(t, tab)), _currentKey);
        if (await GetHtmlAsync() is { } shown && ReferenceEquals(_current, t) && _currentTab == tab) ContentLoaded?.Invoke(t, tab, shown);

        // Read-only for save-off synced templates.
        try
        {
            await EditorWebView.CoreWebView2.ExecuteScriptAsync(
                $"window.editorApi && window.editorApi.setEditable({(_requestedEditable ? "true" : "false")}); window.editorApi.setTabMenu(true)");
        }
        catch { }
    }

    private static string GetHtmlForEditing(string? content)
    {
        content = (content ?? string.Empty).TrimEnd('\0');
        return string.IsNullOrWhiteSpace(content) ? "<p></p>" : content;
    }

    // The key names the template and tab, so the page keeps each tab's undo history while the template is shown.
    private async Task SetHtmlAsync(string html, string? key)
    {
        try
        {
            string arg = System.Text.Json.JsonSerializer.Serialize(html);
            string keyArg = System.Text.Json.JsonSerializer.Serialize(key);
            await EditorWebView.CoreWebView2.ExecuteScriptAsync($"window.editorApi.setContent({arg}, {keyArg})");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Editor] setContent failed: {ex.Message}");
        }
    }

    private void OnContentChanged(string html)
    {
        // The message reflects whatever is currently loaded in the editor, so save it to
        // that template (tracked by reference), not necessarily the current tree selection.
        var t = _current;
        if (t == null) return;

        // The store saves the change (debounced); nothing else to schedule here.
        TemplateTabs.SetContent(t, _currentTab, html);
    }

    // The Translate menu asks each time it opens, so languages installed meanwhile in Settings show up.
    private void PushTranslationChoices()
    {
        if (EditorWebView?.CoreWebView2 == null) return;
        var choices = TranslationChoices.From(TranslationModelDirectory is { } dir ? new ModelStore(dir).Installed() : []);
        var t = _current;
        string json = System.Text.Json.JsonSerializer.Serialize(new
        {
            tab = t == null ? null : new
            {
                language = TemplateTabs.All(t).ElementAtOrDefault(TemplateTabs.IndexOf(t, _currentTab))?.Language,
                languages = TemplateTabs.All(t).Select(x => x.Language).OfType<string>(),
                editable = _requestedEditable && ReferenceEquals(_requested, t),
            },
            sources = choices.Sources.Select(c => new { code = c.Code, name = c.Name }),
            targets = choices.Targets.Select(c => new { code = c.Code, name = c.Name }),
            from = choices.FromOrDefault(Settings?.TranslateFrom),
            to = choices.ToOrDefault(Settings?.TranslateTo),
            formal = Settings?.TranslateFormal ?? false,
        });
        _ = EditorWebView.CoreWebView2.ExecuteScriptAsync($"window.editorApi && window.editorApi.setTranslation({json})");
    }

    private void ApplyTheme()
    {
        if (EditorWebView?.CoreWebView2 == null) return;
        bool dark = ActualTheme == ElementTheme.Dark;
        _ = EditorWebView.CoreWebView2.ExecuteScriptAsync(
            $"window.editorApi && window.editorApi.setTheme({(dark ? "true" : "false")})");
    }

    /// <summary>The language <paramref name="html"/> is written in, among the suggested languages and those the
    /// templates use; null when it can't be told for sure (too little text) or the editor isn't ready.</summary>
    public async Task<string?> DetectLanguageAsync(string html) => await GuessLanguageAsync(html) is { Sure: true } guess ? guess.Code : null;

    /// <summary>Like <see cref="DetectLanguageAsync"/>, plus guesses for shorter texts that aren't sure.</summary>
    public async Task<LanguageGuess?> GuessLanguageAsync(string html)
    {
        string text = HtmlUtils.ToPlainText(html).Trim();
        if (!_ready || EditorWebView?.CoreWebView2 == null || text.Length == 0) return null;
        int id = ++_lastDetection;
        var answer = new TaskCompletionSource<LanguageGuess?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _detections[id] = answer;
        try
        {
            var candidates = LanguageCatalog.DetectionCandidates(SuggestedLanguages(), Templates?.Invoke() ?? []);
            string args = $"{id}, {System.Text.Json.JsonSerializer.Serialize(text)}, {System.Text.Json.JsonSerializer.Serialize(candidates)}";
            await EditorWebView.CoreWebView2.ExecuteScriptAsync($"window.editorApi.detectLanguage({args})");
            return await answer.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Editor] language detection failed: {ex.Message}");
            return null;
        }
        finally
        {
            _detections.Remove(id);
        }
    }

    // The app's UI languages and the installed translator languages.
    private IReadOnlyList<string> SuggestedLanguages()
    {
        var translator = TranslationModelDirectory is { } dir ? new ModelStore(dir).Installed().SelectMany(p => new[] { p.From, p.To }) : [];
        return LanguageCatalog.Suggested(UiLanguages.Available, translator);
    }

    #region Language tabs

    // Ids, languages and order changed (an edit here, a sync, a draft saved): the tab headers follow, once per change.
    private void Requested_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(Models.Template.Language) or nameof(Models.Template.LanguageId) or nameof(Models.Template.AltLanguages)) || _tabsQueued) return;
        _tabsQueued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _tabsQueued = false;
            BuildTabs();
        });
    }

    private void BuildTabs()
    {
        var t = _requested;
        var tabs = t != null ? TemplateTabs.All(t) : [];
        string shown = string.Join("|", tabs.Select(x => $"{x.Id}:{x.Language}")) + $"|{_requestedEditable}|{t?.Id}";
        if (shown != _tabsShown)
        {
            _tabsShown = shown;
            _buildingTabs = true;
            try
            {
                LanguageTabs.TabItems.Clear();
                if (t != null)
                {
                    var offered = TemplateLanguages.Offered(t).Skip(1).Select(o => o.Id).ToHashSet();
                    for (int i = 0; i < tabs.Count; i++)
                        LanguageTabs.TabItems.Add(NewTab(tabs[i], i, tabs.Count, i > 0 && !offered.Contains(tabs[i].Id)));
                }
            }
            finally { _buildingTabs = false; }
            LanguageTabs.CanReorderTabs = _requestedEditable;
            AddLanguageButton.Visibility = _requestedEditable ? Visibility.Visible : Visibility.Collapsed;
        }
        SelectTab(t != null ? TemplateTabs.IndexOf(t, _requestedTab) : -1);
    }

    private void SelectTab(int index)
    {
        int count = LanguageTabs.TabItems.Count;
        _buildingTabs = true;
        try { LanguageTabs.SelectedIndex = index >= 0 && index < count ? index : (count > 0 ? 0 : -1); }
        finally { _buildingTabs = false; }
    }

    // The first tab, pasted by default, has a star once there are others; a tab that isn't pasted (no language, or
    // the language of an earlier tab) a warning sign.
    private TabViewItem NewTab(TemplateLanguage tab, int index, int count, bool notPasted)
    {
        string name = tab.Language != null ? TranslationLanguage.NameOf(tab.Language) : Loc.Get("LanguageTabs_NoLanguage");
        string? description = index == 0 && count > 1 ? Loc.Get("LanguageTabs_DefaultLanguageTip")
            : notPasted ? (tab.Language == null ? Loc.Get("LanguageTabs_UnsetLanguageTip") : Loc.Get("LanguageTabs_DuplicateLanguageTip"))
            : null;
        var item = new TabViewItem
        {
            Header = name,
            Tag = tab.Id,
            IsClosable = false,
            IconSource = index == 0 && count > 1 ? new FontIconSource { Glyph = "\uE735" }
                : notPasted ? new FontIconSource { Glyph = "\uE7BA" } : null,
        };
        if (description != null)
        {
            ToolTipService.SetToolTip(item, description);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(item, description);
        }
        if (_requestedEditable) item.ContextFlyout = TabMenu(item);
        return item;
    }

    private MenuFlyout TabMenu(TabViewItem item)
    {
        var menu = new MenuFlyout();
        MenuFlyoutItem Entry(string text, string glyph, Func<Template, string?, Task> act)
        {
            var entry = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph } };
            entry.Click += async (_, _) => { if (_requested is { } t) await act(t, item.Tag as string); };
            menu.Items.Add(entry);
            return entry;
        }
        // The add button sits outside the keyboard's way through the tabs; the menu (Shift+F10) reaches it too.
        Entry(Loc.Get("LanguageTabs_AddLanguage"), "\uE710", (_, _) => AddLanguageAsync());
        Entry(Loc.Get("LanguageTabs_ChangeLanguage"), "\uE8C1", ChangeLanguageAsync);
        var makeDefault = Entry(Loc.Get("LanguageTabs_MakeDefault"), "\uE735", (t, id) => Moved(t, id, 0));
        var left = Entry(Loc.Get("LanguageTabs_MoveLeft"), "\uE72B", (t, id) => Moved(t, id, TemplateTabs.IndexOf(t, id) - 1));
        var right = Entry(Loc.Get("LanguageTabs_MoveRight"), "\uE72A", (t, id) => Moved(t, id, TemplateTabs.IndexOf(t, id) + 1));
        menu.Items.Add(new MenuFlyoutSeparator());
        var delete = Entry(Loc.Get("LanguageTabs_DeleteLanguage"), "\uE74D", DeleteLanguageAsync);
        menu.Opening += (_, _) =>
        {
            if (_requested is not { } t) return;
            string? id = item.Tag as string;
            int at = TemplateTabs.IndexOf(t, id);
            makeDefault.IsEnabled = TemplateTabs.CanMove(t, id, 0);
            left.IsEnabled = TemplateTabs.CanMove(t, id, at - 1);
            right.IsEnabled = TemplateTabs.CanMove(t, id, at + 1);
            delete.IsEnabled = TemplateTabs.CanRemove(t, id);
        };
        return menu;
    }

    // The toolbar's wrench: the shown tab's menu under the button (page coordinates, which are the WebView's DIPs).
    private void ShowTabMenu(double x, double y)
    {
        if (!_requestedEditable || LanguageTabs.SelectedItem is not TabViewItem { ContextFlyout: MenuFlyout menu }) return;
        menu.ShowAt(EditorWebView, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions
        {
            Position = new Windows.Foundation.Point(x, y),
            Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedLeft,
        });
    }

    private static Task Moved(Template t, string? id, int to)
    {
        TemplateTabs.Move(t, id, to);
        return Task.CompletedTask;
    }

    private void LanguageTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_buildingTabs || _requested is not { } t || LanguageTabs.SelectedItem is not TabViewItem item) return;
        string? id = item.Tag as string;
        if (TemplateTabs.IndexOf(t, id) == TemplateTabs.IndexOf(t, _currentTab) && ReferenceEquals(_current, t)) return;
        _requestedTab = id;
        LoadRequested();
    }

    // A tab dragged to another place: the template takes the new order, or the tabs go back when it can't (a tab
    // without a language can't be first).
    private void LanguageTabs_TabItemsChanged(TabView sender, Windows.Foundation.Collections.IVectorChangedEventArgs args)
    {
        if (_buildingTabs || _requested is not { } t || args.CollectionChange != Windows.Foundation.Collections.CollectionChange.ItemInserted) return;
        var ids = LanguageTabs.TabItems.OfType<TabViewItem>().Select(i => i.Tag as string).ToList();
        if (ids.Count != TemplateTabs.All(t).Count) return;
        if (!TemplateTabs.Reorder(t, ids))
        {
            _tabsShown = null;
            DispatcherQueue.TryEnqueue(BuildTabs);
        }
    }

    private void PreviousTab_Invoked(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        SelectNeighbourTab(-1);
    }

    private void NextTab_Invoked(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        SelectNeighbourTab(1);
    }

    private void SelectNeighbourTab(int step)
    {
        int count = LanguageTabs.TabItems.Count;
        if (count < 2) return;
        LanguageTabs.SelectedIndex = ((Math.Max(0, LanguageTabs.SelectedIndex) + step) % count + count) % count;
    }

    private async void AddLanguage_Click(object sender, RoutedEventArgs e) => await AddLanguageAsync();

    private async Task AddLanguageAsync()
    {
        if (_requested is not { } t || !_requestedEditable) return;
        if (!TemplateTabs.CanAdd(t))
        {
            // The text so far gets its language first (the first tab is the one pasted by default): the detected one
            // when it is sure, else the user's pick, with a guess for a short text offered first.
            var guess = await GuessTabLanguageAsync(t, null);
            if (!ReferenceEquals(_requested, t)) return;
            string? first = guess is { Sure: true } ? guess.Code
                : await PickLanguageAsync(Loc.Get("LanguagePicker_FirstTitle"), Loc.Get("LanguagePicker_FirstMessage"), [], guess?.Code);
            if (first == null || !ReferenceEquals(_requested, t)) return;
            TemplateTabs.SetLanguage(t, null, first);
        }
        // The new tab gets its language later, from its menu; until then it isn't pasted.
        _requestedTab = TemplateTabs.Add(t);
        BuildTabs();
        LoadRequested();
    }

    private async Task ChangeLanguageAsync(Template t, string? id)
    {
        int at = TemplateTabs.IndexOf(t, id);
        if (at < 0) return;
        var others = TemplateTabs.All(t).Where((_, i) => i != at).Select(x => x.Language).ToList();
        var guess = await GuessTabLanguageAsync(t, id);
        string? language = await PickLanguageAsync(Loc.Get("LanguagePicker_ChangeTitle"), null, others, guess?.Code);
        if (language != null && ReferenceEquals(_requested, t)) TemplateTabs.SetLanguage(t, id, language);
    }

    private async Task DeleteLanguageAsync(Template t, string? id)
    {
        int at = TemplateTabs.IndexOf(t, id);
        if (at < 0 || !TemplateTabs.CanRemove(t, id)) return;
        var tab = TemplateTabs.All(t)[at];
        if (HtmlUtils.ToPlainText(tab.Content).Trim().Length > 0)
        {
            string name = tab.Language != null ? TranslationLanguage.NameOf(tab.Language) : Loc.Get("LanguageTabs_NoLanguage");
            var dialog = new ContentDialog
            {
                Title = Loc.Format("LanguageTabs_DeleteLanguageTitle", name),
                Content = Loc.Format("LanguageTabs_DeleteLanguageText", name),
                PrimaryButtonText = Loc.Get("LanguageTabs_DeleteLanguageButton"),
                CloseButtonText = Loc.Get("Common_CancelButton"),
                DefaultButton = ContentDialogButton.Close,
            };
            if (ShowDialog == null || await ShowDialog(dialog) != ContentDialogResult.Primary || !ReferenceEquals(_requested, t)) return;
            at = TemplateTabs.IndexOf(t, id);
            if (at < 0) return;
        }
        bool shown = TemplateTabs.IndexOf(t, _currentTab) == at;
        TemplateTabs.Remove(t, id);
        // The editor then shows the tab that took its place.
        if (shown)
        {
            var tabs = TemplateTabs.All(t);
            _requestedTab = tabs[Math.Min(at, tabs.Count - 1)].Id;
            _current = null;
        }
        BuildTabs();
        if (shown) LoadRequested();
    }

    // The Translate menu's "Add as language": the translation of the tab named by `key` becomes a new tab of the same
    // template, shown next. Dropped when another template is shown by now.
    private void AddTranslatedLanguage(string html, string from, string to, string key)
    {
        if (_current is not { } t || !ReferenceEquals(_requested, t) || !_requestedEditable) return;
        int slash = key.IndexOf('/');
        if (slash < 0 || key[..slash] != t.Id.ToString()) return;
        string? source = key[(slash + 1)..] is var s && s != "first" ? s : null;
        if (TemplateTabs.AddTranslation(t, source, from, to, html) is not { } id) return;
        _requestedTab = id;
        BuildTabs();
        LoadRequested();
    }

    // The language of a tab's text, including what was typed in the last moment.
    private async Task<LanguageGuess?> GuessTabLanguageAsync(Template t, string? tabId)
    {
        if (ReferenceEquals(_current, t) && _currentTab == tabId) await FlushAsync();
        return await GuessLanguageAsync(TemplateTabs.ContentOf(t, tabId));
    }

    // The picker offers the language detected in the tab's text first.
    private async Task<string?> PickLanguageAsync(string title, string? message, IReadOnlyList<string?> taken, string? detected)
    {
        if (ShowDialog == null) return null;
        return await new LanguagePickerDialog(title, message, SuggestedLanguages(), taken, detected).PickAsync(ShowDialog);
    }

    #endregion
}
