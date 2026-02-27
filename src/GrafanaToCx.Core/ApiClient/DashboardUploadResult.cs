using System.Net;

namespace GrafanaToCx.Core.ApiClient;

public sealed record DashboardUploadResult(
    bool Success,
    string? DashboardId,
    HttpStatusCode? StatusCode,
    string? ErrorMessage)
{
    public static DashboardUploadResult Succeeded(string dashboardId) =>
        new(true, dashboardId, null, null);

    public static DashboardUploadResult Failed(HttpStatusCode statusCode, string errorMessage) =>
        new(false, null, statusCode, errorMessage);

    public static DashboardUploadResult NetworkError(string errorMessage) =>
        new(false, null, null, errorMessage);
}
