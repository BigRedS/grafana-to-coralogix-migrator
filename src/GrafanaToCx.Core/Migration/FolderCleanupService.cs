using GrafanaToCx.Core.ApiClient;
using Microsoft.Extensions.Logging;

namespace GrafanaToCx.Core.Migration;

public sealed class FolderCleanupService
{
    private readonly ICoralogixDashboardsClient _dashboardsClient;
    private readonly ICoralogixFoldersClient _foldersClient;
    private readonly ICoralogixDashboardBackupService _backupService;
    private readonly ILogger<FolderCleanupService> _logger;

    public FolderCleanupService(
        ICoralogixDashboardsClient dashboardsClient,
        ICoralogixFoldersClient foldersClient,
        ICoralogixDashboardBackupService backupService,
        ILogger<FolderCleanupService> logger)
    {
        _dashboardsClient = dashboardsClient;
        _foldersClient = foldersClient;
        _backupService = backupService;
        _logger = logger;
    }

    public async Task<FolderCleanupResult> CleanupAsync(
        IReadOnlyList<CxFolderItem> selectedFolders,
        string backupFilePath,
        CancellationToken ct = default)
    {
        // Fetch all folders to build the tree and find descendants
        var allFolders = await _foldersClient.ListFoldersAsync(ct);
        var foldersById = allFolders.ToDictionary(f => f.Id, f => f);
        var childrenByParent = allFolders
            .Where(f => f.ParentId is not null)
            .GroupBy(f => f.ParentId!)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Collect all folders to process: selected + all descendants recursively
        var foldersToProcess = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var foldersToProcessList = new List<CxFolderItem>();

        void AddFolderAndDescendants(CxFolderItem folder)
        {
            if (!foldersToProcess.Add(folder.Id))
                return; // Already added

            foldersToProcessList.Add(folder);

            if (childrenByParent.TryGetValue(folder.Id, out var children))
            {
                foreach (var child in children)
                {
                    AddFolderAndDescendants(child);
                }
            }
        }

        foreach (var selectedFolder in selectedFolders)
        {
            if (foldersById.TryGetValue(selectedFolder.Id, out var folder))
            {
                AddFolderAndDescendants(folder);
            }
        }

        // Build selections with dashboards for all folders (selected + descendants)
        var selections = new List<CoralogixFolderDashboardSelection>(foldersToProcessList.Count);
        foreach (var folder in foldersToProcessList)
        {
            var dashboards = await _dashboardsClient.GetCatalogItemsByFolderAsync(folder.Id, ct);
            selections.Add(new CoralogixFolderDashboardSelection(folder, dashboards));
        }

        var backupResult = await _backupService.BackupAsync(selections, backupFilePath, ct);
        if (!backupResult.Success)
        {
            _logger.LogWarning("Cleanup aborted. Backup was unsuccessful.");
            return new FolderCleanupResult(
                BackupSucceeded: false,
                BackupFilePath: backupFilePath,
                SelectedFolders: selectedFolders.Count,
                BackedUpDashboards: backupResult.WrittenDashboards,
                DeletedDashboards: 0,
                FailedDashboardDeletions: 0,
                DeletedFolders: 0,
                FailedFolderDeletions: 0,
                FailedBackupDashboardIds: backupResult.FailedDashboardIds);
        }

        var deletedDashboards = 0;
        var failedDashboardDeletions = 0;
        var deletedFolders = 0;
        var failedFolderDeletions = 0;

        // Track which folders had dashboard deletion failures (skip folder deletion if any dashboard failed)
        var foldersWithDashboardFailures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Phase 1: Delete all dashboards from all folders first
        foreach (var selection in selections)
        {
            ct.ThrowIfCancellationRequested();

            var folderHasDeleteFailure = false;
            foreach (var dashboard in selection.Dashboards)
            {
                var deleted = await _dashboardsClient.DeleteDashboardAsync(dashboard.Id, ct);
                if (deleted)
                {
                    deletedDashboards++;
                }
                else
                {
                    failedDashboardDeletions++;
                    folderHasDeleteFailure = true;
                }
            }

            if (folderHasDeleteFailure)
            {
                foldersWithDashboardFailures.Add(selection.Folder.Id);
                _logger.LogWarning(
                    "Some dashboard deletions failed for folder '{FolderName}' ({FolderId}). Folder deletion will be skipped.",
                    selection.Folder.Name, selection.Folder.Id);
            }
        }

        // Phase 2: Delete folders bottom-up (children before parents)
        // Sort folders by depth descending (deepest first) to ensure children are deleted before parents
        var folderDepthMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int GetDepth(CxFolderItem folder)
        {
            if (folderDepthMap.TryGetValue(folder.Id, out var depth))
                return depth;

            if (folder.ParentId is null || !foldersById.TryGetValue(folder.ParentId, out var parent))
            {
                depth = 0;
            }
            else
            {
                depth = GetDepth(parent) + 1;
            }

            folderDepthMap[folder.Id] = depth;
            return depth;
        }

        foreach (var folder in foldersToProcessList)
        {
            GetDepth(folder);
        }

        var foldersSortedByDepth = foldersToProcessList
            .OrderByDescending(f => folderDepthMap[f.Id])
            .ThenBy(f => f.Name)
            .ToList();

        var folderDeleteFailures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in foldersSortedByDepth)
        {
            ct.ThrowIfCancellationRequested();

            // Skip if dashboard deletion failed for this folder
            if (foldersWithDashboardFailures.Contains(folder.Id))
            {
                _logger.LogWarning(
                    "Skipping folder deletion for '{FolderName}' ({FolderId}) because one or more dashboard deletions failed.",
                    folder.Name, folder.Id);
                folderDeleteFailures.Add(folder.Id);
                failedFolderDeletions++;
                continue;
            }

            // Skip if any child folder deletion failed (defensive check for bottom-up deletion)
            if (childrenByParent.TryGetValue(folder.Id, out var children))
            {
                var hasFailedChild = children.Any(c => folderDeleteFailures.Contains(c.Id));
                if (hasFailedChild)
                {
                    _logger.LogWarning(
                        "Skipping folder deletion for '{FolderName}' ({FolderId}) because one or more child folder deletions failed.",
                        folder.Name, folder.Id);
                    folderDeleteFailures.Add(folder.Id);
                    failedFolderDeletions++;
                    continue;
                }
            }

            var folderDeleted = await _foldersClient.DeleteFolderAsync(folder.Id, ct);
            if (folderDeleted)
            {
                deletedFolders++;
            }
            else
            {
                failedFolderDeletions++;
                folderDeleteFailures.Add(folder.Id);
            }
        }

        return new FolderCleanupResult(
            BackupSucceeded: true,
            BackupFilePath: backupFilePath,
            SelectedFolders: selectedFolders.Count,
            BackedUpDashboards: backupResult.WrittenDashboards,
            DeletedDashboards: deletedDashboards,
            FailedDashboardDeletions: failedDashboardDeletions,
            DeletedFolders: deletedFolders,
            FailedFolderDeletions: failedFolderDeletions,
            FailedBackupDashboardIds: backupResult.FailedDashboardIds);
    }
}
