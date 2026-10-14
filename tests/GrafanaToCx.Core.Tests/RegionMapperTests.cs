using GrafanaToCx.Core.Migration;

namespace GrafanaToCx.Core.Tests;

public class RegionMapperTests
{
    [Fact]
    public void ResolveGrafana_WithoutUrl_UsesCoralogixHostedGrafanaForRegion()
    {
        Assert.Equal("https://api.eu2.coralogix.com/grafana", RegionMapper.ResolveGrafana(null, "eu2"));
        Assert.Equal("https://api.eu2.coralogix.com/grafana", RegionMapper.ResolveGrafana("  ", "eu2"));
    }

    [Theory]
    [InlineData("https://grafana.example.com", "https://grafana.example.com")]
    [InlineData("https://grafana.example.com/", "https://grafana.example.com")]
    [InlineData("http://localhost:3000/grafana/", "http://localhost:3000/grafana")]
    public void ResolveGrafana_WithUrl_IgnoresRegion(string url, string expected)
    {
        // An unknown region would throw if it were consulted.
        Assert.Equal(expected, RegionMapper.ResolveGrafana(url, "not-a-region"));
    }

    [Theory]
    [InlineData("grafana.example.com")]
    [InlineData("ftp://grafana.example.com")]
    [InlineData("/grafana")]
    public void ResolveGrafana_WithInvalidUrl_ThrowsArgumentException(string url)
    {
        Assert.Throws<ArgumentException>(() => RegionMapper.ResolveGrafana(url, "eu1"));
    }
}
