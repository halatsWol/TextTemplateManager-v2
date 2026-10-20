using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Management.Deployment;

namespace TextTemplateManager.Services.Updates;

/// <summary>Switches the installed package to a staged version: registering it with ForceTargetApplicationShutdown makes
/// Windows close the app, switch, and (with RegisterApplicationRestart) start the new version right away. Tested on a
/// VM with sideloaded packages (2026-10-20); a plain register only fails while the app runs (0x80073D02).</summary>
public sealed class PackageSwitcher : IPackageSwitcher
{
    // RegisterApplicationRestart: restart only for updates, not after a crash, a hang or a reboot.
    private const int RESTART_NO_CRASH = 1, RESTART_NO_HANG = 2, RESTART_NO_REBOOT = 8;

    private readonly PackageId _id = Package.Current.Id;

    public Version RunningVersion => ToVersion(_id.Version);

    public static Version ToVersion(PackageVersion v) => new(v.Major, v.Minor, v.Build, v.Revision);

    /// <summary>The staged package's folder sits next to the installed one (WindowsApps), named by its full name; the
    /// folder can't be listed, but a known path can be checked.</summary>
    public bool IsStaged(Version version)
    {
        try
        {
            string? root = Path.GetDirectoryName(Package.Current.InstalledPath);
            if (root == null) return false;
            string folder = $"{_id.Name}_{version.Major}.{version.Minor}.{version.Build}.{version.Revision}_{_id.Architecture.ToString().ToLowerInvariant()}__{_id.PublisherId}";
            return File.Exists(Path.Combine(root, folder, "AppxManifest.xml"));
        }
        catch { return false; }
    }

    public void RegisterRestart(string arguments)
    {
        int hr = RegisterApplicationRestart(arguments, RESTART_NO_CRASH | RESTART_NO_HANG | RESTART_NO_REBOOT);
        if (hr != 0) Marshal.ThrowExceptionForHR(hr);
    }

    public void UnregisterRestart() => UnregisterApplicationRestart();

    private const string WatchdogTask = "TextTemplateManager update watchdog";

    /// <summary>A one-time task in the user's Task Scheduler, so it runs outside the package: Windows ends the package's
    /// processes at the switch, and on the test VM a child process went with them even with the desktop-app breakaway
    /// attribute. The task deletes itself.</summary>
    public void StartWatchdog(TimeSpan after, string logPath)
    {
        string folder = Path.GetDirectoryName(logPath)!;
        string script = Path.Combine(folder, "update-watchdog.ps1"), xml = Path.Combine(folder, "update-watchdog.xml");
        string log = logPath.Replace("'", "''");
        File.WriteAllText(script, $$"""
            if (Get-Process ttm2 -ErrorAction SilentlyContinue) { $m = 'watchdog: running' }
            else { Start-Process 'shell:AppsFolder\{{_id.FamilyName}}!App'; $m = 'watchdog: started it through the shell' }
            Add-Content -LiteralPath '{{log}}' -Value ((Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + ' ' + $m)
            schtasks.exe /Delete /TN '{{WatchdogTask}}' /F | Out-Null
            """);
        string ps = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
        string Xml(string text) => System.Security.SecurityElement.Escape(text);
        // Task XML: the start time without the locale-dependent date format of schtasks /SD.
        File.WriteAllText(xml, $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <Triggers><TimeTrigger><StartBoundary>{DateTime.Now.Add(after):yyyy-MM-ddTHH:mm:ss}</StartBoundary><Enabled>true</Enabled></TimeTrigger></Triggers>
              <Principals><Principal id="Author"><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>
              <Settings>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <StartWhenAvailable>true</StartWhenAvailable><ExecutionTimeLimit>PT5M</ExecutionTimeLimit><Enabled>true</Enabled>
              </Settings>
              <Actions Context="Author"><Exec><Command>{Xml(ps)}</Command><Arguments>-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File "{Xml(script)}"</Arguments></Exec></Actions>
            </Task>
            """, System.Text.Encoding.Unicode);
        var start = new System.Diagnostics.ProcessStartInfo("schtasks.exe", $"/Create /TN \"{WatchdogTask}\" /XML \"{xml}\" /F")
        { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        using var process = System.Diagnostics.Process.Start(start) ?? throw new InvalidOperationException("schtasks didn't start");
        string error = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(15000) || process.ExitCode != 0)
            throw new InvalidOperationException($"schtasks: {error.Trim()} ({(process.HasExited ? process.ExitCode : -1)})");
    }

    public async Task<string?> SwitchAsync()
    {
        try
        {
            var result = await new PackageManager().RegisterPackageByFamilyNameAsync(
                _id.FamilyName, null, DeploymentOptions.ForceTargetApplicationShutdown, null, null);
            // Reached only when Windows didn't close the app.
            return result.ExtendedErrorCode is { } error && error.HResult != 0
                ? $"{result.ErrorText} (0x{error.HResult:X8})"
                : "Windows didn't switch to the new version.";
        }
        catch (Exception ex) { return $"{ex.Message} (0x{ex.HResult:X8})"; }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegisterApplicationRestart(string? commandLine, int flags);

    [DllImport("kernel32.dll")]
    private static extern int UnregisterApplicationRestart();
}
