namespace GrafanaToCx.Core.Converter.Parsing;

public enum ExpressionBinaryOperator
{
    And,
    Or
}

public abstract record ExpressionAstNode;

public sealed record ExpressionBinaryNode(
    ExpressionBinaryOperator Operator,
    ExpressionAstNode Left,
    ExpressionAstNode Right) : ExpressionAstNode;

public sealed record ExpressionNotNode(ExpressionAstNode Operand) : ExpressionAstNode;

public sealed record ExpressionPredicateNode(string Field, string Operator, string Value, bool IsQuoted) : ExpressionAstNode;

public sealed record ExpressionTextNode(string Text, bool IsQuoted) : ExpressionAstNode;

public sealed record ExpressionParseResult(ExpressionAstNode? Root, IReadOnlyList<string> Errors);
