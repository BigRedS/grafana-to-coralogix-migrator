using GrafanaToCx.Core.Converter.PanelConverters;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GrafanaToCx.Core.Converter;

public sealed class GrafanaToCxConverter : IGrafanaToCxConverter
{
    private static readonly HashSet<string> DirectPanelTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "stat", "singlestat", "gauge", "bargauge", "text", "table", "logs", "piechart", "barchart"
    };

    private static readonly HashSet<string> AllowedFallbackPanelTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "timeseries", "graph"
    };

    private static readonly string[] SectionColors =
    [
        "SECTION_PREDEFINED_COLOR_UNSPECIFIED",
        "SECTION_PREDEFINED_COLOR_BLUE",
        "SECTION_PREDEFINED_COLOR_GREEN",
        "SECTION_PREDEFINED_COLOR_PURPLE",
        "SECTION_PREDEFINED_COLOR_PINK",
        "SECTION_PREDEFINED_COLOR_CYAN",
        "SECTION_PREDEFINED_COLOR_MAGENTA",
        "SECTION_PREDEFINED_COLOR_ORANGE"
    ];

    private readonly ILogger<GrafanaToCxConverter> _logger;
    private readonly LineChartPanelConverter _lineChartConverter = new();
    private readonly GaugePanelConverter _gaugeConverter = new();
    private readonly MarkdownPanelConverter _markdownConverter = new();
    private readonly LogsPanelConverter _logsPanelConverter = new();
    private readonly PieChartPanelConverter _pieChartConverter = new();
    private readonly BarChartPanelConverter _barChartConverter = new();
    private readonly DataTablePanelConverter _dataTableConverter = new();
    private readonly List<PanelConversionDiagnostic> _conversionDiagnostics = [];

    public IReadOnlyList<PanelConversionDiagnostic> ConversionDiagnostics => _conversionDiagnostics;

    public GrafanaToCxConverter(ILogger<GrafanaToCxConverter> logger)
    {
        _logger = logger;
    }

    public string Convert(string grafanaJson, ConversionOptions? options = null)
    {
        var result = ConvertToJObject(grafanaJson, options);
        return result.ToString(Formatting.Indented);
    }

    public JObject ConvertToJObject(string grafanaJson, ConversionOptions? options = null)
    {
        _conversionDiagnostics.Clear();
        var sourceToken = JToken.Parse(grafanaJson);
        var sourceObject = sourceToken as JObject ?? new JObject();
        var grafana = sourceObject["dashboard"] as JObject ?? sourceObject;

        var discoveredMetrics = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var customDashboard = InitializeDashboard(grafana, options);
        ConvertPanels(grafana, customDashboard, discoveredMetrics, options);
        ConvertVariables(grafana, customDashboard, discoveredMetrics);
        ApplyTimeFrame(grafana, customDashboard);

        return customDashboard;
    }

    private static JObject InitializeDashboard(JObject grafana, ConversionOptions? options)
    {
        var name = options?.DashboardName
                   ?? grafana.Value<string>("title")
                   ?? "Imported Grafana Dashboard";

        return new JObject
        {
            ["id"] = Guid.NewGuid().ToString("N")[..21],
            ["name"] = name,
            ["description"] = grafana.Value<string>("description") ?? string.Empty,
            ["layout"] = new JObject { ["sections"] = new JArray() },
            ["variables"] = new JArray(),
            ["variablesV2"] = new JArray(),
            ["filters"] = new JArray(),
            ["relativeTimeFrame"] = "3600s",
            ["annotations"] = new JArray(),
            ["off"] = new JObject(),
            ["actions"] = new JArray()
        };
    }

    private void ConvertPanels(JObject grafana, JObject customDashboard, ISet<string> discoveredMetrics, ConversionOptions? options)
    {
        var panels = grafana["panels"] as JArray ?? new JArray();
        var sections = GroupPanelsIntoSections(panels);

        if (sections.Count == 0 && panels.Count > 0)
        {
            var fallback = panels.Children<JObject>().Where(p => p.Value<string>("type") != "row").ToList();
            sections.Add((null, fallback));
        }

        var outputSections = (JArray)customDashboard["layout"]!["sections"]!;
        var colorIndex = 0;

        foreach (var (title, sectionPanels) in sections.Where(s => s.panels.Count > 0))
        {
            outputSections.Add(CreateSection(sectionPanels, title, colorIndex, discoveredMetrics, options));
            colorIndex++;
        }
    }

    private static List<(string? title, List<JObject> panels)> GroupPanelsIntoSections(JArray panels)
    {
        var sections = new List<(string? title, List<JObject> panels)>();
        var currentTitle = (string?)null;
        var currentPanels = new List<JObject>();

        foreach (var panelToken in panels)
        {
            if (panelToken is not JObject panel)
            {
                continue;
            }

            var type = panel.Value<string>("type") ?? string.Empty;
            if (type == "row")
            {
                if (currentPanels.Count > 0 || currentTitle != null)
                {
                    sections.Add((currentTitle, currentPanels));
                    currentPanels = new List<JObject>();
                }

                currentTitle = panel.Value<string>("title");

                // When a row is collapsed Grafana stores its child panels inside
                // the row panel's own "panels" array instead of at the top level.
                if (panel.Value<bool?>("collapsed") == true)
                {
                    var nestedPanels = panel["panels"] as JArray ?? new JArray();
                    foreach (var nested in nestedPanels.Children<JObject>())
                    {
                        if (!string.Equals(
                                nested.Value<string>("type"), "row",
                                StringComparison.OrdinalIgnoreCase))
                        {
                            currentPanels.Add(nested);
                        }
                    }
                }

                continue;
            }

            currentPanels.Add(panel);
        }

        if (currentPanels.Count > 0 || currentTitle != null)
        {
            sections.Add((currentTitle, currentPanels));
        }

        return sections;
    }

    private JObject CreateSection(
        List<JObject> panels,
        string? sectionTitle,
        int colorIndex,
        ISet<string> discoveredMetrics,
        ConversionOptions? options)
    {
        const int maxWidgetsPerRow = 3;
        var rows = new JArray();
        var currentWidgets = new List<JObject>();

        foreach (var panel in panels)
        {
            var panelType = panel.Value<string>("type") ?? string.Empty;
            if (panelType == "row")
            {
                continue;
            }

            if (panelType == "text")
            {
                FlushWidgets(currentWidgets, rows);
                var markdownWidget = ConvertPanelToWidget(panel, discoveredMetrics, options);
                if (markdownWidget != null)
                {
                    rows.Add(CreateRow(new List<JObject> { markdownWidget }, MarkdownPanelConverter.CalculateHeight(panel)));
                }

                continue;
            }

            if (currentWidgets.Count >= maxWidgetsPerRow)
            {
                FlushWidgets(currentWidgets, rows);
            }

            var widget = ConvertPanelToWidget(panel, discoveredMetrics, options);
            if (widget != null)
            {
                currentWidgets.Add(widget);
            }
        }

        FlushWidgets(currentWidgets, rows);

        var sectionOptions = BuildSectionOptions(sectionTitle, colorIndex);

        return new JObject
        {
            ["id"] = WidgetHelpers.IdObject(),
            ["rows"] = rows,
            ["options"] = sectionOptions
        };
    }

    private static void FlushWidgets(List<JObject> widgets, JArray rows)
    {
        if (widgets.Count == 0)
        {
            return;
        }

        rows.Add(CreateRow(widgets));
        widgets.Clear();
    }

    private static JObject CreateRow(List<JObject> widgets, int height = 19)
    {
        return new JObject
        {
            ["id"] = WidgetHelpers.IdObject(),
            ["appearance"] = new JObject { ["height"] = height },
            ["widgets"] = new JArray(widgets)
        };
    }

    private JObject? ConvertPanelToWidget(JObject panel, ISet<string> discoveredMetrics, ConversionOptions? options)
    {
        var panelType = panel.Value<string>("type") ?? string.Empty;
        var panelTitle = panel.Value<string>("title") is { Length: > 0 } t ? t : $"Panel #{panel.Value<int>("id")}";

        JObject? widget = panelType switch
        {
            "stat" or "singlestat" or "gauge" or "bargauge" => _gaugeConverter.Convert(panel, discoveredMetrics),
            "text" => _markdownConverter.Convert(panel, discoveredMetrics),
            "table" => _dataTableConverter.Convert(panel, discoveredMetrics),
            "logs" => _logsPanelConverter.Convert(panel, discoveredMetrics),
            "piechart" => _pieChartConverter.Convert(panel, discoveredMetrics),
            "barchart" => _barChartConverter.Convert(panel, discoveredMetrics),
            "timeseries" or "graph" => _lineChartConverter.Convert(panel, discoveredMetrics),
            _ => null
        };

        if (widget != null)
        {
            if (AllowedFallbackPanelTypes.Contains(panelType))
            {
                _conversionDiagnostics.Add(new PanelConversionDiagnostic(
                    panelTitle,
                    panelType,
                    "fallback",
                    "Converted with lineChart fallback."));
            }

            return widget;
        }

        if (DirectPanelTypes.Contains(panelType) || AllowedFallbackPanelTypes.Contains(panelType))
        {
            _conversionDiagnostics.Add(new PanelConversionDiagnostic(
                panelTitle,
                panelType,
                "skipped",
                "Panel converter produced no widget (empty/hidden/invalid targets)."));
            return null;
        }

        if (options?.SkipUnsupportedPanels ?? true)
        {
            _conversionDiagnostics.Add(new PanelConversionDiagnostic(
                panelTitle,
                panelType,
                "skipped",
                "Unsupported Grafana panel type."));
            return null;
        }

        widget = _lineChartConverter.Convert(panel, discoveredMetrics);
        if (widget != null)
        {
            _conversionDiagnostics.Add(new PanelConversionDiagnostic(
                panelTitle,
                panelType,
                "fallback",
                "Forced fallback for unsupported panel type."));
        }

        return widget;
    }

    private static JObject BuildSectionOptions(string? sectionTitle, int colorIndex)
    {
        if (string.IsNullOrWhiteSpace(sectionTitle))
        {
            return new JObject { ["internal"] = new JObject() };
        }

        var color = SectionColors[colorIndex % SectionColors.Length];

        return new JObject
        {
            ["custom"] = new JObject
            {
                ["name"] = sectionTitle,
                ["collapsed"] = false,
                ["color"] = new JObject { ["predefined"] = color }
            }
        };
    }

    private void ConvertVariables(JObject grafana, JObject customDashboard, ISet<string> discoveredMetrics)
    {
        var grafanaVariables = grafana["templating"]?["list"] as JArray ?? new JArray();
        var variableConverter = new VariableConverter(_logger);
        customDashboard["variablesV2"] = variableConverter.ConvertVariables(grafanaVariables, discoveredMetrics);
    }

    private static void ApplyTimeFrame(JObject grafana, JObject customDashboard)
    {
        var from = grafana["time"]?["from"]?.ToString();
        customDashboard["relativeTimeFrame"] = QueryHelpers.MapTimeFrame(from);
    }
}
