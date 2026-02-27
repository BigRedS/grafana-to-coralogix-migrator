using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace GrafanaToCx.Core.ApiClient;

public sealed class GrafanaClient : IGrafanaClient, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GrafanaClient> _logger;

    public GrafanaClient(ILogger<GrafanaClient> logger, string baseUrl, string apiKey)
    {
        _logger = logger;
        _httpClient = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
    }

    public async Task<List<GrafanaFolder>> GetFoldersAsync(
        IReadOnlyList<string> folderFilter,
        CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync("api/folders", ct);
        var content = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Failed to fetch Grafana folders. Status: {Status}. Response: {Response}",
                response.StatusCode, content);
            return [];
        }

        var array = JArray.Parse(content);
        var folders = array
            .Select(item => new GrafanaFolder(
                item.Value<int>("id"),
                item.Value<string>("uid") ?? string.Empty,
                item.Value<string>("title") ?? string.Empty))
            .ToList();

        if (folderFilter.Count > 0)
        {
            var filter = new HashSet<string>(folderFilter, StringComparer.OrdinalIgnoreCase);
            folders = folders.Where(f => filter.Contains(f.Title)).ToList();
        }

        _logger.LogInformation("Found {Count} Grafana folders", folders.Count);
        return folders;
    }

    public async Task<List<GrafanaDashboardRef>> GetDashboardsInFolderAsync(
        int folderId,
        CancellationToken ct = default)
    {
        var url = $"api/search?folderIds={folderId}&type=dash-db";
        var response = await _httpClient.GetAsync(url, ct);
        var content = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Failed to fetch dashboards in folder {FolderId}. Status: {Status}",
                folderId, response.StatusCode);
            return [];
        }

        var array = JArray.Parse(content);
        return array
            .Select(item => new GrafanaDashboardRef(
                item.Value<string>("uid") ?? string.Empty,
                item.Value<string>("title") ?? string.Empty,
                item.Value<string>("folderTitle") ?? string.Empty))
            .Where(d => !string.IsNullOrEmpty(d.Uid))
            .ToList();
    }

    public async Task<JObject?> GetDashboardByUidAsync(string uid, CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync($"api/dashboards/uid/{uid}", ct);
        var content = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Failed to fetch dashboard {Uid}. Status: {Status}", uid, response.StatusCode);
            return null;
        }

        return JObject.Parse(content);
    }

    public void Dispose() => _httpClient.Dispose();
}
