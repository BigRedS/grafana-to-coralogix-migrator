using GrafanaToCx.Core.ApiClient;

namespace GrafanaToCx.Core.Migration;

public static class FolderSelectionNormalizer
{
    public static IReadOnlyList<CxFolderItem> NormalizeSelectedRoots(
        IReadOnlyList<CxFolderItem> selectedFolders,
        IReadOnlyList<CxFolderItem> allFolders)
    {
        if (selectedFolders.Count == 0 || allFolders.Count == 0)
            return [];

        var foldersById = allFolders.ToDictionary(f => f.Id, f => f, StringComparer.OrdinalIgnoreCase);
        var selectedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var orderedUniqueSelection = new List<CxFolderItem>();

        foreach (var selected in selectedFolders)
        {
            if (!foldersById.TryGetValue(selected.Id, out var normalized))
                continue;

            if (!selectedIds.Add(normalized.Id))
                continue;

            orderedUniqueSelection.Add(normalized);
        }

        if (orderedUniqueSelection.Count <= 1)
            return orderedUniqueSelection;

        var selectedIdSet = orderedUniqueSelection
            .Select(f => f.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var canonicalRoots = new List<CxFolderItem>(orderedUniqueSelection.Count);
        foreach (var folder in orderedUniqueSelection)
        {
            if (!IsDescendantOfAnotherSelectedFolder(folder, selectedIdSet, foldersById))
                canonicalRoots.Add(folder);
        }

        return canonicalRoots;
    }

    private static bool IsDescendantOfAnotherSelectedFolder(
        CxFolderItem folder,
        HashSet<string> selectedIds,
        Dictionary<string, CxFolderItem> foldersById)
    {
        var parentId = folder.ParentId;

        while (!string.IsNullOrEmpty(parentId))
        {
            if (selectedIds.Contains(parentId))
                return true;

            if (!foldersById.TryGetValue(parentId, out var parentFolder))
                return false;

            parentId = parentFolder.ParentId;
        }

        return false;
    }
}
