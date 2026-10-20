using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Windows.Management.Deployment;

namespace TextTemplateManager.Services.Updates;

/// <summary>Test source for sideloaded builds: the newest .msix of this app in the folder that
/// <see cref="FolderVariable"/> names. Downloading stages it like the Store does; consent and silent download are the
/// same. Lets a VM run the whole update flow without a Store release.</summary>
public sealed class FileUpdateSource : IUpdateSource
{
    public const string FolderVariable = "TTM_UPDATE_SOURCE";
    private readonly string _folder;
    private readonly string _packageName;
    private string? _offerFile;

    public FileUpdateSource(string folder, string packageName)
    {
        _folder = folder;
        _packageName = packageName;
    }

    /// <summary>The folder from <see cref="FolderVariable"/>, or null when it isn't set.</summary>
    public static string? ConfiguredFolder()
    {
        string? folder = Environment.GetEnvironmentVariable(FolderVariable);
        return string.IsNullOrWhiteSpace(folder) ? null : folder;
    }

    public string Name => "folder " + _folder;

    /// <summary>A test switch: a file "consent-only" in the folder makes the source behave like a Store under the
    /// "no automatic updates" policy.</summary>
    public bool CanDownloadSilently => !File.Exists(Path.Combine(_folder, "consent-only"));

    public Task<UpdateOffer?> CheckAsync(CancellationToken cancel) => Task.Run(() =>
    {
        var newest = Directory.EnumerateFiles(_folder, "*.msix")
            .Select(f => (File: f, Version: ReadVersion(f)))
            .Where(p => p.Version != null)
            .OrderByDescending(p => p.Version)
            .FirstOrDefault();
        _offerFile = newest.File;
        return newest.Version == null ? null : new UpdateOffer(newest.Version);
    }, cancel);

    private Version? ReadVersion(string msix)
    {
        try
        {
            using var zip = ZipFile.OpenRead(msix);
            var entry = zip.GetEntry("AppxManifest.xml");
            if (entry == null) return null;
            using var stream = entry.Open();
            var identity = XDocument.Load(stream).Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "Identity");
            if (identity?.Attribute("Name")?.Value != _packageName) return null;
            return Version.TryParse(identity.Attribute("Version")?.Value, out var v) ? v : null;
        }
        catch { return null; }
    }

    public async Task<DownloadResult> DownloadAsync(UpdateOffer offer, IProgress<double> progress, CancellationToken cancel)
    {
        if (_offerFile == null) return new DownloadResult(DownloadOutcome.Failed, "no package in the folder");
        var operation = new PackageManager().StagePackageAsync(new Uri(_offerFile), null);
        var result = await operation.AsTask(cancel, new Progress<DeploymentProgress>(p => progress.Report(p.percentage / 100.0)));
        return result.ExtendedErrorCode is { } error && error.HResult != 0
            ? new DownloadResult(DownloadOutcome.Failed, $"{result.ErrorText} (0x{error.HResult:X8})")
            : DownloadResult.Done;
    }

    public Task<DownloadResult> RequestDownloadAsync(UpdateOffer offer, IntPtr ownerWindow, IProgress<double> progress, CancellationToken cancel) =>
        DownloadAsync(offer, progress, cancel);

    public Task<DownloadResult> DownloadDespitePolicyAsync(UpdateOffer offer, IProgress<double> progress, CancellationToken cancel) =>
        DownloadAsync(offer, progress, cancel);
}
