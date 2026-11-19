using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using TextTemplateManager.Common;
using TextTemplateManager.Data;
using TextTemplateManager.Helpers;
using TextTemplateManager.Models;
using TextTemplateManager.Services.Pasting;

namespace TextTemplateManager.Services.Platform;

/// <summary>Serves the connector from the live template tree. The tree is snapshotted on the UI
/// thread (<see cref="Rebuild"/>) into an immutable structure the connector's background threads
/// read; rendering is a pure function so it runs off-thread safely.</summary>
public sealed class AppConnectorData : IConnectorDataSource
{
    private sealed record Snap(List<ConnectorNodeDto> Tree, Dictionary<string, TemplateEntry> Templates);
    private sealed record TemplateEntry(string Name, IReadOnlyList<TemplateLanguage> Offered, PasteMode Default);

    private volatile Snap _snap = new(new(), new());
    private readonly DataNode _store;

    public AppConnectorData(DataNode store) => _store = store;

    // Captured on the UI thread (this is constructed there); writes marshal back to it.
    private readonly DispatcherQueue? _ui = DispatcherQueue.GetForCurrentThread();

    public string AppVersion => AppInfo.Version;

    public IReadOnlyList<PasteModeDto> PasteModes() =>
        PasteModeLabel.DisplayOrder.Select(m => new PasteModeDto(m.ToString(), PasteModeLabel.Invariant(m))).ToList();

    public IReadOnlyList<ConnectorNodeDto> Tree() => _snap.Tree;

    public TemplateContentDto? Template(string id, string? mode, string? language = null, string? languageId = null)
    {
        if (!_snap.Templates.TryGetValue(id, out var t)) return null;
        var tab = ConnectorLanguages.Find(t.Offered, language, languageId);
        PasteMode m = ParseMode(mode, t.Default);
        // Outgoing content is allow-list sanitized before rendering so nothing active can ride a
        // connector reply back into the browser — even if a template's stored HTML predates the
        // incoming sanitizer or was edited in-app from a pasted web selection.
        var (content, contentType) = PasteService.RenderForMode(HtmlSanitizer.Sanitize(tab.Content), m);
        return new TemplateContentDto(id, PlainName(t.Name), m.ToString(), contentType, content,
            tab.Language, t.Offered.Count > 1 ? tab.Id : null);
    }

    /// <summary>Creates a template in the local area with the given content (and languages) and returns its id/name.
    /// The tree edit and save run on the UI thread; the returned task completes when they are done.</summary>
    public Task<CreatedTemplateDto> CreateTemplateAsync(NewTemplateDto template)
    {
        // Content is untrusted HTML (a browser selection) — strip active content so a stored template can't carry
        // script into paste targets or connector replies.
        var item = new Template
        {
            Id = Guid.NewGuid(),
            Content = HtmlSanitizer.Sanitize(template.Content),
            DefaultPasteMode = _store.CurrentSettings.DefaultPasteMode,
        };
        // Checked here, so a refused request adds nothing.
        ConnectorLanguages.ApplyNew(item, template.Language,
            template.Languages?.Select(l => l with { Content = string.IsNullOrEmpty(l.Content) ? l.Content : HtmlSanitizer.Sanitize(l.Content) }).ToList());

        return OnUiThread(async () =>
        {
            item.Title = ItemTitles.Unique(CleanName(template.Name), _store.LocalItems);
            await _store.AddItemAsync(item);   // parent null -> local root
            Rebuild();                         // include it in the served snapshot
            _store.NotifyTreeChanged();        // refresh the main-window tree projection
            return new CreatedTemplateDto(item.Id.ToString(), item.Title, ConnectorLanguages.Describe(TemplateLanguages.Offered(item)).Languages);
        });
    }

    /// <summary>Makes a change from outside the window to a template, keeping the editor's text and showing the
    /// change in it; false, with nothing changed, while the template has unsaved edits. Set by the app.</summary>
    public Func<Template, Action, Task<bool>>? ChangeTemplate { get; set; }

    public Task<LanguageWriteDto> SetLanguageAsync(string id, string? language, string? languageId, string content)
    {
        content = HtmlSanitizer.Sanitize(content);
        return OnUiThread(async () =>
        {
            var t = Guid.TryParse(id, out var guid) ? _store.AllItems.FirstOrDefault(x => x.Id == guid) : null;
            if (t == null) throw new ConnectorRefusal(404, "not found");
            if (_store.IsItemReadOnly(t)) throw new ConnectorRefusal(403, "read only");
            if (_store.LocalItems.TopLevelOf(t) is Folder { SyncFileMissing: true }) throw new ConnectorRefusal(409, "sync file missing");

            LanguageWriteDto? written = null;
            void Change() => written = ConnectorLanguages.Write(t, language, languageId, content);
            if (ChangeTemplate == null) Change();
            else if (!await ChangeTemplate(t, Change)) throw new ConnectorRefusal(409, "unsaved edits");

            await _store.FlushPendingSaveAsync();
            Rebuild();
            return written!;
        });
    }

    // Runs on the UI thread; the returned task completes with its result. Bounded, so a stalled or closing UI can't
    // hang the request.
    private Task<T> OnUiThread<T>(Func<Task<T>> work)
    {
        var ui = _ui ?? throw new InvalidOperationException("connector has no UI dispatcher");
        var tcs = new TaskCompletionSource<T>();
        bool queued = ui.TryEnqueue(async () =>
        {
            try { tcs.TrySetResult(await work()); }
            catch (Exception ex) { tcs.TrySetException(ex); }
        });
        if (!queued) throw new InvalidOperationException("UI thread unavailable");
        return tcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    // A label served in a connector reply: strip control chars and cap length so a title can't carry
    // control/formatting bytes out. (Empty stays empty here — unlike CleanName's new-item fallback.)
    private static string PlainName(string? s)
    {
        if (string.IsNullOrEmpty(s)) return s ?? "";
        string cleaned = new string(s.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return cleaned.Length > 200 ? cleaned[..200] : cleaned;
    }

    // A plain-text title from the (untrusted) name field: no control chars, trimmed, length-capped.
    private static string CleanName(string? name)
    {
        string s = new string((name ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (s.Length == 0) return Loc.Get("Main_NewTemplateTitle");
        return s.Length > 120 ? s[..120].Trim() : s;
    }

    /// <summary>Rebuilds the snapshot from the live tree. MUST be called on the UI thread.</summary>
    public void Rebuild()
    {
        var templates = new Dictionary<string, TemplateEntry>();
        var tree = BuildNodes(_store.LocalItems, templates);
        _snap = new Snap(tree, templates);
    }

    private List<ConnectorNodeDto> BuildNodes(IEnumerable<BaseItem> items, Dictionary<string, TemplateEntry> templates)
    {
        var list = new List<ConnectorNodeDto>();
        foreach (var item in items)
        {
            string id = item.Id.ToString();
            string source = _store.GetSyncSourceForItem(item)?.Name ?? "local";
            if (item is Template t)
            {
                var offered = TemplateLanguages.Offered(t);
                templates[id] = new TemplateEntry(t.Title, offered, t.DefaultPasteMode);
                var (language, languages) = ConnectorLanguages.Describe(offered);
                list.Add(new ConnectorNodeDto(id, PlainName(t.Title), "template", t.DefaultPasteMode.ToString(), source, null, language, languages));
            }
            else if (item is Folder f)
            {
                list.Add(new ConnectorNodeDto(id, PlainName(f.Title), "folder", null, source, BuildNodes(f.Children, templates)));
            }
        }
        return list;
    }

    private static PasteMode ParseMode(string? mode, PasteMode fallback)
    {
        if (string.IsNullOrWhiteSpace(mode) || mode.Equals("default", StringComparison.OrdinalIgnoreCase))
            return fallback;
        return Enum.TryParse<PasteMode>(mode, ignoreCase: true, out var m) && Enum.IsDefined(m) ? m : fallback;
    }

}
