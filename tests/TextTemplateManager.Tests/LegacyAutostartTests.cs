using Microsoft.Windows.AppLifecycle;
using TextTemplateManager.Models;
using TextTemplateManager.Services.Platform;
using Xunit;

namespace TextTemplateManager.Tests;

/// <summary>1.x's start-with-Windows choice, as it left it in the registry, carries over to TTM 2.</summary>
public class LegacyAutostartTests
{
    private const string Command = @"""C:\Users\u\AppData\Local\Programs\Marflow Software\TextTemplateManager\ttm.exe""";

    [Fact]
    public void The_registry_state_reads_like_windows_does()
    {
        Assert.Null(LegacyAutostart.Parse(null, null));
        Assert.Equal(new LegacyAutostartState(On: true, Hidden: false), LegacyAutostart.Parse(Command, null));
        Assert.Equal(new LegacyAutostartState(On: true, Hidden: true), LegacyAutostart.Parse(Command + " --hidden", new byte[12] { 0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }));
        Assert.Equal(new LegacyAutostartState(On: false, Hidden: true), LegacyAutostart.Parse(Command + " --HIDDEN", new byte[12] { 0x03, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8 }));
        Assert.Equal(new LegacyAutostartState(On: true, Hidden: false), LegacyAutostart.Parse(Command, Array.Empty<byte>()));
    }

    private sealed class FakeEnvironment : IAppEnvironment
    {
        public List<bool> Set { get; } = new();
        public bool IsPackaged => true;
        public string DataDirectory => "";
        public string BackupDirectory => "";
        public bool IsAutostartLaunch => false;
        public string? OpenedFile => null;
        public LaunchRequest ReadRedirected(AppActivationArguments activation) => default;
        public Task<AutostartState> GetAutostartAsync() => Task.FromResult(AutostartState.On);
        public Task<AutostartState> SetAutostartAsync(bool on)
        {
            Set.Add(on);
            return Task.FromResult(on ? AutostartState.On : AutostartState.Off);
        }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task The_1x_choice_carries_over(bool on, bool hidden)
    {
        var environment = new FakeEnvironment();
        var settings = new AppSettings { StartHidden = !hidden };

        await LegacyAutostart.CarryOverAsync(new LegacyAutostartState(on, hidden), environment, settings);

        Assert.Equal(new[] { on }, environment.Set);
        Assert.Equal(hidden, settings.StartHidden);
    }

    [Fact]
    public async Task Nothing_known_changes_nothing()
    {
        var environment = new FakeEnvironment();
        var settings = new AppSettings();

        await LegacyAutostart.CarryOverAsync(null, environment, settings);

        Assert.Empty(environment.Set);
        Assert.True(settings.StartHidden);
    }
}
