using Newtonsoft.Json.Linq;

namespace GrafanaToCx.Core.Converter.Transformations;

/// <summary>
/// Context passed to transformation planners: panel JSON, targets, and parsed transformations.
/// </summary>
public sealed class TransformationContext
{
    public JObject Panel { get; }
    public JArray Targets { get; }
    public JArray Transformations { get; }
    public string PanelType { get; }
    public string PanelTitle { get; }

    public TransformationContext(JObject panel, JArray targets, JArray transformations)
    {
        Panel = panel;
        Targets = targets;
        Transformations = transformations;
        PanelType = panel.Value<string>("type") ?? string.Empty;
        PanelTitle = panel.Value<string>("title") is { Length: > 0 } t ? t : $"Panel #{panel.Value<int>("id")}";
    }

    /// <summary>
    /// Extracts transformations from panel. Supports both root-level and data.spec.transformations.
    /// </summary>
    public static JArray GetTransformations(JObject panel)
    {
        var root = panel["transformations"] as JArray;
        if (root != null && root.Count > 0)
            return root;

        var spec = panel["data"]?["spec"]?["transformations"] as JArray;
        return spec ?? new JArray();
    }
}
