using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GrafanaToCx.Core.ApiClient;

public sealed class CoralogixDashboardsClient : ICoralogixDashboardsClient, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<CoralogixDashboardsClient> _logger;
    private readonly string _endpointRoot;
    private readonly string _baseUrl;
    private readonly Dictionary<string, string?> _dashboardFolderMapping = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _mappingLock = new(1, 1);
    private bool _mappingBuilt = false;

    public CoralogixDashboardsClient(
        ILogger<CoralogixDashboardsClient> logger,
        string endpoint,
        string apiKey)
    {
        _logger = logger;
        _endpointRoot = endpoint.TrimEnd('/');
        _baseUrl = $"{endpoint.TrimEnd('/')}/v1/dashboards/dashboards";
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
    }

    public async Task<string?> CreateDashboardAsync(JObject dashboard, bool isLocked = false, string? folderId = null, CancellationToken ct = default)
    {
        var request = BuildRequest(dashboard, isLocked, folderId);

        try
        {
            var response = await _httpClient.PostAsync(_baseUrl, ToContent(request), ct);
            var content = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to create dashboard. Status: {Status}. Response: {Response}",
                    response.StatusCode, content);
                return null;
            }

            var body = JObject.Parse(content);
            var dashboardId = body["dashboardId"]?.ToString();
            _logger.LogInformation("Created dashboard: {DashboardId}", dashboardId ?? "unknown");
            return dashboardId;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError("Error creating dashboard: {Error}", ex.Message);
            return null;
        }
    }

    public async Task<bool> ReplaceDashboardAsync(JObject dashboard, bool isLocked = false, string? folderId = null, CancellationToken ct = default)
    {
        var request = BuildRequest(dashboard, isLocked, folderId);

        try
        {
            var response = await _httpClient.PutAsync(_baseUrl, ToContent(request), ct);
            var content = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to replace dashboard. Status: {Status}. Response: {Response}",
                    response.StatusCode, content);
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError("Error replacing dashboard: {Error}", ex.Message);
            return false;
        }
    }

    public async Task<List<string>> GetCatalogAsync(CancellationToken ct = default)
    {
        var items = await GetCatalogItemsAsync(ct);
        return items.Select(i => i.Id).ToList();
    }

    public async Task<List<DashboardCatalogItem>> GetCatalogItemsAsync(CancellationToken ct = default)
    {
        var result = new List<DashboardCatalogItem>();
        var url = $"{_endpointRoot}/dashboards/dashboards/v1/catalog";

        try
        {
            var response = await _httpClient.GetAsync(url, ct);
            var content = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                return result;
            }

            var body = JObject.Parse(content);
            var items = body["items"] as JArray;

            if (items is null)
            {
                return result;
            }

            foreach (var item in items)
            {
                var id = item["id"]?.ToString();
                if (string.IsNullOrEmpty(id))
                {
                    continue;
                }

                var name = item["name"]?.ToString() ?? string.Empty;
                var folderId =
                    // Observed in catalog responses.
                    item["folder"]?["id"]?.ToString() ??
                    // Compatible with request-style wrappers.
                    item["folderId"]?["value"]?.ToString() ??
                    item["folderId"]?.ToString() ??
                    // Defensive fallback for wrapped item payloads.
                    item["dashboard"]?["folderId"]?["value"]?.ToString() ??
                    item["dashboard"]?["folderId"]?.ToString();
                result.Add(new DashboardCatalogItem(id, name, folderId));
            }

            _logger.LogInformation("Found {Count} dashboards in catalog", result.Count);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError("Error fetching catalog: {Error}", ex.Message);
        }

        return result;
    }

    public async Task<List<DashboardCatalogItem>> GetCatalogItemsByFolderAsync(string folderId, CancellationToken ct = default)
    {
        var items = await GetCatalogItemsAsync(ct);
        var filtered = items
            .Where(i => string.Equals(i.FolderId, folderId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Fallback: if catalog parsing yielded zero matches, build detailed mapping from full dashboard payloads
        if (filtered.Count == 0 && items.Count > 0)
        {
            _logger.LogDebug(
                "Catalog filtering returned 0 dashboards for folder '{FolderId}' but catalog has {CatalogCount} items. Building detailed mapping.",
                folderId, items.Count);

            await EnsureDashboardFolderMappingAsync(items, ct);

            // Re-filter using the detailed mapping
            filtered = items
                .Where(i =>
                {
                    if (_dashboardFolderMapping.TryGetValue(i.Id, out var mappedFolderId))
                    {
                        return string.Equals(mappedFolderId, folderId, StringComparison.OrdinalIgnoreCase);
                    }
                    // Fallback to original catalog folderId if mapping doesn't have this dashboard
                    return string.Equals(i.FolderId, folderId, StringComparison.OrdinalIgnoreCase);
                })
                .ToList();

            if (filtered.Count > 0)
            {
                _logger.LogInformation(
                    "Fallback mapping found {Count} dashboards in folder '{FolderId}'",
                    filtered.Count, folderId);
            }
        }

        return filtered;
    }

    public async Task<DashboardUploadResult> UploadDashboardAsync(JObject dashboard, bool isLocked = false, string? folderId = null, CancellationToken ct = default)
    {
        var request = BuildRequest(dashboard, isLocked, folderId);

        try
        {
            var response = await _httpClient.PostAsync(_baseUrl, ToContent(request), ct);
            var content = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to upload dashboard. Status: {Status}. Response: {Response}",
                    response.StatusCode, content);
                return DashboardUploadResult.Failed(response.StatusCode, $"HTTP {(int)response.StatusCode}: {content}");
            }

            var body = JObject.Parse(content);
            var dashboardId = body["dashboardId"]?.ToString();

            if (string.IsNullOrEmpty(dashboardId))
            {
                _logger.LogError("Upload succeeded but response contained no dashboardId. Response: {Response}", content);
                return DashboardUploadResult.Failed(response.StatusCode, "Response did not include a dashboardId.");
            }

            _logger.LogInformation("Uploaded dashboard: {DashboardId}", dashboardId);
            return DashboardUploadResult.Succeeded(dashboardId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError("Network error uploading dashboard: {Error}", ex.Message);
            return DashboardUploadResult.NetworkError(ex.Message);
        }
    }

    public async Task<bool> AssignDashboardToFolderAsync(string dashboardId, string folderId, CancellationToken ct = default)
    {
        var url = $"{_baseUrl}/{dashboardId}/folder";
        var payload = new JObject
        {
            ["requestId"] = Guid.NewGuid().ToString(),
            ["folderId"] = folderId
        };
        var content = new StringContent(payload.ToString(Formatting.None), Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.PostAsync(url, content, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "Failed to assign dashboard '{DashboardId}' to folder '{FolderId}'. Status: {Status}. Response: {Response}",
                    dashboardId, folderId, response.StatusCode, body);
                return false;
            }

            _logger.LogInformation("Assigned dashboard '{DashboardId}' to folder '{FolderId}'.", dashboardId, folderId);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError("Error assigning dashboard '{DashboardId}' to folder: {Error}", dashboardId, ex.Message);
            return false;
        }
    }

    public async Task<JObject?> GetDashboardByIdAsync(string dashboardId, CancellationToken ct = default)
    {
        var url = $"{_baseUrl}/{dashboardId}";

        try
        {
            var response = await _httpClient.GetAsync(url, ct);
            var content = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to fetch dashboard '{DashboardId}'. Status: {Status}. Response: {Response}",
                    dashboardId, response.StatusCode, content);
                return null;
            }

            var body = JObject.Parse(content);
            return body["dashboard"] as JObject ?? body;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError("Error fetching dashboard '{DashboardId}': {Error}", dashboardId, ex.Message);
            return null;
        }
    }

    public async Task<bool> DeleteDashboardAsync(string dashboardId, CancellationToken ct = default)
    {
        var url = $"{_baseUrl}/{dashboardId}";

        try
        {
            var response = await _httpClient.DeleteAsync(url, ct);
            var content = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to delete dashboard '{DashboardId}'. Status: {Status}. Response: {Response}",
                    dashboardId, response.StatusCode, content);
                return false;
            }

            _logger.LogInformation("Deleted dashboard '{DashboardId}'.", dashboardId);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError("Error deleting dashboard '{DashboardId}': {Error}", dashboardId, ex.Message);
            return false;
        }
    }

    public void Dispose()
    {
        _mappingLock.Dispose();
        _httpClient.Dispose();
    }

    private static JObject BuildRequest(JObject dashboard, bool isLocked, string? folderId = null)
    {
        // Sanitize dashboard before building request (removes unsupported properties, ensures dataTable columns)
        dashboard = DashboardPayloadSanitizer.Sanitize(dashboard);

        if (!string.IsNullOrWhiteSpace(folderId))
        {
            dashboard = (JObject)dashboard.DeepClone();
            dashboard["folderId"] = new JObject { ["value"] = folderId };
        }

        return new JObject
        {
            ["requestId"] = Guid.NewGuid().ToString(),
            ["isLocked"] = isLocked,
            ["dashboard"] = dashboard
        };
    }

    private static StringContent ToContent(JObject payload) =>
        new StringContent(payload.ToString(Formatting.None), Encoding.UTF8, "application/json");

    private async Task EnsureDashboardFolderMappingAsync(List<DashboardCatalogItem> catalogItems, CancellationToken ct)
    {
        await _mappingLock.WaitAsync(ct);
        try
        {
            if (_mappingBuilt)
                return;

            _logger.LogDebug("Building dashboard-to-folder mapping from full dashboard payloads");

            // Build mapping for all catalog items that don't already have a folderId
            var itemsToMap = catalogItems
                .Where(i => string.IsNullOrEmpty(i.FolderId))
                .ToList();

            if (itemsToMap.Count == 0)
            {
                _mappingBuilt = true;
                return;
            }

            // Fetch full dashboard payloads in parallel (with reasonable concurrency limit)
            const int maxConcurrency = 10;
            var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
            var tasks = itemsToMap.Select(async item =>
            {
                await semaphore.WaitAsync(ct);
                try
                {
                    var dashboard = await GetDashboardByIdAsync(item.Id, ct);
                    if (dashboard != null)
                    {
                        var extractedFolderId = ExtractFolderIdFromDashboard(dashboard);
                        lock (_dashboardFolderMapping)
                        {
                            _dashboardFolderMapping[item.Id] = extractedFolderId;
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning("Failed to fetch dashboard '{DashboardId}' for folder mapping: {Error}",
                        item.Id, ex.Message);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);
            semaphore.Dispose();

            _mappingBuilt = true;
            _logger.LogInformation(
                "Built dashboard-to-folder mapping for {MappedCount} dashboards",
                _dashboardFolderMapping.Count);
        }
        finally
        {
            _mappingLock.Release();
        }
    }

    private static string? ExtractFolderIdFromDashboard(JObject dashboard)
    {
        // Try various shapes observed in full dashboard payloads
        return
            // Direct folderId field (string or wrapped)
            dashboard["folderId"]?["value"]?.ToString() ??
            dashboard["folderId"]?.ToString() ??
            // Folder object with id
            dashboard["folder"]?["id"]?.ToString() ??
            // Nested dashboard wrapper (if GetDashboardByIdAsync returns wrapped payload)
            dashboard["dashboard"]?["folderId"]?["value"]?.ToString() ??
            dashboard["dashboard"]?["folderId"]?.ToString() ??
            dashboard["dashboard"]?["folder"]?["id"]?.ToString() ??
            // Metadata section
            dashboard["meta"]?["folderId"]?.ToString() ??
            dashboard["meta"]?["folder"]?["id"]?.ToString() ??
            null;
    }
}
