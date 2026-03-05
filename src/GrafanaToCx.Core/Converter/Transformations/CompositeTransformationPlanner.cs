namespace GrafanaToCx.Core.Converter.Transformations;

/// <summary>
/// Runs registered planners. Consolidation planners are evaluated first for panel
/// types that support DataPrime merge strategies, then deterministic multi-target
/// reduction runs when needed.
/// </summary>
public sealed class CompositeTransformationPlanner : ITransformationPlanner
{
    private readonly MultiTargetSemanticsPlanner _multiTargetPlanner = new();
    private readonly PieChartTransformationPlanner _pieChartPlanner;
    private readonly TimeSeriesTransformationPlanner _timeSeriesPlanner;
    private readonly BarChartTransformationPlanner _barChartPlanner;
    private readonly StatusHistoryTransformationPlanner _statusHistoryPlanner;

    public CompositeTransformationPlanner(MultiLuceneMergeOptions mergeOptions)
    {
        _pieChartPlanner = new PieChartTransformationPlanner(mergeOptions);
        _timeSeriesPlanner = new TimeSeriesTransformationPlanner(mergeOptions);
        _barChartPlanner = new BarChartTransformationPlanner(mergeOptions);
        _statusHistoryPlanner = new StatusHistoryTransformationPlanner(mergeOptions);
    }

    public TransformationPlan Plan(TransformationContext context)
    {
        if (string.Equals(context.PanelType, "barchart", StringComparison.OrdinalIgnoreCase))
        {
            var barPlan = _barChartPlanner.Plan(context);
            if (barPlan is TransformationPlan.Success
                {
                    ConsolidatedQueryPayload: null,
                    Decision: null
                })
            {
                return _multiTargetPlanner.Plan(context);
            }

            return barPlan;
        }

        if (string.Equals(context.PanelType, "status-history", StringComparison.OrdinalIgnoreCase))
        {
            var statusPlan = _statusHistoryPlanner.Plan(context);
            if (statusPlan is TransformationPlan.Success
                {
                    ConsolidatedQueryPayload: null,
                    Decision: null
                })
            {
                return _multiTargetPlanner.Plan(context);
            }

            return statusPlan;
        }

        return context.PanelType.ToLowerInvariant() switch
        {
            "piechart" => _pieChartPlanner.Plan(context),
            "timeseries" => _timeSeriesPlanner.Plan(context),
            _ => _multiTargetPlanner.Plan(context)
        };
    }
}
