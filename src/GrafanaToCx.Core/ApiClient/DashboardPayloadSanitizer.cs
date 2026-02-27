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
        EnsureDataTableColumns(cloned);
        EnsurePieChartLabelDefinition(cloned);
        return cloned;
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
