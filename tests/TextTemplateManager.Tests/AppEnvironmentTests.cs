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
    public async Task A_dev_run_cannot_start_with_windows_and_was_not_started_by_it()
    {
        var environment = new AppEnvironment.Unpackaged();

        Assert.False(environment.IsAutostartLaunch);
        Assert.Null(environment.OpenedFile);
        Assert.Equal(AutostartState.NotAvailable, await environment.GetAutostartAsync());
        Assert.Equal(AutostartState.NotAvailable, await environment.SetAutostartAsync(true));
    }

    [Fact]
    public void An_opened_file_is_the_first_existing_ttmdata()
    {
        string dir = TestEnvironment.NewScratchDir();
        string existing = Path.Combine(dir, "team.TTMDATA");
        File.WriteAllText(existing, "{}");
        File.WriteAllText(Path.Combine(dir, "notes.txt"), "");

        Assert.Equal(existing, AppEnvironment.FirstTtmDataFile(new[]
        {
            "--hidden", Path.Combine(dir, "notes.txt"), Path.Combine(dir, "missing.ttmdata"), existing,
        }));
        Assert.Null(AppEnvironment.FirstTtmDataFile(new[] { Path.Combine(dir, "missing.ttmdata"), "a\0b.ttmdata" }));
    }

    [Fact]
    public void The_data_folder_override_wins()
    {
        var storage = StorageService.CreateDefault(new AppEnvironment.Unpackaged());

        Assert.StartsWith(TestEnvironment.DataRoot, storage.GetDataPath(), StringComparison.OrdinalIgnoreCase);
    }
}
