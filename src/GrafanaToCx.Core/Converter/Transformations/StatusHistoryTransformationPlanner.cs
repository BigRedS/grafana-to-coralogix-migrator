using Newtonsoft.Json.Linq;

namespace GrafanaToCx.Core.Converter.Transformations;

public sealed class StatusHistoryTransformationPlanner : ITransformationPlanner
{
    private readonly MultiLuceneMergeOptions _mergeOptions;
    private readonly BarChartTransformationPlanner _barChartPlanner;

    public StatusHistoryTransformationPlanner(MultiLuceneMergeOptions mergeOptions)
    {
        _mergeOptions = mergeOptions;
        _barChartPlanner = new BarChartTransformationPlanner(new MultiLuceneMergeOptions(["barchart"]));
    }

    public TransformationPlan Plan(TransformationContext context)
    {
        if (!string.Equals(context.PanelType, "status-history", StringComparison.OrdinalIgnoreCase))
            return new TransformationPlan.Success();

        var visibleTargets = VisibleTargetSelector.Resolve(context.Targets);
        if (visibleTargets.Count == 0)
            return new TransformationPlan.Success(SelectedTargets: visibleTargets);

        if (!_mergeOptions.IsAllowlistedType(context.PanelType))
            return new TransformationPlan.Success(SelectedTargets: visibleTargets);

        var surrogatePanel = (JObject)context.Panel.DeepClone();
        surrogatePanel["type"] = "barchart";
        var surrogateContext = new TransformationContext(surrogatePanel, context.Targets, context.Transformations);

        var plan = _barChartPlanner.Plan(surrogateContext);
        if (plan is not TransformationPlan.Success { ConsolidatedQueryPayload: not null } success)
            return plan;

        var decision = new PanelConversionDecision(
            "fallback",
            "Status-history consolidated into barChart DataPrime query from allowlisted logs targets.",
            "DGR-STH-003",
            [],
            "merge-status-history",
            0.9);

        return new TransformationPlan.Success(
            ConsolidatedQueryPayload: success.ConsolidatedQueryPayload,
            SelectedTargets: success.SelectedTargets,
            Decision: decision);
    }
}
