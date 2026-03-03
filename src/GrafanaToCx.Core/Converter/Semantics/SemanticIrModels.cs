using Newtonsoft.Json.Linq;

namespace GrafanaToCx.Core.Converter.Semantics;

public enum SemanticOperationKind
{
    Filter,
    Group,
    Reduce,
    Reshape,
    Join,
    Calc,
    TimeBucket,
    Order,
    Limit
}

public abstract record SemanticOperationNode(SemanticOperationKind Kind);

public sealed record FilterOperationNode(string Expression) : SemanticOperationNode(SemanticOperationKind.Filter);

public sealed record GroupOperationNode(IReadOnlyList<string> Keys) : SemanticOperationNode(SemanticOperationKind.Group);

public sealed record ReduceOperationNode(string Aggregation, string? Field, string? Unit) : SemanticOperationNode(SemanticOperationKind.Reduce);

public sealed record ReshapeOperationNode(string Shape) : SemanticOperationNode(SemanticOperationKind.Reshape);

public sealed record JoinOperationNode(string Strategy, IReadOnlyList<string> Keys) : SemanticOperationNode(SemanticOperationKind.Join);

public sealed record CalcOperationNode(string Expression) : SemanticOperationNode(SemanticOperationKind.Calc);

public sealed record TimeBucketOperationNode(string Interval) : SemanticOperationNode(SemanticOperationKind.TimeBucket);

public sealed record OrderOperationNode(IReadOnlyList<string> Keys, string Direction) : SemanticOperationNode(SemanticOperationKind.Order);

public sealed record LimitOperationNode(int Value) : SemanticOperationNode(SemanticOperationKind.Limit);

public sealed record SemanticQueryIr(
    string TargetRefId,
    string SourceFamily,
    IReadOnlyList<SemanticOperationNode> Operations,
    JObject RawTarget);
