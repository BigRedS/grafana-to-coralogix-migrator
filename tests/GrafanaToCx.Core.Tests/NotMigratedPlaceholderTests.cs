using GrafanaToCx.Core.Converter;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;

namespace GrafanaToCx.Core.Tests;

/// <summary>
/// Unsupported panels used to vanish with no on-dashboard trace. Panels that showed real
/// data now leave a marker; Grafana's own chrome still goes quietly.
/// </summary>
public class NotMigratedPlaceholderTests
{
    private static List<JObject> Convert(params JObject[] panels)
    {
        var converter = new GrafanaToCxConverter(NullLogger<GrafanaToCxConverter>.Instance);
        var dashboard = new JObject
        {
            ["title"] = "Board",
            ["panels"] = new JArray(panels.Cast<object>().ToArray())
        };
        var result = converter.ConvertToJObject(dashboard.ToString());

        return (result["layout"]?["sections"] as JArray ?? [])
            .Children<JObject>()
            .SelectMany(s => (s["rows"] as JArray ?? []).Children<JObject>())
            .SelectMany(r => (r["widgets"] as JArray ?? []).Children<JObject>())
            .ToList();
    }

    private static JObject Panel(string type, string? title = null) => new()
    {
        ["id"] = 1,
        ["type"] = type,
        ["title"] = title,
        ["targets"] = new JArray(new JObject { ["refId"] = "A", ["expr"] = "up" })
    };

    [Theory]
    [InlineData("table-old")]
    [InlineData("flant-statusmap-panel")]
    public void DataPanelTypes_LeaveAPlaceholder(string panelType)
    {
        var widget = Assert.Single(Convert(Panel(panelType, "Top 10 Devices")));

        Assert.Equal("Top 10 Devices", widget.Value<string>("title"));
        var text = widget["definition"]?["markdown"]?["markdownText"]?.ToString();
        Assert.Contains("Not migrated", text);
        Assert.Contains(panelType, text);
        // The wording must not read as a failure — nothing went wrong.
        Assert.DoesNotContain("Conversion failed", text);
    }

    [Theory]
    [InlineData("welcome")]
    [InlineData("dashlist")]
    [InlineData("news")]
    public void ChromePanelTypes_AreDroppedSilently(string panelType)
    {
        Assert.Empty(Convert(Panel(panelType, "Dashboards")));
    }

    [Fact]
    public void MixedDashboard_MarksDataPanelsOnly()
    {
        var widgets = Convert(
            Panel("flant-statusmap-panel", "Requests"),
            Panel("welcome"),
            Panel("dashlist", "Dashboards"),
            Panel("table-old", "Data Requests"));

        Assert.Equal(2, widgets.Count);
        Assert.Contains(widgets, w => w.Value<string>("title") == "Requests");
        Assert.Contains(widgets, w => w.Value<string>("title") == "Data Requests");
    }

    [Fact]
    public void PlaceholderPanelStillReportsTheSkipDiagnostic()
    {
        var converter = new GrafanaToCxConverter(NullLogger<GrafanaToCxConverter>.Instance);
        var dashboard = new JObject
        {
            ["title"] = "Board",
            ["panels"] = new JArray(Panel("table-old", "Requests"))
        };

        converter.ConvertToJObject(dashboard.ToString());

        Assert.Contains(converter.ConversionDiagnostics,
            d => d.Code == "UNS-PNL-001" && d.PanelType == "table-old");
    }

    [Fact]
    public void UntitledPanel_StillGetsAUsableTitle()
    {
        var widget = Assert.Single(Convert(Panel("table-old")));

        Assert.Equal("Panel #1", widget.Value<string>("title"));
    }
}
