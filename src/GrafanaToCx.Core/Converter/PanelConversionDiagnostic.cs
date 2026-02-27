namespace GrafanaToCx.Core.Converter;

public sealed record PanelConversionDiagnostic(
    string PanelTitle,
    string PanelType,
    string Outcome,
    string Reason);

