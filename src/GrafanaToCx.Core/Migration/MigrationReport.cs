using System.Text;
using GrafanaToCx.Core.Converter;

namespace GrafanaToCx.Core.Migration;

public sealed class MigrationReportEntry
{
    public string FolderTitle { get; init; } = string.Empty;
    public string DashboardTitle { get; init; } = string.Empty;
    public CheckpointStatus Status { get; init; }
    public string? CxDashboardId { get; init; }
    public string? ErrorMessage { get; init; }
    public IReadOnlyList<PanelConversionDiagnostic> ConversionDiagnostics { get; init; } = [];
}

public sealed class MigrationReport
{
    private readonly List<MigrationReportEntry> _entries = [];

    public void Add(MigrationReportEntry entry) => _entries.Add(entry);

    public string Build()
    {
        var succeeded = _entries.Count(e => e.Status == CheckpointStatus.Completed);
        var critical = _entries.Count(e => e.Status == CheckpointStatus.FailedCritical);
        var retryable = _entries.Count(e => e.Status == CheckpointStatus.FailedRetryable);
        var skipped = _entries.Count(e => e.Status == CheckpointStatus.Pending);

        var sb = new StringBuilder();
        sb.AppendLine("Migration Report");
        sb.AppendLine("================");
        sb.AppendLine($"Total:                {_entries.Count}");
        sb.AppendLine($"Succeeded:            {succeeded}");
        sb.AppendLine($"Failed (critical):    {critical}");
        sb.AppendLine($"Failed (retryable):   {retryable}  <- checkpoint saved, re-run to retry");
        if (skipped > 0)
            sb.AppendLine($"Skipped (already done): {skipped}");
        sb.AppendLine();

        foreach (var e in _entries.Where(e => e.Status == CheckpointStatus.Completed))
            sb.AppendLine($"[OK] {e.FolderTitle} / {e.DashboardTitle}  ->  CX ID: {e.CxDashboardId}");

        foreach (var e in _entries.Where(e => e.Status == CheckpointStatus.FailedCritical))
            sb.AppendLine($"[FAIL] {e.FolderTitle} / {e.DashboardTitle}  ->  {e.ErrorMessage}");

        foreach (var e in _entries.Where(e => e.Status == CheckpointStatus.FailedRetryable))
            sb.AppendLine($"[RETRY] {e.FolderTitle} / {e.DashboardTitle}  ->  {e.ErrorMessage}");

        foreach (var e in _entries.Where(e => e.ConversionDiagnostics.Count > 0))
        {
            sb.AppendLine($"[WARN] {e.FolderTitle} / {e.DashboardTitle}");
            foreach (var diagnostic in e.ConversionDiagnostics)
            {
                sb.AppendLine($"  - {diagnostic.PanelType} ({diagnostic.PanelTitle}): {diagnostic.Outcome} - {diagnostic.Reason}");
            }
        }

        return sb.ToString();
    }

    public async Task SaveAsync(string filePath, CancellationToken ct = default)
    {
        await File.WriteAllTextAsync(filePath, Build(), ct);
    }
}
