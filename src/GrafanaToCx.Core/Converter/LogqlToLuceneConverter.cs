using System.Text.RegularExpressions;

namespace GrafanaToCx.Core.Converter;

/// <summary>
/// Converts a LogQL expression (Grafana/Loki) to a Lucene query string (Coralogix).
///
/// Supported LogQL constructs:
///   Label selectors : {k="v"}  {k!="v"}  {k=~"regex"}  {k!~"regex"}
///   Line filters    : |= "text"  != "text"  |~ "regex"  !~ "regex"
///   Metric queries  : sum(rate({...}[5m])) by (label)
///                     → inner selector extracted, by-clause mapped to columns
/// </summary>
public static class LogqlToLuceneConverter
{
    // Maps common Loki label names to Coralogix Lucene field names.
    private static readonly Dictionary<string, string> FieldMapping = new(StringComparer.OrdinalIgnoreCase)
    {
        ["namespace"]  = "kubernetes.namespace_name",
        ["pod"]        = "kubernetes.pod_name",
        ["container"]  = "kubernetes.container_name",
        ["node"]       = "kubernetes.node_name",
        ["app"]        = "kubernetes.labels.app",
        ["component"]  = "kubernetes.labels.component",
        ["instance"]   = "kubernetes.labels.instance",
    };

    // NOT anchored — label block may appear anywhere (e.g. inside metric functions).
    private static readonly Regex LabelBlockRegex =
        new(@"\{([^}]*)\}", RegexOptions.Compiled);

    private static readonly Regex LabelPairRegex =
        new(@"(\w+)\s*(=~|!~|!=|=)\s*""([^""]*?)""", RegexOptions.Compiled);

    // Line filter pipeline: |= "..." |~ "..." != "..." !~ "..."
    private static readonly Regex LineFilterRegex =
        new(@"(\|=|!=|\|~|!~)\s*""([^""]*?)""", RegexOptions.Compiled);

    // Detects metric aggregation functions wrapping a log selector.
    private static readonly Regex MetricFunctionRegex =
        new(@"\b(sum|count|avg|min|max|rate|count_over_time|bytes_rate|bytes_over_time|absent_over_time|topk|bottomk|sort|sort_desc)\s*\(",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Extracts "by (field1, field2)" or "without (field1)" grouping clause.
    private static readonly Regex GroupByRegex =
        new(@"\b(by|without)\s*\(([^)]+)\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // -------------------------------------------------------------------------

    /// <summary>
    /// Converts a LogQL expression to a Lucene query string.
    /// For metric queries the inner log selector and filter pipeline are extracted first.
    /// </summary>
    public static string Convert(string logqlExpr)
    {
        if (string.IsNullOrWhiteSpace(logqlExpr))
            return "*";

        // Strip metric function wrappers — work only with the log part.
        var logPart = ExtractLogPart(logqlExpr);

        var parts = new List<string>();

        // --- label selector ---
        var labelMatch = LabelBlockRegex.Match(logPart);
        if (labelMatch.Success)
        {
            foreach (Match pair in LabelPairRegex.Matches(labelMatch.Groups[1].Value))
            {
                var label = pair.Groups[1].Value;
                var op    = pair.Groups[2].Value;
                var value = pair.Groups[3].Value;
                var field = FieldMapping.TryGetValue(label, out var mapped) ? mapped : label;

                parts.Add(op switch
                {
                    "="  => $"{field}:{QuoteLucene(value)}",
                    "!=" => $"NOT {field}:{QuoteLucene(value)}",
                    "=~" => $"{field}:/{EscapeRegex(value)}/",
                    "!~" => $"NOT {field}:/{EscapeRegex(value)}/",
                    _    => $"{field}:{QuoteLucene(value)}"
                });
            }
        }

        // --- line filter pipeline (everything after the label block) ---
        var suffix = labelMatch.Success
            ? logPart[(labelMatch.Index + labelMatch.Length)..]
            : logPart;

        foreach (Match filter in LineFilterRegex.Matches(suffix))
        {
            var op   = filter.Groups[1].Value;
            var text = filter.Groups[2].Value;

            parts.Add(op switch
            {
                "|="  => $"\"{EscapeLucene(text)}\"",
                "!="  => $"NOT \"{EscapeLucene(text)}\"",
                "|~"  => $"/{EscapeRegex(text)}/",
                "!~"  => $"NOT /{EscapeRegex(text)}/",
                _     => $"\"{EscapeLucene(text)}\""
            });
        }

        return parts.Count == 0 ? "*" : string.Join(" AND ", parts);
    }

    /// <summary>
    /// Returns Coralogix field names from a LogQL "by (...)" grouping clause.
    /// E.g. "sum(rate({app=\"nginx\"}[5m])) by (namespace, pod)"
    ///   → ["kubernetes.namespace_name", "kubernetes.pod_name"]
    /// </summary>
    public static IReadOnlyList<string> ExtractGroupByFields(string logqlExpr)
    {
        if (string.IsNullOrWhiteSpace(logqlExpr))
            return [];

        var match = GroupByRegex.Match(logqlExpr);
        if (!match.Success)
            return [];

        return match.Groups[2].Value
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(f => FieldMapping.TryGetValue(f, out var cx) ? cx : f)
            .ToList();
    }

    // -------------------------------------------------------------------------
    // Private helpers

    /// <summary>
    /// For metric queries, extracts just the log selector block and any inline
    /// filter pipeline, discarding aggregation wrappers and range intervals.
    ///
    /// Examples:
    ///   sum(count_over_time({namespace="test"}[5m])) by (ns) → {namespace="test"}
    ///   rate({job="nginx"}[1m]) |= "error"                   → {job="nginx"} |= "error"
    ///   {app="api"} |= "fail"                               → unchanged
    /// </summary>
    private static string ExtractLogPart(string expr)
    {
        if (!MetricFunctionRegex.IsMatch(expr))
            return expr;

        var braceStart = expr.IndexOf('{');
        if (braceStart < 0) return expr;

        var braceEnd = expr.IndexOf('}', braceStart);
        if (braceEnd < 0) return expr;

        var selector = expr[braceStart..(braceEnd + 1)];

        // Capture filter pipeline stages after the selector, stopping at [ (range) or ) (closing paren).
        var afterSelector = expr[(braceEnd + 1)..];
        var stopAt = afterSelector.IndexOfAny(['[', ')']);
        var pipeline = stopAt >= 0 ? afterSelector[..stopAt] : afterSelector;

        return selector + pipeline;
    }

    private static string QuoteLucene(string value)
    {
        if (!value.Any(c => char.IsWhiteSpace(c) || "+-&|!(){}[]^\"~*?:\\/".Contains(c)))
            return value;

        return $"\"{EscapeLucene(value)}\"";
    }

    private static string EscapeLucene(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string EscapeRegex(string value) =>
        value.Replace("/", "\\/");
}
