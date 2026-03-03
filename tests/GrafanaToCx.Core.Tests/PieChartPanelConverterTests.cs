using GrafanaToCx.Core.Converter.PanelConverters;
using Newtonsoft.Json.Linq;

namespace GrafanaToCx.Core.Tests;

public class PieChartPanelConverterTests
{
    [Fact]
    public void Convert_MetricsPieChart_InfersGroupNames_FromDisplayNameLabelTemplate()
    {
        var panel = new JObject
        {
            ["id"] = 70,
            ["title"] = "Queue - Messages",
            ["type"] = "piechart",
            ["fieldConfig"] = new JObject
            {
                ["defaults"] = new JObject
                {
                    ["displayName"] = "${__field.labels.queue}"
                }
            },
            ["targets"] = new JArray
            {
                new JObject
                {
                    ["refId"] = "A",
                    ["expr"] = "rabbitmq_queue_messages{instance=\"$URL\"}",
                    ["datasource"] = new JObject
                    {
                        ["type"] = "prometheus"
                    }
                }
            }
        };

        var converter = new PieChartPanelConverter();
        var widget = converter.Convert(panel, new HashSet<string>());

        Assert.NotNull(widget);
        var groupNames = widget["definition"]?["pieChart"]?["query"]?["metrics"]?["groupNames"] as JArray;
        Assert.NotNull(groupNames);
        Assert.Single(groupNames);
        Assert.Equal("queue", groupNames[0]?.ToString());
    }
}
