using System;
using System.Collections.Generic;
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

    /// <summary>key_One for a count of 1, key_Other otherwise (English and German need no more forms); {0} is the
    /// count, <paramref name="args"/> follow from {1}.</summary>
    public static string Plural(string key, int count, params object?[] args) =>
        Format(key + (count == 1 ? "_One" : "_Other"), [count, .. args]);

    /// <summary>The guided tour's steps and sample templates, from Strings\&lt;language&gt;\Tour.resw.</summary>
    public static class Tour
    {
        private static readonly Lazy<ResourceLoader> Loader =
            new(() => new ResourceLoader(ResourceFile ?? ResourceLoader.GetDefaultResourceFilePath(), "Tour"));

        public static string Get(string key) => Loader.Value.GetString(key);

        public static string Format(string key, params object?[] args) => string.Format(CultureInfo.CurrentCulture, Get(key), args);
    }

    /// <summary>The editor's and preview's own text, from Strings\&lt;language&gt;\Editor.resw: the page gets all of it
    /// at once (<see cref="EditorPages"/>) and looks it up itself.</summary>
    public static class Editor
    {
        private static readonly Lazy<IReadOnlyDictionary<string, string>> Strings = new(() =>
        {
            var map = new ResourceManager(ResourceFile ?? ResourceLoader.GetDefaultResourceFilePath()).MainResourceMap.GetSubtree("Editor");
            var all = new Dictionary<string, string>();
            for (uint i = 0; i < map.ResourceCount; i++)
            {
                var (name, candidate) = map.GetValueByIndex(i);
                all[name[(name.LastIndexOf('/') + 1)..]] = candidate.ValueAsString;
            }
            return all;
        });

        public static IReadOnlyDictionary<string, string> All() => Strings.Value;

        public static string Get(string key) => Strings.Value.TryGetValue(key, out var text) ? text : key;
    }
}
