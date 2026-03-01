using System.Text.RegularExpressions;
using GrafanaToCx.Core.Converter;
using GrafanaToCx.Core.Converter.Transformations;
using Newtonsoft.Json.Linq;

namespace GrafanaToCx.Core.Converter.PanelConverters;

public sealed class LineChartPanelConverter : IPanelConverter
{
    // PromQL always has a metric name immediately before {. LogQL never does.
    private static readonly Regex PromQlMetricNameRegex =
        new(@"\b[a-zA-Z_:][a-zA-Z0-9_:]*\{", RegexOptions.Compiled);

    // Inner aggregation function in a LogQL metric expression.
    private static readonly Regex LogQlInnerFunctionRegex =
        new(@"\b(bytes_rate|bytes_over_time|absent_over_time|count_over_time|rate|avg_over_time|min_over_time|max_over_time|first_over_time|last_over_time|quantile_over_time)\s*\(",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public JObject? Convert(JObject panel, ISet<string> discoveredMetrics, TransformationPlan? plan = null)
    {
        var targets = panel["targets"] as JArray;
        if (targets == null || targets.Count == 0)
        {
            return null;
        }

        var grafanaUnit = panel["fieldConfig"]?["defaults"]?["unit"]?.ToString() ?? "none";
        var queryDefinitions = new JArray();
        var visibleIndex = 0;

        foreach (var target in targets.Children<JObject>())
        {
            if (target.Value<bool?>("hide") == true)
            {
                continue;
            }

            var refId = target.Value<string>("refId") ?? "A";

            // Elasticsearch / OpenSearch: uses query + bucketAggs + metrics (no expr field).
            if (IsElasticsearchQuery(target))
            {
                var esDef = BuildElasticsearchQueryDefinition(target, refId, visibleIndex, grafanaUnit, panel);
                if (esDef != null)
                {
                    queryDefinitions.Add(esDef);
                    visibleIndex++;
                }
                continue;
            }

            var expr = target.Value<string>("expr");
            if (string.IsNullOrWhiteSpace(expr))
            {
                continue;
            }

            if (IsLokiQuery(target, expr))
            {
                var logDef = BuildLogQueryDefinition(expr, refId, target, visibleIndex, grafanaUnit, panel);
                if (logDef != null)
                {
                    queryDefinitions.Add(logDef);
                    visibleIndex++;
                }
                continue;
            }

            var query = QueryHelpers.CleanQuery(expr, discoveredMetrics);
            var legend = target.Value<string>("legendFormat") ?? refId;
            var seriesName = GenerateSeriesName(legend, query, refId);

            queryDefinitions.Add(new JObject
            {
                ["id"] = Guid.NewGuid().ToString(),
                ["query"] = new JObject
                {
                    ["metrics"] = new JObject
                    {
                        ["promqlQuery"] = new JObject { ["value"] = query },
                        ["filters"] = new JArray(),
                        ["editorMode"] = "METRICS_QUERY_EDITOR_MODE_TEXT",
                        ["seriesLimitType"] = "METRICS_SERIES_LIMIT_TYPE_BY_SERIES_COUNT"
                    }
                },
                ["seriesCountLimit"] = "20",
                ["unit"] = QueryHelpers.MapUnit(grafanaUnit),
                ["scaleType"] = "SCALE_TYPE_LINEAR",
                ["name"] = seriesName,
                ["isVisible"] = true,
                ["colorScheme"] = GetColorScheme(visibleIndex, panel),
                ["resolution"] = new JObject { ["bucketsPresented"] = 96 },
                ["dataModeType"] = "DATA_MODE_TYPE_HIGH_UNSPECIFIED",
                ["customUnit"] = QueryHelpers.GetCustomUnit(grafanaUnit),
                ["hashColors"] = false
            });

            visibleIndex++;
        }

        if (queryDefinitions.Count == 0)
        {
            return null;
        }

        var legendOptions = panel["options"]?["legend"] as JObject ?? new JObject();
        var placement = legendOptions.Value<string>("placement") == "right"
            ? "LEGEND_PLACEMENT_SIDE"
            : "LEGEND_PLACEMENT_AUTO";

        return new JObject
        {
            ["id"] = WidgetHelpers.IdObject(),
            ["title"] = panel.Value<string>("title") is { Length: > 0 } t ? t : $"Panel #{panel.Value<int>("id")}",
            ["description"] = QueryHelpers.CleanHtml(panel.Value<string>("description") ?? string.Empty),
            ["definition"] = new JObject
            {
                ["lineChart"] = new JObject
                {
                    ["legend"] = new JObject
                    {
                        ["isVisible"] = legendOptions.Value<bool?>("showLegend") ?? true,
                        ["columns"] = GetLegendColumns(legendOptions),
                        ["groupByQuery"] = true,
                        ["placement"] = placement
                    },
                    ["tooltip"] = new JObject
                    {
                        ["showLabels"] = true,
                        ["type"] = "TOOLTIP_TYPE_ALL"
                    },
                    ["queryDefinitions"] = queryDefinitions,
                    ["stackedLine"] = "STACKED_LINE_UNSPECIFIED",
                    ["connectNulls"] = false
                }
            }
        };
    }

    private static string GenerateSeriesName(string legendFormat, string query, string refId)
    {
        if (!string.IsNullOrWhiteSpace(legendFormat)
            && legendFormat != "__auto"
            && legendFormat != "{{}}"
            && !legendFormat.StartsWith("{{", StringComparison.Ordinal))
        {
            var name = legendFormat
                .Replace("$quantile_stat", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("$testid", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Trim('_')
                .Trim();

            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }
        }

        return QueryHelpers.DeriveSeriesNameFromQuery(query, refId);
    }

    private static JArray GetLegendColumns(JObject legendOptions)
    {
        var calcs = legendOptions["calcs"] as JArray ?? new JArray();
        var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["mean"] = "LEGEND_COLUMN_AVG",
            ["avg"] = "LEGEND_COLUMN_AVG",
            ["min"] = "LEGEND_COLUMN_MIN",
            ["max"] = "LEGEND_COLUMN_MAX",
            ["sum"] = "LEGEND_COLUMN_SUM",
            ["last"] = "LEGEND_COLUMN_LAST",
            ["first"] = "LEGEND_COLUMN_FIRST"
        };

        var result = new JArray();
        foreach (var calc in calcs.Select(c => c.ToString()))
        {
            if (mapping.TryGetValue(calc, out var mapped))
            {
                result.Add(mapped);
            }
        }

        return result;
    }

    /// <summary>
    /// Returns true when the target uses a Loki datasource or its expression is a LogQL metric query.
    /// PromQL metric expressions always have an identifier immediately before {, LogQL never does.
    /// </summary>
    private static bool IsLokiQuery(JObject target, string expr)
    {
        var dsType = target["datasource"]?["type"]?.ToString();
        if (dsType?.Equals("loki", StringComparison.OrdinalIgnoreCase) == true) return true;
        if (dsType?.Equals("prometheus", StringComparison.OrdinalIgnoreCase) == true) return false;

        // Heuristic: if the expression contains { but no PromQL metric name before it, it's LogQL.
        return expr.Contains('{') && !PromQlMetricNameRegex.IsMatch(expr);
    }

    private static JObject? BuildLogQueryDefinition(
        string logqlExpr, string refId, JObject target,
        int visibleIndex, string grafanaUnit, JObject panel)
    {
        var luceneQuery = QueryHelpers.NormalizeVariablePlaceholders(LogqlToLuceneConverter.Convert(logqlExpr));
        var groupByFields = LogqlToLuceneConverter.ExtractGroupByFields(logqlExpr);
        var aggregation = BuildLogsAggregation(logqlExpr);

        var legend = target.Value<string>("legendFormat") ?? refId;
        var seriesName = GenerateSeriesName(legend, logqlExpr, refId);

        var logsQuery = BuildLineChartLogsQuery(luceneQuery, aggregation, groupByFields);

        return BuildQueryDefinition(logsQuery, visibleIndex, grafanaUnit, seriesName, panel);
    }

    /// <summary>
    /// Maps the innermost LogQL range aggregation to a Coralogix logs aggregation object.
    /// bytes_rate / bytes_over_time → sum of __size__
    /// everything else (rate, count_over_time, …) → count
    /// </summary>
    private static JObject BuildLogsAggregation(string logqlExpr)
    {
        var inner = LogQlInnerFunctionRegex.Match(logqlExpr);
        if (inner.Success)
        {
            var fn = inner.Groups[1].Value.ToLowerInvariant();
            if (fn is "bytes_rate" or "bytes_over_time")
            {
                return new JObject
                {
                    ["sum"] = new JObject { ["field"] = "__size__" }
                };
            }
        }

        return new JObject { ["count"] = new JObject() };
    }

    private static bool IsElasticsearchQuery(JObject target)
    {
        var dsType = target["datasource"]?["type"]?.ToString();
        if (dsType?.Equals("elasticsearch", StringComparison.OrdinalIgnoreCase) == true ||
            dsType?.Equals("opensearch", StringComparison.OrdinalIgnoreCase) == true)
            return true;

        // Fallback heuristic: has bucketAggs (ES format) but no expr (PromQL/LogQL format).
        return target["bucketAggs"] != null && target["expr"] == null;
    }

    private static JObject? BuildElasticsearchQueryDefinition(
        JObject target, string refId, int visibleIndex, string grafanaUnit, JObject panel)
    {
        var luceneQuery = QueryHelpers.NormalizeVariablePlaceholders(target.Value<string>("query") ?? string.Empty);

        // Extract group-by fields from terms bucketAggs (date_histogram is the time axis, skip it).
        var groupByFields = new List<string>();
        var bucketAggs = target["bucketAggs"] as JArray ?? new JArray();
        foreach (var bucket in bucketAggs.Children<JObject>())
        {
            if (bucket.Value<string>("type")?.Equals("terms", StringComparison.OrdinalIgnoreCase) == true)
            {
                var field = bucket.Value<string>("field");
                if (!string.IsNullOrWhiteSpace(field))
                    groupByFields.Add(field);
            }
        }

        var metrics = target["metrics"] as JArray ?? new JArray();
        var aggregation = BuildElasticsearchAggregation(metrics);

        var alias = target.Value<string>("alias");
        var seriesName = !string.IsNullOrWhiteSpace(alias) ? alias
            : panel.Value<string>("title") is { Length: > 0 } t ? t
            : refId;

        var logsQuery = BuildLineChartLogsQuery(luceneQuery, aggregation, groupByFields);

        return BuildQueryDefinition(logsQuery, visibleIndex, grafanaUnit, seriesName, panel);
    }

    private static JObject BuildElasticsearchAggregation(JArray metrics)
    {
        var firstMetric = metrics.Children<JObject>().FirstOrDefault();
        var type = firstMetric?.Value<string>("type")?.ToLowerInvariant();

        return type switch
        {
            "count" or null => new JObject { ["count"] = new JObject() },
            "sum"  => BuildFieldAggregation("sum",     firstMetric!),
            "avg"  => BuildFieldAggregation("average", firstMetric!),
            "min"  => BuildFieldAggregation("min",     firstMetric!),
            "max"  => BuildFieldAggregation("max",     firstMetric!),
            _      => new JObject { ["count"] = new JObject() }
        };
    }

    private static JObject BuildFieldAggregation(string aggType, JObject metric)
    {
        var field = metric.Value<string>("field") ?? "";
        return new JObject
        {
            [aggType] = new JObject { ["field"] = field }
        };
    }

    /// <summary>
    /// Builds the "logs" query object for a lineChart queryDefinition, matching the CX API contract:
    ///   aggregations  — array (not singular aggregation)
    ///   groupBy       — always empty array (legacy field)
    ///   groupBys      — keypath/scope objects derived from field names
    ///   luceneQuery   — included only when there is a non-trivial filter
    /// </summary>
    private static JObject BuildLineChartLogsQuery(
        string luceneQuery, JObject aggregation, IReadOnlyList<string> groupByFields)
    {
        var logsQuery = new JObject
        {
            ["groupBy"] = new JArray(),
            ["aggregations"] = new JArray { aggregation },
            ["filters"] = new JArray(),
            ["groupBys"] = new JArray(groupByFields.Select(f => CxFieldHelper.ToGroupByField(f)))
        };

        if (!string.IsNullOrWhiteSpace(luceneQuery) && luceneQuery != "*")
            logsQuery["luceneQuery"] = new JObject { ["value"] = luceneQuery };

        return logsQuery;
    }

    private static JObject BuildQueryDefinition(
        JObject logsQuery, int visibleIndex, string grafanaUnit, string seriesName, JObject panel)
    {
        return new JObject
        {
            ["id"] = Guid.NewGuid().ToString(),
            ["query"] = new JObject { ["logs"] = logsQuery },
            ["seriesCountLimit"] = "20",
            ["unit"] = QueryHelpers.MapUnit(grafanaUnit),
            ["scaleType"] = "SCALE_TYPE_LINEAR",
            ["name"] = seriesName,
            ["isVisible"] = true,
            ["colorScheme"] = GetColorScheme(visibleIndex, panel),
            ["resolution"] = new JObject { ["bucketsPresented"] = 96 },
            ["dataModeType"] = "DATA_MODE_TYPE_HIGH_UNSPECIFIED",
            ["customUnit"] = QueryHelpers.GetCustomUnit(grafanaUnit),
            ["hashColors"] = false
        };
    }

    private static string GetColorScheme(int index, JObject panel)
    {
        var overrides = panel["fieldConfig"]?["overrides"] as JArray;
        if (overrides != null)
        {
            foreach (var matcherText in overrides
                         .Children<JObject>()
                         .Select(o => o["matcher"]?["options"]?.ToString() ?? string.Empty))
            {
                var lower = matcherText.ToLowerInvariant();
                if (lower is "error" or "errors" or "failed" or "failure")
                {
                    return "negative";
                }
            }
        }

        var schemes = new[] { "classic", "cold", "severity", "classic" };
        return schemes[index % schemes.Length];
    }
}
