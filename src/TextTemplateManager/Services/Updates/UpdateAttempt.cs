using System;
using System.IO;
using System.Text.Json;

namespace TextTemplateManager.Services.Updates;

/// <summary>A switch to a new version that was started: written right before Windows closes the app, read by the next
/// start to tell whether it worked. <see cref="Attempts"/> counts the tries for the same version.</summary>
public sealed record UpdateAttempt(string TargetVersion, string FromVersion, bool Idle, DateTime StartedAt, int Attempts)
{
    /// <summary>After this many failed switches to one version, TTM stops trying it on its own and shows the manual
    /// steps instead.</summary>
    public const int MaxAttempts = 2;

    public static UpdateAttempt? Load(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize(File.ReadAllText(path), UpdateJsonContext.Default.UpdateAttempt) : null; }
        catch { return null; }
    }

    public void Save(string path)
    {
        try { File.WriteAllText(path, JsonSerializer.Serialize(this, UpdateJsonContext.Default.UpdateAttempt)); }
        catch { }
    }

    public static void Clear(string path)
    {
        try { File.Delete(path); }
        catch { }
    }

    /// <summary>What this start makes of a recorded attempt, given the version that now runs.</summary>
    public AttemptOutcome Evaluate(Version running)
    {
        if (!Version.TryParse(TargetVersion, out var target)) return AttemptOutcome.Unknown;
        if (running >= target) return AttemptOutcome.Succeeded;
        return Attempts >= MaxAttempts ? AttemptOutcome.GaveUp : AttemptOutcome.Failed;
    }
}

public enum AttemptOutcome { Unknown, Succeeded, Failed, GaveUp }

[System.Text.Json.Serialization.JsonSerializable(typeof(UpdateAttempt))]
internal sealed partial class UpdateJsonContext : System.Text.Json.Serialization.JsonSerializerContext { }
