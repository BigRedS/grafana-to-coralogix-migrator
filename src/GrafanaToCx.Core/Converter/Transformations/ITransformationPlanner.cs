using Newtonsoft.Json.Linq;

namespace GrafanaToCx.Core.Converter.Transformations;

/// <summary>
/// Plans how to transform a Grafana panel before conversion. Used to consolidate
/// multi-query patterns (e.g. pie charts with boolean-predicate targets) into
/// a single DataPrime-friendly representation, or to fail with an actionable reason.
/// </summary>
public interface ITransformationPlanner
{
    /// <summary>
    /// Returns a plan for the given context. Success allows conversion to proceed;
    /// Failure requires emitting a markdown error widget and diagnostic.
    /// </summary>
    TransformationPlan Plan(TransformationContext context);
}
