using Newtonsoft.Json.Linq;

namespace GrafanaToCx.Core.Migration;

public sealed record ValidationResult(bool IsValid, string? ErrorMessage)
{
    public static ValidationResult Ok() => new(true, null);
    public static ValidationResult Fail(string error) => new(false, error);
}

public sealed class DashboardValidator
{
    public ValidationResult Validate(JObject dashboard)
    {
        var name = dashboard.Value<string>("name");
        if (string.IsNullOrWhiteSpace(name))
            return ValidationResult.Fail("missing required field 'name'");

        var layout = dashboard["layout"] as JObject;
        if (layout is null)
            return ValidationResult.Fail("missing required field 'layout'");

        if (layout["sections"] is not JArray)
            return ValidationResult.Fail("'layout.sections' must be an array");

        return ValidationResult.Ok();
    }
}
