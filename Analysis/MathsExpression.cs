using System.Globalization;

namespace Chassis_Master_Test_Suite.Analysis;

/// <summary>
/// 简单数学通道表达式：+ - * / 与括号；标识符为通道 Id（大小写不敏感）。
/// </summary>
public static class MathsExpression
{
    public static double Evaluate(
        string expression,
        Func<string, double> getChannel)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(getChannel);

        var tokens = Tokenize(expression);
        if (tokens.Count == 0)
            throw new FormatException("Empty expression.");

        var i = 0;
        var value = ParseExpr(tokens, ref i, getChannel);
        if (i != tokens.Count)
            throw new FormatException($"Unexpected token '{tokens[i].Text}' at position {tokens[i].Position}.");

        return value;
    }

    public static bool TryEvaluate(
        string expression,
        Func<string, double> getChannel,
        out double value,
        out string? error)
    {
        try
        {
            value = Evaluate(expression, getChannel);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            value = 0;
            error = ex.Message;
            return false;
        }
    }

    /// <summary>提取表达式中的通道名（去重，保留出现顺序）。</summary>
    public static IReadOnlyList<string> ExtractChannelNames(string expression)
    {
        var tokens = Tokenize(expression);
        var list = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in tokens)
        {
            if (t.Kind != TokenKind.Ident)
                continue;
            if (seen.Add(t.Text))
                list.Add(t.Text);
        }

        return list;
    }

    private enum TokenKind { Number, Ident, Plus, Minus, Star, Slash, LParen, RParen }

    private readonly struct Token
    {
        public Token(TokenKind kind, string text, int position)
        {
            Kind = kind;
            Text = text;
            Position = position;
        }

        public TokenKind Kind { get; }
        public string Text { get; }
        public int Position { get; }
    }

    private static List<Token> Tokenize(string input)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < input.Length)
        {
            var c = input[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (c is '+' or '-' or '*' or '/' or '(' or ')')
            {
                var kind = c switch
                {
                    '+' => TokenKind.Plus,
                    '-' => TokenKind.Minus,
                    '*' => TokenKind.Star,
                    '/' => TokenKind.Slash,
                    '(' => TokenKind.LParen,
                    _ => TokenKind.RParen
                };
                tokens.Add(new Token(kind, c.ToString(), i));
                i++;
                continue;
            }

            if (char.IsDigit(c) || c == '.')
            {
                var start = i;
                i++;
                while (i < input.Length && (char.IsDigit(input[i]) || input[i] == '.'))
                    i++;
                tokens.Add(new Token(TokenKind.Number, input[start..i], start));
                continue;
            }

            if (char.IsLetter(c) || c == '_')
            {
                var start = i;
                i++;
                while (i < input.Length && (char.IsLetterOrDigit(input[i]) || input[i] == '_'))
                    i++;
                tokens.Add(new Token(TokenKind.Ident, input[start..i], start));
                continue;
            }

            throw new FormatException($"Invalid character '{c}' at position {i}.");
        }

        return tokens;
    }

    private static double ParseExpr(List<Token> tokens, ref int i, Func<string, double> getChannel)
    {
        var left = ParseTerm(tokens, ref i, getChannel);
        while (i < tokens.Count && tokens[i].Kind is TokenKind.Plus or TokenKind.Minus)
        {
            var op = tokens[i++].Kind;
            var right = ParseTerm(tokens, ref i, getChannel);
            left = op == TokenKind.Plus ? left + right : left - right;
        }

        return left;
    }

    private static double ParseTerm(List<Token> tokens, ref int i, Func<string, double> getChannel)
    {
        var left = ParseUnary(tokens, ref i, getChannel);
        while (i < tokens.Count && tokens[i].Kind is TokenKind.Star or TokenKind.Slash)
        {
            var op = tokens[i++].Kind;
            var right = ParseUnary(tokens, ref i, getChannel);
            if (op == TokenKind.Star)
                left *= right;
            else
            {
                if (Math.Abs(right) < 1e-15)
                    throw new DivideByZeroException("Division by zero in maths expression.");
                left /= right;
            }
        }

        return left;
    }

    private static double ParseUnary(List<Token> tokens, ref int i, Func<string, double> getChannel)
    {
        if (i < tokens.Count && tokens[i].Kind == TokenKind.Minus)
        {
            i++;
            return -ParseUnary(tokens, ref i, getChannel);
        }

        if (i < tokens.Count && tokens[i].Kind == TokenKind.Plus)
        {
            i++;
            return ParseUnary(tokens, ref i, getChannel);
        }

        return ParsePrimary(tokens, ref i, getChannel);
    }

    private static double ParsePrimary(List<Token> tokens, ref int i, Func<string, double> getChannel)
    {
        if (i >= tokens.Count)
            throw new FormatException("Unexpected end of expression.");

        var t = tokens[i++];
        switch (t.Kind)
        {
            case TokenKind.Number:
                if (!double.TryParse(t.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n))
                    throw new FormatException($"Invalid number '{t.Text}'.");
                return n;

            case TokenKind.Ident:
                return getChannel(t.Text);

            case TokenKind.LParen:
                var inner = ParseExpr(tokens, ref i, getChannel);
                if (i >= tokens.Count || tokens[i].Kind != TokenKind.RParen)
                    throw new FormatException("Missing ')'.");
                i++;
                return inner;

            default:
                throw new FormatException($"Unexpected token '{t.Text}'.");
        }
    }
}
