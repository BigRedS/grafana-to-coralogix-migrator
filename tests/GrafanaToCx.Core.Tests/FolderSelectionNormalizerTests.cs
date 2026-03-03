using GrafanaToCx.Core.ApiClient;
using GrafanaToCx.Core.Migration;

namespace GrafanaToCx.Core.Tests;

public class FolderSelectionNormalizerTests
{
    [Fact]
    public void NormalizeSelectedRoots_SingleSelection_PreservesFolder()
    {
        var root = new CxFolderItem("root", "Root");
        var allFolders = new List<CxFolderItem> { root };

        var result = FolderSelectionNormalizer.NormalizeSelectedRoots([root], allFolders);

        Assert.Single(result);
        Assert.Equal("root", result[0].Id);
    }

    [Fact]
    public void NormalizeSelectedRoots_DisjointRoots_PreservesBoth()
    {
        var rootA = new CxFolderItem("root-a", "Root A");
        var rootB = new CxFolderItem("root-b", "Root B");
        var allFolders = new List<CxFolderItem> { rootA, rootB };

        var result = FolderSelectionNormalizer.NormalizeSelectedRoots([rootA, rootB], allFolders);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, f => f.Id == rootA.Id);
        Assert.Contains(result, f => f.Id == rootB.Id);
    }

    [Fact]
    public void NormalizeSelectedRoots_DuplicateIds_CollapsesToSingleFolder()
    {
        var root = new CxFolderItem("root", "Root");
        var duplicateWithDifferentCase = new CxFolderItem("ROOT", "Root Duplicate");
        var allFolders = new List<CxFolderItem> { root };

        var result = FolderSelectionNormalizer.NormalizeSelectedRoots([root, duplicateWithDifferentCase], allFolders);

        Assert.Single(result);
        Assert.Equal(root.Id, result[0].Id);
    }

    [Fact]
    public void NormalizeSelectedRoots_ParentAndChildSelected_KeepsOnlyParent()
    {
        var parent = new CxFolderItem("parent", "Parent");
        var child = new CxFolderItem("child", "Child", parent.Id);
        var allFolders = new List<CxFolderItem> { parent, child };

        var result = FolderSelectionNormalizer.NormalizeSelectedRoots([parent, child], allFolders);

        Assert.Single(result);
        Assert.Equal(parent.Id, result[0].Id);
    }
}
