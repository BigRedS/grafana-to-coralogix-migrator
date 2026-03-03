namespace GrafanaToCx.Core.Converter;

public sealed record PanelConversionDiagnostic(
    string PanelTitle,
    string PanelType,
    string Outcome,
    string Reason,
    string? Code = null,
    IReadOnlyList<string>? DroppedSemantics = null,
    string? Approximation = null,
    double ConfidenceScore = 1.0);

