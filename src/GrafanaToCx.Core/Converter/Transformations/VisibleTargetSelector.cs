using Newtonsoft.Json.Linq;

namespace GrafanaToCx.Core.Converter.Transformations;

internal static class VisibleTargetSelector
{
    public static IReadOnlyList<JObject> Resolve(JArray targets)
    {
        return targets
            .Children<JObject>()
            .Where(t => t.Value<bool?>("hide") != true)
            .Select((target, index) => (target, index))
            .OrderBy(t => t.target.Value<string>("refId") ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(t => t.index)
            .Select(t => t.target)
            .ToList();
    }
}
