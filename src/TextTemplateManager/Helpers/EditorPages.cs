using System;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TextTemplateManager.Helpers;

/// <summary>The editor and preview pages, single files built by src/editor (styles and scripts inlined).
/// Navigated to as strings, so WebView2 has nothing to fetch and no cache to serve a stale bundle.</summary>
public static partial class EditorPages
{
    public static string Editor => Localized(Read("editor.html"), Loc.Editor.Get("PageTitle"));
    public static string Preview => Localized(Read("preview.html"), Loc.Editor.Get("PreviewPageTitle"));

    private static string Read(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "editor", name));

    // The page's text goes in before its own scripts (window.ttmStrings); the title and lang too, as screen readers
    // read them. The JSON serializer escapes < and >, so the text can't end the script tag.
    internal static string Localized(string html, string title)
    {
        string script = $"<script>window.ttmStrings = {JsonSerializer.Serialize(Loc.Editor.All())};</script>";
        html = HeadTag().Replace(html, m => m.Value + script, 1);
        html = TitleTag().Replace(html, _ => $"<title>{WebUtility.HtmlEncode(title)}</title>", 1);
        return LangAttribute().Replace(html, _ => $"<html lang=\"{UiLanguage()}\"", 1);
    }

    private static string UiLanguage()
    {
        // Languages doesn't list the override first (seen with qps-ploc), so the override wins when there is one.
        try
        {
            string chosen = Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride;
            return string.IsNullOrEmpty(chosen) ? Microsoft.Windows.Globalization.ApplicationLanguages.Languages[0] : chosen;
        }
        catch { return "en-US"; }
    }

    [GeneratedRegex("<head>")]
    private static partial Regex HeadTag();

    [GeneratedRegex("<title>[^<]*</title>")]
    private static partial Regex TitleTag();

    [GeneratedRegex("<html lang=\"[^\"]*\"")]
    private static partial Regex LangAttribute();
}
