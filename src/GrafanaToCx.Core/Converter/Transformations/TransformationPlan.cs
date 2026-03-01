using Newtonsoft.Json.Linq;

namespace GrafanaToCx.Core.Converter.Transformations;

/// <summary>
/// Result of transformation planning. Indicates whether the panel can be converted,
/// requires consolidation (e.g. pie multi-query), or must fail with an error widget.
/// </summary>
public abstract record TransformationPlan
{
    /// <summary>
    /// Plan succeeded; converter may proceed with optional consolidated query payload.
    /// </summary>
    public sealed record Success(JObject? ConsolidatedQueryPayload = null) : TransformationPlan;

    /// <summary>
    /// Plan failed; panel must emit a markdown error widget and diagnostic with outcome "error".
    /// </summary>
    public sealed record Failure(string Reason) : TransformationPlan;
}
