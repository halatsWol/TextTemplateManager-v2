using TextTemplateManager.Services.Platform;
using Xunit;

namespace TextTemplateManager.Tests;

/// <summary>A second launch during a slow first start was redirected to an instance without a window yet and
/// crashed it (CoreMessagingXP, stowed E_POINTER). Such launches now wait until startup is done.</summary>
public class PendingRedirectsTests
{
    [Fact]
    public void Redirects_before_startup_is_done_are_held_in_order()
    {
        var pending = new PendingRedirects();

        Assert.True(pending.Hold(new LaunchRequest(false, null)));
        Assert.True(pending.Hold(new LaunchRequest(false, @"C:\a.ttmdata")));

        Assert.Equal(new[] { new LaunchRequest(false, null), new LaunchRequest(false, @"C:\a.ttmdata") }, pending.Release());
    }

    [Fact]
    public void After_release_redirects_are_not_held()
    {
        var pending = new PendingRedirects();
        pending.Release();

        Assert.False(pending.Hold(new LaunchRequest(false, null)));
        Assert.Empty(pending.Release());
    }

    [Fact]
    public void Release_without_redirects_is_empty()
    {
        Assert.Empty(new PendingRedirects().Release());
    }
}
