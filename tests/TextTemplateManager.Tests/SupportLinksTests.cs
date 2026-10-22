using TextTemplateManager.Common;
using Xunit;

namespace TextTemplateManager.Tests;

/// <summary>The e-mail links of Settings ▸ Support: a prefilled form a mail app can read back intact.</summary>
public class SupportLinksTests
{
    private static (string To, string Subject, string Body) Parse(string mailto)
    {
        Assert.StartsWith("mailto:", mailto);
        var parts = mailto["mailto:".Length..].Split('?', 2);
        var query = parts[1].Split('&').Select(p => p.Split('=', 2))
            .ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));
        return (parts[0], query["subject"], query["body"]);
    }

    [Fact]
    public void A_problem_report_mail_carries_the_versions_and_the_questions_with_CRLF_line_breaks()
    {
        var (to, subject, body) = Parse(SupportLinks.ProblemMail("2.4.0", "Windows 11 24H2 (build 26100.2894)"));

        Assert.Equal("contact@kmarflow.com", to);
        Assert.Equal("TextTemplateManager: problem report", subject);
        Assert.StartsWith("App version: 2.4.0\r\nWindows version: Windows 11 24H2 (build 26100.2894)\r\n", body);
        Assert.Contains("When did it happen (date, time, time zone)?", body);
        Assert.Contains("What did you do right before, step by step?\r\n1.\r\n2.\r\n3.", body);
        Assert.DoesNotContain("\n\n", body.Replace("\r\n", "|"));
        Assert.DoesNotContain(" ", SupportLinks.ProblemMail("2.4.0", "x"));
    }

    [Fact]
    public void Feature_and_security_mails_have_their_own_subject_and_the_version()
    {
        var feature = Parse(SupportLinks.FeatureMail("2.4.0"));
        var security = Parse(SupportLinks.SecurityMail("2.4.0", "Windows 10 22H2 (build 19045.5011)"));

        Assert.Equal("TextTemplateManager: feature request", feature.Subject);
        Assert.Contains("App version: 2.4.0", feature.Body);
        Assert.Equal("TextTemplateManager: security vulnerability", security.Subject);
        Assert.Contains("Windows version: Windows 10 22H2 (build 19045.5011)", security.Body);
        Assert.Contains("What can an attacker do with it?", security.Body);
    }

    [Theory]
    [InlineData("26100", "24H2", 2894, "Windows 11 24H2 (build 26100.2894)")]
    [InlineData("22000", "", 0, "Windows 11 (build 22000.0)")]
    [InlineData("19045", "22H2", 5011, "Windows 10 22H2 (build 19045.5011)")]
    [InlineData("", "24H2", 1, null)]
    public void The_Windows_version_is_named_by_its_build(string build, string display, int ubr, string? expected) =>
        Assert.Equal(expected, SupportLinks.FormatWindowsVersion(build, display, ubr));

    [Fact]
    public void The_running_Windows_version_is_readable() =>
        Assert.Matches(@"^Windows 1[01]", SupportLinks.WindowsVersion());
}
