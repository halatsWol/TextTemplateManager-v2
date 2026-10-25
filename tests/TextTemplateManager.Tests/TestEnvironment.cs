using System.Runtime.CompilerServices;
using TextTemplateManager.Data;

namespace TextTemplateManager.Tests;

/// <summary>Test data lives under a throwaway folder. Tests build their own <see cref="StorageService"/> on
/// a scratch folder; the redirected data root is the safety net for anything that still asks for the
/// app's default storage (<see cref="StorageService.CreateDefault"/>), which would otherwise read and
/// write the user's real templates and settings under %LocalAppData%\Marflow Software\TextTemplateManager.
///
/// [ModuleInitializer] runs when the test assembly loads, ahead of any test, fixture or class constructor.</summary>
internal static class TestEnvironment
{
    /// <summary>The per-run data root. Left on disk after the run for post-mortem inspection; it lives
    /// under the temp folder, so it costs nothing.</summary>
    internal static string DataRoot { get; private set; } = "";

    [ModuleInitializer]
    internal static void Initialize()
    {
        DataRoot = Path.Combine(Path.GetTempPath(), "ttm-tests", $"run-{Guid.NewGuid():N}");
        Directory.CreateDirectory(DataRoot);
        Environment.SetEnvironmentVariable(StorageService.DataDirOverrideVariable, DataRoot);

        // The app registers the Windows code pages at launch (App.OnLaunched); RTF needs Windows-1252.
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

        Helpers.Loc.ResourceFile = Path.Combine(AppContext.BaseDirectory, "ttm2.pri");
    }

    /// <summary>A fresh empty directory for one test, so file-system tests can't interfere with
    /// each other when xUnit runs collections in parallel.</summary>
    internal static string NewScratchDir([CallerMemberName] string name = "")
    {
        string dir = Path.Combine(DataRoot, "scratch", $"{name}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
