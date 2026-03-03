namespace GrafanaToCx.Core.Converter.Parsing;

public sealed class LuceneLikeExpressionParser : BooleanExpressionParserBase, ILuceneLikeExpressionParser
{
    public ExpressionParseResult Parse(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return new ExpressionParseResult(null, []);

        return ParseInternal(expression);
    }
}
