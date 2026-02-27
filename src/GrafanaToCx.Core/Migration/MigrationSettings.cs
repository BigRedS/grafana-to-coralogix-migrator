namespace GrafanaToCx.Core.Migration;

public sealed class MigrationSettings
{
    public GrafanaSettings Grafana { get; init; } = new();
    public CoralogixSettings Coralogix { get; init; } = new();
    public MigrationRunSettings Migration { get; init; } = new();
}

public sealed class GrafanaSettings
{
    public string Region { get; init; } = "eu2";
    public List<string> Folders { get; init; } = [];
}

public sealed class CoralogixSettings
{
    public string Region { get; init; } = "eu2";
    public string? FolderId { get; init; }
    public bool IsLocked { get; init; }
    public bool MigrateFolderStructure { get; init; }

    /// <summary>
    /// When MigrateFolderStructure is true, auto-created CX folders are nested under this parent folder ID.
    /// </summary>
    public string? ParentFolderId { get; init; }

    /// <summary>
    /// Explicit per-folder mapping: Grafana folder title → Coralogix folder ID.
    /// When present for a folder, takes priority over MigrateFolderStructure and FolderId.
    /// A null value means "no folder" for that Grafana folder.
    /// </summary>
    public Dictionary<string, string?> FolderMappings { get; init; } = [];

    /// <summary>
    /// When true, dashboards that already exist in Coralogix (matched by name or checkpoint ID) are replaced
    /// instead of skipped. Completed checkpoint entries are re-processed.
    /// </summary>
    public bool OverwriteExisting { get; init; }
}

public sealed class MigrationRunSettings
{
    public string CheckpointFile { get; init; } = "migration-checkpoint.json";
    public string ReportFile { get; init; } = "migration-report.txt";

    /// <summary>
    /// Path for the pre-migration backup ZIP archive. Set to empty string to disable backup.
    /// </summary>
    public string BackupFile { get; init; } = "grafana-backup.zip";

    public int MaxRetries { get; init; } = 5;
    public int InitialRetryDelaySeconds { get; init; } = 2;
}
