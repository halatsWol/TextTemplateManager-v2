using System;
using System.IO;

namespace TextTemplateManager.Helpers;

/// <summary>The editor and preview pages, single files built by src/editor (styles and scripts inlined).
/// Navigated to as strings, so WebView2 has nothing to fetch and no cache to serve a stale bundle.</summary>
public static class EditorPages
{
    public static string Editor => Read("editor.html");
    public static string Preview => Read("preview.html");

    private static string Read(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "editor", name));
}
