using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Windows.Globalization;

namespace TextTemplateManager.Helpers;

/// <summary>The languages the UI ships in (a Strings\&lt;language&gt; folder each) and the one chosen in Settings: the
/// Windows language or one of these. The choice is a file of its own in the data folder (per device; no settings, sync
/// or backup file carries it) and applies from the next start, as the app's language override. Windows keeps that
/// override only for a packaged app, so the file is the record and the override is set from it at every start, for the
/// Windows language too (the shipped language that matches it).</summary>
public static class UiLanguages
{
    public static readonly IReadOnlyList<string> Available = ["en-US", "de-DE", "hu-HU", "it-IT"];

    /// <summary>The pseudo-language that only Debug builds carry (see build/New-PseudoLoc.ps1).</summary>
    public const string Pseudo = "qps-ploc";

    private const string FileName = "ui-language.txt";
    private static string? _dataDirectory;

    /// <summary>The language the running app started in ("" for the Windows language).</summary>
    public static string AtStart { get; private set; } = "";

    /// <summary>The choice for the next start ("" for the Windows language).</summary>
    public static string Chosen { get; private set; } = "";

    /// <summary>At startup, before the first window: reads the choice in <paramref name="dataDirectory"/> and sets the
    /// override. A dev run's TTM_UI_LANGUAGE wins, without being saved.</summary>
    public static void Apply(string dataDirectory)
    {
        _dataDirectory = dataDirectory;
        Chosen = Read(dataDirectory);
        string language = Environment.GetEnvironmentVariable("TTM_UI_LANGUAGE") is { Length: > 0 } dev ? dev : Chosen;
        // Always a concrete language: a packaged app kept a previous override after it was set to "" (VM test).
        string effective = language.Length > 0 ? language : ForWindows(Windows.System.UserProfile.GlobalizationPreferences.Languages);
        try { ApplicationLanguages.PrimaryLanguageOverride = effective; }
        catch (Exception) { language = ""; }   // the app then follows Windows
        AtStart = language;
        Effective = effective;
        // .NET's UI culture names languages (CultureInfo.DisplayName); formats keep following CurrentCulture.
        try { CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(effective); }
        catch (CultureNotFoundException) { }
    }

    /// <summary>The language the running app shows (a code, also for the Windows language).</summary>
    public static string Effective { get; private set; } = "en-US";

    /// <summary>The shipped language for the Windows display languages <paramref name="windowsLanguages"/> (in their
    /// order): the first one shipped as it is or in another region (de-AT gets de-DE), else the first shipped one.</summary>
    internal static string ForWindows(IEnumerable<string> windowsLanguages)
    {
        foreach (string tag in windowsLanguages)
        {
            string language = tag.Split('-')[0];
            if ((Available.FirstOrDefault(a => a.Equals(tag, StringComparison.OrdinalIgnoreCase))
                ?? Available.FirstOrDefault(a => a.Split('-')[0].Equals(language, StringComparison.OrdinalIgnoreCase))) is { } shipped)
                return shipped;
        }
        return Available[0];
    }

    /// <summary>Saves <paramref name="code"/> ("" for the Windows language) for the next start.</summary>
    public static void Choose(string code)
    {
        Chosen = code;
        if (_dataDirectory != null) Write(_dataDirectory, code);
    }

    internal static string Read(string dataDirectory)
    {
        string file = Path.Combine(dataDirectory, FileName);
        try { return File.Exists(file) ? File.ReadAllText(file).Trim() : ""; }
        catch (IOException) { return ""; }
    }

    internal static void Write(string dataDirectory, string code)
    {
        string file = Path.Combine(dataDirectory, FileName);
        if (code.Length == 0) File.Delete(file);
        else File.WriteAllText(file, code);
    }

    /// <summary>The language's own name ("English", "Deutsch"), as language pickers show it.</summary>
    public static string NameOf(string code)
    {
        if (code == Pseudo) return "Pseudo-language (test)";   // Debug builds only
        string name = new CultureInfo(code).Parent.NativeName;
        return name.Length > 0 ? char.ToUpper(name[0], CultureInfo.GetCultureInfo(code)) + name[1..] : code;
    }
}
