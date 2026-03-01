using GrafanaToCx.Core.Converter;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;

namespace GrafanaToCx.Core.Tests;

public class TransformationPlannerTests
{
    [Fact]
    public void PieChart_MultiTarget_BooleanPredicate_ConsolidatesToDataPrime()
    {
        var converter = CreateConverter();
        var panel = new JObject
        {
            ["id"] = 1,
            ["title"] = "Email vs Non-Email",
            ["type"] = "piechart",
            ["targets"] = new JArray
            {
                new JObject
                {
                    ["refId"] = "A",
                    ["query"] = "payload.isEmail:true",
                    ["bucketAggs"] = new JArray { new JObject { ["type"] = "terms", ["field"] = "payload.isEmail" } },
                    ["metrics"] = new JArray { new JObject { ["type"] = "count" } }
                },
                new JObject
                {
                    ["refId"] = "B",
                    ["query"] = "payload.isEmail:false",
                    ["bucketAggs"] = new JArray { new JObject { ["type"] = "terms", ["field"] = "payload.isEmail" } },
                    ["metrics"] = new JArray { new JObject { ["type"] = "count" } }
                }
            }
        };

        var result = converter.ConvertToJObject(BuildDashboardJson(panel));
        var widget = GetFirstWidget(result);
        Assert.NotNull(widget);

        var query = widget["definition"]?["pieChart"]?["query"] as JObject;
        Assert.NotNull(query);
        Assert.Null(query["logs"]);
        Assert.NotNull(query["dataprime"]);

        var dataPrime = query["dataprime"]?["dataprimeQuery"]?["text"]?.ToString();
        Assert.NotNull(dataPrime);
        Assert.Contains("source logs", dataPrime);
        Assert.Contains("groupby payload.isEmail agg count()", dataPrime);
    }

    [Fact]
    public void PieChart_MultiTarget_DottedField_ConsolidatesWithFullPath()
    {
        var converter = CreateConverter();
        var panel = new JObject
        {
            ["id"] = 10,
            ["title"] = "Dotted Field Pie",
            ["type"] = "piechart",
            ["targets"] = new JArray
            {
                new JObject
                {
                    ["refId"] = "A",
                    ["query"] = "payload.isEmail:true",
                    ["bucketAggs"] = new JArray { new JObject { ["type"] = "terms", ["field"] = "payload.isEmail" } },
                    ["metrics"] = new JArray { new JObject { ["type"] = "count" } }
                },
                new JObject
                {
                    ["refId"] = "B",
                    ["query"] = "payload.isEmail:false",
                    ["bucketAggs"] = new JArray { new JObject { ["type"] = "terms", ["field"] = "payload.isEmail" } },
                    ["metrics"] = new JArray { new JObject { ["type"] = "count" } }
                }
            }
        };

        var result = converter.ConvertToJObject(BuildDashboardJson(panel));
        var widget = GetFirstWidget(result);
        Assert.NotNull(widget);

        var dataPrime = widget["definition"]?["pieChart"]?["query"]?["dataprime"]?["dataprimeQuery"]?["text"]?.ToString();
        Assert.NotNull(dataPrime);
        Assert.Contains("groupby payload.isEmail agg count()", dataPrime);
    }

    [Fact]
    public void PieChart_MultiTarget_TrailingPredicate_ConsolidatesWithFullPath()
    {
        var converter = CreateConverter();
        var panel = new JObject
        {
            ["id"] = 11,
            ["title"] = "Trailing Predicate Pie",
            ["type"] = "piechart",
            ["targets"] = new JArray
            {
                new JObject
                {
                    ["refId"] = "A",
                    ["query"] = "app:foo AND payload.isEmail:true",
                    ["bucketAggs"] = new JArray { new JObject { ["type"] = "terms", ["field"] = "payload.isEmail" } },
                    ["metrics"] = new JArray { new JObject { ["type"] = "count" } }
                },
                new JObject
                {
                    ["refId"] = "B",
                    ["query"] = "app:foo AND payload.isEmail:false",
                    ["bucketAggs"] = new JArray { new JObject { ["type"] = "terms", ["field"] = "payload.isEmail" } },
                    ["metrics"] = new JArray { new JObject { ["type"] = "count" } }
                }
            }
        };

        var result = converter.ConvertToJObject(BuildDashboardJson(panel));
        var widget = GetFirstWidget(result);
        Assert.NotNull(widget);

        var dataPrime = widget["definition"]?["pieChart"]?["query"]?["dataprime"]?["dataprimeQuery"]?["text"]?.ToString();
        Assert.NotNull(dataPrime);
        Assert.Contains("groupby payload.isEmail agg count()", dataPrime);
        Assert.Contains("app:foo", dataPrime);
    }

    [Fact]
    public void PieChart_MultiTarget_TrailingPredicate_EscapesLuceneForDataPrimeLiteral()
    {
        var converter = CreateConverter();
        var panel = new JObject
        {
            ["id"] = 12,
            ["title"] = "Escaped Lucene Pie",
            ["type"] = "piechart",
            ["targets"] = new JArray
            {
                new JObject
                {
                    ["refId"] = "A",
                    ["query"] = @"source:C:\logs AND user:'bob' AND payload.isEmail:true",
                    ["bucketAggs"] = new JArray { new JObject { ["type"] = "terms", ["field"] = "payload.isEmail" } },
                    ["metrics"] = new JArray { new JObject { ["type"] = "count" } }
                },
                new JObject
                {
                    ["refId"] = "B",
                    ["query"] = @"source:C:\logs AND user:'bob' AND payload.isEmail:false",
                    ["bucketAggs"] = new JArray { new JObject { ["type"] = "terms", ["field"] = "payload.isEmail" } },
                    ["metrics"] = new JArray { new JObject { ["type"] = "count" } }
                }
            }
        };

        var result = converter.ConvertToJObject(BuildDashboardJson(panel));
        var widget = GetFirstWidget(result);
        Assert.NotNull(widget);

        var dataPrime = widget["definition"]?["pieChart"]?["query"]?["dataprime"]?["dataprimeQuery"]?["text"]?.ToString();
        Assert.NotNull(dataPrime);
        Assert.Contains("lucene 'source:C:\\\\logs AND user:\\'bob\\''", dataPrime);
        Assert.Contains("groupby payload.isEmail agg count()", dataPrime);
    }

    [Fact]
    public void PieChart_MultiTarget_SumMetric_UsesSumAggregationInDataPrime()
    {
        var converter = CreateConverter();
        var panel = new JObject
        {
            ["id"] = 13,
            ["title"] = "Sum Metric Pie",
            ["type"] = "piechart",
            ["targets"] = new JArray
            {
                new JObject
                {
                    ["refId"] = "A",
                    ["query"] = "app:foo AND payload.isEmail:true",
                    ["bucketAggs"] = new JArray { new JObject { ["type"] = "terms", ["field"] = "payload.isEmail" } },
                    ["metrics"] = new JArray { new JObject { ["type"] = "sum", ["field"] = "payload.size.keyword" } }
                },
                new JObject
                {
                    ["refId"] = "B",
                    ["query"] = "app:foo AND payload.isEmail:false",
                    ["bucketAggs"] = new JArray { new JObject { ["type"] = "terms", ["field"] = "payload.isEmail" } },
                    ["metrics"] = new JArray { new JObject { ["type"] = "sum", ["field"] = "payload.size.keyword" } }
                }
            }
        };

        var result = converter.ConvertToJObject(BuildDashboardJson(panel));
        var widget = GetFirstWidget(result);
        Assert.NotNull(widget);

        var dataPrime = widget["definition"]?["pieChart"]?["query"]?["dataprime"]?["dataprimeQuery"]?["text"]?.ToString();
        Assert.NotNull(dataPrime);
        Assert.Contains("groupby payload.isEmail agg sum(payload.size)", dataPrime);
    }

    [Fact]
    public void PieChart_MultiTarget_NonConsolidatable_EmitsErrorWidget()
    {
        var converter = CreateConverter();
        var panel = new JObject
        {
            ["id"] = 2,
            ["title"] = "Mixed Queries",
            ["type"] = "piechart",
            ["targets"] = new JArray
            {
                new JObject
                {
                    ["refId"] = "A",
                    ["query"] = "app:foo",
                    ["bucketAggs"] = new JArray { new JObject { ["type"] = "terms", ["field"] = "level" } },
                    ["metrics"] = new JArray { new JObject { ["type"] = "count" } }
                },
                new JObject
                {
                    ["refId"] = "B",
                    ["query"] = "app:bar",
                    ["bucketAggs"] = new JArray { new JObject { ["type"] = "terms", ["field"] = "level" } },
                    ["metrics"] = new JArray { new JObject { ["type"] = "count" } }
                }
            }
        };

        var result = converter.ConvertToJObject(BuildDashboardJson(panel));
        var widget = GetFirstWidget(result);
        Assert.NotNull(widget);

        Assert.NotNull(widget["definition"]?["markdown"]);
        var markdown = widget["definition"]?["markdown"]?["markdownText"]?.ToString();
        Assert.NotNull(markdown);
        Assert.Contains("Conversion failed", markdown);
        Assert.Contains("cannot be consolidated", markdown);

        var diag = converter.ConversionDiagnostics.FirstOrDefault(
            d => d.PanelTitle == "Mixed Queries" && d.Outcome == "error");
        Assert.NotNull(diag);
        Assert.Contains("cannot be consolidated", diag.Reason);
    }

    [Fact]
    public void PieChart_MultiTarget_MixedDatasource_EmitsErrorWidget()
    {
        var converter = CreateConverter();
        var panel = new JObject
        {
            ["id"] = 4,
            ["title"] = "Mixed Datasource Pie",
            ["type"] = "piechart",
            ["targets"] = new JArray
            {
                new JObject
                {
                    ["refId"] = "A",
                    ["query"] = "payload.isEmail:true",
                    ["datasource"] = new JObject { ["type"] = "elasticsearch" },
                    ["bucketAggs"] = new JArray { new JObject { ["type"] = "terms", ["field"] = "payload.isEmail" } },
                    ["metrics"] = new JArray { new JObject { ["type"] = "count" } }
                },
                new JObject
                {
                    ["refId"] = "B",
                    ["expr"] = "sum(up)",
                    ["datasource"] = new JObject { ["type"] = "prometheus" }
                }
            }
        };

        var result = converter.ConvertToJObject(BuildDashboardJson(panel));
        var widget = GetFirstWidget(result);
        Assert.NotNull(widget);
        Assert.NotNull(widget["definition"]?["markdown"]);

        var diag = converter.ConversionDiagnostics.FirstOrDefault(
            d => d.PanelTitle == "Mixed Datasource Pie" && d.Outcome == "error");
        Assert.NotNull(diag);
        Assert.Contains("unsupported datasource mix", diag.Reason);
    }

    [Fact]
    public void PieChart_HiddenSecondaryTarget_DoesNotForceError()
    {
        var converter = CreateConverter();
        var panel = new JObject
        {
            ["id"] = 5,
            ["title"] = "Hidden Target Pie",
            ["type"] = "piechart",
            ["targets"] = new JArray
            {
                new JObject
                {
                    ["refId"] = "A",
                    ["query"] = "payload.isEmail:true",
                    ["datasource"] = new JObject { ["type"] = "elasticsearch" },
                    ["bucketAggs"] = new JArray { new JObject { ["type"] = "terms", ["field"] = "payload.isEmail" } },
                    ["metrics"] = new JArray { new JObject { ["type"] = "count" } }
                },
                new JObject
                {
                    ["refId"] = "B",
                    ["hide"] = true,
                    ["query"] = "payload.isEmail:false",
                    ["datasource"] = new JObject { ["type"] = "elasticsearch" }
                }
            }
        };

        var result = converter.ConvertToJObject(BuildDashboardJson(panel));
        var widget = GetFirstWidget(result);
        Assert.NotNull(widget);
        Assert.NotNull(widget["definition"]?["pieChart"]);

        var diag = converter.ConversionDiagnostics.FirstOrDefault(
            d => d.PanelTitle == "Hidden Target Pie" && d.Outcome == "error");
        Assert.Null(diag);
    }

    [Fact]
    public void PieChart_SingleTarget_Unchanged()
    {
        var converter = CreateConverter();
        var panel = new JObject
        {
            ["id"] = 3,
            ["title"] = "Single Slice",
            ["type"] = "piechart",
            ["targets"] = new JArray
            {
                new JObject
                {
                    ["refId"] = "A",
                    ["query"] = "payload.isEmail:true",
                    ["bucketAggs"] = new JArray { new JObject { ["type"] = "terms", ["field"] = "level" } },
                    ["metrics"] = new JArray { new JObject { ["type"] = "count" } }
                }
            }
        };

        var result = converter.ConvertToJObject(BuildDashboardJson(panel));
        var widget = GetFirstWidget(result);
        Assert.NotNull(widget);

        var query = widget["definition"]?["pieChart"]?["query"] as JObject;
        Assert.NotNull(query);
        Assert.NotNull(query["logs"]);
        Assert.Null(query["dataprime"]);
        Assert.Null(query["dataPrime"]);
    }

    private static GrafanaToCxConverter CreateConverter() =>
        new(NullLogger<GrafanaToCxConverter>.Instance);

    private static string BuildDashboardJson(JObject panel) =>
        new JObject
        {
            ["dashboard"] = new JObject
            {
                ["title"] = "Test",
                ["panels"] = new JArray { panel }
            }
        }.ToString();

    private static JObject? GetFirstWidget(JObject dashboard)
    {
        var sections = dashboard["layout"]?["sections"] as JArray ?? [];
        foreach (var section in sections.Children<JObject>())
        {
            var rows = section["rows"] as JArray ?? [];
            foreach (var row in rows.Children<JObject>())
            {
                var widgets = row["widgets"] as JArray ?? [];
                var first = widgets.FirstOrDefault();
                if (first is JObject w)
                    return w;
            }
        }
        return null;
    }
}
