using System;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace TextTemplateManager.Helpers;

/// <summary>The one WebView2 environment the editor and the Quick Paste preview share (WebView2 refuses a second one
/// with other options for the same data folder), in the app's language: its own menus (the editor's right-click menu,
/// spelling suggestions) then match the app.</summary>
internal static class WebViewEnvironment
{
    private static Task<CoreWebView2Environment>? _environment;

    public static Task<CoreWebView2Environment> GetAsync() =>
        _environment ??= CoreWebView2Environment.CreateWithOptionsAsync(null, null,
            new CoreWebView2EnvironmentOptions { Language = UiLanguages.Effective }).AsTask();
}
