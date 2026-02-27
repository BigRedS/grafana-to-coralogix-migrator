namespace GrafanaToCx.Core.Migration;

public sealed record CoralogixDashboardBackupResult(
    int ExpectedDashboards,
    int WrittenDashboards,
    IReadOnlyList<string> FailedDashboardIds)
{
    public bool Success => FailedDashboardIds.Count == 0 && WrittenDashboards == ExpectedDashboards;
}
