using TextTemplateManager.Services.Platform;
using Xunit;

namespace TextTemplateManager.Tests;

/// <summary>Only a ttm.exe inside the 1.x install folder counts as 1.x; TTM 2 (package or dev build) and
/// anything merely named alike never do.</summary>
public class LegacyAppTests
{
    private const string Root = @"C:\Users\u\AppData\Local\Programs\Marflow Software";

    [Theory]
    [InlineData(@"C:\Users\u\AppData\Local\Programs\Marflow Software\TextTemplateManager\ttm.exe", true)]
    [InlineData(@"c:\users\U\appdata\local\programs\marflow software\TextTemplateManager\TTM.EXE", true)]
    [InlineData(@"C:\Users\u\AppData\Local\Programs\Marflow Software Other\ttm.exe", false)]
    [InlineData(@"C:\Program Files\WindowsApps\MarflowSoftware.TextTemplateManager_2.0.0.0_x64__eb1r5ey0kd4ny\ttm.exe", false)]
    [InlineData(@"C:\src\TextTemplateManager\bin\x64\Debug\ttm.exe", false)]
    [InlineData(@"C:\Users\u\AppData\Local\Programs\Marflow Software\..\Evil\ttm.exe", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_the_1x_install_folder_counts(string? exePath, bool expected) =>
        Assert.Equal(expected, LegacyApp.IsUnder(exePath, Root));

    [Fact]
    public void The_install_root_is_the_per_user_programs_folder() =>
        Assert.EndsWith(Path.Combine("AppData", "Local", "Programs", "Marflow Software"), LegacyApp.InstallRoot);
}
