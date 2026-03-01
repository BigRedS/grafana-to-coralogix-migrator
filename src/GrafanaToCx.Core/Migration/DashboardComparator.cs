using Newtonsoft.Json.Linq;

namespace GrafanaToCx.Core.Migration;

public sealed record WidgetVerificationResult(
    string PanelTitle,
    string GrafanaType,
    string? ActualCxType,
    bool TypeMatches,
    bool HasValidQuery,
    string Notes)
{
    public bool IsWorking => TypeMatches && HasValidQuery;
}

public sealed record DashboardComparisonResult(
    string DashboardTitle,
    IReadOnlyList<WidgetVerificationResult> Widgets)
{
    public int Total => Widgets.Count;
    public int Working => Widgets.Count(w => w.IsWorking);
    public double Coverage => Total > 0 ? (double)Working / Total * 100.0 : 0.0;
    public bool Passed => Coverage >= 80.0;
}

/// <summary>
/// Compares a Grafana source dashboard against its Coralogix conversion widget-by-widget.
/// A widget is "working" when:
///   1. Its CX widget type matches the expected type for that Grafana panel type.
///   2. Its query/content is non-empty and structurally valid.
/// </summary>
public static class DashboardComparator
{
    public static DashboardComparisonResult Compare(JObject grafanaDashboard, JObject cxDashboard)
    {
        var dashboardTitle = cxDashboard.Value<string>("name")
                             ?? grafanaDashboard.Value<string>("title")
                             ?? "Unknown";

        var grafana = grafanaDashboard["dashboard"] as JObject ?? grafanaDashboard;

        var sourcePanels = grafana["panels"]?
            .Children<JObject>()
            .SelectMany(p => ExpandPanel(p))
            .Where(p => !string.Equals(p.Value<string>("type"), "row", StringComparison.OrdinalIgnoreCase))
            .ToList() ?? [];

        var cxWidgets = ExtractCxWidgets(cxDashboard);
        var usedWidgetIndices = new HashSet<int>();
        var results = new List<WidgetVerificationResult>();

        foreach (var panel in sourcePanels)
        {
            var panelTitle = panel.Value<string>("title") ?? $"Panel #{panel.Value<int>("id")}";
            var grafanaType = panel.Value<string>("type") ?? "unknown";
            var expectedCxType = GetExpectedCxType(grafanaType);

            JObject? matchedWidget = null;
            for (var idx = 0; idx < cxWidgets.Count; idx++)
            {
                if (usedWidgetIndices.Contains(idx)) continue;
                if (string.Equals(
                        cxWidgets[idx].Value<string>("title"), panelTitle,
                        StringComparison.OrdinalIgnoreCase))
                {
                    matchedWidget = cxWidgets[idx];
                    usedWidgetIndices.Add(idx);
                    break;
                }
            }

            WidgetVerificationResult result;
            if (matchedWidget == null)
            {
                result = new WidgetVerificationResult(
                    panelTitle, grafanaType, null, false, false,
                    "No widget with matching title found");
            }
            else
            {
                var definition = matchedWidget["definition"] as JObject;
                var actualCxType = definition?.Properties().FirstOrDefault()?.Name ?? "(none)";
                var typeMatches = string.Equals(
                    actualCxType, expectedCxType, StringComparison.OrdinalIgnoreCase);
                var hasQuery = HasValidQuery(matchedWidget, actualCxType);

                var notes = (typeMatches, hasQuery) switch
                {
                    (true, true)   => $"type={actualCxType}",
                    (false, _)     => $"expected={expectedCxType}, got={actualCxType}",
                    (true, false)  => $"type={actualCxType} but query is empty/missing"
                };

                result = new WidgetVerificationResult(
                    panelTitle, grafanaType, actualCxType, typeMatches, hasQuery, notes);
            }

            results.Add(result);
        }

        return new DashboardComparisonResult(dashboardTitle, results);
    }

    /// <summary>
    /// Flattens collapsed row panels — Grafana sometimes stores child panels
    /// inside the row's own "panels" array instead of at the top level.
    /// </summary>
    private static IEnumerable<JObject> ExpandPanel(JObject panel)
    {
        if (string.Equals(panel.Value<string>("type"), "row", StringComparison.OrdinalIgnoreCase))
        {
            var children = panel["panels"] as JArray;
            if (children != null && children.Count > 0)
                return children.Children<JObject>();

            return [panel];
        }

        return [panel];
    }

    public static List<JObject> ExtractCxWidgets(JObject cxDashboard)
    {
        var result = new List<JObject>();
        var sections = cxDashboard["layout"]?["sections"] as JArray ?? [];
        foreach (var section in sections.Children<JObject>())
        {
            var rows = section["rows"] as JArray ?? [];
            foreach (var row in rows.Children<JObject>())
            {
                var widgets = row["widgets"] as JArray ?? [];
                foreach (var widget in widgets.Children<JObject>())
                    result.Add(widget);
            }
        }

        return result;
    }

    /// <summary>
    /// Maps a Grafana panel type to the expected Coralogix widget definition key.
    /// </summary>
    public static string GetExpectedCxType(string grafanaType) =>
        grafanaType.ToLowerInvariant() switch
        {
            "stat" or "singlestat" or "gauge" or "bargauge" => "gauge",
            "text"                                           => "markdown",
            "table" or "logs"                                => "dataTable",
            "piechart"                                       => "pieChart",
            "barchart"                                       => "barChart",
            _                                                => "lineChart"
        };

    /// <summary>
    /// Returns true when the widget has a structurally non-empty, valid query or content.
    /// </summary>
    public static bool HasValidQuery(JObject widget, string cxType)
    {
        var definition = widget["definition"] as JObject;
        if (definition == null) return false;

        return cxType.ToLowerInvariant() switch
        {
            "linechart" => CheckLineChartQuery(definition),
            "gauge"     => CheckGaugeQuery(definition),
            "markdown"  => CheckMarkdownContent(definition),
            "datatable" => CheckDataTableQuery(definition),
            "piechart"  => CheckPieChartQuery(definition),
            "barchart"  => CheckBarChartQuery(definition),
            _           => false
        };
    }

    private static bool CheckLineChartQuery(JObject definition)
    {
        var queryDefs = definition["lineChart"]?["queryDefinitions"] as JArray;
        if (queryDefs == null || queryDefs.Count == 0) return false;

        foreach (var qd in queryDefs.Children<JObject>())
        {
            var promql = qd["query"]?["metrics"]?["promqlQuery"]?["value"]?.ToString();
            if (IsRealPromql(promql)) return true;

            // Logs-based line chart (ES/Loki aggregation) — valid if aggregations exist.
            var logsAgg = qd["query"]?["logs"]?["aggregations"] as JArray;
            if (logsAgg != null && logsAgg.Count > 0) return true;
        }

        return false;
    }

    private static bool CheckGaugeQuery(JObject definition)
    {
        var promql = definition["gauge"]?["query"]?["metrics"]?["promqlQuery"]?["value"]?.ToString();
        if (IsRealPromql(promql)) return true;

        var logsAgg = definition["gauge"]?["query"]?["logs"]?["logsAggregation"] as JObject;
        return logsAgg != null;
    }

    /// <summary>
    /// Returns true only for a non-empty PromQL value that is not the "up" fallback
    /// emitted by the converter when no real expr was found for the panel.
    /// </summary>
    private static bool IsRealPromql(string? promql) =>
        !string.IsNullOrWhiteSpace(promql) &&
        !string.Equals(promql.Trim(), "up", StringComparison.OrdinalIgnoreCase);

    private static bool CheckMarkdownContent(JObject definition)
    {
        var text = definition["markdown"]?["markdownText"]?.ToString();
        return !string.IsNullOrWhiteSpace(text);
    }

    private static bool CheckDataTableQuery(JObject definition)
    {
        // A dataTable (logs panel) is valid with or without a lucene filter —
        // an empty filter means "show all logs", which is intentional.
        // Just verify the query.logs object was emitted by the converter.
        var logsQuery = definition["dataTable"]?["query"]?["logs"] as JObject;
        var metricsQuery = definition["dataTable"]?["query"]?["metrics"] as JObject;
        return logsQuery != null || metricsQuery != null;
    }

    private static bool CheckPieChartQuery(JObject definition)
    {
        var query = definition["pieChart"]?["query"] as JObject;
        if (query == null) return false;
        return query["logs"] is JObject || query["metrics"] is JObject || query["dataPrime"] is JObject || query["dataprime"] is JObject;
    }

    private static bool CheckBarChartQuery(JObject definition)
    {
        var query = definition["barChart"]?["query"] as JObject;
        if (query == null) return false;
        return query["logs"] is JObject || query["metrics"] is JObject || query["dataPrime"] is JObject || query["dataprime"] is JObject;
    }
}
