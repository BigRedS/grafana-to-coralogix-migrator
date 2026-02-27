using Newtonsoft.Json.Linq;

namespace GrafanaToCx.Core.ApiClient;

public interface ICoralogixDashboardsClient
{
    Task<string?> CreateDashboardAsync(JObject dashboard, bool isLocked = false, string? folderId = null, CancellationToken ct = default);
    Task<bool> ReplaceDashboardAsync(JObject dashboard, bool isLocked = false, string? folderId = null, CancellationToken ct = default);
    Task<List<string>> GetCatalogAsync(CancellationToken ct = default);
    Task<List<DashboardCatalogItem>> GetCatalogItemsAsync(CancellationToken ct = default);
    Task<List<DashboardCatalogItem>> GetCatalogItemsByFolderAsync(string folderId, CancellationToken ct = default);
    Task<DashboardUploadResult> UploadDashboardAsync(JObject dashboard, bool isLocked = false, string? folderId = null, CancellationToken ct = default);
    Task<bool> AssignDashboardToFolderAsync(string dashboardId, string folderId, CancellationToken ct = default);
    Task<JObject?> GetDashboardByIdAsync(string dashboardId, CancellationToken ct = default);
    Task<bool> DeleteDashboardAsync(string dashboardId, CancellationToken ct = default);
}
