namespace GrafanaToCx.Core.Converter.Transformations;

public sealed class TimeSeriesTransformationPlanner : ITransformationPlanner
{
    private readonly PieMultiQueryConsolidationPlanner _consolidationPlanner;

    public TimeSeriesTransformationPlanner(MultiLuceneMergeOptions mergeOptions)
    {
        _consolidationPlanner = new PieMultiQueryConsolidationPlanner(mergeOptions);
    }

    public TransformationPlan Plan(TransformationContext context)
    {
        if (!string.Equals(context.PanelType, "timeseries", StringComparison.OrdinalIgnoreCase))
            return new TransformationPlan.Success();

        return _consolidationPlanner.Plan(context);
    }
}
