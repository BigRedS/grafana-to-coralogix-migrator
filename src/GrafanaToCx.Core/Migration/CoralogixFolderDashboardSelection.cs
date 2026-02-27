using GrafanaToCx.Core.ApiClient;

namespace GrafanaToCx.Core.Migration;

public sealed record CoralogixFolderDashboardSelection(
    CxFolderItem Folder,
    IReadOnlyList<DashboardCatalogItem> Dashboards);
