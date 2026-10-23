using System;
using TextTemplateManager.Models;

namespace TextTemplateManager.Services.Translation;

/// <summary>Title and multi-key shortcut of a template created by the Translate menu: the original's with the target
/// language's tag ("Offer (DE)", MSG → MSGDE). A tag of the source language the original already ends with is replaced,
/// so translating a translation doesn't stack tags ("Offer (DE)" to English: "Offer (EN)", MSGDE → MSGEN).</summary>
public static class TranslatedCopy
{
    /// <summary>The tag for a language code: the code in capitals, Chinese as ZH and ZH-TW.</summary>
    public static string Tag(string code) => code switch
    {
        "zh-Hans" => "ZH",
        "zh-Hant" => "ZH-TW",
        _ => code.ToUpperInvariant(),
    };

    public static string Title(string title, string from, string to) =>
        $"{WithoutTag(title.TrimEnd(), from, out _)} ({Tag(to)})";

    /// <summary>The copy's multi-key shortcut; empty when the original has none.</summary>
    public static string MultiKey(string shortcut, string title, string from, string to)
    {
        if (string.IsNullOrEmpty(shortcut)) return string.Empty;
        WithoutTag(title.TrimEnd(), from, out bool tagged);
        string fromTag = Template.CleanShortcut(Tag(from));
        // Only when the title shows it: a shortcut that merely ends in the same letters keeps them.
        if (tagged && shortcut.Length > fromTag.Length && shortcut.EndsWith(fromTag, StringComparison.OrdinalIgnoreCase))
            shortcut = shortcut[..^fromTag.Length];
        return Template.CleanShortcut(shortcut + Tag(to));
    }

    private static string WithoutTag(string title, string from, out bool tagged)
    {
        string suffix = $" ({Tag(from)})";
        tagged = title.Length > suffix.Length && title.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
        return tagged ? title[..^suffix.Length] : title;
    }
}
