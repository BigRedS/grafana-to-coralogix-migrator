namespace GrafanaToCx.Core.Converter.Parsing;

public sealed class LogqlLikeExpressionParser : BooleanExpressionParserBase, ILogqlLikeExpressionParser
{
    public ExpressionParseResult Parse(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return new ExpressionParseResult(null, []);

        // Normalize pipeline delimiters into whitespace so boolean parsing can focus
        // on precedence/grouping while preserving operators and quoted literals.
        var normalized = expression.Replace("|", " ", StringComparison.Ordinal);
        return ParseInternal(normalized);
    }
}
