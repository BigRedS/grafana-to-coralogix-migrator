using GrafanaToCx.Core.Converter;
using GrafanaToCx.Core.Converter.PanelConverters;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;

namespace GrafanaToCx.Core.Tests;

public class VariablePlaceholderNormalizationTests
{
    private static GrafanaToCxConverter CreateConverter() =>
        new(NullLogger<GrafanaToCxConverter>.Instance);

    private static string BuildDashboardJson(JObject panel) =>
        new JObject
        {
            ["dashboard"] = new JObject
            {
                ["title"] = "Test Dashboard",
                ["panels"] = new JArray { panel }
            }
        }.ToString();

    private static JObject? GetFirstWidget(JObject dashboard)
    {
        var sections = dashboard["layout"]?["sections"] as JArray;
        var firstSection = sections?[0] as JObject;
        var rows = firstSection?["rows"] as JArray;
        var firstRow = rows?[0] as JObject;
        var widgets = firstRow?["widgets"] as JArray;
        return widgets?[0] as JObject;
    }

    private static string? GetLuceneQuery(JObject? widget)
    {
        return widget?["definition"]?["dataTable"]?["query"]?["logs"]?["luceneQuery"]?["value"]?.ToString()
            ?? widget?["definition"]?["barChart"]?["query"]?["logs"]?["luceneQuery"]?["value"]?.ToString()
            ?? widget?["definition"]?["pieChart"]?["query"]?["logs"]?["luceneQuery"]?["value"]?.ToString()
            ?? widget?["definition"]?["gauge"]?["query"]?["logs"]?["luceneQuery"]?["value"]?.ToString()
            ?? widget?["definition"]?["lineChart"]?["queryDefinitions"]?[0]?["query"]?["logs"]?["luceneQuery"]?["value"]?.ToString();
    }

    private static string? GetPromqlQuery(JObject? widget)
    {
        return widget?["definition"]?["dataTable"]?["query"]?["metrics"]?["promqlQuery"]?["value"]?.ToString()
            ?? widget?["definition"]?["barChart"]?["query"]?["metrics"]?["promqlQuery"]?["value"]?.ToString()
            ?? widget?["definition"]?["pieChart"]?["query"]?["metrics"]?["promqlQuery"]?["value"]?.ToString()
            ?? widget?["definition"]?["gauge"]?["query"]?["metrics"]?["promqlQuery"]?["value"]?.ToString()
            ?? widget?["definition"]?["lineChart"]?["queryDefinitions"]?[0]?["query"]?["metrics"]?["promqlQuery"]?["value"]?.ToString();
    }

    [Fact]
    public void NormalizeVariablePlaceholders_ConvertsPlainVarsToBraced()
    {
        var input = "app:EmailVerificationV2 AND name:VerificationComplete AND emailVerification.senderDomain.keyword:$sender_domain AND emailVerification.ip.keyword:$ip";
        var result = QueryHelpers.NormalizeVariablePlaceholders(input);
        Assert.Equal(
            "app:EmailVerificationV2 AND name:VerificationComplete AND emailVerification.senderDomain.keyword:${sender_domain} AND emailVerification.ip.keyword:${ip}",
            result);
    }

    [Fact]
    public void NormalizeVariablePlaceholders_LeavesAlreadyBracedUnchanged()
    {
        var input = "app:test AND field:${sender_domain} AND other:${ip}";
        var result = QueryHelpers.NormalizeVariablePlaceholders(input);
        Assert.Equal(input, result);
    }

    [Fact]
    public void NormalizeVariablePlaceholders_PromQLPodRegex()
    {
        var input = "rate(http_requests_total{pod=~$pod}[5m])";
        var result = QueryHelpers.NormalizeVariablePlaceholders(input);
        Assert.Equal("rate(http_requests_total{pod=~${pod}}[5m])", result);
    }

    [Fact]
    public void NormalizeVariablePlaceholders_SkipsGrafanaBuiltIns()
    {
        var input = "rate(metric{$__rate_interval})[$__range]";
        var result = QueryHelpers.NormalizeVariablePlaceholders(input);
        Assert.Equal("rate(metric{$__rate_interval})[$__range]", result);
    }

    [Fact]
    public void NormalizeVariablePlaceholders_LeavesEmptyUnchanged()
    {
        Assert.Equal("", QueryHelpers.NormalizeVariablePlaceholders(""));
    }

    [Fact]
    public void DataTable_ES_LogsQuery_NormalizesPlaceholders()
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
                    ["query"] = "app:EmailVerificationV2 AND name:VerificationComplete AND emailVerification.senderDomain.keyword:$sender_domain AND emailVerification.ip.keyword:$ip",
                    ["datasource"] = new JObject { ["type"] = "elasticsearch" },
                    ["bucketAggs"] = new JArray(),
                    ["metrics"] = new JArray { new JObject { ["type"] = "count" } }
                }
            }
        };

        var result = converter.ConvertToJObject(BuildDashboardJson(panel));
        var widget = GetFirstWidget(result);
        Assert.NotNull(widget);

        var luceneQuery = GetLuceneQuery(widget);
        Assert.NotNull(luceneQuery);
        Assert.Contains("${sender_domain}", luceneQuery);
        Assert.Contains("${ip}", luceneQuery);
        Assert.DoesNotContain(":$sender_domain", luceneQuery);
        Assert.DoesNotContain(":$ip ", luceneQuery);
    }

    [Fact]
    public void LogsPanel_Loki_LogQLConverted_NormalizesPlaceholders()
    {
        var converter = CreateConverter();
        var panel = new JObject
        {
            ["id"] = 1,
            ["title"] = "Logs",
            ["type"] = "logs",
            ["targets"] = new JArray
            {
                new JObject
                {
                    ["refId"] = "A",
                    ["expr"] = "sum(rate({app=\"nginx\", pod=~\"$pod\"}[5m]))",
                    ["datasource"] = new JObject { ["type"] = "loki" }
                }
            }
        };

        var result = converter.ConvertToJObject(BuildDashboardJson(panel));
        var widget = GetFirstWidget(result);
        Assert.NotNull(widget);

        var luceneQuery = GetLuceneQuery(widget);
        Assert.NotNull(luceneQuery);
        Assert.Contains("${pod}", luceneQuery);
    }

    [Fact]
    public void LineChart_PromQL_NormalizesVariablePlaceholders()
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
                    ["expr"] = "rate(http_requests_total{pod=~$pod}[5m])",
                    ["datasource"] = new JObject { ["type"] = "prometheus" }
                }
            }
        };

        var result = converter.ConvertToJObject(BuildDashboardJson(panel));
        var widget = GetFirstWidget(result);
        Assert.NotNull(widget);

        var promql = GetPromqlQuery(widget);
        Assert.NotNull(promql);
        Assert.Contains("${pod}", promql);
        Assert.Contains("pod=~${pod}", promql);
    }
}
