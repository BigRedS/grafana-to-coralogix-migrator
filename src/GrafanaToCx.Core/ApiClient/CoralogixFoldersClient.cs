using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GrafanaToCx.Core.ApiClient;

public sealed class CoralogixFoldersClient : ICoralogixFoldersClient, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<CoralogixFoldersClient> _logger;
    private readonly string _baseUrl;

    public CoralogixFoldersClient(
        ILogger<CoralogixFoldersClient> logger,
        string endpoint,
        string apiKey)
    {
        _logger = logger;
        _baseUrl = $"{endpoint.TrimEnd('/')}/dashboards/dashboards/v1/folders";
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
    }

    public async Task<string?> GetOrCreateFolderAsync(string name, string? parentId = null, CancellationToken ct = default)
    {
        var folders = await ListFoldersAsync(ct);
        var existing = folders.FirstOrDefault(f =>
            string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase) &&
            f.ParentId == parentId);
        if (existing is not null)
        {
            _logger.LogInformation("Found existing CX folder '{Name}' with ID: {FolderId}", name, existing.Id);
            return existing.Id;
        }

        return await CreateFolderAsync(name, parentId, ct);
    }

    public async Task<List<CxFolderItem>> ListFoldersAsync(CancellationToken ct = default)
    {
        var result = new List<CxFolderItem>();
        try
        {
            var response = await _httpClient.GetAsync(_baseUrl, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Could not list CX folders. Status: {Status}.", response.StatusCode);
                return result;
            }

            var json = JObject.Parse(body);
            var folders = json["folders"] as JArray ?? json["folder"] as JArray ?? [];

            foreach (var folder in folders.Children<JObject>())
            {
                var inner = folder["folder"] as JObject ?? folder;
                var id = inner["id"]?.ToString();
                var name = inner["name"]?.ToString();
                var parentId = inner["parentId"]?.ToString();
                if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                    result.Add(new CxFolderItem(id, name, string.IsNullOrEmpty(parentId) ? null : parentId));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("Error listing CX folders: {Error}.", ex.Message);
        }

        return result;
    }

    public async Task<bool> DeleteFolderAsync(string folderId, CancellationToken ct = default)
    {
        var url = $"{_baseUrl}/{folderId}";

        try
        {
            var response = await _httpClient.DeleteAsync(url, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to delete CX folder '{FolderId}'. Status: {Status}. Response: {Response}",
                    folderId, response.StatusCode, body);
                return false;
            }

            _logger.LogInformation("Deleted CX folder '{FolderId}'.", folderId);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError("Error deleting CX folder '{FolderId}': {Error}", folderId, ex.Message);
            return false;
        }
    }

    private async Task<string?> CreateFolderAsync(string name, string? parentId, CancellationToken ct)
    {
        var folderObject = new JObject { ["name"] = name };
        if (!string.IsNullOrEmpty(parentId))
            folderObject["parentId"] = parentId;

        var payload = new JObject
        {
            ["requestId"] = Guid.NewGuid().ToString(),
            ["folder"] = folderObject
        };
        var content = new StringContent(payload.ToString(Formatting.None), Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.PostAsync(_baseUrl, content, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                var conflictId = ExtractFolderIdFromConflict(body);
                if (conflictId is not null)
                {
                    _logger.LogInformation("CX folder '{Name}' already exists with ID: {FolderId}", name, conflictId);
                    return conflictId;
                }

                _logger.LogError("CX folder '{Name}' already exists but could not parse its ID. Response: {Response}",
                    name, body);
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to create CX folder '{Name}'. Status: {Status}. Response: {Response}",
                    name, response.StatusCode, body);
                return null;
            }

            var json = JObject.Parse(body);
            var folderId = json["folderId"]?.ToString();
            _logger.LogInformation("Created CX folder '{Name}' with ID: {FolderId}", name, folderId ?? "unknown");
            return folderId;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError("Error creating CX folder '{Name}': {Error}", name, ex.Message);
            return null;
        }
    }

    private static string? ExtractFolderIdFromConflict(string errorBody)
    {
        var match = Regex.Match(
            errorBody,
            @"DashboardFolderId\(([0-9a-f\-]{36})\)",
            RegexOptions.IgnoreCase);

        return match.Success ? match.Groups[1].Value : null;
    }

    public void Dispose() => _httpClient.Dispose();
}
