using Newtonsoft.Json.Linq;

namespace GrafanaToCx.Core.Converter.Transformations;

/// <summary>
/// Provides deterministic target selection/merge rules for multi-target panels.
/// </summary>
public sealed class MultiTargetSemanticsPlanner : ITransformationPlanner
{
    private static readonly HashSet<string> DeterministicSingleQueryPanelTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "barchart",
        "table",
        "status-history",
        "gauge",
        "bargauge",
        "stat",
        "singlestat"
    };

    public TransformationPlan Plan(TransformationContext context)
    {
        var visibleTargets = context.Targets
            .Children<JObject>()
            .Where(t => t.Value<bool?>("hide") != true)
            .Select((target, index) => (target, index))
            .OrderBy(t => t.target.Value<string>("refId") ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(t => t.index)
            .Select(t => t.target)
            .ToList();

        if (visibleTargets.Count == 0)
        {
            return new TransformationPlan.Failure(
                "No visible targets available after hide=true filtering.",
                "UNS-TGT-001",
                [],
                "none",
                1.0);
        }

        if (visibleTargets.Count == 1)
            return new TransformationPlan.Success(SelectedTargets: visibleTargets);

        if (!DeterministicSingleQueryPanelTypes.Contains(context.PanelType))
            return new TransformationPlan.Success(SelectedTargets: visibleTargets);

        var families = visibleTargets.Select(ResolveFamily).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (families.Count > 1 || families[0] == "unknown")
        {
            return new TransformationPlan.Failure(
                "Visible targets use conflicting datasource/query families and cannot be aligned into one query shape.",
                "UNS-MTG-001",
                visibleTargets.Select(GetTargetIdentity).ToList(),
                "none",
                1.0);
        }

        var selected = visibleTargets[0];
        var dropped = visibleTargets.Skip(1).Select(GetTargetIdentity).ToList();
        var decision = new PanelConversionDecision(
            "fallback",
            "Multiple visible targets reduced to one deterministic target to preserve a valid oneof query branch.",
            "DGR-MTG-001",
            dropped,
            "select-one",
            0.72);

        return new TransformationPlan.Success(
            SelectedTargets: [selected],
            Decision: decision);
    }

    private static string ResolveFamily(JObject target)
    {
        var dsType = target["datasource"]?["type"]?.ToString();
        if (string.Equals(dsType, "prometheus", StringComparison.OrdinalIgnoreCase))
            return "metrics";
        if (string.Equals(dsType, "elasticsearch", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(dsType, "opensearch", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(dsType, "loki", StringComparison.OrdinalIgnoreCase))
            return "logs";

        if (target["expr"] is JValue)
            return "metrics";
        if (target["query"] is JValue || target["bucketAggs"] is JArray)
            return "logs";
        return "unknown";
    }

    private static string GetTargetIdentity(JObject target) =>
        target.Value<string>("refId")
        ?? target.Value<string>("query")
        ?? target.Value<string>("expr")
        ?? "<unknown-target>";
}
