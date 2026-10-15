using TextTemplateManager.Helpers;
using Xunit;

namespace TextTemplateManager.Tests;

/// <summary>The main window opens at a size the settings pages fit in, scaled to the display, centred, and never
/// larger than the screen it opens on.</summary>
public class WindowBoundsTests
{
    [Fact]
    public void A_large_screen_gets_the_full_size_centred_in_the_work_area()
    {
        var b = WindowHelper.CenteredBounds(0, 0, 1920, 1040, 1.0, 1200, 820);

        Assert.Equal((360, 110, 1200, 820), b);
    }

    [Fact]
    public void The_size_follows_the_display_scaling()
    {
        var b = WindowHelper.CenteredBounds(0, 0, 3840, 2080, 1.5, 1200, 820);

        Assert.Equal((1800, 1230), (b.Width, b.Height));
        Assert.Equal((1020, 425), (b.X, b.Y));
    }

    [Fact]
    public void A_small_screen_caps_the_window_at_90_percent_of_its_work_area()
    {
        var b = WindowHelper.CenteredBounds(0, 0, 1366, 728, 1.25, 1200, 820);

        Assert.Equal((1229, 655), (b.Width, b.Height));
        Assert.Equal((68, 36), (b.X, b.Y));
    }

    [Fact]
    public void A_secondary_monitor_offset_is_kept()
    {
        var b = WindowHelper.CenteredBounds(-1920, 200, 1920, 1040, 1.0, 1200, 820);

        Assert.Equal((-1560, 310), (b.X, b.Y));
    }
}
