using GrafanaToCx.Core.Converter.Transformations;
using Newtonsoft.Json.Linq;

namespace GrafanaToCx.Core.Converter.PanelConverters;

/// <summary>
/// Converts Grafana piechart panels to Coralogix PieChart widgets.
///
/// Supports both Elasticsearch (logs query with groupNamesFields) and
/// Prometheus (metrics query with promqlQuery).
///
/// Multi-target Elasticsearch panels use the first target's Lucene query;
/// the shared groupBy field preserves the grouping dimension.
/// When a transformation plan provides ConsolidatedQueryPayload (e.g. from
/// PieMultiQueryConsolidationPlanner), that payload is used instead.
/// </summary>
public sealed class PieChartPanelConverter : IPanelConverter
{
    public JObject? Convert(JObject panel, ISet<string> discoveredMetrics, TransformationPlan? plan = null)
    {
        var targets = panel["targets"] as JArray;
        if (targets == null || targets.Count == 0)
            return null;

        var target = targets.Children<JObject>()
            .FirstOrDefault(t => t.Value<bool?>("hide") != true);
        if (target == null)
            return null;

        var grafanaUnit = panel["fieldConfig"]?["defaults"]?["unit"]?.ToString() ?? "none";
        var legendOptions = panel["options"]?["legend"] as JObject ?? new JObject();

        JObject pieQuery;
        if (plan is TransformationPlan.Success success && success.ConsolidatedQueryPayload != null)
        {
            pieQuery = success.ConsolidatedQueryPayload;
        }
        else
        {
            pieQuery = IsElasticsearchTarget(target)
                ? BuildLogsQuery(target)
                : BuildMetricsQuery(target, discoveredMetrics);
        }

        return new JObject
        {
            ["id"] = WidgetHelpers.IdObject(),
            ["title"] = panel.Value<string>("title") is { Length: > 0 } t ? t : $"Panel #{panel.Value<int>("id")}",
            ["description"] = QueryHelpers.CleanHtml(panel.Value<string>("description") ?? string.Empty),
            ["definition"] = new JObject
            {
                ["pieChart"] = new JObject
                {
                    ["query"] = pieQuery,
                    ["maxSlicesPerChart"] = 24,
                    ["minSlicePercentage"] = 0,
                    ["showLegend"] = legendOptions.Value<bool?>("showLegend") ?? true,
                    ["colorScheme"] = "classic",
                    ["unit"] = QueryHelpers.MapUnitForGauge(grafanaUnit),
                    ["dataModeType"] = "DATA_MODE_TYPE_HIGH_UNSPECIFIED",
                    ["stackDefinition"] = new JObject
                    {
                        ["maxSlicesPerStack"] = 8
                    },
                    ["labelDefinition"] = new JObject
                    {
                        ["labelSource"] = "LABEL_SOURCE_INNER",
                        ["isVisible"] = true,
                        ["showName"] = true,
                        ["showValue"] = true,
                        ["showPercentage"] = true
                    }
                }
            }
        };
    }

    private static JObject BuildLogsQuery(JObject target)
    {
        var groupNamesFields = new JArray();
        var bucketAggs = target["bucketAggs"] as JArray ?? new JArray();

        foreach (var bucket in bucketAggs.Children<JObject>())
        {
            if (!string.Equals(bucket.Value<string>("type"), "terms", StringComparison.OrdinalIgnoreCase))
                continue;
            var field = bucket.Value<string>("field") ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(field))
                groupNamesFields.Add(CxFieldHelper.ToGroupByField(field));
        }

        var aggregation = BuildLogsAggregation(target["metrics"] as JArray ?? new JArray());
        var luceneQuery = QueryHelpers.NormalizeVariablePlaceholders(target.Value<string>("query") ?? string.Empty);

        var logsQuery = new JObject
        {
            ["aggregation"] = aggregation,
            ["filters"] = new JArray(),
            ["groupNamesFields"] = groupNamesFields
        };

        if (!string.IsNullOrWhiteSpace(luceneQuery) && luceneQuery != "*")
            logsQuery["luceneQuery"] = new JObject { ["value"] = luceneQuery };

        return new JObject { ["logs"] = logsQuery };
    }

    private static JObject BuildMetricsQuery(JObject target, ISet<string> discoveredMetrics)
    {
        var expr = target.Value<string>("expr") ?? string.Empty;
        var promql = QueryHelpers.CleanQuery(expr, discoveredMetrics);

        return new JObject
        {
            ["metrics"] = new JObject
            {
                ["promqlQuery"] = new JObject { ["value"] = promql },
                ["aggregation"] = "AGGREGATION_LAST",
                ["editorMode"] = "METRICS_QUERY_EDITOR_MODE_TEXT",
                ["filters"] = new JArray(),
                ["groupNames"] = new JArray()
            }
        };
    }

    private static JObject BuildLogsAggregation(JArray metrics)
    {
        var first = metrics.Children<JObject>().FirstOrDefault();
        var type = first?.Value<string>("type")?.ToLowerInvariant();
        var field = CxFieldHelper.StripLogsFieldSuffixes(first?.Value<string>("field") ?? "");

        return type switch
        {
            "sum" => new JObject { ["sum"] = new JObject { ["field"] = field } },
            "avg" => new JObject { ["average"] = new JObject { ["field"] = field } },
            "min" => new JObject { ["min"] = new JObject { ["field"] = field } },
            "max" => new JObject { ["max"] = new JObject { ["field"] = field } },
            _ => new JObject { ["count"] = new JObject() }
        };
    }

    private static bool IsElasticsearchTarget(JObject target)
    {
        var dsType = target["datasource"]?["type"]?.ToString();
        if (dsType?.Equals("elasticsearch", StringComparison.OrdinalIgnoreCase) == true ||
            dsType?.Equals("opensearch", StringComparison.OrdinalIgnoreCase) == true)
            return true;

        return target["bucketAggs"] != null && target["expr"] == null;
    }
}
