namespace GrafanaToCx.Core.Converter;

/// <summary>
/// Maps superseded Grafana panel type identifiers onto the modern type the converter
/// dispatches on. Only for types whose target shape is already what the modern converter
/// consumes — this is a routing table, not a compatibility layer.
///
/// The original identifier is kept for diagnostics, so a report still says which
/// Grafana type the panel actually used.
/// </summary>
public static class PanelTypes
{
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        // The community pie plugin superseded by the built-in piechart. Its targets carry the
        // same alias/bucketAggs/metrics/query/timeField shape the native piechart converter reads.
        ["grafana-piechart-panel"] = "piechart"
    };

    public static string Normalize(string? panelType)
    {
        if (string.IsNullOrWhiteSpace(panelType))
            return string.Empty;

        return Aliases.TryGetValue(panelType, out var canonical) ? canonical : panelType;
    }

    public static bool IsAlias(string? panelType) =>
        !string.IsNullOrWhiteSpace(panelType) && Aliases.ContainsKey(panelType);
}
