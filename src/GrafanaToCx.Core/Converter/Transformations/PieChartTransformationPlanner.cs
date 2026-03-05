namespace GrafanaToCx.Core.Converter.Transformations;

public sealed class PieChartTransformationPlanner : ITransformationPlanner
{
    private readonly PieMultiQueryConsolidationPlanner _consolidationPlanner;

    public PieChartTransformationPlanner(MultiLuceneMergeOptions mergeOptions)
    {
        _consolidationPlanner = new PieMultiQueryConsolidationPlanner(mergeOptions);
    }

    public TransformationPlan Plan(TransformationContext context)
    {
        if (!string.Equals(context.PanelType, "piechart", StringComparison.OrdinalIgnoreCase))
            return new TransformationPlan.Success();

        return _consolidationPlanner.Plan(context);
    }
}
