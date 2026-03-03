using GrafanaToCx.Core.Converter;
using Microsoft.Extensions.Logging.Abstractions;

namespace GrafanaToCx.Core.Tests.Differential;

public class DifferentialDeterministicSelectionTests
{
    [Fact]
    [Trait("Category", "Differential")]
    public void BarChart_TargetOrderPermutation_ProducesSameSelectedQuery()
    {
        var variantA = TestFixtureLoader.LoadFixture("differential_bar_order_a.json").ToString();
        var variantB = TestFixtureLoader.LoadFixture("differential_bar_order_b.json").ToString();

        var converterA = new GrafanaToCxConverter(NullLogger<GrafanaToCxConverter>.Instance);
        var converterB = new GrafanaToCxConverter(NullLogger<GrafanaToCxConverter>.Instance);

        var outputA = converterA.ConvertToJObject(variantA);
        var outputB = converterB.ConvertToJObject(variantB);

        var queryA = outputA["layout"]!["sections"]![0]!["rows"]![0]!["widgets"]![0]!["definition"]!["barChart"]!["query"]!["logs"]!["luceneQuery"]!["value"]!.ToString();
        var queryB = outputB["layout"]!["sections"]![0]!["rows"]![0]!["widgets"]![0]!["definition"]!["barChart"]!["query"]!["logs"]!["luceneQuery"]!["value"]!.ToString();
        Assert.Equal(queryA, queryB);

        Assert.Contains(converterA.ConversionDiagnostics, d => d.Code == "DGR-MTG-001");
        Assert.Contains(converterB.ConversionDiagnostics, d => d.Code == "DGR-MTG-001");
    }
}
