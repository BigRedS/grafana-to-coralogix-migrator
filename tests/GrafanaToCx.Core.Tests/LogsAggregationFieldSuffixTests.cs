using GrafanaToCx.Core.Converter;
using GrafanaToCx.Core.Converter.PanelConverters;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;

namespace GrafanaToCx.Core.Tests;

public class LogsAggregationFieldSuffixTests
{
    private static GrafanaToCxConverter CreateConverter() =>
        new(NullLogger<GrafanaToCxConverter>.Instance);

    private static string BuildDashboardJson(JObject panel)
    {
        return new JObject
        {
            ["dashboard"] = new JObject
            {
                ["title"] = "Test Dashboard",
                ["panels"] = new JArray { panel }
            }
        }.ToString();
    }

    private static JObject? GetFirstWidget(JObject dashboard)
    {
        var sections = dashboard["layout"]?["sections"] as JArray;
        var firstSection = sections?[0] as JObject;
        var rows = firstSection?["rows"] as JArray;
        var firstRow = rows?[0] as JObject;
        var widgets = firstRow?["widgets"] as JArray;
        return widgets?[0] as JObject;
    }

    [Fact]
    public void StripLogsFieldSuffixes_StripsKeywordSuffix()
    {
        Assert.Equal("status", CxFieldHelper.StripLogsFieldSuffixes("status.keyword"));
    }

    [Fact]
    public void StripLogsFieldSuffixes_StripsKeywordSuffix_CaseInsensitive()
    {
        Assert.Equal("status", CxFieldHelper.StripLogsFieldSuffixes("status.KEYWORD"));
        Assert.Equal("status", CxFieldHelper.StripLogsFieldSuffixes("status.Keyword"));
    }

    [Fact]
    public void StripLogsFieldSuffixes_StripsNumericSuffix()
    {
        Assert.Equal("duration", CxFieldHelper.StripLogsFieldSuffixes("duration.numeric"));
    }

    [Fact]
    public void StripLogsFieldSuffixes_StripsNumericSuffix_CaseInsensitive()
    {
        Assert.Equal("duration", CxFieldHelper.StripLogsFieldSuffixes("duration.NUMERIC"));
        Assert.Equal("duration", CxFieldHelper.StripLogsFieldSuffixes("duration.Numeric"));
    }

    [Fact]
    public void StripLogsFieldSuffixes_LeavesUnsuffixedFieldUnchanged()
    {
        Assert.Equal("severity", CxFieldHelper.StripLogsFieldSuffixes("severity"));
        Assert.Equal("kubernetes.namespace_name", CxFieldHelper.StripLogsFieldSuffixes("kubernetes.namespace_name"));
    }

    [Fact]
    public void StripLogsFieldSuffixes_HandlesEmptyAndNull()
    {
        Assert.Equal("", CxFieldHelper.StripLogsFieldSuffixes(""));
        Assert.Equal("", CxFieldHelper.StripLogsFieldSuffixes(null!));
    }

    [Fact]
    public void DataTable_LogsAggregation_StripsKeywordFromSumField()
    {
        var converter = CreateConverter();
        var panel = new JObject
        {
            ["id"] = 1,
            ["title"] = "Table",
            ["type"] = "table",
            ["targets"] = new JArray
            {
                new JObject
                {
                    ["refId"] = "A",
                    ["query"] = "*",
                    ["bucketAggs"] = new JArray
                    {
                        new JObject { ["type"] = "terms", ["field"] = "status.keyword" }
                    },
                    ["metrics"] = new JArray
                    {
                        new JObject { ["type"] = "sum", ["field"] = "duration.keyword" }
                    }
                }
            }
        };

        var result = converter.ConvertToJObject(BuildDashboardJson(panel));
        var widget = GetFirstWidget(result);
        Assert.NotNull(widget);

        var agg = widget["definition"]?["dataTable"]?["query"]?["logs"]?["grouping"]?["aggregations"]?[0] as JObject;
        Assert.NotNull(agg);
        var sumField = agg["aggregation"]?["sum"]?["field"]?.ToString();
        Assert.Equal("duration", sumField);
    }

    [Fact]
    public void DataTable_LogsAggregation_StripsNumericFromAvgField()
    {
        var converter = CreateConverter();
        var panel = new JObject
        {
            ["id"] = 1,
            ["title"] = "Table",
            ["type"] = "table",
            ["targets"] = new JArray
            {
                new JObject
                {
                    ["refId"] = "A",
                    ["query"] = "*",
                    ["bucketAggs"] = new JArray
                    {
                        new JObject { ["type"] = "terms", ["field"] = "host" }
                    },
                    ["metrics"] = new JArray
                    {
                        new JObject { ["type"] = "avg", ["field"] = "latency.numeric" }
                    }
                }
            }
        };

        var result = converter.ConvertToJObject(BuildDashboardJson(panel));
        var widget = GetFirstWidget(result);
        Assert.NotNull(widget);

        var agg = widget["definition"]?["dataTable"]?["query"]?["logs"]?["grouping"]?["aggregations"]?[0] as JObject;
        Assert.NotNull(agg);
        var avgField = agg["aggregation"]?["average"]?["field"]?.ToString();
        Assert.Equal("latency", avgField);
    }

    [Fact]
    public void DataTable_LogsAggregation_LeavesUnsuffixedFieldUnchanged()
    {
        var converter = CreateConverter();
        var panel = new JObject
        {
            ["id"] = 1,
            ["title"] = "Table",
            ["type"] = "table",
            ["targets"] = new JArray
            {
                new JObject
                {
                    ["refId"] = "A",
                    ["query"] = "*",
                    ["bucketAggs"] = new JArray
                    {
                        new JObject { ["type"] = "terms", ["field"] = "severity" }
                    },
                    ["metrics"] = new JArray
                    {
                        new JObject { ["type"] = "sum", ["field"] = "bytes" }
                    }
                }
            }
        };

        var result = converter.ConvertToJObject(BuildDashboardJson(panel));
        var widget = GetFirstWidget(result);
        Assert.NotNull(widget);

        var agg = widget["definition"]?["dataTable"]?["query"]?["logs"]?["grouping"]?["aggregations"]?[0] as JObject;
        Assert.NotNull(agg);
        var sumField = agg["aggregation"]?["sum"]?["field"]?.ToString();
        Assert.Equal("bytes", sumField);
    }

    [Fact]
    public void DataTable_LogsAggregation_StripsKeywordFromColumnField()
    {
        var converter = CreateConverter();
        var panel = new JObject
        {
            ["id"] = 1,
            ["title"] = "Table",
            ["type"] = "table",
            ["targets"] = new JArray
            {
                new JObject
                {
                    ["refId"] = "A",
                    ["query"] = "*",
                    ["bucketAggs"] = new JArray
                    {
                        new JObject { ["type"] = "terms", ["field"] = "status.keyword" }
                    },
                    ["metrics"] = new JArray { new JObject { ["type"] = "count" } }
                }
            }
        };

        var result = converter.ConvertToJObject(BuildDashboardJson(panel));
        var widget = GetFirstWidget(result);
        Assert.NotNull(widget);

        var columns = widget["definition"]?["dataTable"]?["columns"] as JArray;
        Assert.NotNull(columns);
        Assert.Single(columns);
        Assert.Equal("status", columns[0]?["field"]?.ToString());
    }

    [Fact]
    public void BarChart_LogsAggregation_StripsKeywordFromField()
    {
        var converter = CreateConverter();
        var panel = new JObject
        {
            ["id"] = 1,
            ["title"] = "Bar",
            ["type"] = "barchart",
            ["targets"] = new JArray
            {
                new JObject
                {
                    ["refId"] = "A",
                    ["query"] = "*",
                    ["bucketAggs"] = new JArray
                    {
                        new JObject { ["type"] = "terms", ["field"] = "app.keyword" }
                    },
                    ["metrics"] = new JArray
                    {
                        new JObject { ["type"] = "sum", ["field"] = "count.keyword" }
                    }
                }
            }
        };

        var result = converter.ConvertToJObject(BuildDashboardJson(panel));
        var widget = GetFirstWidget(result);
        Assert.NotNull(widget);

        var agg = widget["definition"]?["barChart"]?["query"]?["logs"]?["aggregation"] as JObject;
        Assert.NotNull(agg);
        var sumField = agg["sum"]?["field"]?.ToString();
        Assert.Equal("count", sumField);
    }

    [Fact]
    public void Gauge_LogsAggregation_StripsNumericFromField()
    {
        var converter = CreateConverter();
        var panel = new JObject
        {
            ["id"] = 1,
            ["title"] = "Gauge",
            ["type"] = "gauge",
            ["targets"] = new JArray
            {
                new JObject
                {
                    ["refId"] = "A",
                    ["query"] = "*",
                    ["datasource"] = new JObject { ["type"] = "elasticsearch" },
                    ["metrics"] = new JArray
                    {
                        new JObject { ["type"] = "avg", ["field"] = "response_time.numeric" }
                    }
                }
            }
        };

        var result = converter.ConvertToJObject(BuildDashboardJson(panel));
        var widget = GetFirstWidget(result);
        Assert.NotNull(widget);

        var agg = widget["definition"]?["gauge"]?["query"]?["logs"]?["logsAggregation"] as JObject;
        Assert.NotNull(agg);
        var avgField = agg["average"]?["field"]?.ToString();
        Assert.Equal("response_time", avgField);
    }

    [Fact]
    public void PieChart_LogsAggregation_StripsKeywordFromField()
    {
        var converter = CreateConverter();
        var panel = new JObject
        {
            ["id"] = 1,
            ["title"] = "Pie",
            ["type"] = "piechart",
            ["targets"] = new JArray
            {
                new JObject
                {
                    ["refId"] = "A",
                    ["query"] = "*",
                    ["bucketAggs"] = new JArray
                    {
                        new JObject { ["type"] = "terms", ["field"] = "level.keyword" }
                    },
                    ["metrics"] = new JArray
                    {
                        new JObject { ["type"] = "sum", ["field"] = "value.keyword" }
                    }
                }
            }
        };

        var result = converter.ConvertToJObject(BuildDashboardJson(panel));
        var widget = GetFirstWidget(result);
        Assert.NotNull(widget);

        var agg = widget["definition"]?["pieChart"]?["query"]?["logs"]?["aggregation"] as JObject;
        Assert.NotNull(agg);
        var sumField = agg["sum"]?["field"]?.ToString();
        Assert.Equal("value", sumField);
    }

    [Fact]
    public void MetricsConversion_Unchanged_PromQLExprPreserved()
    {
        var converter = CreateConverter();
        var panel = new JObject
        {
            ["id"] = 1,
            ["title"] = "Metrics",
            ["type"] = "timeseries",
            ["targets"] = new JArray
            {
                new JObject
                {
                    ["refId"] = "A",
                    ["expr"] = "sum(rate(http_requests_total[5m]))"
                }
            }
        };

        var result = converter.ConvertToJObject(BuildDashboardJson(panel));
        var widget = GetFirstWidget(result);
        Assert.NotNull(widget);

        // Metrics path: query uses metrics (PromQL), not logs; no field suffix stripping applies.
        var query = widget["definition"]?["lineChart"]?["queryDefinitions"]?[0]?["query"] as JObject;
        Assert.NotNull(query);
        Assert.NotNull(query["metrics"]);
        Assert.Null(query["logs"]);

        var promql = query["metrics"]?["promqlQuery"]?["value"]?.ToString();
        Assert.NotNull(promql);
        Assert.Contains("http_requests_total", promql);
    }

    [Fact]
    public void MetricsConversion_Unchanged_DataTablePrometheusPath()
    {
        var converter = CreateConverter();
        var panel = new JObject
        {
            ["id"] = 1,
            ["title"] = "Table",
            ["type"] = "table",
            ["targets"] = new JArray
            {
                new JObject
                {
                    ["refId"] = "A",
                    ["expr"] = "up",
                    ["datasource"] = new JObject { ["type"] = "prometheus" }
                }
            }
        };

        var result = converter.ConvertToJObject(BuildDashboardJson(panel));
        var widget = GetFirstWidget(result);
        Assert.NotNull(widget);

        var promql = widget["definition"]?["dataTable"]?["query"]?["metrics"]?["promqlQuery"]?["value"]?.ToString();
        Assert.Equal("up", promql);
    }
}
