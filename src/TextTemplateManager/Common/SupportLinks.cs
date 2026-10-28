using System;
using Microsoft.Win32;

namespace TextTemplateManager.Common;

/// <summary>Where users get help and report problems, feature requests and vulnerabilities: the website, e-mail with
/// a prefilled form (the same questions as the website and the GitHub issue forms), and GitHub, which needs an
/// account.</summary>
public static class SupportLinks
{
    public const string Email = "contact@kmarflow.com";
    public const string WebsiteUrl = "https://software.kmarflow.com/TextTemplateManager-v2/";
    public const string HelpUrl = WebsiteUrl + "help/";
    public const string LogCollectorUrl = HelpUrl + "#collecting-the-logs";
    public const string MailSetupUrl = HelpUrl + "#no-e-mail-window-opens";
    private const string Repository = "https://github.com/halatsWol/TextTemplateManager-v2";
    public const string GitHubProblemUrl = Repository + "/issues/new?template=bug_report.yml";
    public const string GitHubFeatureUrl = Repository + "/issues/new?template=feature_request.yml";
    public const string GitHubSecurityUrl = Repository + "/security/advisories/new";

    public static string ProblemMail(string appVersion, string windowsVersion) => Mail("problem report",
        $"""
        App version: {appVersion}
        Windows version: {windowsVersion}
        Company PC that manages updates or the Microsoft Store (yes / no / don't know):

        When did it happen (date, time, time zone)?

        How often (every time / sometimes / once)?

        What did you do right before, step by step?
        1.
        2.
        3.

        What did you expect?

        What happened instead (exact message text, error code)?

        Please attach screenshots and, for install, update or start problems, the logs: Settings > Support > Export logs
        (if the app doesn't start: {LogCollectorUrl}).
        """);

    public static string FeatureMail(string appVersion) => Mail("feature request",
        $"""
        What would you like to do that the app doesn't support today, or makes hard?

        How do you do it today (workaround)?

        How could it work (idea, example, how another app does it)?

        How often would you use it (several times a day / daily / weekly / rarely)?

        App version: {appVersion}
        """);

    public static string SecurityMail(string appVersion, string windowsVersion) => Mail("security vulnerability",
        $"""
        App version: {appVersion}
        Windows version: {windowsVersion}

        What is affected?

        How to reproduce it, step by step:
        1.
        2.
        3.

        What can an attacker do with it?
        """);

    /// <summary>A report about a translation: language and version for sorting, then the questions in the app's language
    /// (whoever reports a translation may not read English well).</summary>
    public static string TranslationMail(string language, string appVersion, string where, string now, string should, string more) =>
        Mail($"translation issue ({language})",
        $"""
        App language: {language}
        App version: {appVersion}

        {where}

        {now}

        {should}

        {more}
        """);

    // Mail apps expect CRLF line breaks in a mailto body.
    private static string Mail(string kind, string body) =>
        $"mailto:{Email}?subject={Uri.EscapeDataString("TextTemplateManager: " + kind)}"
        + $"&body={Uri.EscapeDataString(body.ReplaceLineEndings("\r\n") + "\r\n")}";

    /// <summary>"Windows 11 24H2 (build 26100.2894)", from the registry; Windows 11 still names itself Windows 10 in
    /// ProductName, so the build decides.</summary>
    public static string WindowsVersion()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            string build = key?.GetValue("CurrentBuild") as string ?? "";
            string display = key?.GetValue("DisplayVersion") as string ?? "";
            int ubr = key?.GetValue("UBR") is int u ? u : 0;
            return FormatWindowsVersion(build, display, ubr) ?? Environment.OSVersion.VersionString;
        }
        catch { return Environment.OSVersion.VersionString; }
    }

    public static string? FormatWindowsVersion(string build, string displayVersion, int ubr)
    {
        if (!int.TryParse(build, out int number)) return null;
        string name = number >= 22000 ? "Windows 11" : "Windows 10";
        string release = string.IsNullOrEmpty(displayVersion) ? "" : " " + displayVersion;
        return $"{name}{release} (build {number}.{ubr})";
    }
}
