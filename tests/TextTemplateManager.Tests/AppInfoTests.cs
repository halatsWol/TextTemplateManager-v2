using TextTemplateManager.Common;
using Xunit;

namespace TextTemplateManager.Tests;

public class AppInfoTests
{
    [Fact]
    public void A_dev_build_reports_its_version_without_a_commit_hash_and_no_release_date()
    {
        Assert.False(string.IsNullOrWhiteSpace(AppInfo.Version));
        Assert.DoesNotContain('+', AppInfo.Version);
        Assert.Equal("", AppInfo.ReleaseDate);
    }
}
