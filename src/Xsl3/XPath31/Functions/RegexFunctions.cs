// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.XPath;
using Xsl3.Xdm;

namespace Xsl3.XPath31.Functions
{
    /// <summary>
    /// An XPath regular expression (F&amp;O 3.1 §5.6.1) translated to .NET (SPEC §13.4). The translator
    /// parses the pattern itself: XPath rejects much that .NET accepts (stray braces, unknown escapes), and
    /// several constructs mean different things (<c>.</c>, <c>$</c>, <c>\s</c>, <c>\w</c>, and the XML name
    /// classes <c>\i</c>/<c>\c</c> that .NET lacks).
    /// </summary>
    internal sealed class XPathRegex
    {
        private static readonly ConcurrentDictionary<(string, string), XPathRegex> s_cache = new();

        private XPathRegex(Regex regex, int groupCount, int[] parents)
        {
            Regex = regex;
            GroupCount = groupCount;
            GroupParents = parents;
        }

        public Regex Regex { get; }

        public int GroupCount { get; }

        /// <summary>For capturing group n (1-based), the number of the group that encloses it, 0 for none.</summary>
        public int[] GroupParents { get; }

        public static XPathRegex Get(string pattern, string flags)
        {
            if (s_cache.Count > 1000)
            {
                s_cache.Clear();
            }
            return s_cache.GetOrAdd((pattern, flags), key => Compile(key.Item1, key.Item2));
        }

        private static XPathRegex Compile(string pattern, string flags)
        {
            var options = RegexOptions.CultureInvariant;
            bool dotAll = false, multiline = false, extended = false, literal = false;
            foreach (char f in flags)
            {
                switch (f)
                {
                    case 's': dotAll = true; break;
                    case 'm': multiline = true; options |= RegexOptions.Multiline; break;
                    case 'i': options |= RegexOptions.IgnoreCase; break;
                    case 'x': extended = true; break;
                    case 'q': literal = true; break;
                    default: throw new XdmException("FORX0001", $"Invalid regular expression flag '{f}'");
                }
            }
            if (literal)
            {
                return new XPathRegex(new Regex(Regex.Escape(pattern), options), 0, new[] { 0 });
            }
            if (extended)
            {
                pattern = StripWhitespace(pattern);
            }
            var translator = new Translator(pattern, dotAll, multiline);
            string net = translator.Translate();
            try
            {
                return new XPathRegex(new Regex(net, options), translator.GroupCount, translator.Parents.ToArray());
            }
            catch (System.ArgumentException e)
            {
                throw new XdmException("FORX0002", $"Invalid regular expression '{pattern}': {e.Message}");
            }
        }

        // Flag x: whitespace is removed, except inside character class expressions.
        private static string StripWhitespace(string pattern)
        {
            var sb = new StringBuilder(pattern.Length);
            int depth = 0;
            for (int i = 0; i < pattern.Length; i++)
            {
                char c = pattern[i];
                if (c == '\\' && i + 1 < pattern.Length)
                {
                    sb.Append(c).Append(pattern[++i]);
                    continue;
                }
                if (c == '[') depth++;
                else if (c == ']' && depth > 0) depth--;
                if (depth == 0 && c is ' ' or '\t' or '\n' or '\r')
                {
                    continue;
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        private static XdmException Invalid(string pattern, string why) => new("FORX0002", $"Invalid regular expression '{pattern}': {why}");

        private sealed class Translator
        {
            // XML 1.0 Fifth Edition NameStartChar / NameChar, as .NET character class contents (BMP only).
            private const string NameStartClass = ":A-Z_a-z\\u00C0-\\u00D6\\u00D8-\\u00F6\\u00F8-\\u02FF\\u0370-\\u037D\\u037F-\\u1FFF\\u200C-\\u200D\\u2070-\\u218F\\u2C00-\\u2FEF\\u3001-\\uD7FF\\uF900-\\uFDCF\\uFDF0-\\uFFFD";
            private const string NameClass = NameStartClass + "\\-.0-9\\u00B7\\u0300-\\u036F\\u203F-\\u2040";

            private readonly string _p;
            private readonly bool _dotAll, _multiline;
            private readonly StringBuilder _out = new();
            private readonly Stack<int> _open = new();
            private readonly HashSet<int> _closed = new();
            private int _i;

            public Translator(string pattern, bool dotAll, bool multiline)
            {
                _p = pattern;
                _dotAll = dotAll;
                _multiline = multiline;
                Parents.Add(0);
            }

            public int GroupCount { get; private set; }

            public List<int> Parents { get; } = new();

            public string Translate()
            {
                bool canQuantify = false;
                while (_i < _p.Length)
                {
                    char c = _p[_i++];
                    switch (c)
                    {
                        case '\\':
                            Escape(inClass: false);
                            canQuantify = true;
                            break;
                        case '[':
                            CharClass();
                            canQuantify = true;
                            break;
                        case '(':
                            if (_i < _p.Length && _p[_i] == '?')
                            {
                                if (_i + 1 < _p.Length && _p[_i + 1] == ':')
                                {
                                    _i += 2;
                                    _out.Append("(?:");
                                    _open.Push(0);
                                    canQuantify = false;
                                    break;
                                }
                                throw Invalid(_p, "'(?' is only allowed as '(?:'");
                            }
                            GroupCount++;
                            Parents.Add(_open.FirstOrDefault(g => g > 0));
                            _open.Push(GroupCount);
                            _out.Append('(');
                            canQuantify = false;
                            break;
                        case ')':
                            if (_open.Count == 0) throw Invalid(_p, "unbalanced ')'");
                            _closed.Add(_open.Pop());
                            _out.Append(')');
                            canQuantify = true;
                            break;
                        case '|':
                            _out.Append('|');
                            canQuantify = false;
                            break;
                        case '.':
                            // One code point; except newline unless the s flag is set.
                            _out.Append(_dotAll ? "(?:[\\uD800-\\uDBFF][\\uDC00-\\uDFFF]|[\\s\\S])" : "(?:[\\uD800-\\uDBFF][\\uDC00-\\uDFFF]|[^\\n])");
                            canQuantify = true;
                            break;
                        case '^':
                            _out.Append('^');
                            canQuantify = false;
                            break;
                        case '$':
                            // Without m, '$' is the end of the whole string (.NET's '$' would also match before a final newline).
                            _out.Append(_multiline ? "(?=\\n|\\z)" : "\\z");
                            canQuantify = false;
                            break;
                        case '*':
                        case '+':
                        case '?':
                            if (!canQuantify) throw Invalid(_p, $"quantifier '{c}' with nothing to repeat");
                            _out.Append(c);
                            Reluctant();
                            canQuantify = false;
                            break;
                        case '{':
                            if (!canQuantify) throw Invalid(_p, "quantifier '{' with nothing to repeat");
                            Quantifier();
                            canQuantify = false;
                            break;
                        case '}':
                        case ']':
                            throw Invalid(_p, $"unescaped '{c}'");
                        default:
                            AppendLiteral(c);
                            canQuantify = true;
                            break;
                    }
                }
                if (_open.Count != 0)
                {
                    throw Invalid(_p, "unbalanced '('");
                }
                return _out.ToString();
            }

            private void Reluctant()
            {
                if (_i < _p.Length && _p[_i] == '?')
                {
                    _out.Append('?');
                    _i++;
                }
            }

            // {n}, {n,}, {n,m}
            private void Quantifier()
            {
                int start = _i;
                while (_i < _p.Length && char.IsAsciiDigit(_p[_i])) _i++;
                if (_i == start) throw Invalid(_p, "a quantifier needs a minimum");
                string min = _p[start.._i];
                string? max = null;
                bool comma = false;
                if (_i < _p.Length && _p[_i] == ',')
                {
                    comma = true;
                    _i++;
                    int maxStart = _i;
                    while (_i < _p.Length && char.IsAsciiDigit(_p[_i])) _i++;
                    max = _p[maxStart.._i];
                }
                if (_i >= _p.Length || _p[_i] != '}') throw Invalid(_p, "malformed quantifier");
                _i++;
                if (max is { Length: > 0 } && System.Numerics.BigInteger.Parse(max) < System.Numerics.BigInteger.Parse(min))
                {
                    throw Invalid(_p, "quantifier maximum below minimum");
                }
                _out.Append('{').Append(min).Append(comma ? "," + max : "").Append('}');
                Reluctant();
            }

            private void AppendLiteral(char c)
            {
                if (c is '#' or ' ')
                {
                    _out.Append('\\');
                }
                _out.Append(c);
            }

            // An escape outside or inside a character class (the backslash is consumed).
            private void Escape(bool inClass)
            {
                if (_i >= _p.Length) throw Invalid(_p, "trailing backslash");
                char e = _p[_i++];
                switch (e)
                {
                    case 'n': _out.Append("\\n"); return;
                    case 'r': _out.Append("\\r"); return;
                    case 't': _out.Append("\\t"); return;
                    case '\\': case '|': case '.': case '?': case '*': case '+': case '(': case ')':
                    case '{': case '}': case '-': case '[': case ']': case '^': case '$':
                        _out.Append('\\').Append(e);
                        return;
                    case 'd': _out.Append(inClass ? "\\d" : "\\d"); return;
                    case 'D': _out.Append("\\D"); return;
                    // XSD \s is exactly space, tab, newline and carriage return.
                    case 's': _out.Append(inClass ? " \\t\\n\\r" : "[ \\t\\n\\r]"); return;
                    case 'S': _out.Append(inClass ? "\\S" : "[^ \\t\\n\\r]"); return;
                    // XSD \w is everything except punctuation, separators and other (\p{P}, \p{Z}, \p{C}).
                    case 'w': _out.Append(inClass ? "\\w" : "[^\\p{P}\\p{Z}\\p{C}]"); return;
                    case 'W': _out.Append(inClass ? "\\W" : "[\\p{P}\\p{Z}\\p{C}]"); return;
                    case 'i': _out.Append(inClass ? NameStartClass : "[" + NameStartClass + "]"); return;
                    case 'c': _out.Append(inClass ? NameClass : "[" + NameClass + "]"); return;
                    case 'I': if (inClass) throw Invalid(_p, "\\I inside a character class"); _out.Append("[^" + NameStartClass + "]"); return;
                    case 'C': if (inClass) throw Invalid(_p, "\\C inside a character class"); _out.Append("[^" + NameClass + "]"); return;
                    case 'p':
                    case 'P':
                        Property(e);
                        return;
                }
                if (!inClass && e >= '1' && e <= '9')
                {
                    BackReference(e);
                    return;
                }
                throw Invalid(_p, $"unknown escape '\\{e}'");
            }

            // \p{L}, \p{IsBasicLatin}, …
            private void Property(char e)
            {
                if (_i >= _p.Length || _p[_i] != '{') throw Invalid(_p, $"\\{e} must be followed by {{");
                int close = _p.IndexOf('}', _i);
                if (close < 0) throw Invalid(_p, $"unterminated \\{e}{{");
                string name = _p[(_i + 1)..close];
                _i = close + 1;
                if (name.Length == 0) throw Invalid(_p, "empty property name");
                if (name.StartsWith("Is", System.StringComparison.Ordinal) && s_supplementaryBlocks.TryGetValue(name[2..], out (int Low, int High) range))
                {
                    // .NET only knows blocks of the Basic Multilingual Plane; these are matched as surrogate pairs.
                    string pairs = Utf16Range(range.Low, range.High);
                    _out.Append(e == 'p' ? $"(?:{pairs})" : $"(?!{pairs})(?:[\\uD800-\\uDBFF][\\uDC00-\\uDFFF]|[\\s\\S])");
                    return;
                }
                _out.Append('\\').Append(e).Append('{').Append(name).Append('}');
            }

            // XML Schema 1.0 block names (Unicode 3.1) outside the Basic Multilingual Plane.
            private static readonly Dictionary<string, (int, int)> s_supplementaryBlocks = new(System.StringComparer.Ordinal)
            {
                ["OldItalic"] = (0x10300, 0x1032F),
                ["Gothic"] = (0x10330, 0x1034F),
                ["Deseret"] = (0x10400, 0x1044F),
                ["ByzantineMusicalSymbols"] = (0x1D000, 0x1D0FF),
                ["MusicalSymbols"] = (0x1D100, 0x1D1FF),
                ["MathematicalAlphanumericSymbols"] = (0x1D400, 0x1D7FF),
                ["CJKUnifiedIdeographsExtensionB"] = (0x20000, 0x2A6DF),
                ["CJKCompatibilityIdeographsSupplement"] = (0x2F800, 0x2FA1F),
                ["Tags"] = (0xE0000, 0xE007F),
                ["SupplementaryPrivateUseArea-A"] = (0xF0000, 0xFFFFF),
                ["SupplementaryPrivateUseArea-B"] = (0x100000, 0x10FFFF),
            };

            // An alternation matching the UTF-16 encodings of the code points low..high (both supplementary).
            private static string Utf16Range(int low, int high)
            {
                static (char Hi, char Lo) Split(int cp) => (char.ConvertFromUtf32(cp)[0], char.ConvertFromUtf32(cp)[1]);
                static string U(char c) => "\\u" + ((int)c).ToString("X4", System.Globalization.CultureInfo.InvariantCulture);
                var (h1, l1) = Split(low);
                var (h2, l2) = Split(high);
                if (h1 == h2)
                {
                    return $"{U(h1)}[{U(l1)}-{U(l2)}]";
                }
                var parts = new List<string> { $"{U(h1)}[{U(l1)}-\\uDFFF]" };
                if (h2 - h1 > 1)
                {
                    parts.Add($"[{U((char)(h1 + 1))}-{U((char)(h2 - 1))}][\\uDC00-\\uDFFF]");
                }
                parts.Add($"{U(h2)}[\\uDC00-{U(l2)}]");
                return string.Join("|", parts);
            }

            // \N: as many digits as still name a group that has been closed before this point.
            private void BackReference(char first)
            {
                int n = first - '0';
                while (_i < _p.Length && char.IsAsciiDigit(_p[_i]) && n * 10 + (_p[_i] - '0') <= GroupCount)
                {
                    n = n * 10 + (_p[_i] - '0');
                    _i++;
                }
                if (n > GroupCount || !_closed.Contains(n))
                {
                    throw Invalid(_p, $"back-reference \\{n} to a group that isn't complete");
                }
                _out.Append("(?:\\").Append(n).Append(')');
            }

            // [...], [^...], with subtraction [a-z-[aeiou]] (XSD 1.0 §F.1.1). A '-' is a range separator, or
            // literal only as the first or last character of a group.
            private void CharClass()
            {
                _out.Append('[');
                if (_i < _p.Length && _p[_i] == '^')
                {
                    _out.Append('^');
                    _i++;
                }
                bool first = true;
                while (true)
                {
                    if (_i >= _p.Length) throw Invalid(_p, "unterminated character class");
                    char c = _p[_i];
                    if (c == ']')
                    {
                        if (first) throw Invalid(_p, "empty character class");
                        _i++;
                        _out.Append(']');
                        return;
                    }
                    if (c == '-' && _i + 1 < _p.Length && _p[_i + 1] == '[')
                    {
                        if (first) throw Invalid(_p, "character class subtraction with nothing to subtract from");
                        _i += 2;
                        _out.Append('-');
                        CharClass();
                        if (_i >= _p.Length || _p[_i] != ']') throw Invalid(_p, "character class subtraction must end the class");
                        continue;
                    }
                    if (c == '[')
                    {
                        throw Invalid(_p, "unescaped '[' in a character class");
                    }
                    if (c == '-')
                    {
                        // A literal '-': only first, or last before ']'.
                        bool last = _i + 1 < _p.Length && _p[_i + 1] == ']';
                        if (!first && !last) throw Invalid(_p, "'-' must be escaped here");
                        _i++;
                        _out.Append("\\-");
                        first = false;
                        continue;
                    }
                    // One item: a character or escape, possibly the start of a range.
                    int itemStart = _out.Length;
                    int? low = ClassChar();
                    if (_i + 1 < _p.Length && _p[_i] == '-' && _p[_i + 1] != ']' && _p[_i + 1] != '[')
                    {
                        if (low == null) throw Invalid(_p, "a range can't start with a multi-character escape");
                        _i++;
                        if (_p[_i] == '-') throw Invalid(_p, "'-' can't end a range unescaped");
                        _out.Append('-');
                        int? high = ClassChar();
                        if (high == null) throw Invalid(_p, "a range can't end with a multi-character escape");
                        if (high < low) throw Invalid(_p, "range out of order");
                        if (_i < _p.Length && _p[_i] == '-' && _i + 1 < _p.Length && _p[_i + 1] != ']' && _p[_i + 1] != '[')
                        {
                            throw Invalid(_p, "'-' must be escaped after a range");
                        }
                    }
                    first = false;
                }
            }

            // A single character (returning its code point) or a class escape (returning null), appended to the output.
            private int? ClassChar()
            {
                char c = _p[_i++];
                if (c == '\\')
                {
                    if (_i < _p.Length && _p[_i] is 'n' or 'r' or 't' or '\\' or '|' or '.' or '?' or '*' or '+' or '(' or ')' or '{' or '}' or '-' or '[' or ']' or '^' or '$')
                    {
                        char e = _p[_i];
                        Escape(inClass: true);
                        return e switch { 'n' => '\n', 'r' => '\r', 't' => '\t', _ => e };
                    }
                    Escape(inClass: true);
                    return null;
                }
                if (char.IsHighSurrogate(c) && _i < _p.Length && char.IsLowSurrogate(_p[_i]))
                {
                    _out.Append(c).Append(_p[_i]);
                    return char.ConvertToUtf32(c, _p[_i++]);
                }
                if (c is '^')
                {
                    _out.Append('\\');
                }
                _out.Append(c);
                return c;
            }
        }
    }

    // fn:matches, fn:replace, fn:tokenize, fn:analyze-string (F&O 3.1 §5.6).
    internal static partial class FunctionLibrary
    {
        private static void RegisterRegex()
        {
            Define("fn:matches($input as xs:string?, $pattern as xs:string) as xs:boolean", (c, a) => Bool(XPathRegex.Get(S(a[1]), "").Regex.IsMatch(S(a[0]))));
            Define("fn:matches($input as xs:string?, $pattern as xs:string, $flags as xs:string) as xs:boolean", (c, a) => Bool(XPathRegex.Get(S(a[1]), S(a[2])).Regex.IsMatch(S(a[0]))));
            Define("fn:replace($input as xs:string?, $pattern as xs:string, $replacement as xs:string) as xs:string", (c, a) => Str(Replace(S(a[0]), S(a[1]), S(a[2]), "")));
            Define("fn:replace($input as xs:string?, $pattern as xs:string, $replacement as xs:string, $flags as xs:string) as xs:string", (c, a) => Str(Replace(S(a[0]), S(a[1]), S(a[2]), S(a[3]))));
            Define("fn:tokenize($input as xs:string?) as xs:string*", (c, a) => Tokenize(Lexical.Collapse(S(a[0])), " ", ""));
            Define("fn:tokenize($input as xs:string?, $pattern as xs:string) as xs:string*", (c, a) => Tokenize(S(a[0]), S(a[1]), ""));
            Define("fn:tokenize($input as xs:string?, $pattern as xs:string, $flags as xs:string) as xs:string*", (c, a) => Tokenize(S(a[0]), S(a[1]), S(a[2])));
            Define("fn:analyze-string($input as xs:string?, $pattern as xs:string) as element()", (c, a) => One(AnalyzeString(S(a[0]), S(a[1]), "")));
            Define("fn:analyze-string($input as xs:string?, $pattern as xs:string, $flags as xs:string) as element()", (c, a) => One(AnalyzeString(S(a[0]), S(a[1]), S(a[2]))));
        }

        private static XPathRegex NonEmptyMatching(string pattern, string flags)
        {
            XPathRegex re = XPathRegex.Get(pattern, flags);
            if (re.Regex.IsMatch(""))
            {
                throw new XdmException("FORX0003", $"The pattern '{pattern}' matches a zero-length string");
            }
            return re;
        }

        private static string Replace(string input, string pattern, string replacement, string flags)
        {
            XPathRegex re = NonEmptyMatching(pattern, flags);
            bool literal = flags.Contains('q');
            // Parse the replacement once: literal text and $N group references.
            var parts = new List<(string? Text, int Group)>();
            var text = new StringBuilder();
            for (int i = 0; i < replacement.Length; i++)
            {
                char ch = replacement[i];
                if (literal)
                {
                    text.Append(ch);
                }
                else if (ch == '\\')
                {
                    if (i + 1 < replacement.Length && replacement[i + 1] is '\\' or '$')
                    {
                        text.Append(replacement[++i]);
                    }
                    else
                    {
                        throw new XdmException("FORX0004", $"Invalid replacement string '{replacement}'");
                    }
                }
                else if (ch == '$')
                {
                    if (i + 1 >= replacement.Length || !char.IsAsciiDigit(replacement[i + 1]))
                    {
                        throw new XdmException("FORX0004", $"Invalid replacement string '{replacement}'");
                    }
                    int n = replacement[++i] - '0';
                    while (i + 1 < replacement.Length && char.IsAsciiDigit(replacement[i + 1]) && n * 10 + (replacement[i + 1] - '0') <= re.GroupCount)
                    {
                        n = n * 10 + (replacement[++i] - '0');
                    }
                    if (text.Length > 0)
                    {
                        parts.Add((text.ToString(), 0));
                        text.Clear();
                    }
                    parts.Add((null, n));
                }
                else
                {
                    text.Append(ch);
                }
            }
            if (text.Length > 0)
            {
                parts.Add((text.ToString(), 0));
            }
            return re.Regex.Replace(input, m =>
            {
                var sb = new StringBuilder();
                foreach (var (t, g) in parts)
                {
                    // A reference to a group that doesn't exist or didn't take part is the empty string.
                    sb.Append(t ?? (g <= re.GroupCount ? m.Groups[g].Value : ""));
                }
                return sb.ToString();
            });
        }

        private static IList<XPathItem> Tokenize(string input, string pattern, string flags)
        {
            XPathRegex re = NonEmptyMatching(pattern, flags);
            if (input.Length == 0)
            {
                return Empty;
            }
            var tokens = new List<XPathItem>();
            int pos = 0;
            for (Match m = re.Regex.Match(input); m.Success; m = m.NextMatch())
            {
                tokens.Add(AtomicValue.OfString(input[pos..m.Index]));
                pos = m.Index + m.Length;
            }
            tokens.Add(AtomicValue.OfString(input[pos..]));
            return tokens;
        }

        private const string FnNs = "http://www.w3.org/2005/xpath-functions";

        /// <summary>fn:analyze-string: an fn:analyze-string-result element of fn:match / fn:non-match / nested fn:group.</summary>
        private static XPathNavigator AnalyzeString(string input, string pattern, string flags)
        {
            XPathRegex re = NonEmptyMatching(pattern, flags);
            var sb = new StringBuilder();
            var w = XmlWriter.Create(sb, new XmlWriterSettings { OmitXmlDeclaration = true });
            w.WriteStartElement("fn", "analyze-string-result", FnNs);
            int pos = 0;
            if (input.Length > 0)
            {
                for (Match m = re.Regex.Match(input); m.Success; m = m.NextMatch())
                {
                    if (m.Index > pos)
                    {
                        w.WriteElementString("fn", "non-match", FnNs, input[pos..m.Index]);
                    }
                    w.WriteStartElement("fn", "match", FnNs);
                    WriteGroupContent(w, re, m, 0, m.Index, m.Index + m.Length, input);
                    w.WriteEndElement();
                    pos = m.Index + m.Length;
                }
                if (pos < input.Length)
                {
                    w.WriteElementString("fn", "non-match", FnNs, input[pos..]);
                }
            }
            w.WriteEndElement();
            w.Flush();
            XPathNavigator doc = new XPathDocument(new System.IO.StringReader(sb.ToString())).CreateNavigator();
            doc.MoveToFirstChild();
            return doc;
        }

        // The text of [start, end) with the participating child groups of "parent" as fn:group elements.
        private static void WriteGroupContent(XmlWriter w, XPathRegex re, Match m, int parent, int start, int end, string input)
        {
            int pos = start;
            for (int g = 1; g <= re.GroupCount; g++)
            {
                if (re.GroupParents[g] != parent || !m.Groups[g].Success)
                {
                    continue;
                }
                Group group = m.Groups[g];
                if (group.Index > pos)
                {
                    w.WriteString(input[pos..group.Index]);
                }
                w.WriteStartElement("fn", "group", FnNs);
                w.WriteAttributeString("nr", g.ToString(System.Globalization.CultureInfo.InvariantCulture));
                WriteGroupContent(w, re, m, g, group.Index, group.Index + group.Length, input);
                w.WriteEndElement();
                pos = group.Index + group.Length;
            }
            if (end > pos)
            {
                w.WriteString(input[pos..end]);
            }
        }
    }
}
