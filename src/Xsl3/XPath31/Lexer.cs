// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Text;

namespace Xsl3.XPath31
{
    /// <summary>A static error found while parsing or analysing an XPath expression.</summary>
    internal sealed class XPathStaticException : Exception
    {
        public XPathStaticException(string code, string message, int position)
            : base($"{code}: {message}")
        {
            Code = code;
            Position = position;
        }

        /// <summary>The error code in the <c>err:</c> namespace, e.g. <c>XPST0003</c>.</summary>
        public string Code { get; }

        /// <summary>Offset in the expression text, or -1.</summary>
        public int Position { get; }

        public static XPathStaticException Syntax(string message, int position) => new("XPST0003", message, position);
    }

    internal enum TokenKind
    {
        End,
        /// <summary>An NCName or lexical QName (<c>p:local</c>); keywords are names too.</summary>
        Name,
        /// <summary><c>Q{uri}local</c>.</summary>
        UriQualifiedName,
        /// <summary><c>p:*</c>, <c>*:local</c> or <c>Q{uri}*</c>. A lone <c>*</c> is <see cref="Symbol"/>.</summary>
        Wildcard,
        IntegerLiteral,
        DecimalLiteral,
        DoubleLiteral,
        StringLiteral,
        /// <summary>Punctuation and operators: <c>( ) [ ] { } , . .. @ $ / // ! != = &lt; &lt;= &lt;&lt; …</c></summary>
        Symbol,
    }

    internal readonly record struct Token(TokenKind Kind, string Text, int Start, int End, bool PrecededBySpace)
    {
        // For Name: the prefix (null if none) and local part. For UriQualifiedName/Wildcard: as applicable.
        public string? Prefix { get; init; }
        public string? LocalName { get; init; }
        public string? Uri { get; init; }
        public bool AnyNamespace { get; init; }

        public bool Is(string symbol) => Kind == TokenKind.Symbol && Text == symbol;

        /// <summary>An unprefixed name with this text (how keywords are recognised).</summary>
        public bool IsKeyword(string keyword) => Kind == TokenKind.Name && Prefix == null && LocalName == keyword;

        public override string ToString() => Kind == TokenKind.End ? "end of expression" : $"'{Text}'";
    }

    /// <summary>
    /// Splits an XPath 3.1 expression into tokens (XPath 3.1 Appendix A.2). Whitespace and comments
    /// separate tokens and are dropped. The grammar is tokenised up front: the only context-dependent
    /// readings (a <c>*</c> as wildcard or multiplication, a name as keyword or name) are left to the parser.
    /// </summary>
    internal static class Lexer
    {
        public static List<Token> Tokenize(string text)
        {
            var tokens = new List<Token>();
            int i = 0;
            while (true)
            {
                bool space = SkipWhitespaceAndComments(text, ref i);
                if (i >= text.Length)
                {
                    tokens.Add(new Token(TokenKind.End, "", i, i, space));
                    return tokens;
                }
                tokens.Add(Next(text, ref i, space));
            }
        }

        private static bool SkipWhitespaceAndComments(string text, ref int i)
        {
            int start = i;
            while (i < text.Length)
            {
                char c = text[i];
                if (c is ' ' or '\t' or '\r' or '\n')
                {
                    i++;
                }
                else if (c == '(' && i + 1 < text.Length && text[i + 1] == ':')
                {
                    SkipComment(text, ref i);
                }
                else
                {
                    break;
                }
            }
            return i > start;
        }

        // Comments nest: (: a (: b :) c :)
        private static void SkipComment(string text, ref int i)
        {
            int start = i;
            int depth = 0;
            while (i < text.Length)
            {
                if (text[i] == '(' && i + 1 < text.Length && text[i + 1] == ':')
                {
                    depth++;
                    i += 2;
                }
                else if (text[i] == ':' && i + 1 < text.Length && text[i + 1] == ')')
                {
                    depth--;
                    i += 2;
                    if (depth == 0)
                    {
                        return;
                    }
                }
                else
                {
                    i++;
                }
            }
            throw XPathStaticException.Syntax("Unterminated comment", start);
        }

        private static Token Next(string text, ref int i, bool space)
        {
            int start = i;
            char c = text[i];

            if (c is '"' or '\'')
            {
                return StringLiteral(text, ref i, space);
            }

            if (IsDigit(c) || (c == '.' && i + 1 < text.Length && IsDigit(text[i + 1])))
            {
                return NumericLiteral(text, ref i, space);
            }

            if (c == 'Q' && i + 1 < text.Length && text[i + 1] == '{')
            {
                return BracedName(text, ref i, space);
            }

            if (IsNameStart(text, i))
            {
                return NameOrWildcard(text, ref i, space);
            }

            if (c == '*' && i + 2 < text.Length && text[i + 1] == ':' && IsNameStart(text, i + 2))
            {
                i += 2;
                string local = ReadNCName(text, ref i);
                return new Token(TokenKind.Wildcard, text[start..i], start, i, space) { LocalName = local, AnyNamespace = true };
            }

            // Two-character symbols first.
            if (i + 1 < text.Length)
            {
                string two = text.Substring(i, 2);
                switch (two)
                {
                    case "..": case "//": case "::": case ":=": case "!=": case "<=": case ">=":
                    case "<<": case ">>": case "||": case "=>":
                        i += 2;
                        return new Token(TokenKind.Symbol, two, start, i, space);
                }
            }

            switch (c)
            {
                case '(': case ')': case '[': case ']': case '{': case '}': case ',': case '.': case '@':
                case '$': case '/': case '!': case '=': case '<': case '>': case '+': case '-': case '*':
                case '?': case ':': case '#': case '|':
                    i++;
                    return new Token(TokenKind.Symbol, c.ToString(), start, i, space);
            }

            throw XPathStaticException.Syntax($"Unexpected character '{(char.IsSurrogate(c) ? char.ConvertFromUtf32(char.ConvertToUtf32(text, i)) : c.ToString())}'", i);
        }

        private static Token StringLiteral(string text, ref int i, bool space)
        {
            int start = i;
            char quote = text[i++];
            var sb = new StringBuilder();
            while (true)
            {
                if (i >= text.Length)
                {
                    throw XPathStaticException.Syntax("Unterminated string literal", start);
                }
                char c = text[i++];
                if (c == quote)
                {
                    // A doubled quote is an escaped quote (EscapeQuot / EscapeApos).
                    if (i < text.Length && text[i] == quote)
                    {
                        sb.Append(quote);
                        i++;
                        continue;
                    }
                    break;
                }
                sb.Append(c);
            }
            return new Token(TokenKind.StringLiteral, sb.ToString(), start, i, space);
        }

        private static Token NumericLiteral(string text, ref int i, bool space)
        {
            int start = i;
            TokenKind kind = TokenKind.IntegerLiteral;
            while (i < text.Length && IsDigit(text[i])) i++;
            if (i < text.Length && text[i] == '.' && !(i + 1 < text.Length && text[i + 1] == '.'))
            {
                kind = TokenKind.DecimalLiteral;
                i++;
                while (i < text.Length && IsDigit(text[i])) i++;
            }
            if (i < text.Length && (text[i] == 'e' || text[i] == 'E'))
            {
                int save = i;
                i++;
                if (i < text.Length && (text[i] == '+' || text[i] == '-')) i++;
                if (i < text.Length && IsDigit(text[i]))
                {
                    kind = TokenKind.DoubleLiteral;
                    while (i < text.Length && IsDigit(text[i])) i++;
                }
                else
                {
                    i = save; // reported below: a numeric literal followed by a name character
                }
            }

            // XPath 3.1 A.2.2: a numeric literal must not be immediately followed by a name or another number,
            // so "10div 3" and "1.2.3" are errors rather than two tokens.
            if (i < text.Length && (IsNameStart(text, i) || text[i] == '.' && !(i + 1 < text.Length && text[i + 1] == '.') && kind != TokenKind.IntegerLiteral))
            {
                throw XPathStaticException.Syntax($"Numeric literal '{text[start..i]}' must be separated from the following '{text[i]}'", i);
            }
            return new Token(kind, text[start..i], start, i, space);
        }

        private static Token BracedName(string text, ref int i, bool space)
        {
            int start = i;
            i += 2;
            int close = text.IndexOf('}', i);
            if (close < 0)
            {
                throw XPathStaticException.Syntax("Unterminated braced URI literal", start);
            }
            string uri = text[i..close];
            if (uri.Contains('{'))
            {
                throw XPathStaticException.Syntax("'{' is not allowed in a braced URI literal", start);
            }
            i = close + 1;
            uri = NormalizeUriLiteral(uri);
            if (i < text.Length && text[i] == '*')
            {
                i++;
                return new Token(TokenKind.Wildcard, text[start..i], start, i, space) { Uri = uri };
            }
            if (!IsNameStart(text, i))
            {
                throw XPathStaticException.Syntax("A braced URI literal must be followed by a local name or '*'", i);
            }
            string local = ReadNCName(text, ref i);
            return new Token(TokenKind.UriQualifiedName, text[start..i], start, i, space) { Uri = uri, LocalName = local };
        }

        // XPath 3.1 §2.5.5 / §3.1.1: whitespace in a BracedURILiteral is normalised as for xs:anyURI.
        private static string NormalizeUriLiteral(string uri)
        {
            var sb = new StringBuilder();
            bool pendingSpace = false;
            foreach (char c in uri.Trim(' ', '\t', '\r', '\n'))
            {
                if (c is ' ' or '\t' or '\r' or '\n')
                {
                    pendingSpace = true;
                    continue;
                }
                if (pendingSpace)
                {
                    sb.Append(' ');
                    pendingSpace = false;
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        private static Token NameOrWildcard(string text, ref int i, bool space)
        {
            int start = i;
            string first = ReadNCName(text, ref i);

            // "p:local" and "p:*" have no whitespace around the colon (A.2.4.1). "p::" is an axis, "p:=" a binding.
            if (i + 1 < text.Length && text[i] == ':')
            {
                if (IsNameStart(text, i + 1))
                {
                    i++;
                    string local = ReadNCName(text, ref i);
                    return new Token(TokenKind.Name, text[start..i], start, i, space) { Prefix = first, LocalName = local };
                }
                if (text[i + 1] == '*')
                {
                    i += 2;
                    return new Token(TokenKind.Wildcard, text[start..i], start, i, space) { Prefix = first };
                }
            }
            return new Token(TokenKind.Name, first, start, i, space) { LocalName = first };
        }

        private static string ReadNCName(string text, ref int i)
        {
            int start = i;
            i += char.IsHighSurrogate(text[i]) ? 2 : 1;
            while (i < text.Length && IsNameChar(text, i))
            {
                i += char.IsHighSurrogate(text[i]) ? 2 : 1;
            }
            return text[start..i];
        }

        private static bool IsDigit(char c) => c is >= '0' and <= '9';

        // NCName characters per XML 1.0 Fifth Edition / Namespaces 1.0 (no colon). Supplementary characters
        // U+10000..U+EFFFF are name characters, and arrive here as surrogate pairs.
        private static bool IsNameStart(string text, int i)
        {
            char c = text[i];
            if (char.IsHighSurrogate(c))
            {
                return IsSupplementaryNameChar(text, i);
            }
            return c != ':' && XmlConvert.IsStartNCNameChar(c);
        }

        private static bool IsNameChar(string text, int i)
        {
            char c = text[i];
            if (char.IsHighSurrogate(c))
            {
                return IsSupplementaryNameChar(text, i);
            }
            return c != ':' && XmlConvert.IsNCNameChar(c);
        }

        private static bool IsSupplementaryNameChar(string text, int i)
        {
            if (i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1]))
            {
                return false;
            }
            int cp = char.ConvertToUtf32(text[i], text[i + 1]);
            return cp is >= 0x10000 and <= 0xEFFFF;
        }
    }
}
