namespace GrafanaToCx.Core.ApiClient;

public interface ICoralogixFoldersClient
{
    Task<string?> GetOrCreateFolderAsync(string name, string? parentId = null, CancellationToken ct = default);
    Task<List<CxFolderItem>> ListFoldersAsync(CancellationToken ct = default);
    Task<bool> DeleteFolderAsync(string folderId, CancellationToken ct = default);
}
