using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace GrafanaToCx.Core.Converter;

/// <summary>
/// Fixes PromQL label matchers that reference a dashboard variable.
///
/// Grafana writes <c>instance="$server"</c>. Coralogix substitutes <c>${server}</c> itself and
/// expands a multi-value variable into a regex alternation, so the placeholder must be
/// unquoted, and a multi-value variable needs a regex operator rather than equality. Left as
/// Grafana wrote it, the API rejects the widget outright:
/// <c>null is not allowed here</c> / <c>multi value is not allowed here</c>.
///
/// Runs over the finished dashboard because only then is it known which variables ended up
/// multi-value.
/// </summary>
public static class PromqlVariableMatchers
{
    // label="${var}" / label!="${var}" / label=~"${var}" / label!~"${var}", quoted or not.
    // Longer operators first so != and =~ are not partially matched as =.
    //
    // The \k<quote> backreference makes the quoting balance: the placeholder must be the whole
    // matcher value. Something like instance=~"${instance}.*" is deliberately left alone —
    // stripping only its opening quote would produce invalid PromQL.
    private static readonly Regex MatcherPattern = new(
        """(?<op>=~|!~|!=|=)\s*(?<quote>"?)(?<placeholder>\$\{(?<name>[A-Za-z_][A-Za-z0-9_]*)\})\k<quote>""",
        RegexOptions.Compiled);

    public static void Normalize(JObject dashboard)
    {
        var multiValueVariables = CollectMultiValueVariableNames(dashboard);
        RewritePromqlValues(dashboard, multiValueVariables);
    }

    /// <summary>
    /// Names of variables emitted as multi-value. Coralogix expands these into a regex
    /// alternation (<c>a|b|c</c>), which only works against a regex operator.
    /// </summary>
    private static HashSet<string> CollectMultiValueVariableNames(JObject dashboard)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var variable in (dashboard["variablesV2"] as JArray ?? []).Children<JObject>())
        {
            if (variable["value"]?["multiString"] is null)
                continue;

            var name = variable.Value<string>("name");
            if (!string.IsNullOrEmpty(name))
                names.Add(name);
        }

        return names;
    }

    private static void RewritePromqlValues(JToken token, HashSet<string> multiValueVariables)
    {
        switch (token)
        {
            case JObject obj:
                if (obj["promqlQuery"] is JObject promqlQuery
                    && promqlQuery["value"] is JValue { Type: JTokenType.String } value)
                {
                    promqlQuery["value"] = Rewrite(value.Value<string>() ?? string.Empty, multiValueVariables);
                }

                foreach (var property in obj.Properties())
                    RewritePromqlValues(property.Value, multiValueVariables);
                break;

            case JArray array:
                foreach (var item in array)
                    RewritePromqlValues(item, multiValueVariables);
                break;
        }
    }

    /// <summary>
    /// Rewrites the variable matchers in one PromQL expression. Public so the rule can be
    /// exercised directly rather than only through a whole dashboard conversion.
    /// </summary>
    public static string Rewrite(string promql, ISet<string> multiValueVariables)
    {
        if (string.IsNullOrWhiteSpace(promql))
            return promql ?? string.Empty;

        return MatcherPattern.Replace(promql, match =>
        {
            var op = match.Groups["op"].Value;
            var name = match.Groups["name"].Value;
            var placeholder = match.Groups["placeholder"].Value;

            if (multiValueVariables.Contains(name))
                op = op is "!=" or "!~" ? "!~" : "=~";

            return op + placeholder;
        });
    }
}
