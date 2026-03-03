namespace GrafanaToCx.Core.Converter.Parsing;

public interface ILuceneLikeExpressionParser
{
    ExpressionParseResult Parse(string expression);
}

public interface ILogqlLikeExpressionParser
{
    ExpressionParseResult Parse(string expression);
}
