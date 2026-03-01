using GrafanaToCx.Core.Converter.Transformations;
using Newtonsoft.Json.Linq;

namespace GrafanaToCx.Core.Converter.PanelConverters;

/// <summary>
/// Converts Grafana "logs" panels to a Coralogix dataTable widget showing raw log lines.
///
/// Query translation:
///   Loki (LogQL) expr → Lucene via LogqlToLuceneConverter
///   Elasticsearch query → used as-is (already Lucene)
/// </summary>
public sealed class LogsPanelConverter : IPanelConverter
{
    // Standard CX log columns mirroring Grafana's default logs panel fields.
    private static readonly string[] DefaultColumns =
    [
        "coralogix.timestamp",
        "coralogix.metadata.severity",
        "coralogix.text",
        "coralogix.metadata.applicationName",
        "coralogix.metadata.subsystemName"
    ];

    public JObject? Convert(JObject panel, ISet<string> discoveredMetrics, TransformationPlan? plan = null)
    {
        var targets = panel["targets"] as JArray;
        if (targets == null || targets.Count == 0)
            return null;

        var target = targets
            .Children<JObject>()
            .FirstOrDefault(t => t.Value<bool?>("hide") != true);

        if (target == null)
            return null;

        var luceneQuery = QueryHelpers.NormalizeVariablePlaceholders(ExtractLuceneQuery(target));
        var columns = BuildColumns(luceneQuery);

        var logsQuery = new JObject { ["filters"] = new JArray() };
        if (!string.IsNullOrWhiteSpace(luceneQuery) && luceneQuery != "*")
            logsQuery["luceneQuery"] = new JObject { ["value"] = luceneQuery };

        var sortOrder = panel["options"]?["sortOrder"]?.ToString() ?? "Descending";
        var orderDirection = sortOrder.Equals("Ascending", StringComparison.OrdinalIgnoreCase)
            ? "ORDER_DIRECTION_ASC"
            : "ORDER_DIRECTION_DESC";

        return new JObject
        {
            ["id"] = WidgetHelpers.IdObject(),
            ["title"] = panel.Value<string>("title") is { Length: > 0 } t ? t : $"Panel #{panel.Value<int>("id")}",
            ["description"] = QueryHelpers.CleanHtml(panel.Value<string>("description") ?? string.Empty),
            ["definition"] = new JObject
            {
                ["dataTable"] = new JObject
                {
                    ["query"] = new JObject { ["logs"] = logsQuery },
                    ["resultsPerPage"] = 100,
                    ["rowStyle"] = "ROW_STYLE_ONE_LINE",
                    ["columns"] = columns,
                    ["orderBy"] = new JObject
                    {
                        ["field"] = "coralogix.timestamp",
                        ["orderDirection"] = orderDirection
                    },
                    ["dataModeType"] = "DATA_MODE_TYPE_HIGH_UNSPECIFIED"
                }
            }
        };
    }

    private static string ExtractLuceneQuery(JObject target)
    {
        // Elasticsearch / OpenSearch datasource: query field is already Lucene.
        var dsType = target["datasource"]?["type"]?.ToString();
        if (dsType?.Equals("elasticsearch", StringComparison.OrdinalIgnoreCase) == true ||
            dsType?.Equals("opensearch", StringComparison.OrdinalIgnoreCase) == true)
        {
            return target.Value<string>("query") ?? string.Empty;
        }

        // Loki datasource: translate LogQL expr to Lucene.
        var expr = target.Value<string>("expr") ?? string.Empty;
        return string.IsNullOrWhiteSpace(expr) ? string.Empty : LogqlToLuceneConverter.Convert(expr);
    }

    private static JArray BuildColumns(string luceneQuery)
    {
        var columns = new JArray();
        foreach (var field in DefaultColumns)
            columns.Add(new JObject { ["field"] = field });
        return columns;
    }
}
