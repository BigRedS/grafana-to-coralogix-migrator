namespace GrafanaToCx.Core.Converter.Transformations;

/// <summary>
/// Runs registered planners. Consolidation planners are evaluated first for panel
/// types that support DataPrime merge strategies, then deterministic multi-target
/// reduction runs when needed.
/// </summary>
public sealed class CompositeTransformationPlanner : ITransformationPlanner
{
    private readonly MultiTargetSemanticsPlanner _multiTargetPlanner = new();
    private readonly PieMultiQueryConsolidationPlanner _piePlanner;

    public CompositeTransformationPlanner(MultiLuceneMergeOptions mergeOptions)
    {
        _piePlanner = new PieMultiQueryConsolidationPlanner(mergeOptions);
    }

    public TransformationPlan Plan(TransformationContext context)
    {
        var supportsConsolidation =
            string.Equals(context.PanelType, "piechart", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(context.PanelType, "timeseries", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(context.PanelType, "barchart", StringComparison.OrdinalIgnoreCase);

        if (supportsConsolidation)
        {
            var isBarChart = string.Equals(context.PanelType, "barchart", StringComparison.OrdinalIgnoreCase);
            var consolidationPlan = _piePlanner.Plan(context);
            if (consolidationPlan is TransformationPlan.Failure)
                return consolidationPlan;

            if (!isBarChart)
                return consolidationPlan;

            if (consolidationPlan is TransformationPlan.Success
                {
                    ConsolidatedQueryPayload: not null
                } consolidatedSuccess)
            {
                return consolidatedSuccess;
            }

            if (isBarChart && consolidationPlan is TransformationPlan.Success { Decision: not null } skippedWithReason)
                return skippedWithReason;
        }

        return _multiTargetPlanner.Plan(context);
    }
}
