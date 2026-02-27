using GrafanaToCx.Core.Converter;
using GrafanaToCx.Core.Migration;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;

namespace GrafanaToCx.Core.Tests;

public class UnitTest1
{
    [Fact]
    public void UnsupportedType_IsSoftSkipped_ByDefault()
    {
        var converter = CreateConverter();

        var result = converter.ConvertToJObject(
            BuildDashboardJson(new JObject
            {
                ["id"] = 1,
                ["title"] = "Status Map",
                ["type"] = "flant-statusmap-panel",
                ["targets"] = new JArray
                {
                    new JObject
                    {
                        ["refId"] = "A",
                        ["expr"] = "up"
                    }
                }
            }));

        Assert.Empty(ExtractWidgets(result));
        Assert.Contains(
            converter.ConversionDiagnostics,
            d => d.PanelType == "flant-statusmap-panel" && d.Outcome == "skipped");
    }

    [Fact]
    public void Timeseries_UsesAllowedLineChartFallback()
    {
        var converter = CreateConverter();

        var result = converter.ConvertToJObject(
            BuildDashboardJson(new JObject
            {
                ["id"] = 2,
                ["title"] = "CPU",
                ["type"] = "timeseries",
                ["targets"] = new JArray
                {
                    new JObject
                    {
                        ["refId"] = "A",
                        ["expr"] = "sum(rate(container_cpu_usage_seconds_total[5m]))"
                    }
                }
            }));

        var widgets = ExtractWidgets(result);
        Assert.Single(widgets);
        Assert.NotNull(widgets[0]["definition"]?["lineChart"]);
        Assert.Contains(
            converter.ConversionDiagnostics,
            d => d.PanelType == "timeseries" && d.Outcome == "fallback");
    }

    [Fact]
    public void UnsupportedType_CanUseForcedFallback_WhenConfigured()
    {
        var converter = CreateConverter();

        var result = converter.ConvertToJObject(
            BuildDashboardJson(new JObject
            {
                ["id"] = 3,
                ["title"] = "Unknown Visual",
                ["type"] = "custom-panel-type",
                ["targets"] = new JArray
                {
                    new JObject
                    {
                        ["refId"] = "A",
                        ["expr"] = "sum(up)"
                    }
                }
            }),
            new ConversionOptions
            {
                SkipUnsupportedPanels = false
            });

        var widgets = ExtractWidgets(result);
        Assert.Single(widgets);
        Assert.NotNull(widgets[0]["definition"]?["lineChart"]);
        Assert.Contains(
            converter.ConversionDiagnostics,
            d => d.PanelType == "custom-panel-type" && d.Outcome == "fallback");
    }

    [Theory]
    [InlineData("stat", "gauge")]
    [InlineData("bargauge", "gauge")]
    [InlineData("table", "dataTable")]
    [InlineData("piechart", "pieChart")]
    [InlineData("barchart", "barChart")]
    [InlineData("timeseries", "lineChart")]
    public void Comparator_TypeMapping_CoversExtendedWidgetTypes(string grafanaType, string expectedCxType)
    {
        Assert.Equal(expectedCxType, DashboardComparator.GetExpectedCxType(grafanaType));
    }

    [Fact]
    public void Comparator_RecognizesPieAndBarQueries()
    {
        var pieWidget = new JObject
        {
            ["definition"] = new JObject
            {
                ["pieChart"] = new JObject
                {
                    ["query"] = new JObject
                    {
                        ["metrics"] = new JObject
                        {
                            ["promqlQuery"] = new JObject { ["value"] = "sum(up)" }
                        }
                    }
                }
            }
        };

        var barWidget = new JObject
        {
            ["definition"] = new JObject
            {
                ["barChart"] = new JObject
                {
                    ["query"] = new JObject
                    {
                        ["logs"] = new JObject
                        {
                            ["aggregations"] = new JArray { new JObject { ["count"] = new JObject() } }
                        }
                    }
                }
            }
        };

        Assert.True(DashboardComparator.HasValidQuery(pieWidget, "pieChart"));
        Assert.True(DashboardComparator.HasValidQuery(barWidget, "barChart"));
    }

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

    private static List<JObject> ExtractWidgets(JObject dashboard)
    {
        var sections = dashboard["layout"]?["sections"] as JArray ?? [];
        return sections
            .Children<JObject>()
            .SelectMany(section => (section["rows"] as JArray ?? []).Children<JObject>())
            .SelectMany(row => (row["widgets"] as JArray ?? []).Children<JObject>())
            .ToList();
    }
}
