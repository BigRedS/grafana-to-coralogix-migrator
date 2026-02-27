namespace GrafanaToCx.Core.Migration;

public static class RegionMapper
{
    private static readonly Dictionary<string, string> RegionBaseUrls = new(StringComparer.OrdinalIgnoreCase)
    {
        ["eu1"] = "https://api.coralogix.com",
        ["eu2"] = "https://api.eu2.coralogix.com",
        ["us1"] = "https://api.coralogix.us",
        ["us2"] = "https://api.us2.coralogix.com",
        ["ap1"] = "https://api.ap1.coralogix.com",
        ["ap2"] = "https://api.ap2.coralogix.com",
        ["ap3"] = "https://api.ap3.coralogix.com",
        ["in1"] = "https://api.app.coralogix.in",
    };

    private static string GetBaseUrl(string region)
    {
        if (RegionBaseUrls.TryGetValue(region, out var url))
            return url;

        throw new ArgumentException(
            $"Unknown Coralogix region '{region}'. Valid regions: {string.Join(", ", RegionBaseUrls.Keys)}");
    }

    /// <summary>
    /// Returns the Coralogix REST API base URL for the given region.
    /// Example: eu1 → https://api.coralogix.com/mgmt/openapi/latest
    /// </summary>
    public static string Resolve(string region) => $"{GetBaseUrl(region)}/mgmt/openapi/latest";

    /// <summary>
    /// Returns the embedded Grafana API base URL for the given region.
    /// Example: eu1 → https://api.coralogix.com/grafana
    /// </summary>
    public static string ResolveGrafana(string region) => $"{GetBaseUrl(region)}/grafana";
}
