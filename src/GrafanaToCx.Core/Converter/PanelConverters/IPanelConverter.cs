using Newtonsoft.Json.Linq;

namespace GrafanaToCx.Core.Converter.PanelConverters;

public interface IPanelConverter
{
    JObject? Convert(JObject panel, ISet<string> discoveredMetrics);
}
