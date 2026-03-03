using System.Text.RegularExpressions;

namespace GrafanaToCx.Core.Converter;

public static class QueryHelpers
{
    private static readonly Dictionary<string, string> UnitMapping = new(StringComparer.OrdinalIgnoreCase)
    {
        ["none"] = "UNIT_UNSPECIFIED",
        ["short"] = "UNIT_UNSPECIFIED",
        ["bytes"] = "UNIT_BYTES",
        ["decbytes"] = "UNIT_BYTES",
        ["bits"] = "UNIT_BITS",
        ["Bps"] = "UNIT_UNSPECIFIED",
        ["binBps"] = "UNIT_UNSPECIFIED",
        ["bytes/sec"] = "UNIT_UNSPECIFIED",
        ["percent"] = "UNIT_PERCENT",
        ["percentunit"] = "UNIT_PERCENT",
        ["s"] = "UNIT_SECONDS",
        ["ms"] = "UNIT_MILLISECONDS",
        ["us"] = "UNIT_MICROSECONDS",
        ["µs"] = "UNIT_MICROSECONDS",
        ["ns"] = "UNIT_NANOSECONDS",
        ["reqps"] = "UNIT_UNSPECIFIED",
        ["rps"] = "UNIT_UNSPECIFIED",
        ["ops"] = "UNIT_UNSPECIFIED"
    };

    private static readonly Dictionary<string, string> CustomUnitMapping = new(StringComparer.OrdinalIgnoreCase)
    {
        ["reqps"] = "req/s",
        ["rps"] = "req/s",
        ["ops"] = "ops/s",
        ["VUs"] = "VUs",
        ["Bps"] = "bytes/s",
        ["binBps"] = "bytes/s",
        ["bytes/sec"] = "bytes/s"
    };

    // Gauge panels reject UNIT_UNSPECIFIED — map units that need a concrete fallback.
    private static readonly Dictionary<string, string> GaugeUnitOverrides = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Bps"] = "UNIT_BYTES",
        ["binBps"] = "UNIT_BYTES",
        ["bytes/sec"] = "UNIT_BYTES",
        ["reqps"] = "UNIT_UNSPECIFIED",
        ["rps"] = "UNIT_UNSPECIFIED",
        ["ops"] = "UNIT_UNSPECIFIED"
    };

    private static readonly Dictionary<string, string> TimeFrameMapping = new(StringComparer.OrdinalIgnoreCase)
    {
        ["now-1h"] = "3600s",
        ["now-3h"] = "10800s",
        ["now-6h"] = "21600s",
        ["now-12h"] = "43200s",
        ["now-24h"] = "86400s",
        ["now-1d"] = "86400s",
        ["now-7d"] = "604800s",
        ["now-30d"] = "2592000s"
    };

    public static string MapUnit(string grafanaUnit) =>
        UnitMapping.TryGetValue(grafanaUnit, out var value) ? value : "UNIT_UNSPECIFIED";

    public static string MapUnitForGauge(string grafanaUnit)
    {
        if (GaugeUnitOverrides.TryGetValue(grafanaUnit, out var overrideValue))
            return overrideValue;

        var mapped = MapUnit(grafanaUnit);
        return mapped == "UNIT_UNSPECIFIED" ? "UNIT_BYTES" : mapped;
    }

    public static string GetCustomUnit(string grafanaUnit) =>
        CustomUnitMapping.TryGetValue(grafanaUnit, out var value) ? value : string.Empty;

    public static string MapTimeFrame(string? grafanaFrom) =>
        grafanaFrom != null && TimeFrameMapping.TryGetValue(grafanaFrom, out var value) ? value : "3600s";

    /// <summary>
    /// Grafana built-in variable names that should not be wrapped in braces.
    /// These are replaced with literal values in CleanQuery before normalization.
    /// </summary>
    private static readonly HashSet<string> GrafanaBuiltInVariables = new(StringComparer.Ordinal)
    {
        "__rate_interval",
        "__auto_interval_interval",
        "__auto_interval",
        "__range",
        "__from",
        "__to",
        "interval",
        "quantile_stat"
    };

    /// <summary>
    /// Normalizes variable placeholders in query strings: $identifier → ${identifier}.
    /// Already braced ${identifier} is left unchanged. Grafana built-ins are skipped.
    /// </summary>
    public static string NormalizeVariablePlaceholders(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return query ?? string.Empty;

        return Regex.Replace(query, @"\$([a-zA-Z_][a-zA-Z0-9_]*)", match =>
        {
            var name = match.Groups[1].Value;
            if (GrafanaBuiltInVariables.Contains(name))
                return match.Value;
            return $"${{{name}}}";
        });
    }

    public static string CleanQuery(string query, ISet<string> discoveredMetrics)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return "up";
        }

        var metricMatch = Regex.Match(query, @"([a-zA-Z_:][a-zA-Z0-9_:]*)\{");
        if (metricMatch.Success)
        {
            discoveredMetrics.Add(metricMatch.Groups[1].Value);
        }

        query = query
            .Replace("$__rate_interval", "5m", StringComparison.Ordinal)
            .Replace("$__auto_interval_interval", "5m", StringComparison.Ordinal)
            .Replace("$__auto_interval", "5m", StringComparison.Ordinal)
            .Replace("$__range", "5m", StringComparison.Ordinal)
            .Replace("$__from", "now-1h", StringComparison.Ordinal)
            .Replace("$__to", "now", StringComparison.Ordinal)
            .Replace("$interval", "5m", StringComparison.Ordinal)
            .Replace("$quantile_stat", "p95", StringComparison.Ordinal)
            .Replace("${quantile_stat}", "p95", StringComparison.Ordinal);

        query = Regex.Replace(query, "testid=~\"\\$testid\"", "testid=~${testid}");
        query = Regex.Replace(query, "testid=~\"\\$\\{testid\\}\"", "testid=~${testid}");
        query = Regex.Replace(query, @"\[5m\]|\[1m\]|\[15m\]", "[${interval}]");

        query = NormalizeVariablePlaceholders(query);
        query = Regex.Replace(query, "=~\"\\$\\{([a-zA-Z_][a-zA-Z0-9_]*)\\}\"", "=~${$1}");
        query = Regex.Replace(query, "=~\"\\$([a-zA-Z_][a-zA-Z0-9_]*)\"", "=~${$1}");
        query = Regex.Replace(query, "!~\"\\$\\{([a-zA-Z_][a-zA-Z0-9_]*)\\}\"", "!~${$1}");
        query = Regex.Replace(query, "!~\"\\$([a-zA-Z_][a-zA-Z0-9_]*)\"", "!~${$1}");

        return query;
    }

    public static string CleanHtml(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        text = Regex.Replace(
            text,
            "<a\\s+[^>]*href=[\"']([^\"']+)[\"'][^>]*>([^<]+)</a>",
            "[$2]($1)",
            RegexOptions.IgnoreCase);

        text = Regex.Replace(text, "<[^>]+>", string.Empty);
        return System.Net.WebUtility.HtmlDecode(text).Trim();
    }

    public static string DeriveSeriesNameFromQuery(string query, string refId)
    {
        var k6Names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["k6_vus"] = "VUs",
            ["k6_vus_max"] = "Max VUs",
            ["k6_http_reqs_total"] = "HTTP Requests",
            ["k6_http_req_duration"] = "HTTP Duration",
            ["k6_http_req_duration_p95"] = "HTTP Duration P95",
            ["k6_http_req_duration_p99"] = "HTTP Duration P99",
            ["k6_http_req_duration_avg"] = "HTTP Duration Avg",
            ["k6_http_req_duration_min"] = "HTTP Duration Min",
            ["k6_http_req_duration_max"] = "HTTP Duration Max",
            ["k6_http_req_failed"] = "HTTP Failed",
            ["k6_data_sent"] = "Data Sent",
            ["k6_data_received"] = "Data Received",
            ["k6_iteration_duration"] = "Iteration Duration",
            ["k6_iterations"] = "Iterations"
        };

        foreach (var pair in k6Names)
        {
            if (!query.Contains(pair.Key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (query.Contains("irate(", StringComparison.OrdinalIgnoreCase)
                || query.Contains("rate(", StringComparison.OrdinalIgnoreCase))
            {
                return $"{pair.Value}/s";
            }

            if (query.Contains("expected_response=\"false\"", StringComparison.OrdinalIgnoreCase))
            {
                return $"{pair.Value} (Errors)";
            }

            if (query.Contains("expected_response=\"true\"", StringComparison.OrdinalIgnoreCase))
            {
                return $"{pair.Value} (Success)";
            }

            return pair.Value;
        }

        var metricMatch = Regex.Match(query, @"(\w+)\{");
        if (metricMatch.Success)
        {
            var metricName = metricMatch.Groups[1].Value;
            var title = string.Join(" ", metricName.Split('_', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => char.ToUpperInvariant(s[0]) + s[1..]));

            if (query.Contains("irate(", StringComparison.OrdinalIgnoreCase)
                || query.Contains("rate(", StringComparison.OrdinalIgnoreCase))
            {
                return $"{title}/s";
            }

            return title;
        }

        return $"Series {refId}";
    }
}
