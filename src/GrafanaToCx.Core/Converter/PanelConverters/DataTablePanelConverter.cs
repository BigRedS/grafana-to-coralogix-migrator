using GrafanaToCx.Core.Converter.Transformations;
using Newtonsoft.Json.Linq;

namespace GrafanaToCx.Core.Converter.PanelConverters;

/// <summary>
/// Converts Grafana table panels to Coralogix DataTable widgets.
///
/// Elasticsearch: raw_data → plain luceneQuery; aggregated (bucketAggs[terms]) → grouping.
/// Prometheus: promqlQuery mapped directly.
/// </summary>
public sealed class DataTablePanelConverter : IPanelConverter
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

        if (!IsElasticsearchTarget(target))
            return BuildFromPrometheus(panel, target, discoveredMetrics);

        var luceneQuery = QueryHelpers.NormalizeVariablePlaceholders(target.Value<string>("query") ?? string.Empty);
        var bucketAggs = target["bucketAggs"] as JArray ?? new JArray();
        var metrics = target["metrics"] as JArray ?? new JArray();

        var hasTermsBuckets = bucketAggs.Children<JObject>()
            .Any(b => string.Equals(b.Value<string>("type"), "terms", StringComparison.OrdinalIgnoreCase));

        var logsQuery = BuildLogsQuery(luceneQuery, bucketAggs, metrics, hasTermsBuckets);
        var columns = BuildColumns(bucketAggs, metrics, hasTermsBuckets);
        var resultsPerPage = ResolvePageSize(metrics);

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
                    ["resultsPerPage"] = resultsPerPage,
                    ["rowStyle"] = "ROW_STYLE_ONE_LINE",
                    ["dataModeType"] = "DATA_MODE_TYPE_HIGH_UNSPECIFIED",
                    ["columns"] = columns
                }
            }
        };
    }

    private static JObject BuildLogsQuery(
        string luceneQuery, JArray bucketAggs, JArray metrics, bool hasTermsBuckets)
    {
        var query = new JObject { ["filters"] = new JArray() };

        if (!string.IsNullOrWhiteSpace(luceneQuery) && luceneQuery != "*")
            query["luceneQuery"] = new JObject { ["value"] = luceneQuery };

        if (hasTermsBuckets)
        {
            var groupBys = new JArray();
            foreach (var bucket in bucketAggs.Children<JObject>())
            {
                if (!string.Equals(bucket.Value<string>("type"), "terms", StringComparison.OrdinalIgnoreCase))
                    continue;
                var field = bucket.Value<string>("field") ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(field))
                    groupBys.Add(CxFieldHelper.ToGroupByField(field));
            }

            var aggregations = new JArray();
            foreach (var metric in metrics.Children<JObject>())
            {
                var agg = BuildLogsAggregation(metric);
                if (agg == null) continue;
                aggregations.Add(new JObject
                {
                    ["id"] = metric.Value<string>("id") ?? Guid.NewGuid().ToString(),
                    ["name"] = metric.Value<string>("type") ?? "count",
                    ["isVisible"] = true,
                    ["aggregation"] = agg
                });
            }

            query["grouping"] = new JObject
            {
                ["groupBy"] = new JArray(),
                ["groupBys"] = groupBys,
                ["aggregations"] = aggregations
            };
        }

        return query;
    }

    private static JObject? BuildLogsAggregation(JObject metric)
    {
        var type = metric.Value<string>("type")?.ToLowerInvariant();
        var field = CxFieldHelper.StripLogsFieldSuffixes(metric.Value<string>("field") ?? "");
        return type switch
        {
            "count" or "raw_data" or null => new JObject { ["count"] = new JObject() },
            "sum" => new JObject { ["sum"] = new JObject { ["field"] = field } },
            "avg" => new JObject { ["average"] = new JObject { ["field"] = field } },
            "min" => new JObject { ["min"] = new JObject { ["field"] = field } },
            "max" => new JObject { ["max"] = new JObject { ["field"] = field } },
            _ => null
        };
    }

    private static JArray BuildColumns(JArray bucketAggs, JArray metrics, bool hasTermsBuckets)
    {
        var cols = new JArray();

        if (hasTermsBuckets)
        {
            foreach (var bucket in bucketAggs.Children<JObject>())
            {
                if (!string.Equals(bucket.Value<string>("type"), "terms", StringComparison.OrdinalIgnoreCase))
                    continue;
                var field = bucket.Value<string>("field") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(field)) continue;
                cols.Add(new JObject { ["field"] = CxFieldHelper.StripLogsFieldSuffixes(field) });
            }
        }

        // Coralogix API requires at least one column. Use default for raw ES data (no terms).
        if (cols.Count == 0)
            cols.Add(new JObject { ["field"] = "coralogix.text" });

        return cols;
    }

    private static int ResolvePageSize(JArray metrics)
    {
        foreach (var metric in metrics.Children<JObject>())
        {
            var size = metric["settings"]?["size"]?.ToString();
            if (int.TryParse(size, out var parsed) && parsed > 0)
                return Math.Min(parsed, 100);
        }

        return 20;
    }

    private JObject? BuildFromPrometheus(JObject panel, JObject target, ISet<string> discoveredMetrics)
    {
        var expr = target.Value<string>("expr") ?? string.Empty;
        var promql = QueryHelpers.CleanQuery(expr, discoveredMetrics);

        var metricsQuery = new JObject
        {
            ["promqlQuery"] = new JObject { ["value"] = promql },
            ["editorMode"] = "METRICS_QUERY_EDITOR_MODE_TEXT",
            ["filters"] = new JArray()
        };

        // Coralogix API requires at least one column. PromQL metrics typically expose "value".
        var defaultMetricsColumns = new JArray { new JObject { ["field"] = "value" } };

        return new JObject
        {
            ["id"] = WidgetHelpers.IdObject(),
            ["title"] = panel.Value<string>("title") is { Length: > 0 } t ? t : $"Panel #{panel.Value<int>("id")}",
            ["description"] = QueryHelpers.CleanHtml(panel.Value<string>("description") ?? string.Empty),
            ["definition"] = new JObject
            {
                ["dataTable"] = new JObject
                {
                    ["query"] = new JObject { ["metrics"] = metricsQuery },
                    ["resultsPerPage"] = 20,
                    ["rowStyle"] = "ROW_STYLE_ONE_LINE",
                    ["dataModeType"] = "DATA_MODE_TYPE_HIGH_UNSPECIFIED",
                    ["columns"] = defaultMetricsColumns
                }
            }
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
