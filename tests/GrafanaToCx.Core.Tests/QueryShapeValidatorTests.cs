using GrafanaToCx.Core.Converter.Semantics;
using Newtonsoft.Json.Linq;
using Xunit;

namespace GrafanaToCx.Core.Tests;

public class QueryShapeValidatorTests
{
    [Fact]
    public void ValidateDashboard_FlagsLogsGroupNames_AsUnsupported()
    {
        var dashboard = new JObject
        {
            ["name"] = "shape-check",
            ["layout"] = new JObject
            {
                ["sections"] = new JArray
                {
                    new JObject
                    {
                        ["rows"] = new JArray
                        {
                            new JObject
                            {
                                ["widgets"] = new JArray
                                {
                                    new JObject
                                    {
                                        ["definition"] = new JObject
                                        {
                                            ["pieChart"] = new JObject
                                            {
                                                ["query"] = new JObject
                                                {
                                                    ["logs"] = new JObject
                                                    {
                                                        ["filters"] = new JArray(),
                                                        ["groupNames"] = new JArray { "service.name" }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

        var validator = new QueryShapeValidator();

        var errors = validator.ValidateDashboard(dashboard);

        Assert.Contains(errors, e => e.Message.Contains("logs.groupNames is unsupported in logs branch", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateDashboard_AllowsDataprimeWithoutGroupNames()
    {
        var dashboard = new JObject
        {
            ["name"] = "shape-check",
            ["layout"] = new JObject
            {
                ["sections"] = new JArray
                {
                    new JObject
                    {
                        ["rows"] = new JArray
                        {
                            new JObject
                            {
                                ["widgets"] = new JArray
                                {
                                    new JObject
                                    {
                                        ["definition"] = new JObject
                                        {
                                            ["pieChart"] = new JObject
                                            {
                                                ["query"] = new JObject
                                                {
                                                    ["dataprime"] = new JObject
                                                    {
                                                        ["dataprimeQuery"] = new JObject
                                                        {
                                                            ["text"] = "source logs | agg count()"
                                                        },
                                                        ["filters"] = new JArray()
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

        var validator = new QueryShapeValidator();

        var errors = validator.ValidateDashboard(dashboard);

        Assert.Empty(errors);
    }
}
