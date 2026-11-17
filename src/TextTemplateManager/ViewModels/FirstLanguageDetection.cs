using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TextTemplateManager.Helpers;
using TextTemplateManager.Models;

namespace TextTemplateManager.ViewModels;

/// <summary>Gives a template added on this device its first language: once it has text and is saved or left, its
/// language is detected, once; when that can't be told it stays without one. Other templates (existing, imported,
/// duplicated, added through the connector) are never detected: it would rewrite their files.</summary>
public sealed class FirstLanguageDetection
{
    private readonly HashSet<Guid> _pending = [];

    /// <summary>The language a tab's HTML is written in; null when it can't be told.</summary>
    public Func<string, Task<string?>>? Detect { get; set; }

    public void Added(Template t) => _pending.Add(t.Id);

    public bool IsPending(Template t) => _pending.Contains(t.Id);

    /// <summary>Detects <paramref name="t"/>'s language if it still waits for one and has text, and gives it to the
    /// template and to its unsaved copy (<paramref name="draftOf"/>, asked once the answer is in) unless one of them
    /// got a language meanwhile. The detected code, or null.</summary>
    public async Task<string?> RunAsync(Template t, Func<Template?>? draftOf = null)
    {
        if (!_pending.Contains(t.Id) || Detect is not { } detect) return null;
        if (t.Language != null) { _pending.Remove(t.Id); return null; }
        if (HtmlUtils.ToPlainText(t.Content).Trim().Length == 0) return null;
        _pending.Remove(t.Id);
        string? code = TemplateLanguages.NormalizeCode(await detect(t.Content));
        var draft = draftOf?.Invoke();
        if (code == null || t.Language != null || draft?.Language != null) return null;
        TemplateTabs.SetLanguage(t, null, code);
        if (draft != null) TemplateTabs.SetLanguage(draft, null, code);
        return code;
    }
}
