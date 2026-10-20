using System;
using System.IO;

namespace TextTemplateManager.Services.Updates;

/// <summary>A small text log of every update step (check, download, switch, result), next to the crash log, for
/// support cases. Trimmed to its newer half when it grows past <see cref="MaxBytes"/>.</summary>
public sealed class UpdateLog
{
    public const long MaxBytes = 256 * 1024;
    private readonly string _path;
    private readonly object _lock = new();

    public UpdateLog(string path) => _path = path;

    public string Path => _path;

    public void Write(string message)
    {
        lock (_lock)
        {
            try
            {
                var info = new FileInfo(_path);
                if (info.Exists && info.Length > MaxBytes)
                {
                    string text = File.ReadAllText(_path);
                    int cut = text.IndexOf('\n', text.Length / 2);
                    File.WriteAllText(_path, cut >= 0 ? text[(cut + 1)..] : "");
                }
                File.AppendAllText(_path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
            catch { /* logging must never throw */ }
        }
    }
}
