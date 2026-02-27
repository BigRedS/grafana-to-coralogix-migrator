namespace GrafanaToCx.Core.Migration;

public sealed record FolderCleanupResult(
    bool BackupSucceeded,
    string BackupFilePath,
    int SelectedFolders,
    int BackedUpDashboards,
    int DeletedDashboards,
    int FailedDashboardDeletions,
    int DeletedFolders,
    int FailedFolderDeletions,
    IReadOnlyList<string> FailedBackupDashboardIds);
