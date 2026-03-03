namespace GrafanaToCx.Core.Converter.Transformations;

public sealed class MultiLuceneMergeOptions
{
    private readonly HashSet<string> _allowlistedWidgetTypes;

    public static MultiLuceneMergeOptions Disabled { get; } = new([]);

    public MultiLuceneMergeOptions(IEnumerable<string> allowlistedWidgetTypes)
    {
        _allowlistedWidgetTypes = allowlistedWidgetTypes
            .Where(type => !string.IsNullOrWhiteSpace(type))
            .Select(type => type.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public bool IsEnabled => _allowlistedWidgetTypes.Count > 0;

    public bool IsAllowlistedType(string panelType) =>
        !string.IsNullOrWhiteSpace(panelType) &&
        _allowlistedWidgetTypes.Contains(panelType.Trim());
}
