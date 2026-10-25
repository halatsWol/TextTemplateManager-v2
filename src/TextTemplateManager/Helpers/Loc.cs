using System;
using System.Globalization;
using Microsoft.Windows.ApplicationModel.Resources;

namespace TextTemplateManager.Helpers;

/// <summary>UI text set in code, from Strings\&lt;language&gt;\Resources.resw. XAML takes its text through x:Uid.</summary>
internal static class Loc
{
    /// <summary>The .pri file to read; null for the app's default one. The test host isn't ttm2.exe, so it has no
    /// default file and points this at ttm2.pri before the first lookup.</summary>
    internal static string? ResourceFile { get; set; }

    private static readonly Lazy<ResourceLoader> Loader =
        new(() => ResourceFile is null ? new ResourceLoader() : new ResourceLoader(ResourceFile));

    public static string Get(string key) => Loader.Value.GetString(key);

    public static string Format(string key, params object?[] args) => string.Format(CultureInfo.CurrentCulture, Get(key), args);
}
