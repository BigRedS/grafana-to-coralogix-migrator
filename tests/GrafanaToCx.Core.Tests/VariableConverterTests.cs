using GrafanaToCx.Core.Converter;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;

namespace GrafanaToCx.Core.Tests;

public class VariableConverterTests
{
    private static GrafanaToCxConverter CreateConverter() =>
        new(NullLogger<GrafanaToCxConverter>.Instance);

    private static string BuildDashboardJsonWithVariables(params JObject[] variables) =>
        new JObject
        {
            ["dashboard"] = new JObject
            {
                ["title"] = "Variable Test Dashboard",
                ["panels"] = new JArray(),
                ["templating"] = new JObject
                {
                    ["list"] = new JArray(variables)
                }
            }
        }.ToString();

    private static JObject? GetVariable(JObject convertedDashboard, string variableName)
    {
        var variables = convertedDashboard["variablesV2"] as JArray;
        return variables?
            .Children<JObject>()
            .FirstOrDefault(v => string.Equals(v.Value<string>("name"), variableName, StringComparison.Ordinal));
    }

    [Fact]
    public void ConvertToJObject_QueryVariableWithoutLabelValues_UsesOptionsFallback()
    {
        var converter = CreateConverter();
        var grafanaVariable = new JObject
        {
            ["name"] = "instanceUrl",
            ["type"] = "query",
            ["label"] = "Instance URL",
            ["query"] = "instances_for_service",
            ["current"] = new JObject
            {
                ["text"] = "https://a.example",
                ["value"] = "https://a.example"
            },
            ["options"] = new JArray
            {
                new JObject { ["text"] = "https://a.example", ["value"] = "https://a.example", ["selected"] = true },
                new JObject { ["text"] = "https://b.example", ["value"] = "https://b.example", ["selected"] = false }
            }
        };

        var result = converter.ConvertToJObject(BuildDashboardJsonWithVariables(grafanaVariable));
        var convertedVariable = GetVariable(result, "instanceUrl");

        Assert.NotNull(convertedVariable);
        Assert.Equal("VARIABLE_DISPLAY_TYPE_V2_LABEL_VALUE", convertedVariable!["displayType"]?.ToString());
        var staticValues = convertedVariable["source"]?["static"]?["values"] as JArray;
        Assert.NotNull(staticValues);
        Assert.Equal(2, staticValues!.Count);
        Assert.Equal("https://a.example", staticValues[0]?["value"]?.ToString());
        Assert.Equal("https://b.example", staticValues[1]?["value"]?.ToString());
    }

    [Fact]
    public void ConvertToJObject_QueryVariableWithoutOptions_UsesCurrentValueFallback()
    {
        var converter = CreateConverter();
        var grafanaVariable = new JObject
        {
            ["name"] = "instanceUrl",
            ["type"] = "query",
            ["query"] = "some_backend_query",
            ["current"] = new JObject
            {
                ["text"] = "https://prod.example",
                ["value"] = "https://prod.example"
            }
        };

        var result = converter.ConvertToJObject(BuildDashboardJsonWithVariables(grafanaVariable));
        var convertedVariable = GetVariable(result, "instanceUrl");

        Assert.NotNull(convertedVariable);
        var staticValues = convertedVariable!["source"]?["static"]?["values"] as JArray;
        Assert.NotNull(staticValues);
        Assert.Single(staticValues!);
        Assert.Equal("https://prod.example", staticValues[0]?["value"]?.ToString());
    }

    [Fact]
    public void ConvertToJObject_QueryVariableSimpleCsvQuery_UsesQueryValuesFallback()
    {
        var converter = CreateConverter();
        var grafanaVariable = new JObject
        {
            ["name"] = "environment",
            ["type"] = "query",
            ["query"] = "dev, staging , prod"
        };

        var result = converter.ConvertToJObject(BuildDashboardJsonWithVariables(grafanaVariable));
        var convertedVariable = GetVariable(result, "environment");

        Assert.NotNull(convertedVariable);
        var staticValues = convertedVariable!["source"]?["static"]?["values"] as JArray;
        Assert.NotNull(staticValues);
        Assert.Equal(3, staticValues!.Count);
        Assert.Equal("dev", staticValues[0]?["value"]?.ToString());
        Assert.Equal("staging", staticValues[1]?["value"]?.ToString());
        Assert.Equal("prod", staticValues[2]?["value"]?.ToString());
    }

    [Fact]
    public void ConvertToJObject_QueryVariableWithCurrentArray_UsesMultiAllValue()
    {
        var converter = CreateConverter();
        var grafanaVariable = new JObject
        {
            ["name"] = "region",
            ["type"] = "query",
            ["query"] = "regions_for_service",
            ["current"] = new JObject
            {
                ["text"] = new JArray("us-east-1", "eu-west-1"),
                ["value"] = new JArray("us-east-1", "eu-west-1")
            }
        };

        var result = converter.ConvertToJObject(BuildDashboardJsonWithVariables(grafanaVariable));
        var convertedVariable = GetVariable(result, "region");

        Assert.NotNull(convertedVariable);
        var staticValues = convertedVariable!["source"]?["static"]?["values"] as JArray;
        Assert.NotNull(staticValues);
        Assert.Equal(2, staticValues!.Count);
        Assert.NotNull(convertedVariable["value"]?["multiString"]?["all"]);
    }

    [Fact]
    public void ConvertToJObject_QueryVariableWithMetricsFunction_RemainsSkipped()
    {
        var converter = CreateConverter();
        var grafanaVariable = new JObject
        {
            ["name"] = "metric_selector",
            ["type"] = "query",
            ["query"] = "metrics(http_requests_total)"
        };

        var result = converter.ConvertToJObject(BuildDashboardJsonWithVariables(grafanaVariable));
        var convertedVariable = GetVariable(result, "metric_selector");

        Assert.Null(convertedVariable);
    }
}
