namespace TextTemplateManager.Data;

/// <summary>What a direct import does with a template that already exists (same id, or same title in the same folder).</summary>
public enum ImportConflicts
{
    OverwriteAll,
    /// <summary>Overwrite only when the imported template was changed more recently.</summary>
    OverwriteOlder,
    Skip,
}

/// <summary>Templates added, overwritten and skipped by an import; <c>Conflicts</c> counts the ones that already existed.</summary>
public sealed record ImportSummary(int Added, int Overwritten, int Skipped, int Conflicts = 0);
