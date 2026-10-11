using TextTemplateManager.Data;
using TextTemplateManager.Services.Platform;
using Xunit;

namespace TextTemplateManager.Tests;

/// <summary>TTM 2 never keeps its live data in the 1.x folder, which it only ever reads: a dev run (no package
/// identity, as in this test host) gets its own folder, and TTM_DATA_DIR still wins over both.</summary>
public class AppEnvironmentTests
{
    [Fact]
    public void A_dev_run_is_unpackaged_and_keeps_its_data_apart_from_1x()
    {
        var environment = AppEnvironment.Detect();

        Assert.False(environment.IsPackaged);
        Assert.EndsWith(Path.Combine("Marflow Software", "TextTemplateManager 2 Dev"), environment.DataDirectory);
        Assert.NotEqual(
            Path.GetFullPath(AppEnvironment.LegacyDataDirectory).TrimEnd('\\'),
            Path.GetFullPath(environment.DataDirectory).TrimEnd('\\'),
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_data_folder_override_wins()
    {
        var storage = StorageService.CreateDefault(new AppEnvironment.Unpackaged());

        Assert.StartsWith(TestEnvironment.DataRoot, storage.GetDataPath(), StringComparison.OrdinalIgnoreCase);
    }
}
