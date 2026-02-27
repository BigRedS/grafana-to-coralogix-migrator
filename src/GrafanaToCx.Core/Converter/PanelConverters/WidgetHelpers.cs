using Newtonsoft.Json.Linq;

namespace GrafanaToCx.Core.Converter.PanelConverters;

public static class WidgetHelpers
{
    public static JObject IdObject() =>
        new JObject { ["value"] = Guid.NewGuid().ToString() };
}
