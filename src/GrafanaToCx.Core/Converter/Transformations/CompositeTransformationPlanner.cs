namespace GrafanaToCx.Core.Converter.Transformations;

/// <summary>
/// Runs registered planners. For piechart panels, runs PieMultiQueryConsolidationPlanner.
/// Other panel types receive Success (pass-through).
/// </summary>
public sealed class CompositeTransformationPlanner : ITransformationPlanner
{
    private readonly PieMultiQueryConsolidationPlanner _piePlanner = new();

    public TransformationPlan Plan(TransformationContext context)
    {
        return _piePlanner.Plan(context);
    }
}
