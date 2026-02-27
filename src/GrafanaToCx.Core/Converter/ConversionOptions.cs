namespace GrafanaToCx.Core.Converter;

public sealed class ConversionOptions
{
    public string? FolderId { get; init; }
    public string? DashboardName { get; init; }
    public bool SkipUnsupportedPanels { get; init; } = true;
}
