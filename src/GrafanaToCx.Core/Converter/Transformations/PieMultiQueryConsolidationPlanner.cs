using GrafanaToCx.Core.Converter;
using GrafanaToCx.Core.Converter.Parsing;
using GrafanaToCx.Core.Converter.Semantics;
using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;

namespace GrafanaToCx.Core.Converter.Transformations;

/// <summary>
/// Consolidates piechart/timeseries panels with multiple Elasticsearch/OpenSearch targets where
/// Lucene queries differ only by one boolean predicate (e.g. payload.isEmail:true vs payload.isEmail:false)
/// into a single DataPrime group-by representation.
/// Also supports allowlisted barchart panels where all targets share the same Lucene/date_histogram
/// semantics and vary by metric field/alias.
/// </summary>
public sealed class PieMultiQueryConsolidationPlanner : ITransformationPlanner
{
    private static readonly IAggregationMapper AggregationMapper = new AggregationMapper();
    private static readonly ILuceneLikeExpressionParser LuceneParser = new LuceneLikeExpressionParser();
    private readonly MultiLuceneMergeOptions _mergeOptions;

    public PieMultiQueryConsolidationPlanner(MultiLuceneMergeOptions mergeOptions)
    {
        _mergeOptions = mergeOptions;
    }

    public TransformationPlan Plan(TransformationContext context)
    {
        var isPieChart = string.Equals(context.PanelType, "piechart", StringComparison.OrdinalIgnoreCase);
        var isTimeSeries = string.Equals(context.PanelType, "timeseries", StringComparison.OrdinalIgnoreCase);
        var isBarChart = string.Equals(context.PanelType, "barchart", StringComparison.OrdinalIgnoreCase);
        if (!isPieChart && !isTimeSeries && !isBarChart)
            return new TransformationPlan.Success();

        var visibleTargets = context.Targets
            .Children<JObject>()
            .Where(t => t.Value<bool?>("hide") != true)
            .Select((target, index) => (target, index))
            .OrderBy(t => t.target.Value<string>("refId") ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(t => t.index)
            .Select(t => t.target)
            .ToList();

        if (visibleTargets.Count < 2)
            return new TransformationPlan.Success(SelectedTargets: visibleTargets);

        if (isTimeSeries && !_mergeOptions.IsAllowlistedType(context.PanelType))
            return new TransformationPlan.Success(SelectedTargets: visibleTargets);

        if (isBarChart && !_mergeOptions.IsAllowlistedType(context.PanelType))
            return new TransformationPlan.Success(SelectedTargets: visibleTargets);

        if (isBarChart)
            return PlanBarChartMetricVariation(context.PanelTitle, visibleTargets);

        if (!_mergeOptions.IsAllowlistedType(context.PanelType))
            return BuildSkipPlan(
                visibleTargets,
                "DGR-LMG-001",
                "Multi-Lucene merge skipped: widget type is not allowlisted.");

        if (!visibleTargets.All(IsElasticsearchTarget))
        {
            return BuildSkipPlan(
                visibleTargets,
                "DGR-LMG-002",
                "Multi-Lucene merge skipped: visible targets are not all Elasticsearch/OpenSearch queries.");
        }

        var parsedPredicates = new List<IReadOnlyList<LucenePredicate>>(visibleTargets.Count);
        foreach (var target in visibleTargets)
        {
            if (!TryParseTargetPredicates(target, out var predicates))
            {
                return BuildSkipPlan(
                    visibleTargets,
                    "DGR-LMG-003",
                    "Multi-Lucene merge skipped: at least one Lucene target failed strict predicate parsing.");
            }

            parsedPredicates.Add(predicates);
        }

        if (!TryResolveCommonAndVaryingPredicates(parsedPredicates, out var commonPredicates, out var varyingPredicates))
        {
            return BuildSkipPlan(
                visibleTargets,
                "DGR-LMG-004",
                "Multi-Lucene merge skipped: targets do not differ by exactly one predicate on the same field/operator.");
        }

        if (!TryResolveAggregation(visibleTargets, out var aggregation, out var aggregationCode, out var aggregationReason))
            return BuildSkipPlan(visibleTargets, aggregationCode, aggregationReason);

        if (!TryResolveDateHistogram(visibleTargets, out var histogram, out var histogramCode, out var histogramReason))
            return BuildSkipPlan(visibleTargets, histogramCode, histogramReason);

        var groupByField = PanelConverters.CxFieldHelper.StripLogsFieldSuffixes(varyingPredicates[0].Field);
        if (string.IsNullOrWhiteSpace(groupByField))
            groupByField = varyingPredicates[0].Field;

        var baseFilter = string.Join(
            " AND ",
            commonPredicates
                .OrderBy(p => p.Signature, StringComparer.Ordinal)
                .Select(FormatPredicate));

        var payload = BuildConsolidatedPayload(baseFilter, groupByField, aggregation, histogram);
        if (isPieChart && !HasNonEmptyDataPrimeGroupNames(payload))
        {
            return BuildSkipPlan(
                visibleTargets,
                "DGR-LMG-010",
                "Multi-Lucene merge skipped: consolidated piechart payload requires non-empty dataprime.groupNames.");
        }

        return new TransformationPlan.Success(
            ConsolidatedQueryPayload: payload,
            SelectedTargets: visibleTargets,
            Decision: new PanelConversionDecision(
                "fallback",
                "Multi-Lucene merge applied: synthesized one DataPrime query from allowlisted targets.",
                "DGR-LMG-000",
                [],
                "merge-multi-lucene",
                0.92));
    }

    private TransformationPlan PlanBarChartMetricVariation(string panelTitle, IReadOnlyList<JObject> visibleTargets)
    {
        if (!visibleTargets.All(IsElasticsearchTarget))
        {
            return BuildSkipPlan(
                visibleTargets,
                "DGR-BMG-002",
                "Bar multi-metric merge skipped: visible targets are not all Elasticsearch/OpenSearch queries.");
        }

        if (!TryResolveCanonicalLucene(visibleTargets, out var baseLucene, out var luceneCode, out var luceneReason))
            return BuildSkipPlan(visibleTargets, luceneCode, luceneReason);

        if (!TryResolveRequiredDateHistogram(visibleTargets, out var histogram, out var histogramCode, out var histogramReason))
            return BuildSkipPlan(visibleTargets, histogramCode, histogramReason);

        if (!TryResolveMetricVariationAggregations(visibleTargets, out var aggregations, out var metricCode, out var metricReason))
            return BuildSkipPlan(visibleTargets, metricCode, metricReason);

        var payload = BuildBarChartConsolidatedPayload(baseLucene, histogram!, aggregations);
        return new TransformationPlan.Success(
            ConsolidatedQueryPayload: payload,
            SelectedTargets: visibleTargets,
            Decision: new PanelConversionDecision(
                "fallback",
                $"Bar multi-metric merge applied for allowlisted panel '{panelTitle}'.",
                "DGR-BMG-000",
                [],
                "merge-multi-lucene-metric-variation",
                0.93));
    }

    private static bool TryResolveDateHistogram(
        IReadOnlyList<JObject> targets,
        out DateHistogramSemantics? histogram,
        out string code,
        out string reason)
    {
        histogram = null;
        code = string.Empty;
        reason = string.Empty;

        var parsed = new List<DateHistogramSemantics?>(targets.Count);
        foreach (var target in targets)
        {
            if (!TryExtractDateHistogram(target, out var semantics))
            {
                code = "DGR-LMG-007";
                reason = "Multi-Lucene merge skipped: date_histogram definition is invalid or incomplete.";
                return false;
            }

            parsed.Add(semantics);
        }

        var hasHistogram = parsed.Any(p => p != null);
        if (!hasHistogram)
            return true;

        if (parsed.Any(p => p == null))
        {
            code = "DGR-LMG-008";
            reason = "Multi-Lucene merge skipped: date_histogram presence is inconsistent across visible targets.";
            return false;
        }

        var definitions = parsed.OfType<DateHistogramSemantics>().ToList();
        var fieldCount = definitions.Select(d => d.Field).Distinct(StringComparer.Ordinal).Count();
        var intervalCount = definitions.Select(d => d.Interval).Distinct(StringComparer.Ordinal).Count();
        if (fieldCount != 1 || intervalCount != 1)
        {
            code = "DGR-LMG-009";
            reason = "Multi-Lucene merge skipped: date_histogram field/interval semantics are inconsistent across visible targets.";
            return false;
        }

        histogram = definitions[0];
        return true;
    }

    private static bool TryResolveRequiredDateHistogram(
        IReadOnlyList<JObject> targets,
        out DateHistogramSemantics? histogram,
        out string code,
        out string reason)
    {
        histogram = null;
        code = string.Empty;
        reason = string.Empty;

        if (!TryResolveDateHistogram(targets, out var resolved, out code, out reason))
            return false;

        if (resolved == null)
        {
            code = "DGR-BMG-004";
            reason = "Bar multi-metric merge skipped: date_histogram is required and must be consistent across visible targets.";
            return false;
        }

        histogram = resolved;
        return true;
    }

    private static bool TryExtractDateHistogram(JObject target, out DateHistogramSemantics? histogram)
    {
        histogram = null;
        var buckets = target["bucketAggs"] as JArray;
        if (buckets == null || buckets.Count == 0)
            return true;

        var dateBuckets = buckets
            .Children<JObject>()
            .Where(b => string.Equals(b.Value<string>("type"), "date_histogram", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (dateBuckets.Count == 0)
            return true;
        if (dateBuckets.Count > 1)
            return false;

        var bucket = dateBuckets[0];
        var field = PanelConverters.CxFieldHelper.StripLogsFieldSuffixes(bucket.Value<string>("field") ?? string.Empty);
        var intervalRaw = bucket["settings"]?["interval"]?.ToString() ?? string.Empty;
        var interval = QueryHelpers.NormalizeVariablePlaceholders(intervalRaw).Trim();
        if (string.IsNullOrWhiteSpace(field) || string.IsNullOrWhiteSpace(interval))
            return false;

        histogram = new DateHistogramSemantics(field, interval);
        return true;
    }

    private static TransformationPlan.Success BuildSkipPlan(
        IReadOnlyList<JObject> visibleTargets,
        string code,
        string reason)
    {
        var selected = visibleTargets.Count > 0
            ? new List<JObject> { visibleTargets[0] }
            : new List<JObject>();
        var dropped = visibleTargets.Skip(1).Select(GetTargetIdentity).ToList();

        return new TransformationPlan.Success(
            SelectedTargets: selected,
            Decision: new PanelConversionDecision(
                "fallback",
                reason,
                code,
                dropped,
                "select-one",
                0.72));
    }

    private static bool TryResolveAggregation(
        IReadOnlyList<JObject> targets,
        out string aggregation,
        out string code,
        out string reason)
    {
        aggregation = string.Empty;
        code = string.Empty;
        reason = string.Empty;

        var signatures = targets.Select(GetMetricSignature).Distinct(StringComparer.Ordinal).ToList();
        if (signatures.Count != 1)
        {
            code = "DGR-LMG-005";
            reason = "Multi-Lucene merge skipped: target metric semantics are inconsistent across visible targets.";
            return false;
        }

        var firstMetric = (targets[0]["metrics"] as JArray)?.Children<JObject>().FirstOrDefault();
        var metricType = (firstMetric?.Value<string>("type") ?? "count").ToLowerInvariant();
        var metricField = PanelConverters.CxFieldHelper.StripLogsFieldSuffixes(firstMetric?.Value<string>("field") ?? string.Empty);
        if ((metricType is "sum" or "avg" or "min" or "max" or "percentile" or "distinct") &&
            string.IsNullOrWhiteSpace(metricField))
        {
            code = "DGR-LMG-006";
            reason = "Multi-Lucene merge skipped: metric aggregation requires a field but no field is defined.";
            return false;
        }

        aggregation = AggregationMapper.MapDataPrimeAggregation(targets[0]["metrics"] as JArray ?? []);
        return true;
    }

    private static bool TryResolveMetricVariationAggregations(
        IReadOnlyList<JObject> targets,
        out IReadOnlyList<BarAggregationSeries> aggregations,
        out string code,
        out string reason)
    {
        aggregations = [];
        code = string.Empty;
        reason = string.Empty;

        var parsedMetrics = new List<(string Type, string Field, int Percentile, string RefId, string Alias)>(targets.Count);
        foreach (var target in targets)
        {
            var metric = (target["metrics"] as JArray)?.Children<JObject>().FirstOrDefault();
            if (metric == null)
            {
                code = "DGR-BMG-006";
                reason = "Bar multi-metric merge skipped: at least one target has no metric definition.";
                return false;
            }

            var type = (metric.Value<string>("type") ?? "count").ToLowerInvariant();
            var field = PanelConverters.CxFieldHelper.StripLogsFieldSuffixes(metric.Value<string>("field") ?? string.Empty);
            var percentile = metric["settings"]?["percent"]?.Value<int?>()
                             ?? metric["settings"]?["percents"]?[0]?.Value<int?>()
                             ?? 95;
            var refId = target.Value<string>("refId") ?? string.Empty;
            var alias = target.Value<string>("alias") ?? string.Empty;

            parsedMetrics.Add((type, field, percentile, refId, alias));
        }

        var metricType = parsedMetrics[0].Type;
        var metricPercent = parsedMetrics[0].Percentile;
        if (parsedMetrics.Any(m => !string.Equals(m.Type, metricType, StringComparison.Ordinal)))
        {
            code = "DGR-BMG-005";
            reason = "Bar multi-metric merge skipped: metric types are incompatible across visible targets.";
            return false;
        }

        if (string.Equals(metricType, "percentile", StringComparison.Ordinal) &&
            parsedMetrics.Any(m => m.Percentile != metricPercent))
        {
            code = "DGR-BMG-007";
            reason = "Bar multi-metric merge skipped: percentile values are inconsistent across visible targets.";
            return false;
        }

        var requiresField = metricType is "sum" or "avg" or "min" or "max" or "percentile" or "distinct";
        if (requiresField && parsedMetrics.Any(m => string.IsNullOrWhiteSpace(m.Field)))
        {
            code = "DGR-BMG-008";
            reason = "Bar multi-metric merge skipped: one or more metrics require a field but no field is defined.";
            return false;
        }

        var supported = metricType is "count" or "sum" or "avg" or "min" or "max" or "percentile" or "distinct";
        if (!supported)
        {
            code = "DGR-BMG-009";
            reason = "Bar multi-metric merge skipped: unsupported metric type for DataPrime metric-variation merge.";
            return false;
        }

        var usedAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<BarAggregationSeries>(parsedMetrics.Count);
        foreach (var metric in parsedMetrics)
        {
            var expression = BuildAggregationExpression(metric.Type, metric.Field, metric.Percentile);
            var alias = BuildAggregationAlias(metric, usedAliases);
            result.Add(new BarAggregationSeries(expression, alias));
        }

        aggregations = result;
        return true;
    }

    private static string BuildAggregationExpression(string metricType, string field, int percentile)
    {
        return metricType switch
        {
            "count" => "count()",
            "sum" => $"sum({field})",
            "avg" => $"avg({field})",
            "min" => $"min({field})",
            "max" => $"max({field})",
            "percentile" => $"percentile({field}, {percentile})",
            "distinct" => $"distinct({field})",
            _ => "count()"
        };
    }

    private static string BuildAggregationAlias(
        (string Type, string Field, int Percentile, string RefId, string Alias) metric,
        HashSet<string> usedAliases)
    {
        var preferred = !string.IsNullOrWhiteSpace(metric.Alias)
            ? metric.Alias
            : (!string.IsNullOrWhiteSpace(metric.RefId) ? metric.RefId : metric.Field);

        var normalized = Regex.Replace(preferred, @"[^a-zA-Z0-9_]", "_");
        normalized = normalized.Trim('_');
        if (string.IsNullOrWhiteSpace(normalized))
            normalized = "series";

        var candidate = normalized;
        var suffix = 2;
        while (!usedAliases.Add(candidate))
        {
            candidate = $"{normalized}_{suffix}";
            suffix++;
        }

        return candidate;
    }

    private static bool TryResolveCanonicalLucene(
        IReadOnlyList<JObject> targets,
        out string canonicalLucene,
        out string code,
        out string reason)
    {
        canonicalLucene = string.Empty;
        code = string.Empty;
        reason = string.Empty;

        string? first = null;
        foreach (var target in targets)
        {
            var rawQuery = target.Value<string>("query") ?? string.Empty;
            var normalizedQuery = QueryHelpers.NormalizeVariablePlaceholders(rawQuery).Trim();
            if (!TryBuildCanonicalPredicateKey(target, normalizedQuery, out var key))
            {
                code = "DGR-BMG-003";
                reason = "Bar multi-metric merge skipped: at least one Lucene target failed strict parsing and boundary-safe predicate canonicalization.";
                return false;
            }

            if (first == null)
            {
                first = key;
                canonicalLucene = normalizedQuery;
                continue;
            }

            if (!string.Equals(first, key, StringComparison.Ordinal))
            {
                code = "DGR-BMG-001";
                reason = "Bar multi-metric merge skipped: visible targets must share the same normalized Lucene predicates.";
                return false;
            }
        }

        return true;
    }

    private static bool TryBuildCanonicalPredicateKey(JObject target, string normalizedQuery, out string key)
    {
        key = string.Empty;
        if (TryParseTargetPredicates(target, out var predicates))
        {
            key = BuildSignatureKey(predicates.Select(p => p.Signature));
            return true;
        }

        if (!TryBuildBoundaryCanonicalPredicateKey(normalizedQuery, out var fallbackKey))
            return false;

        key = fallbackKey;
        return true;
    }

    private static bool TryBuildBoundaryCanonicalPredicateKey(string query, out string key)
    {
        key = string.Empty;
        if (string.IsNullOrWhiteSpace(query) || query == "*")
        {
            key = string.Empty;
            return true;
        }

        var masked = MaskQuotedSegments(query);
        if (masked.IndexOf('(') >= 0 || masked.IndexOf(')') >= 0)
            return false;
        if (Regex.IsMatch(masked, @"\b(?:OR|NOT)\b", RegexOptions.IgnoreCase))
            return false;

        var andMatches = Regex.Matches(masked, @"\s+AND\s+", RegexOptions.IgnoreCase);
        var signatures = new List<string>(andMatches.Count + 1);
        var start = 0;
        foreach (Match andMatch in andMatches)
        {
            var clause = query[start..andMatch.Index].Trim();
            if (!TryBuildBoundaryPredicateSignature(clause, out var signature))
                return false;
            signatures.Add(signature);
            start = andMatch.Index + andMatch.Length;
        }

        var trailingClause = query[start..].Trim();
        if (!TryBuildBoundaryPredicateSignature(trailingClause, out var trailingSignature))
            return false;
        signatures.Add(trailingSignature);

        key = BuildSignatureKey(signatures);
        return true;
    }

    private static string BuildSignatureKey(IEnumerable<string> signatures) =>
        string.Join("|", signatures.OrderBy(s => s, StringComparer.Ordinal));

    private static bool TryBuildBoundaryPredicateSignature(string clause, out string signature)
    {
        signature = string.Empty;
        if (string.IsNullOrWhiteSpace(clause))
            return false;

        var colonIndex = FindFirstUnquotedColon(clause);
        if (colonIndex <= 0 || colonIndex >= clause.Length - 1)
            return false;

        var field = clause[..colonIndex].Trim();
        var value = clause[(colonIndex + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(field) || field.Any(char.IsWhiteSpace))
            return false;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        value = NormalizeBoundaryPredicateValue(value);
        if (string.IsNullOrWhiteSpace(value) || IsUnsupportedPredicateValue(value))
            return false;

        signature = $"{field}:{value}";
        return true;
    }

    private static int FindFirstUnquotedColon(string text)
    {
        var inQuotes = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '"' && !IsEscaped(text, i))
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (!inQuotes && c == ':')
                return i;
        }

        return -1;
    }

    private static string NormalizeBoundaryPredicateValue(string value)
    {
        var normalized = value.Trim();
        if (normalized.StartsWith("\\\"", StringComparison.Ordinal) &&
            normalized.EndsWith("\\\"", StringComparison.Ordinal) &&
            normalized.Length >= 4)
        {
            normalized = normalized[2..^2];
        }
        else if (normalized.StartsWith('"') && normalized.EndsWith('"') && normalized.Length >= 2)
        {
            normalized = normalized[1..^1];
        }

        normalized = normalized
            .Replace("\\\"", "\"", StringComparison.Ordinal)
            .Replace("\\\\", "\\", StringComparison.Ordinal)
            .Trim();

        return normalized;
    }

    private static string MaskQuotedSegments(string text)
    {
        var chars = text.ToCharArray();
        var inQuotes = false;
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] == '"' && !IsEscaped(text, i))
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (inQuotes)
                chars[i] = 'Q';
        }

        return new string(chars);
    }

    private static bool IsEscaped(string text, int index)
    {
        var slashCount = 0;
        for (var i = index - 1; i >= 0 && text[i] == '\\'; i--)
            slashCount++;

        return slashCount % 2 == 1;
    }

    private static string GetMetricSignature(JObject target)
    {
        var metric = (target["metrics"] as JArray)?.Children<JObject>().FirstOrDefault();
        var type = (metric?.Value<string>("type") ?? "count").ToLowerInvariant();
        var field = PanelConverters.CxFieldHelper.StripLogsFieldSuffixes(metric?.Value<string>("field") ?? string.Empty);
        var percentile = metric?["settings"]?["percent"]?.Value<int?>()
                         ?? metric?["settings"]?["percents"]?[0]?.Value<int?>()
                         ?? 95;
        return $"{type}|{field}|{percentile}";
    }

    private static bool TryResolveCommonAndVaryingPredicates(
        IReadOnlyList<IReadOnlyList<LucenePredicate>> parsedPredicates,
        out IReadOnlyList<LucenePredicate> commonPredicates,
        out IReadOnlyList<LucenePredicate> varyingPredicates)
    {
        commonPredicates = [];
        varyingPredicates = [];
        if (parsedPredicates.Count < 2)
            return false;

        var commonCounts = BuildCounts(parsedPredicates[0]);
        foreach (var predicates in parsedPredicates.Skip(1))
        {
            var currentCounts = BuildCounts(predicates);
            foreach (var key in commonCounts.Keys.ToList())
            {
                if (!currentCounts.TryGetValue(key, out var count))
                    commonCounts[key] = 0;
                else
                    commonCounts[key] = Math.Min(commonCounts[key], count);
            }
        }

        var common = ExpandPredicates(commonCounts, parsedPredicates);
        var varying = new List<LucenePredicate>(parsedPredicates.Count);
        foreach (var predicates in parsedPredicates)
        {
            var residual = Subtract(predicates, commonCounts);
            if (residual.Count != 1)
                return false;

            varying.Add(residual[0]);
        }

        var field = varying[0].Field;
        var op = varying[0].Operator;
        if (varying.Any(p =>
                !string.Equals(p.Field, field, StringComparison.Ordinal) ||
                !string.Equals(p.Operator, op, StringComparison.Ordinal)))
            return false;

        if (varying.Select(p => p.Value).Distinct(StringComparer.Ordinal).Count() < 2)
            return false;

        commonPredicates = common;
        varyingPredicates = varying;
        return true;
    }

    private static Dictionary<string, int> BuildCounts(IReadOnlyList<LucenePredicate> predicates)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var predicate in predicates)
        {
            counts.TryGetValue(predicate.Signature, out var count);
            counts[predicate.Signature] = count + 1;
        }

        return counts;
    }

    private static List<LucenePredicate> ExpandPredicates(
        Dictionary<string, int> commonCounts,
        IReadOnlyList<IReadOnlyList<LucenePredicate>> parsedPredicates)
    {
        var lookup = parsedPredicates
            .SelectMany(p => p)
            .GroupBy(p => p.Signature, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var result = new List<LucenePredicate>();
        foreach (var (signature, count) in commonCounts)
        {
            if (count <= 0 || !lookup.TryGetValue(signature, out var predicate))
                continue;

            for (var i = 0; i < count; i++)
                result.Add(predicate);
        }

        return result;
    }

    private static List<LucenePredicate> Subtract(
        IReadOnlyList<LucenePredicate> predicates,
        Dictionary<string, int> commonCounts)
    {
        var remaining = new Dictionary<string, int>(commonCounts, StringComparer.Ordinal);
        var result = new List<LucenePredicate>();
        foreach (var predicate in predicates)
        {
            if (remaining.TryGetValue(predicate.Signature, out var count) && count > 0)
            {
                remaining[predicate.Signature] = count - 1;
                continue;
            }

            result.Add(predicate);
        }

        return result;
    }

    private static bool TryParseTargetPredicates(JObject target, out IReadOnlyList<LucenePredicate> predicates)
    {
        predicates = [];
        var rawQuery = target.Value<string>("query") ?? string.Empty;
        var normalized = QueryHelpers.NormalizeVariablePlaceholders(rawQuery).Trim();

        var parseResult = LuceneParser.Parse(normalized);
        if (parseResult.Errors.Count > 0)
            return false;

        var output = new List<LucenePredicate>();
        if (parseResult.Root == null)
        {
            predicates = output;
            return true;
        }

        if (!TryCollectConjunctivePredicates(parseResult.Root, output))
            return false;

        predicates = output;
        return true;
    }

    private static bool TryCollectConjunctivePredicates(ExpressionAstNode node, List<LucenePredicate> output)
    {
        switch (node)
        {
            case ExpressionBinaryNode { Operator: ExpressionBinaryOperator.And } andNode:
                return TryCollectConjunctivePredicates(andNode.Left, output)
                    && TryCollectConjunctivePredicates(andNode.Right, output);

            case ExpressionPredicateNode predicateNode:
                if (!string.Equals(predicateNode.Operator, ":", StringComparison.Ordinal))
                    return false;
                if (string.IsNullOrWhiteSpace(predicateNode.Field))
                    return false;
                if (IsUnsupportedPredicateValue(predicateNode.Value))
                    return false;

                output.Add(new LucenePredicate(
                    predicateNode.Field,
                    predicateNode.Operator,
                    predicateNode.Value,
                    predicateNode.IsQuoted));
                return true;

            default:
                return false;
        }
    }

    private static bool IsUnsupportedPredicateValue(string value)
    {
        return value.Contains('*')
               || value.Contains('[')
               || value.Contains(']')
               || value.Contains('{')
               || value.Contains('}')
               || value.Contains(" TO ", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsElasticsearchTarget(JObject target)
    {
        var dsType = target["datasource"]?["type"]?.ToString();
        if (dsType?.Equals("elasticsearch", StringComparison.OrdinalIgnoreCase) == true ||
            dsType?.Equals("opensearch", StringComparison.OrdinalIgnoreCase) == true)
            return true;
        return target["bucketAggs"] != null && target["expr"] == null;
    }

    private static string GetTargetIdentity(JObject target) =>
        target.Value<string>("refId")
        ?? target.Value<string>("query")
        ?? "<unknown-target>";

    private static string FormatPredicate(LucenePredicate predicate)
    {
        var value = predicate.IsQuoted
            ? $"\"{predicate.Value.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : predicate.Value;
        return $"{predicate.Field}{predicate.Operator}{value}";
    }

    private static JObject BuildConsolidatedPayload(
        string baseLucene,
        string groupByField,
        string aggregation,
        DateHistogramSemantics? histogram)
    {
        var groupByClause = histogram == null
            ? $"groupby {groupByField}"
            : $"groupby $m.timestamp / {histogram.Interval}, {groupByField}";

        var dataPrimeQuery = string.IsNullOrWhiteSpace(baseLucene)
            ? $"source logs | {groupByClause} agg {aggregation}"
            : $"source logs | lucene '{EscapeForDataPrimeLiteral(baseLucene)}' | {groupByClause} agg {aggregation}";

        return new JObject
        {
            ["dataprime"] = new JObject
            {
                ["dataprimeQuery"] = new JObject
                {
                    ["text"] = dataPrimeQuery
                },
                ["filters"] = new JArray(),
                ["groupNames"] = new JArray(groupByField)
            }
        };
    }

    private static bool HasNonEmptyDataPrimeGroupNames(JObject payload)
    {
        return payload["dataprime"]?["groupNames"] is JArray groupNames && groupNames.Count > 0;
    }

    private static string EscapeForDataPrimeLiteral(string lucene) =>
        lucene.Replace("\\", "\\\\").Replace("'", "\\'");

    private static JObject BuildBarChartConsolidatedPayload(
        string baseLucene,
        DateHistogramSemantics histogram,
        IReadOnlyList<BarAggregationSeries> aggregations)
    {
        var aggregationClause = string.Join(", ", aggregations.Select(a => $"{a.Expression} as {a.Alias}"));
        var dataPrimeQuery = string.IsNullOrWhiteSpace(baseLucene)
            ? $"source logs | groupby $m.timestamp / {histogram.Interval} agg {aggregationClause}"
            : $"source logs | lucene '{EscapeForDataPrimeLiteral(baseLucene)}' | groupby $m.timestamp / {histogram.Interval} agg {aggregationClause}";

        return new JObject
        {
            ["dataprime"] = new JObject
            {
                ["dataprimeQuery"] = new JObject
                {
                    ["text"] = dataPrimeQuery
                },
                ["filters"] = new JArray()
            }
        };
    }

    private sealed record LucenePredicate(string Field, string Operator, string Value, bool IsQuoted)
    {
        public string Signature => $"{Field}{Operator}{Value}";
    }

    private sealed record BarAggregationSeries(string Expression, string Alias);

    private sealed record DateHistogramSemantics(string Field, string Interval);
}
