using Newtonsoft.Json.Linq;

namespace GrafanaToCx.Core.Converter.PanelConverters;

/// <summary>
/// Maps Grafana/Elasticsearch field names to Coralogix dashboard groupBy keypath/scope objects.
///
/// Coralogix scopes:
///   DATASET_SCOPE_METADATA  — system metadata: severity, timestamp
///   DATASET_SCOPE_LABEL     — indexed labels:   subsystemname, applicationname, …
///   DATASET_SCOPE_USER_DATA — user-defined JSON fields: kubernetes.namespace_name, …
/// </summary>
public static class CxFieldHelper
{
    private const string KeywordSuffix = ".keyword";
    private const string NumericSuffix = ".numeric";

    private static readonly HashSet<string> MetadataFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "severity", "timestamp"
    };

    private static readonly HashSet<string> LabelFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "applicationname", "subsystemname", "categoryname",
        "classname", "computername", "ipaddress", "threadid",
        "app", "component", "instance", "job"
    };

    // Grafana Elasticsearch datasource uses coralogix.metadata.* prefix.
    private const string CxMetadataPrefix = "coralogix.metadata.";

    /// <summary>
    /// Strips terminal .keyword and .numeric suffixes from a logs aggregation field name (case-insensitive).
    /// Used for LOGS-based aggregation field migration from Elasticsearch/OpenSearch.
    /// </summary>
    public static string StripLogsFieldSuffixes(string fieldName)
    {
        if (fieldName == null) return "";
        if (fieldName.Length == 0) return fieldName;

        var result = fieldName;
        if (result.EndsWith(KeywordSuffix, StringComparison.OrdinalIgnoreCase))
            result = result[..^KeywordSuffix.Length];
        if (result.EndsWith(NumericSuffix, StringComparison.OrdinalIgnoreCase))
            result = result[..^NumericSuffix.Length];
        return result;
    }

    /// <summary>
    /// Returns a CX groupBy keypath/scope object for the given field name.
    /// Handles coralogix.metadata.* ES prefixes, known metadata/label fields,
    /// and dotted user-data paths (split into keypath array).
    /// </summary>
    public static JObject ToGroupByField(string fieldName)
    {
        fieldName = StripLogsFieldSuffixes(fieldName);

        var normalized = fieldName.StartsWith(CxMetadataPrefix, StringComparison.OrdinalIgnoreCase)
            ? fieldName[CxMetadataPrefix.Length..].ToLowerInvariant()
            : fieldName;

        var (keypath, scope) = ResolveScope(normalized);

        return new JObject
        {
            ["keypath"] = new JArray(keypath),
            ["scope"] = scope
        };
    }

    private static (string[] keypath, string scope) ResolveScope(string fieldName)
    {
        var lower = fieldName.ToLowerInvariant();

        if (MetadataFields.Contains(lower))
            return ([lower], "DATASET_SCOPE_METADATA");

        if (LabelFields.Contains(lower))
            return ([lower], "DATASET_SCOPE_LABEL");

        // Nested paths (e.g. kubernetes.namespace_name) split by dot into keypath.
        var parts = fieldName.Split('.');
        return (parts, "DATASET_SCOPE_USER_DATA");
    }
}
