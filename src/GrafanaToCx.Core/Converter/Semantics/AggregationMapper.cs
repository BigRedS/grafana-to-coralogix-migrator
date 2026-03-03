using GrafanaToCx.Core.Converter.PanelConverters;
using Newtonsoft.Json.Linq;

namespace GrafanaToCx.Core.Converter.Semantics;

public interface IAggregationMapper
{
    JObject MapLogsAggregation(JArray metrics);
    string MapDataPrimeAggregation(JArray metrics);
}

public sealed class AggregationMapper : IAggregationMapper
{
    public JObject MapLogsAggregation(JArray metrics)
    {
        var metric = metrics.Children<JObject>().FirstOrDefault();
        var type = (metric?.Value<string>("type") ?? "count").ToLowerInvariant();
        var field = CxFieldHelper.StripLogsFieldSuffixes(metric?.Value<string>("field") ?? string.Empty);
        var percentile = ResolvePercentile(metric);

        return type switch
        {
            "sum" when !string.IsNullOrWhiteSpace(field) => new JObject { ["sum"] = new JObject { ["field"] = field } },
            "avg" when !string.IsNullOrWhiteSpace(field) => new JObject { ["average"] = new JObject { ["field"] = field } },
            "min" when !string.IsNullOrWhiteSpace(field) => new JObject { ["min"] = new JObject { ["field"] = field } },
            "max" when !string.IsNullOrWhiteSpace(field) => new JObject { ["max"] = new JObject { ["field"] = field } },
            "percentile" when !string.IsNullOrWhiteSpace(field) => new JObject
            {
                ["percentile"] = new JObject
                {
                    ["field"] = field,
                    ["percent"] = percentile
                }
            },
            "distinct" when !string.IsNullOrWhiteSpace(field) => new JObject
            {
                ["distinctCount"] = new JObject { ["field"] = field }
            },
            _ => new JObject { ["count"] = new JObject() }
        };
    }

    public string MapDataPrimeAggregation(JArray metrics)
    {
        var metric = metrics.Children<JObject>().FirstOrDefault();
        var type = (metric?.Value<string>("type") ?? "count").ToLowerInvariant();
        var field = CxFieldHelper.StripLogsFieldSuffixes(metric?.Value<string>("field") ?? string.Empty);
        var percentile = ResolvePercentile(metric);

        return type switch
        {
            "sum" when !string.IsNullOrWhiteSpace(field) => $"sum({field})",
            "avg" when !string.IsNullOrWhiteSpace(field) => $"avg({field})",
            "min" when !string.IsNullOrWhiteSpace(field) => $"min({field})",
            "max" when !string.IsNullOrWhiteSpace(field) => $"max({field})",
            "percentile" when !string.IsNullOrWhiteSpace(field) => $"percentile({field}, {percentile})",
            "distinct" when !string.IsNullOrWhiteSpace(field) => $"distinct({field})",
            _ => "count()"
        };
    }

    private static int ResolvePercentile(JObject? metric)
    {
        var percentToken = metric?["settings"]?["percent"] ?? metric?["settings"]?["percents"]?[0];
        if (percentToken?.Type == JTokenType.Integer && percentToken.Value<int>() is > 0 and <= 100)
            return percentToken.Value<int>();

        return 95;
    }
}
