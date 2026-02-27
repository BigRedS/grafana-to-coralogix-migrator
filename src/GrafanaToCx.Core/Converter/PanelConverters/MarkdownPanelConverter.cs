using Newtonsoft.Json.Linq;

namespace GrafanaToCx.Core.Converter.PanelConverters;

public sealed class MarkdownPanelConverter : IPanelConverter
{
    public JObject? Convert(JObject panel, ISet<string> discoveredMetrics)
    {
        var content = panel["options"]?["content"]?.ToString();
        if (string.IsNullOrWhiteSpace(content))
        {
            content = panel["content"]?.ToString();
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        return new JObject
        {
            ["id"] = WidgetHelpers.IdObject(),
            ["title"] = string.Empty,
            ["definition"] = new JObject
            {
                ["markdown"] = new JObject
                {
                    ["markdownText"] = QueryHelpers.CleanHtml(content)
                }
            }
        };
    }

    public static int CalculateHeight(JObject panel)
    {
        var content = panel["options"]?["content"]?.ToString() ?? panel["content"]?.ToString() ?? string.Empty;
        var lines = content.Count(c => c == '\n') + 1;
        if (lines <= 3) return 8;
        if (lines <= 10) return 15;
        return 23;
    }
}
