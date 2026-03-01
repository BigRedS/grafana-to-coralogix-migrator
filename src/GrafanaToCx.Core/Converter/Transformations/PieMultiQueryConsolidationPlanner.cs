using System.Text.RegularExpressions;
using GrafanaToCx.Core.Converter;
using Newtonsoft.Json.Linq;

namespace GrafanaToCx.Core.Converter.Transformations;

/// <summary>
/// Consolidates piechart panels with multiple Elasticsearch/OpenSearch targets where
/// Lucene queries differ only by one boolean predicate (e.g. payload.isEmail:true vs payload.isEmail:false)
/// into a single DataPrime group-by representation.
/// </summary>
public sealed class PieMultiQueryConsolidationPlanner : ITransformationPlanner
{
    // Matches when entire query is a boolean predicate: payload.isEmail:true (preserves full dotted path)
    private static readonly Regex SimpleBooleanPredicateRegex = new(
        @"^([\w.]+):(true|false)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Matches trailing predicate: "foo AND payload.isEmail:true" (preserves full dotted path)
    private static readonly Regex TrailingPredicateRegex = new(
        @"\s+(AND\s+)?([\w.]+):(true|false)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public TransformationPlan Plan(TransformationContext context)
    {
        if (!string.Equals(context.PanelType, "piechart", StringComparison.OrdinalIgnoreCase))
            return new TransformationPlan.Success();

        var visibleTargets = context.Targets
            .Children<JObject>()
            .Where(t => t.Value<bool?>("hide") != true)
            .ToList();

        if (visibleTargets.Count < 2)
            return new TransformationPlan.Success();

        if (!visibleTargets.All(IsElasticsearchTarget))
        {
            return new TransformationPlan.Failure(
                "Pie chart has multiple targets with unsupported datasource mix. " +
                "Only Elasticsearch/OpenSearch boolean-split targets can be consolidated to a single DataPrime group-by query.");
        }

        if (!TryExtractConsolidation(visibleTargets, out var baseLucene, out var groupByField))
        {
            return new TransformationPlan.Failure(
                "Pie chart has multiple Elasticsearch/OpenSearch targets that cannot be consolidated. " +
                "Targets must differ only by a single boolean predicate (e.g. field:true vs field:false).");
        }

        var consolidated = BuildConsolidatedPayload(baseLucene, groupByField, visibleTargets[0]);
        return new TransformationPlan.Success(consolidated);
    }

    private static bool IsElasticsearchTarget(JObject target)
    {
        var dsType = target["datasource"]?["type"]?.ToString();
        if (dsType?.Equals("elasticsearch", StringComparison.OrdinalIgnoreCase) == true ||
            dsType?.Equals("opensearch", StringComparison.OrdinalIgnoreCase) == true)
            return true;
        return target["bucketAggs"] != null && target["expr"] == null;
    }

    private static bool TryExtractConsolidation(
        List<JObject> targets,
        out string baseLucene,
        out string groupByField)
    {
        baseLucene = string.Empty;
        groupByField = string.Empty;

        var predicates = new List<(string Base, string Field, string Value)>();
        foreach (var target in targets)
        {
            var raw = target.Value<string>("query") ?? string.Empty;
            var query = QueryHelpers.NormalizeVariablePlaceholders(raw).Trim();
            if (string.IsNullOrWhiteSpace(query))
                query = "*";

            if (!TryParseBooleanPredicate(query, out var b, out var f, out var v))
                return false;

            predicates.Add((b, f, v));
        }

        if (predicates.Count < 2)
            return false;

        var firstField = predicates[0].Field;
        if (predicates.Any(p => !string.Equals(p.Field, firstField, StringComparison.Ordinal)))
            return false;

        var firstBase = predicates[0].Base;
        if (predicates.Any(p => !string.Equals(NormalizeBase(p.Base), NormalizeBase(firstBase), StringComparison.Ordinal)))
            return false;

        var values = predicates.Select(p => p.Value.ToLowerInvariant()).Distinct().ToList();
        if (values.Count != 2 || !values.Contains("true") || !values.Contains("false"))
            return false;

        baseLucene = string.IsNullOrWhiteSpace(firstBase) || firstBase == "*" ? "*" : firstBase.Trim();
        groupByField = firstField;
        return true;
    }

    private static string NormalizeBase(string b)
    {
        var t = b.Trim();
        if (string.IsNullOrWhiteSpace(t) || t == "*") return "*";
        return t;
    }

    private static bool TryParseBooleanPredicate(string query, out string basePart, out string field, out string value)
    {
        basePart = string.Empty;
        field = string.Empty;
        value = string.Empty;

        // Case 1: Entire query is predicate (e.g. payload.isEmail:true) — preserve full dotted path
        var simple = SimpleBooleanPredicateRegex.Match(query);
        if (simple.Success)
        {
            basePart = string.Empty;
            field = simple.Groups[1].Value;
            value = simple.Groups[2].Value;
            return true;
        }

        // Case 2: Trailing predicate (e.g. foo AND payload.isEmail:true)
        var m = TrailingPredicateRegex.Match(query);
        if (m.Success)
        {
            basePart = query[..m.Index].Trim();
            field = m.Groups[2].Value;
            value = m.Groups[3].Value;
            return true;
        }

        return false;
    }

    private static JObject BuildConsolidatedPayload(string baseLucene, string groupByField, JObject firstTarget)
    {
        var luceneFilter = string.IsNullOrWhiteSpace(baseLucene) || baseLucene == "*"
            ? string.Empty
            : baseLucene;

        var dataPrimeQuery = string.IsNullOrWhiteSpace(luceneFilter)
            ? $"source logs | groupby {groupByField} agg count()"
            : $"source logs | lucene '{EscapeForDataPrimeLiteral(luceneFilter)}' | groupby {groupByField} agg count()";

        var logsQuery = BuildLogsQuery(firstTarget, baseLucene, groupByField);
        var dataPrimeObj = new JObject { ["value"] = dataPrimeQuery };

        return new JObject
        {
            ["logs"] = logsQuery,
            ["dataPrime"] = dataPrimeObj
        };
    }

    private static string EscapeForDataPrimeLiteral(string lucene)
    {
        return lucene.Replace("\\", "\\\\").Replace("'", "\\'");
    }

    private static JObject BuildLogsQuery(JObject target, string baseLucene, string groupByField)
    {
        var groupNamesFields = new JArray
        {
            PanelConverters.CxFieldHelper.ToGroupByField(groupByField)
        };

        var aggregation = BuildLogsAggregation(target["metrics"] as JArray ?? new JArray());
        var luceneQuery = string.IsNullOrWhiteSpace(baseLucene) || baseLucene == "*"
            ? $"{groupByField}:*"
            : $"{baseLucene} AND {groupByField}:*";

        var logsQuery = new JObject
        {
            ["aggregation"] = aggregation,
            ["filters"] = new JArray(),
            ["groupNamesFields"] = groupNamesFields
        };

        var normalized = QueryHelpers.NormalizeVariablePlaceholders(luceneQuery);
        if (!string.IsNullOrWhiteSpace(normalized) && normalized != "*")
            logsQuery["luceneQuery"] = new JObject { ["value"] = normalized };

        return logsQuery;
    }

    private static JObject BuildLogsAggregation(JArray metrics)
    {
        var first = metrics?.Children<JObject>().FirstOrDefault();
        var type = first?.Value<string>("type")?.ToLowerInvariant();
        var field = PanelConverters.CxFieldHelper.StripLogsFieldSuffixes(first?.Value<string>("field") ?? "");

        return type switch
        {
            "sum" => new JObject { ["sum"] = new JObject { ["field"] = field } },
            "avg" => new JObject { ["average"] = new JObject { ["field"] = field } },
            "min" => new JObject { ["min"] = new JObject { ["field"] = field } },
            "max" => new JObject { ["max"] = new JObject { ["field"] = field } },
            _ => new JObject { ["count"] = new JObject() }
        };
    }
}
