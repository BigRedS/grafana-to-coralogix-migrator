using GrafanaToCx.Core.ApiClient;
using GrafanaToCx.Core.Migration;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;

namespace GrafanaToCx.Core.Tests;

public class FolderCleanupServiceTests
{
    [Fact]
    public async Task CleanupAsync_BackupSuccess_DeletesDashboardsThenFolders()
    {
        var folderA = new CxFolderItem("folder-a", "Folder A");
        var folderB = new CxFolderItem("folder-b", "Folder B");

        var dashboardsClient = new FakeDashboardsClient(new Dictionary<string, List<DashboardCatalogItem>>
        {
            [folderA.Id] =
            [
                new DashboardCatalogItem("dash-1", "Dash 1", folderA.Id),
                new DashboardCatalogItem("dash-2", "Dash 2", folderA.Id)
            ],
            [folderB.Id] = []
        });

        var foldersClient = new FakeFoldersClient([folderA, folderB]);
        var backupService = new FakeBackupService((selections, _) =>
            new CoralogixDashboardBackupResult(2, 2, []));

        var sut = new FolderCleanupService(
            dashboardsClient,
            foldersClient,
            backupService,
            NullLogger<FolderCleanupService>.Instance);

        var result = await sut.CleanupAsync([folderA, folderB], "backup.zip");

        Assert.True(result.BackupSucceeded);
        Assert.Equal(2, result.BackedUpDashboards);
        Assert.Equal(2, result.DeletedDashboards);
        Assert.Equal(0, result.FailedDashboardDeletions);
        Assert.Equal(2, result.DeletedFolders);
        Assert.Equal(0, result.FailedFolderDeletions);
        Assert.Equal(1, backupService.CallCount);
        Assert.Equal(2, dashboardsClient.DeletedDashboardIds.Count);
        Assert.Equal(2, foldersClient.DeletedFolderIds.Count);
    }

    [Fact]
    public async Task CleanupAsync_BackupFailure_AbortsAllDeletions()
    {
        var folder = new CxFolderItem("folder-a", "Folder A");
        var dashboardsClient = new FakeDashboardsClient(new Dictionary<string, List<DashboardCatalogItem>>
        {
            [folder.Id] = [new DashboardCatalogItem("dash-1", "Dash 1", folder.Id)]
        });

        var foldersClient = new FakeFoldersClient([folder]);
        var backupService = new FakeBackupService((_, _) =>
            new CoralogixDashboardBackupResult(1, 0, ["dash-1"]));

        var sut = new FolderCleanupService(
            dashboardsClient,
            foldersClient,
            backupService,
            NullLogger<FolderCleanupService>.Instance);

        var result = await sut.CleanupAsync([folder], "backup.zip");

        Assert.False(result.BackupSucceeded);
        Assert.Equal(0, result.DeletedDashboards);
        Assert.Equal(0, result.DeletedFolders);
        Assert.Empty(dashboardsClient.DeletedDashboardIds);
        Assert.Empty(foldersClient.DeletedFolderIds);
    }

    [Fact]
    public async Task CleanupAsync_DashboardDeleteFailure_SkipsFolderDeletionForThatFolder()
    {
        var folderA = new CxFolderItem("folder-a", "Folder A");
        var folderB = new CxFolderItem("folder-b", "Folder B");

        var dashboardsClient = new FakeDashboardsClient(new Dictionary<string, List<DashboardCatalogItem>>
        {
            [folderA.Id] =
            [
                new DashboardCatalogItem("dash-a1", "Dash A1", folderA.Id),
                new DashboardCatalogItem("dash-a2", "Dash A2", folderA.Id)
            ],
            [folderB.Id] =
            [
                new DashboardCatalogItem("dash-b1", "Dash B1", folderB.Id)
            ]
        })
        {
            DeleteResults =
            {
                ["dash-a1"] = true,
                ["dash-a2"] = false,
                ["dash-b1"] = true
            }
        };

        var foldersClient = new FakeFoldersClient([folderA, folderB]);
        var backupService = new FakeBackupService((_, _) =>
            new CoralogixDashboardBackupResult(3, 3, []));

        var sut = new FolderCleanupService(
            dashboardsClient,
            foldersClient,
            backupService,
            NullLogger<FolderCleanupService>.Instance);

        var result = await sut.CleanupAsync([folderA, folderB], "backup.zip");

        Assert.True(result.BackupSucceeded);
        Assert.Equal(2, result.DeletedDashboards);
        Assert.Equal(1, result.FailedDashboardDeletions);
        Assert.Equal(1, result.DeletedFolders);
        Assert.Contains(folderB.Id, foldersClient.DeletedFolderIds);
        Assert.DoesNotContain(folderA.Id, foldersClient.DeletedFolderIds);
    }

    [Fact]
    public async Task CleanupAsync_EmptyFolder_DeletesFolderAfterBackup()
    {
        var folder = new CxFolderItem("folder-empty", "Folder Empty");
        var dashboardsClient = new FakeDashboardsClient(new Dictionary<string, List<DashboardCatalogItem>>
        {
            [folder.Id] = []
        });

        var foldersClient = new FakeFoldersClient([folder]);
        var backupService = new FakeBackupService((_, _) =>
            new CoralogixDashboardBackupResult(0, 0, []));

        var sut = new FolderCleanupService(
            dashboardsClient,
            foldersClient,
            backupService,
            NullLogger<FolderCleanupService>.Instance);

        var result = await sut.CleanupAsync([folder], "backup.zip");

        Assert.True(result.BackupSucceeded);
        Assert.Equal(0, result.DeletedDashboards);
        Assert.Equal(1, result.DeletedFolders);
        Assert.Contains(folder.Id, foldersClient.DeletedFolderIds);
    }

    private sealed class FakeDashboardsClient : ICoralogixDashboardsClient
    {
        private readonly Dictionary<string, List<DashboardCatalogItem>> _itemsByFolder;
        public Dictionary<string, bool> DeleteResults { get; } = [];
        public List<string> DeletedDashboardIds { get; } = [];

        public FakeDashboardsClient(Dictionary<string, List<DashboardCatalogItem>> itemsByFolder)
        {
            _itemsByFolder = itemsByFolder;
        }

        public Task<List<DashboardCatalogItem>> GetCatalogItemsByFolderAsync(string folderId, CancellationToken ct = default)
        {
            var items = _itemsByFolder.TryGetValue(folderId, out var value) ? value : [];
            return Task.FromResult(items.ToList());
        }

        public Task<bool> DeleteDashboardAsync(string dashboardId, CancellationToken ct = default)
        {
            DeletedDashboardIds.Add(dashboardId);
            if (DeleteResults.TryGetValue(dashboardId, out var result))
                return Task.FromResult(result);

            return Task.FromResult(true);
        }

        public Task<string?> CreateDashboardAsync(JObject dashboard, bool isLocked = false, string? folderId = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> ReplaceDashboardAsync(JObject dashboard, bool isLocked = false, string? folderId = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<string>> GetCatalogAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<DashboardCatalogItem>> GetCatalogItemsAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<DashboardUploadResult> UploadDashboardAsync(JObject dashboard, bool isLocked = false, string? folderId = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> AssignDashboardToFolderAsync(string dashboardId, string folderId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<JObject?> GetDashboardByIdAsync(string dashboardId, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class FakeFoldersClient : ICoralogixFoldersClient
    {
        private readonly IReadOnlyList<CxFolderItem> _folders;

        public FakeFoldersClient(IReadOnlyList<CxFolderItem> folders)
        {
            _folders = folders;
        }

        public List<string> DeletedFolderIds { get; } = [];

        public Task<bool> DeleteFolderAsync(string folderId, CancellationToken ct = default)
        {
            DeletedFolderIds.Add(folderId);
            return Task.FromResult(true);
        }

        public Task<string?> GetOrCreateFolderAsync(string name, string? parentId = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<CxFolderItem>> ListFoldersAsync(CancellationToken ct = default) => Task.FromResult(_folders.ToList());
    }

    private sealed class FakeBackupService : ICoralogixDashboardBackupService
    {
        private readonly Func<IReadOnlyList<CoralogixFolderDashboardSelection>, string, CoralogixDashboardBackupResult> _factory;
        public int CallCount { get; private set; }

        public FakeBackupService(Func<IReadOnlyList<CoralogixFolderDashboardSelection>, string, CoralogixDashboardBackupResult> factory)
        {
            _factory = factory;
        }

        public Task<CoralogixDashboardBackupResult> BackupAsync(
            IReadOnlyList<CoralogixFolderDashboardSelection> selections,
            string backupFilePath,
            CancellationToken ct = default)
        {
            CallCount++;
            return Task.FromResult(_factory(selections, backupFilePath));
        }
    }
}
