namespace GrafanaToCx.Core.Migration;

public interface ICoralogixDashboardBackupService
{
    Task<CoralogixDashboardBackupResult> BackupAsync(
        IReadOnlyList<CoralogixFolderDashboardSelection> selections,
        string backupFilePath,
        CancellationToken ct = default);
}
