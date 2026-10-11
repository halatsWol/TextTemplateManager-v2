using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TextTemplateManager.Helpers;
using TextTemplateManager.Models;

namespace TextTemplateManager.Controls;

/// <summary>The rich-text editor (TipTap in a WebView2). It shows one template at a time and writes the
/// edits back to that template; the store saves them. Must not live in a DataTemplate.</summary>
public sealed partial class TemplateEditor : UserControl
{
    private bool _initStarted;
    private bool _ready;
    // Template loaded in the editor (by reference), so edits save to the right one.
    private Template? _current;
    private Template? _requested;
    private bool _requestedEditable = true;

    /// <summary>The editor asked to move focus back to the search box.</summary>
    public event EventHandler? FocusSearchRequested;

    public TemplateEditor() => InitializeComponent();

    /// <summary>Shows <paramref name="template"/> (null: an empty editor), read-only unless
    /// <paramref name="editable"/>. Before the editor is ready this is remembered and shown once it is.</summary>
    public void Show(Template? template, bool editable)
    {
        _requested = template;
        _requestedEditable = editable;
        LoadRequested();
    }

    /// <summary>Reads the editor's HTML and saves it to the template it belongs to.</summary>
    public async Task FlushAsync()
    {
        var t = _current;
        if (t == null || EditorWebView?.CoreWebView2 == null) return;

        try
        {
            // ExecuteScriptAsync returns the result JSON-encoded (a quoted string).
            string result = await EditorWebView.CoreWebView2.ExecuteScriptAsync("window.editorApi.getContent()");
            string html = System.Text.Json.JsonSerializer.Deserialize<string>(result) ?? string.Empty;
            if (t.Content != html) t.Content = html;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Editor] flush failed: {ex.Message}");
        }
    }

    private async void EditorWebView_Loaded(object sender, RoutedEventArgs e)
    {
        if (_initStarted) return;
        _initStarted = true;
        try
        {
            await EditorWebView.EnsureCoreWebView2Async();
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

            EditorWebView.NavigateToString(EditorPages.Editor);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Editor] init failed: {ex.Message}");
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

    // Entries WebView2 exposes with no stable Name, matched by label instead: the
    // "Writing Direction" submenu and "Send tab to your devices".
    private static readonly string[] _hiddenContextLabels = { "writing direction", "to your devices" };

    private void Editor_ContextMenuRequested(CoreWebView2 sender, CoreWebView2ContextMenuRequestedEventArgs args)
    {
        var items = args.MenuItems;
        for (int i = items.Count - 1; i >= 0; i--)
            if (_hiddenContextItems.Contains(items[i].Name) || HasHiddenLabel(items[i].Label))
                items.RemoveAt(i);

        // Drop separators left dangling at the menu edges after the removals.
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
                OnContentChanged(html ?? string.Empty);
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
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Editor] message parse error: {ex.Message}");
        }
    }

    /// <summary>Loads the requested template's HTML into the editor (migrating legacy RTF).</summary>
    private async void LoadRequested()
    {
        if (!_ready || EditorWebView?.CoreWebView2 == null) return;

        // Flush the previous template's debounced edits before swapping content.
        await FlushAsync();

        if (_requested is not Template t)
        {
            _current = null;
            await SetHtmlAsync("<p></p>");
            return;
        }

        _current = t;
        await SetHtmlAsync(GetHtmlForEditing(t));

        // Read-only for save-off synced templates.
        try
        {
            await EditorWebView.CoreWebView2.ExecuteScriptAsync(
                $"window.editorApi && window.editorApi.setEditable({(_requestedEditable ? "true" : "false")})");
        }
        catch { }
    }

    /// <summary>Template content as HTML; legacy RTF is converted once and written back.</summary>
    private static string GetHtmlForEditing(Template t)
    {
        string content = (t.Content ?? string.Empty).TrimEnd('\0');

        if (content.TrimStart().StartsWith("{\\rtf1", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                string html = RtfPipe.Rtf.ToHtml(content);
                t.Content = html; // migrate: store HTML from now on
                return html;
            }
            catch
            {
                return "<p></p>";
            }
        }

        return string.IsNullOrWhiteSpace(content) ? "<p></p>" : content;
    }

    private async Task SetHtmlAsync(string html)
    {
        try
        {
            string arg = System.Text.Json.JsonSerializer.Serialize(html);
            await EditorWebView.CoreWebView2.ExecuteScriptAsync($"window.editorApi.setContent({arg})");
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
        if (t.Content != html) t.Content = html;
    }

    private void ApplyTheme()
    {
        if (EditorWebView?.CoreWebView2 == null) return;
        bool dark = ActualTheme == ElementTheme.Dark;
        _ = EditorWebView.CoreWebView2.ExecuteScriptAsync(
            $"window.editorApi && window.editorApi.setTheme({(dark ? "true" : "false")})");
    }
}
