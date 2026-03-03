using GrafanaToCx.Core.Converter.Transformations;
using Newtonsoft.Json.Linq;

namespace GrafanaToCx.Core.Converter.PanelConverters;

internal static class PanelTargetSelector
{
    public static IReadOnlyList<JObject> ResolveVisibleTargets(JObject panel, TransformationPlan? plan)
    {
        if (plan is TransformationPlan.Success { SelectedTargets: { Count: > 0 } selected })
            return selected;

        var targets = panel["targets"] as JArray ?? [];
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
