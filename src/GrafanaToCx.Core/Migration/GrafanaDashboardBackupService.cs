using System.IO.Compression;
using GrafanaToCx.Core.ApiClient;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GrafanaToCx.Core.Migration;

public sealed class GrafanaDashboardBackupService
{
    private static readonly char[] InvalidPathChars = Path.GetInvalidFileNameChars();

    private readonly IGrafanaClient _grafanaClient;
    private readonly ILogger<GrafanaDashboardBackupService> _logger;

    public GrafanaDashboardBackupService(
        IGrafanaClient grafanaClient,
        ILogger<GrafanaDashboardBackupService> logger)
    {
        _grafanaClient = grafanaClient;
        _logger = logger;
    }

    public async Task BackupAsync(
        IReadOnlyList<GrafanaFolder> folders,
        string backupFilePath,
        CancellationToken ct = default)
    {
        var dir = Path.GetDirectoryName(backupFilePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        _logger.LogInformation("Starting Grafana dashboard backup to '{BackupFile}'.", backupFilePath);

        int total = 0, failed = 0;

        using var stream = new FileStream(backupFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false);

        foreach (var folder in folders)
        {
            ct.ThrowIfCancellationRequested();

            List<GrafanaDashboardRef> dashboards;
            try
            {
                dashboards = await _grafanaClient.GetDashboardsInFolderAsync(folder.Id, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Backup: failed to list dashboards in folder '{Folder}'. Skipping folder.",
                    folder.Title);
                failed++;
                continue;
            }

            var safeFolder = Sanitize(folder.Title);

            foreach (var dash in dashboards)
            {
                ct.ThrowIfCancellationRequested();

                JObject? json;
                try
                {
                    json = await _grafanaClient.GetDashboardByUidAsync(dash.Uid, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Backup: failed to fetch dashboard '{Title}' ({Uid}). Skipping.",
                        dash.Title, dash.Uid);
                    failed++;
                    continue;
                }

                if (json is null)
                {
                    _logger.LogWarning(
                        "Backup: dashboard '{Title}' ({Uid}) returned empty response. Skipping.",
                        dash.Title, dash.Uid);
                    failed++;
                    continue;
                }

                var entryName = $"{safeFolder}/{Sanitize(dash.Title)}_{dash.Uid}.json";
                var entry = zip.CreateEntry(entryName, CompressionLevel.SmallestSize);

                await using var entryStream = entry.Open();
                await using var writer = new StreamWriter(entryStream);
                await writer.WriteAsync(json.ToString(Formatting.Indented));

                total++;
                _logger.LogDebug("Backup: saved '{Entry}'.", entryName);
            }
        }

        _logger.LogInformation(
            "Backup complete: {Total} dashboard(s) saved, {Failed} skipped. Archive: '{BackupFile}'.",
            total, failed, backupFilePath);
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
