using Newtonsoft.Json.Linq;

namespace GrafanaToCx.Core.Converter;

public interface IGrafanaToCxConverter
{
    string Convert(string grafanaJson, ConversionOptions? options = null);
    JObject ConvertToJObject(string grafanaJson, ConversionOptions? options = null);
    IReadOnlyList<PanelConversionDiagnostic> ConversionDiagnostics { get; }
}
