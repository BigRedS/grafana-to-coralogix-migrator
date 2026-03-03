using System.Text;

namespace GrafanaToCx.Core.Converter.Parsing;

public abstract class BooleanExpressionParserBase
{
    private IReadOnlyList<Token> _tokens = [];
    private int _index;
    private readonly List<string> _errors = [];

    protected ExpressionParseResult ParseInternal(string expression)
    {
        _tokens = Tokenize(expression);
        _index = 0;
        _errors.Clear();

        if (_tokens.Count == 0)
            return new ExpressionParseResult(null, []);

        var root = ParseOr();
        if (_index < _tokens.Count)
            _errors.Add($"Unexpected token '{_tokens[_index].Text}'.");

        return new ExpressionParseResult(root, _errors);
    }

    protected virtual IReadOnlyList<Token> Tokenize(string expression)
    {
        var tokens = new List<Token>();
        var sb = new StringBuilder();
        var inQuotes = false;

        void FlushCurrentToken()
        {
            if (sb.Length == 0)
                return;

            tokens.Add(new Token(TokenKind.Text, sb.ToString(), false));
            sb.Clear();
        }

        for (var i = 0; i < expression.Length; i++)
        {
            var c = expression[i];
            if (c == '"')
            {
                if (inQuotes)
                {
                    tokens.Add(new Token(TokenKind.Text, sb.ToString(), true));
                    sb.Clear();
                    inQuotes = false;
                }
                else
                {
                    FlushCurrentToken();
                    inQuotes = true;
                }
                continue;
            }

            if (inQuotes)
            {
                sb.Append(c);
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                FlushCurrentToken();
                continue;
            }

            if (c is '(' or ')')
            {
                FlushCurrentToken();
                tokens.Add(new Token(c == '(' ? TokenKind.LParen : TokenKind.RParen, c.ToString(), false));
                continue;
            }

            sb.Append(c);
        }

        if (inQuotes)
            _errors.Add("Unclosed quote.");

        FlushCurrentToken();
        return NormalizeKeywords(tokens);
    }

    private static IReadOnlyList<Token> NormalizeKeywords(List<Token> tokens)
    {
        for (var i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Kind != TokenKind.Text || tokens[i].InQuotes)
                continue;

            var value = tokens[i].Text;
            if (value.Equals("AND", StringComparison.OrdinalIgnoreCase))
                tokens[i] = tokens[i] with { Kind = TokenKind.And };
            else if (value.Equals("OR", StringComparison.OrdinalIgnoreCase))
                tokens[i] = tokens[i] with { Kind = TokenKind.Or };
            else if (value.Equals("NOT", StringComparison.OrdinalIgnoreCase))
                tokens[i] = tokens[i] with { Kind = TokenKind.Not };
        }

        return tokens;
    }

    private ExpressionAstNode ParseOr()
    {
        var left = ParseAnd();
        while (Match(TokenKind.Or))
        {
            var right = ParseAnd();
            left = new ExpressionBinaryNode(ExpressionBinaryOperator.Or, left, right);
        }

        return left;
    }

    private ExpressionAstNode ParseAnd()
    {
        var left = ParseUnary();
        while (true)
        {
            if (Match(TokenKind.And))
            {
                var right = ParseUnary();
                left = new ExpressionBinaryNode(ExpressionBinaryOperator.And, left, right);
                continue;
            }

            if (CanImplicitlyAnd(Current))
            {
                var right = ParseUnary();
                left = new ExpressionBinaryNode(ExpressionBinaryOperator.And, left, right);
                continue;
            }

            break;
        }

        return left;
    }

    private static bool CanImplicitlyAnd(Token? token) =>
        token is { Kind: TokenKind.Text or TokenKind.Not or TokenKind.LParen };

    private ExpressionAstNode ParseUnary()
    {
        if (Match(TokenKind.Not))
            return new ExpressionNotNode(ParseUnary());

        return ParsePrimary();
    }

    private ExpressionAstNode ParsePrimary()
    {
        if (Match(TokenKind.LParen))
        {
            var node = ParseOr();
            if (!Match(TokenKind.RParen))
                _errors.Add("Missing closing parenthesis.");
            return node;
        }

        var token = Consume();
        if (token == null)
        {
            _errors.Add("Unexpected end of expression.");
            return new ExpressionTextNode(string.Empty, false);
        }

        return BuildLeafNode(token.Value);
    }

    protected virtual ExpressionAstNode BuildLeafNode(Token token)
    {
        var text = token.Text;
        var firstColon = text.IndexOf(':');
        if (firstColon > 0 && firstColon < text.Length - 1)
        {
            var field = text[..firstColon];
            var value = text[(firstColon + 1)..];
            return new ExpressionPredicateNode(field, ":", value, token.InQuotes);
        }

        return new ExpressionTextNode(text, token.InQuotes);
    }

    private Token? Current => _index < _tokens.Count ? _tokens[_index] : null;

    private bool Match(TokenKind kind)
    {
        if (Current is { Kind: var currentKind } && currentKind == kind)
        {
            _index++;
            return true;
        }

        return false;
    }

    private Token? Consume()
    {
        var current = Current;
        if (current != null)
            _index++;
        return current;
    }

    protected readonly record struct Token(TokenKind Kind, string Text, bool InQuotes);

    protected enum TokenKind
    {
        Text,
        And,
        Or,
        Not,
        LParen,
        RParen
    }
}
