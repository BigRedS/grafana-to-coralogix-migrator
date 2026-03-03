using Newtonsoft.Json.Linq;

namespace GrafanaToCx.Core.Tests;

internal static class TestFixtureLoader
{
    public static JObject LoadFixture(string fileName)
    {
        var fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "tests", "GrafanaToCx.Core.Tests", "Fixtures", fileName);

        var json = File.ReadAllText(Path.GetFullPath(fixturePath));
        return JObject.Parse(json);
    }
}
