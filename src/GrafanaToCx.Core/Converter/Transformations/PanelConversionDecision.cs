namespace GrafanaToCx.Core.Converter.Transformations;

public sealed record PanelConversionDecision(
    string Outcome,
    string Reason,
    string Code,
    IReadOnlyList<string> DroppedSemantics,
    string Approximation,
    double ConfidenceScore);
