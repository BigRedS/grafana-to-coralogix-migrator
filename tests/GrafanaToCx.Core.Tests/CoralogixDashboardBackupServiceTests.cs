using System.IO.Compression;
using System.Text.Json;
using GrafanaToCx.Core.ApiClient;
using GrafanaToCx.Core.Migration;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;

namespace GrafanaToCx.Core.Tests;

public class CoralogixDashboardBackupServiceTests
{
    [Fact]
    public async Task BackupAsync_ZeroDashboards_ProducesReadableZipWithManifest()
    {
        var folder = new CxFolderItem("folder-a", "Folder A");
        var selections = new List<CoralogixFolderDashboardSelection>
        {
            new(folder, [])
        };

        var dashboardsClient = new FakeBackupDashboardsClient(_ => null);
        var sut = new CoralogixDashboardBackupService(
            dashboardsClient,
            NullLogger<CoralogixDashboardBackupService>.Instance);

        var path = Path.Combine(Path.GetTempPath(), $"backup-zero-{Guid.NewGuid():N}.zip");
        try
        {
            var result = await sut.BackupAsync(selections, path);

            Assert.True(result.Success);
            Assert.Equal(0, result.ExpectedDashboards);
            Assert.Equal(0, result.WrittenDashboards);
            Assert.Empty(result.FailedDashboardIds);

            Assert.True(File.Exists(path));
            var fileInfo = new FileInfo(path);
            Assert.True(fileInfo.Length > 22, "ZIP must be non-empty (not the minimal 22-byte empty archive).");

            using var zip = ZipFile.OpenRead(path);
            var manifestEntry = zip.GetEntry("_manifest.json");
            Assert.NotNull(manifestEntry);

            using var manifestStream = manifestEntry.Open();
            using var reader = new StreamReader(manifestStream);
            var manifestJson = await reader.ReadToEndAsync();
            var manifest = JsonSerializer.Deserialize<BackupManifestDto>(manifestJson);
            Assert.NotNull(manifest);
            Assert.Equal(0, manifest.Expected);
            Assert.Equal(0, manifest.Written);
            Assert.Empty(manifest.FailedIds);
            Assert.NotNull(manifest.Note);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public async Task BackupAsync_SuccessfulBackup_ProducesZipWithDashboardEntries()
    {
        var folder = new CxFolderItem("folder-a", "Folder A");
        var dash = new DashboardCatalogItem("dash-1", "My Dashboard", folder.Id);
        var selections = new List<CoralogixFolderDashboardSelection>
        {
            new(folder, [dash])
        };

        var dashboardPayload = new JObject { ["id"] = dash.Id, ["name"] = dash.Name };
        var dashboardsClient = new FakeBackupDashboardsClient(id => id == dash.Id ? dashboardPayload : null);
        var sut = new CoralogixDashboardBackupService(
            dashboardsClient,
            NullLogger<CoralogixDashboardBackupService>.Instance);

        var path = Path.Combine(Path.GetTempPath(), $"backup-ok-{Guid.NewGuid():N}.zip");
        try
        {
            var result = await sut.BackupAsync(selections, path);

            Assert.True(result.Success);
            Assert.Equal(1, result.ExpectedDashboards);
            Assert.Equal(1, result.WrittenDashboards);
            Assert.Empty(result.FailedDashboardIds);

            using var zip = ZipFile.OpenRead(path);
            var entries = zip.Entries.Select(e => e.FullName).ToList();
            Assert.Contains(entries, e => e.Contains("dash-1.json") && !e.StartsWith("_"));
            Assert.DoesNotContain(entries, e => e == "_manifest.json");
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public async Task BackupAsync_AllFailures_ProducesDiagnosticArtifactAndFailedResult()
    {
        var folder = new CxFolderItem("folder-a", "Folder A");
        var dash = new DashboardCatalogItem("dash-1", "Dash 1", folder.Id);
        var selections = new List<CoralogixFolderDashboardSelection>
        {
            new(folder, [dash])
        };

        var dashboardsClient = new FakeBackupDashboardsClient(_ => null);
        var sut = new CoralogixDashboardBackupService(
            dashboardsClient,
            NullLogger<CoralogixDashboardBackupService>.Instance);

        var path = Path.Combine(Path.GetTempPath(), $"backup-fail-{Guid.NewGuid():N}.zip");
        try
        {
            var result = await sut.BackupAsync(selections, path);

            Assert.False(result.Success);
            Assert.Equal(1, result.ExpectedDashboards);
            Assert.Equal(0, result.WrittenDashboards);
            Assert.Single(result.FailedDashboardIds, "dash-1");

            using var zip = ZipFile.OpenRead(path);
            var manifestEntry = zip.GetEntry("_manifest.json");
            Assert.NotNull(manifestEntry);

            using var manifestStream = manifestEntry.Open();
            using var reader = new StreamReader(manifestStream);
            var manifestJson = await reader.ReadToEndAsync();
            var manifest = JsonSerializer.Deserialize<BackupManifestDto>(manifestJson);
            Assert.NotNull(manifest);
            Assert.Equal(1, manifest.Expected);
            Assert.Equal(0, manifest.Written);
            Assert.Single(manifest.FailedIds, "dash-1");
            Assert.NotNull(manifest.Note);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public async Task BackupAsync_PartialFailures_ProducesDiagnosticArtifactAndFailedResult()
    {
        var folder = new CxFolderItem("folder-a", "Folder A");
        var dash1 = new DashboardCatalogItem("dash-1", "Dash 1", folder.Id);
        var dash2 = new DashboardCatalogItem("dash-2", "Dash 2", folder.Id);
        var selections = new List<CoralogixFolderDashboardSelection>
        {
            new(folder, [dash1, dash2])
        };

        var dashboardsClient = new FakeBackupDashboardsClient(id => id == "dash-1" ? new JObject { ["id"] = id } : null);
        var sut = new CoralogixDashboardBackupService(
            dashboardsClient,
            NullLogger<CoralogixDashboardBackupService>.Instance);

        var path = Path.Combine(Path.GetTempPath(), $"backup-partial-{Guid.NewGuid():N}.zip");
        try
        {
            var result = await sut.BackupAsync(selections, path);

            Assert.False(result.Success);
            Assert.Equal(2, result.ExpectedDashboards);
            Assert.Equal(1, result.WrittenDashboards);
            Assert.Single(result.FailedDashboardIds, "dash-2");

            using var zip = ZipFile.OpenRead(path);
            var manifestEntry = zip.GetEntry("_manifest.json");
            Assert.NotNull(manifestEntry);

            using var manifestStream = manifestEntry.Open();
            using var reader = new StreamReader(manifestStream);
            var manifestJson = await reader.ReadToEndAsync();
            var manifest = JsonSerializer.Deserialize<BackupManifestDto>(manifestJson);
            Assert.NotNull(manifest);
            Assert.Equal(2, manifest.Expected);
            Assert.Equal(1, manifest.Written);
            Assert.Single(manifest.FailedIds, "dash-2");
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public async Task BackupAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        var folder = new CxFolderItem("folder-a", "Folder A");
        var dash1 = new DashboardCatalogItem("dash-1", "Dash 1", folder.Id);
        var dash2 = new DashboardCatalogItem("dash-2", "Dash 2", folder.Id);
        var selections = new List<CoralogixFolderDashboardSelection>
        {
            new(folder, [dash1, dash2])
        };

        var cts = new CancellationTokenSource();
        var dashboardsClient = new FakeBackupDashboardsClient(id =>
        {
            if (id == "dash-1")
                cts.Cancel();
            return new JObject { ["id"] = id };
        });

        var sut = new CoralogixDashboardBackupService(
            dashboardsClient,
            NullLogger<CoralogixDashboardBackupService>.Instance);

        var path = Path.Combine(Path.GetTempPath(), $"backup-cancel-{Guid.NewGuid():N}.zip");
        try
        {
            await Assert.ThrowsAsync<OperationCanceledException>(
                () => sut.BackupAsync(selections, path, cts.Token));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private sealed class BackupManifestDto
    {
        public int Expected { get; set; }
        public int Written { get; set; }
        public List<string> FailedIds { get; set; } = [];
        public string? Note { get; set; }
    }

    private sealed class FakeBackupDashboardsClient : ICoralogixDashboardsClient
    {
        private readonly Func<string, JObject?> _getDashboardById;

        public FakeBackupDashboardsClient(Func<string, JObject?> getDashboardById)
        {
            _getDashboardById = getDashboardById;
        }

        public Task<JObject?> GetDashboardByIdAsync(string dashboardId, CancellationToken ct = default) =>
            Task.FromResult(_getDashboardById(dashboardId));

        public Task<List<DashboardCatalogItem>> GetCatalogItemsByFolderAsync(string folderId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> DeleteDashboardAsync(string dashboardId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<string?> CreateDashboardAsync(JObject dashboard, bool isLocked = false, string? folderId = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> ReplaceDashboardAsync(JObject dashboard, bool isLocked = false, string? folderId = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<string>> GetCatalogAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<DashboardCatalogItem>> GetCatalogItemsAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<DashboardUploadResult> UploadDashboardAsync(JObject dashboard, bool isLocked = false, string? folderId = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> AssignDashboardToFolderAsync(string dashboardId, string folderId, CancellationToken ct = default) => throw new NotImplementedException();
    }
}
