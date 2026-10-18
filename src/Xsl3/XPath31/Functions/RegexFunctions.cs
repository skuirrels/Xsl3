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
            bool dotAll = false, multiline = false, extended = false, literal = false, ignoreCase = false;
            foreach (char f in flags)
            {
                switch (f)
                {
                    case 's': dotAll = true; break;
                    case 'm': multiline = true; break;
                    case 'i': ignoreCase = true; break;
                    case 'x': extended = true; break;
                    case 'q': literal = true; break;
                    default: throw new XdmException("FORX0001", $"Invalid regular expression flag '{f}'");
                }
            }
            if (literal)
            {
                return new XPathRegex(new Regex(Regex.Escape(pattern), ignoreCase ? options | RegexOptions.IgnoreCase : options), 0, new[] { 0 });
            }
            if (extended)
            {
                pattern = StripWhitespace(pattern);
            }
            // Case-insensitivity is translated too: .NET's IgnoreCase would also widen \p{Lu} and friends.
            var translator = new Translator(pattern, dotAll, multiline, ignoreCase);
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
                if (c == '\\' && i + 1 < pattern.Length && pattern[i + 1] is not (' ' or '\t' or '\n' or '\r'))
                {
                    // An escaped bracket doesn't open or close a class; whitespace after a backslash is still removed.
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
            // XML 1.0 Fifth Edition NameStartChar and NameChar.
            private static readonly CodePointSet s_nameStart = CodePointSet.Of(
                (':', ':'), ('A', 'Z'), ('_', '_'), ('a', 'z'), (0xC0, 0xD6), (0xD8, 0xF6), (0xF8, 0x2FF), (0x370, 0x37D), (0x37F, 0x1FFF),
                (0x200C, 0x200D), (0x2070, 0x218F), (0x2C00, 0x2FEF), (0x3001, 0xD7FF), (0xF900, 0xFDCF), (0xFDF0, 0xFFFD), (0x10000, 0xEFFFF));

            private static readonly CodePointSet s_name = s_nameStart.Union(CodePointSet.Of(('-', '.'), ('0', '9'), (0xB7, 0xB7), (0x300, 0x36F), (0x203F, 0x2040)));

            // XSD \s is exactly space, tab, newline and carriage return.
            private static readonly CodePointSet s_space = CodePointSet.Of((' ', ' '), ('\t', '\n'), ('\r', '\r'));

            private static CodePointSet? s_word;

            // XSD \w is everything except punctuation, separators and "other" (\p{P}, \p{Z}, \p{C}).
            private static CodePointSet Word => s_word ??= CodePointSet.All
                .Subtract(CodePointSet.Category("P")!).Subtract(CodePointSet.Category("Z")!).Subtract(CodePointSet.Category("C")!);

            private static readonly string s_dot = CodePointSet.All.Subtract(CodePointSet.Of(('\n', '\n'), ('\r', '\r'))).ToPattern();
            private static readonly string s_dotAll = CodePointSet.All.ToPattern();

            private readonly string _p;
            private readonly bool _dotAll, _multiline, _ignoreCase;
            private readonly StringBuilder _out = new();
            private readonly Stack<int> _open = new();
            private readonly HashSet<int> _closed = new();
            private int _i;

            public Translator(string pattern, bool dotAll, bool multiline, bool ignoreCase)
            {
                _p = pattern;
                _dotAll = dotAll;
                _multiline = multiline;
                _ignoreCase = ignoreCase;
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
                            Escape();
                            canQuantify = true;
                            break;
                        case '[':
                            _out.Append(CharClass().ToPattern());
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
                            // One code point; except newline and carriage return unless the s flag is set.
                            _out.Append(_dotAll ? s_dotAll : s_dot);
                            canQuantify = true;
                            break;
                        case '^':
                            // With m, the start of any line: after a newline, but not after one that ends the string.
                            // Anchors are atoms in XPath, so they may be quantified ("alp^?ha").
                            _out.Append(_multiline ? "(?:\\A|(?<=\\n)(?!\\z))" : "(?:\\A)");
                            canQuantify = true;
                            break;
                        case '$':
                            // Without m, the end of the whole string (.NET's '$' would also match before a final newline).
                            _out.Append(_multiline ? "(?:(?=\\n)|\\z)" : "(?:\\z)");
                            canQuantify = true;
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
                            if (char.IsHighSurrogate(c) && _i < _p.Length && char.IsLowSurrogate(_p[_i]))
                            {
                                Literal(char.ConvertToUtf32(c, _p[_i++]));
                            }
                            else
                            {
                                Literal(c);
                            }
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

            // A literal code point; with the i flag, any of its case variants.
            private void Literal(int cp)
            {
                if (_ignoreCase)
                {
                    CodePointSet variants = CodePointSet.Single(cp).WithCaseVariants();
                    if (variants.Ranges.Count > 1 || variants.Ranges[0].Low != variants.Ranges[0].High)
                    {
                        _out.Append(variants.ToPattern());
                        return;
                    }
                }
                if (cp > 0xFFFF)
                {
                    _out.Append(char.ConvertFromUtf32(cp));
                    return;
                }
                char c = (char)cp;
                if ("\\*+?|{}[]()^$.# \t\n\r\f".IndexOf(c) >= 0)
                {
                    _out.Append(c switch { '\t' => "\\t", '\n' => "\\n", '\r' => "\\r", '\f' => "\\f", _ => "\\" + c });
                }
                else
                {
                    _out.Append(c);
                }
            }

            // An escape outside a character class (the backslash is consumed).
            private void Escape()
            {
                if (_i < _p.Length && _p[_i] >= '1' && _p[_i] <= '9')
                {
                    BackReference(_p[_i++]);
                    return;
                }
                var (cp, set) = EscapeItem();
                if (cp is int single)
                {
                    Literal(single);
                }
                else
                {
                    _out.Append(set!.ToPattern());
                }
            }

            // A single-character escape (its code point) or a class escape (its set); the backslash is consumed.
            private (int? CodePoint, CodePointSet? Set) EscapeItem()
            {
                if (_i >= _p.Length) throw Invalid(_p, "trailing backslash");
                char e = _p[_i++];
                switch (e)
                {
                    case 'n': return ('\n', null);
                    case 'r': return ('\r', null);
                    case 't': return ('\t', null);
                    case '\\': case '|': case '.': case '?': case '*': case '+': case '(': case ')':
                    case '{': case '}': case '-': case '[': case ']': case '^': case '$':
                        return (e, null);
                    case 'd': return (null, CodePointSet.Category("Nd"));
                    case 'D': return (null, CodePointSet.Category("Nd")!.Complement());
                    case 's': return (null, s_space);
                    case 'S': return (null, s_space.Complement());
                    case 'w': return (null, Word);
                    case 'W': return (null, Word.Complement());
                    case 'i': return (null, s_nameStart);
                    case 'I': return (null, s_nameStart.Complement());
                    case 'c': return (null, s_name);
                    case 'C': return (null, s_name.Complement());
                    case 'p': return (null, Property());
                    case 'P': return (null, Property().Complement());
                }
                throw Invalid(_p, $"unknown escape '\\{e}'");
            }

            // \p{L}, \p{IsBasicLatin}, … (after the 'p' or 'P').
            private CodePointSet Property()
            {
                if (_i >= _p.Length || _p[_i] != '{') throw Invalid(_p, "\\p must be followed by {");
                int close = _p.IndexOf('}', _i);
                if (close < 0) throw Invalid(_p, "unterminated \\p{");
                string name = _p[(_i + 1)..close];
                _i = close + 1;
                if (name.Length == 0) throw Invalid(_p, "empty property name");
                CodePointSet? set = name.StartsWith("Is", System.StringComparison.Ordinal) ? CodePointSet.Block(name[2..]) : CodePointSet.Category(name);
                return set ?? throw Invalid(_p, $"unknown property '{name}'");
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
                _out.Append(_ignoreCase ? "(?i:\\" : "(?:\\").Append(n).Append(')');
            }

            // [...], [^...], with subtraction [a-z-[aeiou]] (XSD 1.0 §F.1.1), after the '['. A '-' is a range
            // separator, or literal only as the first or last character of a group.
            private CodePointSet CharClass()
            {
                bool negate = false;
                if (_i < _p.Length && _p[_i] == '^')
                {
                    negate = true;
                    _i++;
                }
                CodePointSet literals = CodePointSet.Empty; // characters and ranges: case variants apply to these
                CodePointSet escapes = CodePointSet.Empty;
                CodePointSet? subtract = null;
                bool first = true;
                while (true)
                {
                    if (_i >= _p.Length) throw Invalid(_p, "unterminated character class");
                    char c = _p[_i];
                    if (c == ']')
                    {
                        if (first) throw Invalid(_p, "empty character class");
                        _i++;
                        break;
                    }
                    if (c == '-' && _i + 1 < _p.Length && _p[_i + 1] == '[')
                    {
                        if (first) throw Invalid(_p, "character class subtraction with nothing to subtract from");
                        _i += 2;
                        subtract = CharClass();
                        if (_i >= _p.Length || _p[_i] != ']') throw Invalid(_p, "character class subtraction must end the class");
                        _i++;
                        break;
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
                        literals = literals.Union(CodePointSet.Single('-'));
                        first = false;
                        continue;
                    }
                    // One item: a character or escape, possibly the start of a range.
                    var (low, set) = ClassItem();
                    if (_i + 1 < _p.Length && _p[_i] == '-' && _p[_i + 1] != ']' && _p[_i + 1] != '[')
                    {
                        if (low == null) throw Invalid(_p, "a range can't start with a multi-character escape");
                        _i++;
                        if (_p[_i] == '-') throw Invalid(_p, "'-' can't end a range unescaped");
                        var (high, _) = ClassItem();
                        if (high == null) throw Invalid(_p, "a range can't end with a multi-character escape");
                        if (high < low) throw Invalid(_p, "range out of order");
                        if (_i < _p.Length && _p[_i] == '-' && _i + 1 < _p.Length && _p[_i + 1] != ']' && _p[_i + 1] != '[')
                        {
                            throw Invalid(_p, "'-' must be escaped after a range");
                        }
                        literals = literals.Union(CodePointSet.Of((low.Value, high.Value)));
                    }
                    else if (low is int cp)
                    {
                        literals = literals.Union(CodePointSet.Single(cp));
                    }
                    else
                    {
                        escapes = escapes.Union(set!);
                    }
                    first = false;
                }
                CodePointSet result = (_ignoreCase ? literals.WithCaseVariants() : literals).Union(escapes);
                if (negate)
                {
                    result = result.Complement();
                }
                if (subtract != null)
                {
                    result = result.Subtract(subtract);
                }
                return result;
            }

            // A single character (its code point) or a class escape (its set) inside a character class.
            private (int? CodePoint, CodePointSet? Set) ClassItem()
            {
                char c = _p[_i++];
                if (c == '\\')
                {
                    return EscapeItem();
                }
                if (char.IsHighSurrogate(c) && _i < _p.Length && char.IsLowSurrogate(_p[_i]))
                {
                    return (char.ConvertToUtf32(c, _p[_i++]), null);
                }
                return (c, null);
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
