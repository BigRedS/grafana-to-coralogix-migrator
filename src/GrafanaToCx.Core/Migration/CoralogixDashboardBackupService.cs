using System.IO.Compression;
using System.Text;
using GrafanaToCx.Core.ApiClient;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace GrafanaToCx.Core.Migration;

public sealed class CoralogixDashboardBackupService : ICoralogixDashboardBackupService
{
    private const string ManifestEntryName = "_manifest.json";
    private static readonly char[] InvalidPathChars = Path.GetInvalidFileNameChars();

    private readonly ICoralogixDashboardsClient _dashboardsClient;
    private readonly ILogger<CoralogixDashboardBackupService> _logger;

    public CoralogixDashboardBackupService(
        ICoralogixDashboardsClient dashboardsClient,
        ILogger<CoralogixDashboardBackupService> logger)
    {
        _dashboardsClient = dashboardsClient;
        _logger = logger;
    }

    public async Task<CoralogixDashboardBackupResult> BackupAsync(
        IReadOnlyList<CoralogixFolderDashboardSelection> selections,
        string backupFilePath,
        CancellationToken ct = default)
    {
        var expected = selections.Sum(s => s.Dashboards.Count);
        var written = 0;
        var failedDashboardIds = new List<string>();

        var dir = Path.GetDirectoryName(backupFilePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        _logger.LogInformation("Starting Coralogix dashboard backup to '{BackupFile}'.", backupFilePath);

        using var stream = new FileStream(backupFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false);

        foreach (var selection in selections)
        {
            ct.ThrowIfCancellationRequested();

            var safeFolderName = Sanitize(selection.Folder.Name);
            foreach (var dashboard in selection.Dashboards)
            {
                ct.ThrowIfCancellationRequested();

                var fullDashboard = await _dashboardsClient.GetDashboardByIdAsync(dashboard.Id, ct);
                if (fullDashboard is null)
                {
                    failedDashboardIds.Add(dashboard.Id);
                    _logger.LogWarning(
                        "Backup: failed to fetch dashboard '{DashboardName}' ({DashboardId}) from folder '{FolderName}'.",
                        dashboard.Name, dashboard.Id, selection.Folder.Name);
                    continue;
                }

                var entryName = $"{safeFolderName}/{Sanitize(dashboard.Name)}_{dashboard.Id}.json";
                var entry = zip.CreateEntry(entryName, CompressionLevel.SmallestSize);

                await using var entryStream = entry.Open();
                await using var writer = new StreamWriter(entryStream);
                await writer.WriteAsync(fullDashboard.ToString(Formatting.Indented));

                written++;
            }
        }

        var needsManifest = written == 0 || failedDashboardIds.Count > 0;
        if (needsManifest)
        {
            var manifest = new BackupManifest
            {
                Expected = expected,
                Written = written,
                FailedIds = [..failedDashboardIds],
                Note = expected == 0
                    ? "No dashboards in selected folders."
                    : failedDashboardIds.Count > 0
                        ? $"Backup incomplete: {failedDashboardIds.Count} dashboard(s) failed to fetch."
                        : null
            };
            var manifestJson = JsonConvert.SerializeObject(manifest, Formatting.Indented);
            var manifestEntry = zip.CreateEntry(ManifestEntryName, CompressionLevel.SmallestSize);
            await using (var manifestStream = manifestEntry.Open())
            await using (var manifestWriter = new StreamWriter(manifestStream, Encoding.UTF8))
            {
                await manifestWriter.WriteAsync(manifestJson);
            }
        }

        var result = new CoralogixDashboardBackupResult(expected, written, failedDashboardIds);
        if (result.Success)
        {
            _logger.LogInformation("Coralogix backup complete: {Written}/{Expected} dashboard(s) saved to '{BackupFile}'.",
                written, expected, backupFilePath);
        }
        else
        {
            _logger.LogError("Coralogix backup failed integrity checks: {Written}/{Expected} saved, {FailedCount} failed.",
                written, expected, failedDashboardIds.Count);
        }

        return result;
    }

    private sealed class BackupManifest
    {
        public int Expected { get; set; }
        public int Written { get; set; }
        public IReadOnlyList<string> FailedIds { get; set; } = [];
        public string? Note { get; set; }
    }

    private static string Sanitize(string name)
    {
        var chars = name.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(InvalidPathChars, chars[i]) >= 0 || chars[i] == '/')
                chars[i] = '_';
        }

        return new string(chars).Trim();
    }
}
