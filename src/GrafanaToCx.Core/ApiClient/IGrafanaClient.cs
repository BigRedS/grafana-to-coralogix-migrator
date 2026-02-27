using Newtonsoft.Json.Linq;

namespace GrafanaToCx.Core.ApiClient;

public interface IGrafanaClient
{
    Task<List<GrafanaFolder>> GetFoldersAsync(IReadOnlyList<string> folderFilter, CancellationToken ct = default);
    Task<List<GrafanaDashboardRef>> GetDashboardsInFolderAsync(int folderId, CancellationToken ct = default);
    Task<JObject?> GetDashboardByUidAsync(string uid, CancellationToken ct = default);
}

public sealed record GrafanaFolder(int Id, string Uid, string Title);
public sealed record GrafanaDashboardRef(string Uid, string Title, string FolderTitle);
