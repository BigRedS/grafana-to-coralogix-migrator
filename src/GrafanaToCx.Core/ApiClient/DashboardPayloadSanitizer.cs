using Newtonsoft.Json.Linq;

namespace GrafanaToCx.Core.ApiClient;

/// <summary>
/// Sanitizes dashboard payloads before upload to Coralogix API.
/// Removes unsupported properties and ensures required fields are present.
/// </summary>
public static class DashboardPayloadSanitizer
{
    private static readonly string[] PropertiesToRemove = { "stackedGroupName", "stackedGroupNameField" };
    private static readonly JArray DefaultDataTableColumns = new JArray
    {
        new JObject { ["field"] = "coralogix.text" }
    };

    /// <summary>
    /// Sanitizes a dashboard JObject by removing unsupported properties and ensuring dataTable columns exist.
    /// </summary>
    /// <param name="dashboard">The dashboard to sanitize (will be cloned if modified).</param>
    /// <returns>A sanitized dashboard (may be the same instance if no changes were needed).</returns>
    public static JObject Sanitize(JObject dashboard)
    {
        var cloned = (JObject)dashboard.DeepClone();
        RemovePropertiesRecursively(cloned, PropertiesToRemove);
        MigratePieChartDataPrimeQueries(cloned);
        EnsureDataTableColumns(cloned);
        EnsurePieChartLabelDefinition(cloned);
        return cloned;
    }

    /// <summary>
    /// Coralogix upload API rejects pieChart.query.dataPrime. Preserve it by:
    /// 1) removing unsupported dataPrime from pieChart.query
    /// 2) injecting an adjacent markdown widget that keeps the DataPrime query text
    /// </summary>
    private static void MigratePieChartDataPrimeQueries(JToken token)
    {
        if (token is JObject obj)
        {
            foreach (var prop in obj.Properties())
            {
                if (prop.Name == "widgets" && prop.Value is JArray widgets)
                {
                    ProcessWidgetsArrayForDataPrime(widgets);
                    continue;
                }

                MigratePieChartDataPrimeQueries(prop.Value);
            }
        }
        else if (token is JArray array)
        {
            foreach (var item in array)
            {
                MigratePieChartDataPrimeQueries(item);
            }
        }
    }

    private static void ProcessWidgetsArrayForDataPrime(JArray widgets)
    {
        for (var i = 0; i < widgets.Count; i++)
        {
            if (widgets[i] is not JObject widget)
                continue;

            if (!TryExtractAndRemoveDataPrime(widget, out var dataPrimeQuery))
                continue;

            var title = widget["title"]?.ToString() ?? "Widget";
            var markdownWidget = new JObject
            {
                ["id"] = new JObject { ["value"] = Guid.NewGuid().ToString() },
                ["title"] = $"{title} (DataPrime Query)",
                ["description"] = "Preserved from converter output: API does not accept pieChart.query.dataPrime directly.",
                ["definition"] = new JObject
                {
                    ["markdown"] = new JObject
                    {
                        ["markdownText"] =
                            "## DataPrime Query (Preserved)\n\n```dataprime\n" +
                            dataPrimeQuery +
                            "\n```"
                    }
                }
            };

            widgets.Insert(i + 1, markdownWidget);
            i++;
        }
    }

    private static bool TryExtractAndRemoveDataPrime(JObject widget, out string dataPrimeQuery)
    {
        dataPrimeQuery = string.Empty;

        var query = widget["definition"]?["pieChart"]?["query"] as JObject;
        var dataPrime = query?["dataPrime"] as JObject;
        var value = dataPrime?["value"]?.ToString();

        if (string.IsNullOrWhiteSpace(value))
            return false;

        dataPrimeQuery = value;
        query!.Property("dataPrime")?.Remove();
        return true;
    }

    private static void RemovePropertiesRecursively(JToken token, string[] propertyNames)
    {
        if (token is JObject obj)
        {
            var propertiesToRemove = new List<JProperty>();
            foreach (var prop in obj.Properties())
            {
                if (Array.Exists(propertyNames, name => string.Equals(prop.Name, name, StringComparison.Ordinal)))
                {
                    propertiesToRemove.Add(prop);
                }
                else
                {
                    RemovePropertiesRecursively(prop.Value, propertyNames);
                }
            }

            foreach (var prop in propertiesToRemove)
            {
                prop.Remove();
            }
        }
        else if (token is JArray array)
        {
            foreach (var item in array)
            {
                RemovePropertiesRecursively(item, propertyNames);
            }
        }
    }

    private static void EnsureDataTableColumns(JToken token)
    {
        if (token is JObject obj)
        {
            if (obj["dataTable"] is JObject dataTable)
            {
                var columns = dataTable["columns"] as JArray;
                if (columns == null || columns.Count == 0)
                {
                    dataTable["columns"] = DefaultDataTableColumns.DeepClone() as JArray ?? DefaultDataTableColumns;
                }
            }

            foreach (var prop in obj.Properties())
            {
                EnsureDataTableColumns(prop.Value);
            }
        }
        else if (token is JArray array)
        {
            foreach (var item in array)
            {
                EnsureDataTableColumns(item);
            }
        }
    }

    private static void EnsurePieChartLabelDefinition(JToken token)
    {
        if (token is JObject obj)
        {
            if (obj["pieChart"] is JObject pieChart)
            {
                if (pieChart["labelDefinition"] == null)
                {
                    pieChart["labelDefinition"] = new JObject
                    {
                        ["labelSource"] = "LABEL_SOURCE_INNER",
                        ["isVisible"] = true,
                        ["showName"] = true,
                        ["showValue"] = true,
                        ["showPercentage"] = true
                    };
                }
            }

            foreach (var prop in obj.Properties())
            {
                EnsurePieChartLabelDefinition(prop.Value);
            }
        }
        else if (token is JArray array)
        {
            foreach (var item in array)
            {
                EnsurePieChartLabelDefinition(item);
            }
        }
    }
}
