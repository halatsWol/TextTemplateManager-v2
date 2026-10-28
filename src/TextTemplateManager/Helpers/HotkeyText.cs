using System;
using System.Linq;

namespace TextTemplateManager.Helpers;

/// <summary>A shortcut as the user reads it ("Strg+Umschalt+Leertaste"). The stored and registered form stays
/// "Ctrl+Shift+Space", which <see cref="Services.Platform.HotkeyListener"/> parses.</summary>
internal static class HotkeyText
{
    public static string For(string? hotkey) => For(hotkey, KeyName);

    internal static string For(string? hotkey, Func<string, string?> name) =>
        string.IsNullOrEmpty(hotkey) || hotkey == "None" ? "" : string.Join("+", hotkey.Split('+').Select(k => name(k) ?? k));

    private static string? KeyName(string key) => key switch
    {
        "Ctrl" => Loc.Get("Key_Ctrl"),
        "Shift" => Loc.Get("Key_Shift"),
        "Alt" => Loc.Get("Key_Alt"),
        "Win" => Loc.Get("Key_Win"),
        "Space" => Loc.Get("Key_Space"),
        _ => null,
    };
}
