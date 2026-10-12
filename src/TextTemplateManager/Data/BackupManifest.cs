using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TextTemplateManager.Data;

/// <summary>The schedule a backup belongs to; each keeps its own number of copies.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<BackupTier>))]
public enum BackupTier { Recent, TwoHourly, Daily, V1 }

/// <summary>What a file in a backup is, which decides how it is checked and restored.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<BackupFileKind>))]
public enum BackupFileKind { Data, Settings, SyncSettings, SyncFile }

/// <summary>manifest.json, the last entry written into a backup zip.</summary>
public sealed class BackupManifest
{
    public const int CurrentFormat = 1;

    public int Format { get; set; } = CurrentFormat;
    public string AppVersion { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
    public BackupTier Tier { get; set; }
    public List<BackupEntry> Files { get; set; } = new();
}

public sealed class BackupEntry
{
    /// <summary>Path inside the zip (e.g. "data.ttmdata", "sync/&lt;source id&gt;.ttmdata").</summary>
    public string Name { get; set; } = "";
    public BackupFileKind Kind { get; set; }

    /// <summary>Sync files only: where the file was linked from, and the source it belongs to.</summary>
    public string? OriginalPath { get; set; }
    public Guid? SourceId { get; set; }

    /// <summary>False for a sync file that couldn't be read when the backup was made; the rest is still valid.</summary>
    public bool Included { get; set; } = true;
    public string? Problem { get; set; }

    public long Size { get; set; }
    public string Sha256 { get; set; } = "";
}
