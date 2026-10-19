// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.XPath;
using Xsl3.XPath31;
using Xsl3.XPath31.Functions;
using Xsl3.Xdm;

namespace Xsl3.Runtime
{
    /// <summary>
    /// Xsl3: run-time support for the XSLT 2.0 rules that QilGenerator.V2 compiles: content of constructed nodes,
    /// simple content, attribute value templates, sequence-type conversion of variables and parameters.
    /// </summary>
    public sealed partial class XsltLibrary
    {
        /// <summary>A sequence that must have at most one item, as a string (empty for an empty sequence).</summary>
        public string X31String(IList<XPathItem> items)
        {
            AtomicValue? a = Sequences.AtomizeOptional(items, "the argument");
            return a?.Value ?? "";
        }

        public XPathNavigator X31NodeItem(XPathItem item, string code) =>
            item as XPathNavigator ?? throw new XdmException(code, "A node is required, but the item is an atomic value");

        public double X31RaiseDouble(string code, string message) => throw new XdmException(code, message);

        /// <summary>A sequence that must be exactly one node.</summary>
        public XPathNavigator X31SingleNode(IList<XPathItem> items, string code) =>
            items.Count == 1 && items[0] is XPathNavigator n ? n : throw new XdmException(code, items.Count == 1 ? "A node is required, but the item is an atomic value" : $"A single node is required, not {items.Count} items");

        /// <summary>
        /// Key values (xsl:key's use, key()'s second argument) as the strings the 1.0 key machinery compares:
        /// the atomised values, encoded so that equal strings are values that are equal by eq (XSLT 3.0 §20.2.2).
        /// Values that equal nothing (NaN) are dropped.
        /// </summary>
        public IList<XPathItem> X31KeyValues(IList<XPathItem> values, string collation)
        {
            var result = new List<XPathItem>(values.Count);
            Collation? coll = collation == Collation.CodepointUri ? null : KeyCollation(collation);
            foreach (XPathItem item in Sequences.Atomize(values))
            {
                var a = (AtomicValue)item;
                string? key = coll != null && a.Type.Primitive is Primitive.String or Primitive.UntypedAtomic or Primitive.AnyUri
                    ? "c" + System.Convert.ToHexString(coll.KeyBytes(a.Value))
                    : KeyString(a);
                if (key != null)
                {
                    result.Add(AtomicValue.OfString(key));
                }
            }
            return result;
        }

        private readonly Dictionary<string, Collation> _keyCollations = new(System.StringComparer.Ordinal);

        private Collation KeyCollation(string uri)
        {
            if (!_keyCollations.TryGetValue(uri, out Collation? c))
            {
                _keyCollations[uri] = c = Collation.Resolve(uri);
            }
            return c;
        }

        private static string? KeyString(AtomicValue a)
        {
            if (a.Type.IsNumeric)
            {
                double d = Numeric.ToDouble(a);
                return double.IsNaN(d) ? null : "n" + d.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            }
            Primitive p = a.Type.Primitive;
            return a switch
            {
                _ when p is Primitive.String or Primitive.UntypedAtomic or Primitive.AnyUri => "s" + a.Value,
                // Dates and times are equal when they are the same instant (timezones normalised).
                DateTimeValue dt => "t" + p + " " + dt.DateTime.ToTimeline(p, 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
                DurationValue d => "d" + d.Duration.Months + " " + d.Duration.Seconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
                _ => "t" + p + " " + a.Value,
            };
        }

        /// <summary>An attribute value template's expression: the atomised items joined with single spaces.</summary>
        public string X31AvtString(IList<XPathItem> items)
        {
            if (items.Count == 1 && items[0] is not XdmArray)
            {
                return Sequences.Atomize(items[0]).Value;
            }
            // XSLT 3.0 §5.6.1: as simple content (§5.7.2), so adjacent text nodes are merged with no separator.
            return X31SimpleContent(items, " ");
        }

        /// <summary>
        /// The items of complex content (XSLT 3.0 §5.7.1): each run of adjacent atomic values becomes one string
        /// with single spaces between the values (empty strings are dropped), and a document node is replaced by
        /// its children. Nodes are returned as they are; the caller copies them.
        /// </summary>
        public IList<XPathItem> X31ContentItems(IList<XPathItem> items)
        {
            if (items.Any(i => i is XdmArray))
            {
                // Arrays in content are flattened to their members, nodes included (XSLT 3.0 §5.7.1).
                items = Flatten(items);
            }
            var result = new List<XPathItem>(items.Count);
            StringBuilder? run = null;
            void Flush()
            {
                if (run != null)
                {
                    if (run.Length > 0) result.Add(AtomicValue.OfString(run.ToString()));
                    run = null;
                }
            }
            foreach (XPathItem item in items)
            {
                if (item is XPathNavigator nav)
                {
                    Flush();
                    if (nav.NodeType == XPathNodeType.Root)
                    {
                        XPathNavigator child = nav.Clone();
                        if (child.MoveToFirstChild())
                        {
                            do
                            {
                                result.Add(child.Clone());
                            }
                            while (child.MoveToNext());
                        }
                    }
                    else
                    {
                        result.Add(nav);
                    }
                }
                else
                {
                    if (run == null)
                    {
                        run = new StringBuilder();
                    }
                    else
                    {
                        run.Append(' ');
                    }
                    if (item is XdmArray array)
                    {
                        // An array atomizes to its members' atomized values (XPath 3.1 §2.5.2).
                        run.Append(string.Join(" ", Sequences.Atomize(Sequences.One(array)).Select(a => a.Value)));
                    }
                    else
                    {
                        run.Append(Sequences.Atomize(item).Value);
                    }
                }
            }
            Flush();
            return result;
        }

        public bool X31IsArray(XPathItem item) => item is XdmArray;

        /// <summary>The members of an array, flattened (?*); empty for other items.</summary>
        public IList<XPathItem> X31ArrayMembers(XPathItem item) =>
            item is XdmArray array ? array.Members.SelectMany(m => m).ToList() : Sequences.Empty;

        private static List<XPathItem> Flatten(IList<XPathItem> items)
        {
            var flat = new List<XPathItem>(items.Count);
            foreach (XPathItem item in items)
            {
                if (item is XdmArray array)
                {
                    foreach (IList<XPathItem> member in array.Members)
                    {
                        flat.AddRange(Flatten(member));
                    }
                }
                else
                {
                    flat.Add(item);
                }
            }
            return flat;
        }

        /// <summary>
        /// Simple content (XSLT 3.0 §5.7.2), for attributes, comments, processing instructions, namespace nodes and
        /// xsl:value-of: adjacent text nodes merge, zero-length text nodes are dropped, every item is atomised, and
        /// the strings are joined with <paramref name="separator"/>.
        /// </summary>
        public string X31SimpleContent(IList<XPathItem> items, string separator)
        {
            var parts = new List<string>(items.Count);
            StringBuilder? text = null;
            foreach (XPathItem item in items)
            {
                if (item is XPathNavigator { NodeType: XPathNodeType.Text or XPathNodeType.Whitespace or XPathNodeType.SignificantWhitespace } t)
                {
                    (text ??= new StringBuilder()).Append(t.Value);
                    continue;
                }
                if (text != null)
                {
                    if (text.Length > 0) parts.Add(text.ToString());
                    text = null;
                }
                if (item is XPathNavigator { NodeType: XPathNodeType.Root or XPathNodeType.Element } container)
                {
                    parts.Add(container.Value);
                }
                else if (item is XdmArray array)
                {
                    foreach (XPathItem atom in Sequences.Atomize(Sequences.One(array)))
                    {
                        parts.Add(atom.Value);
                    }
                }
                else
                {
                    parts.Add(Sequences.Atomize(item).Value);
                }
            }
            if (text != null && text.Length > 0)
            {
                parts.Add(text.ToString());
            }
            return string.Join(separator, parts);
        }

        /// <summary>The function conversion rules to a variable's, parameter's or function's declared type.</summary>
        public IList<XPathItem> X31Coerce(IList<XPathItem> value, double typeId, string errorCode)
        {
            var type = (SequenceTypeTest)ConstantPool.Get((int)typeId);
            return Unshared(Coercion.CoerceTo(value, type, errorCode, () => "The value"), new[] { value });
        }

        /// <summary>The value, if it is an instance of the type (no conversion is applied); else the error.</summary>
        public IList<XPathItem> X31InstanceCheck(IList<XPathItem> value, double typeId, string errorCode)
        {
            var type = (SequenceTypeTest)ConstantPool.Get((int)typeId);
            return type.Matches(value) ? value : throw new XdmException(errorCode, $"The context item must be {type}, but is {Coercion.Describe(value)}");
        }

        /// <summary>Nothing: the argument is evaluated for its errors (a type check).</summary>
        public IList<XPathItem> X31Discard(IList<XPathItem> value) => Sequences.Empty;

        /// <summary>The text of the built-in template rule for an item that isn't a node: an atomic value's string value.</summary>
        public string X31ItemText(XPathItem item) => item is AtomicValue a ? a.Value : "";

        // ------------------------------------------------------------ xsl:where-populated, xsl:on-empty

        /// <summary>Whether a sequence has only vacuous items (XSLT 3.0 §8.4.2): zero-length strings and text nodes, documents without children, empty arrays.</summary>
        public bool X31Vacuous(IList<XPathItem> items)
        {
            foreach (XPathItem item in items)
            {
                if (!IsVacuous(item))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsVacuous(XPathItem item) => item switch
        {
            XPathNavigator n => n.NodeType switch
            {
                XPathNodeType.Text or XPathNodeType.Whitespace or XPathNodeType.SignificantWhitespace => n.Value.Length == 0,
                XPathNodeType.Root => !n.HasChildren,
                _ => false,
            },
            XdmArray a => a.Members.All(m => m.All(IsVacuous)),
            XdmFunction => false,
            AtomicValue v => v.Type.Primitive is Primitive.String or Primitive.UntypedAtomic && v.Value.Length == 0,
            _ => false,
        };

        /// <summary>xsl:where-populated: the items without those deemed empty (also elements without children).</summary>
        public IList<XPathItem> X31Populated(IList<XPathItem> items)
        {
            var result = new List<XPathItem>(items.Count);
            foreach (XPathItem item in items)
            {
                // Deemed empty (XSLT 3.0 §8.4.1): a document or element without children, another node whose string
                // value is "", an atomic value that is "" as a string, an empty map.
                bool empty = IsVacuous(item) || item is XPathNavigator { NodeType: XPathNodeType.Element, HasChildren: false } ||
                    item is XPathNavigator { NodeType: not (XPathNodeType.Element or XPathNodeType.Root or XPathNodeType.Namespace) } node && node.Value.Length == 0 ||
                    item is AtomicValue atomic && atomic.Value.Length == 0 ||
                    item is XdmMap { Count: 0 };
                if (!empty)
                {
                    result.Add(item is XPathNavigator n ? n.Clone() : item);
                }
            }
            return result;
        }

        // ------------------------------------------------------------ xsl:result-document

        /// <summary>
        /// Writes a secondary result (XSLT 3.0 §25.1). An empty href means the principal result: the document's
        /// children are returned to become part of it.
        /// </summary>
        public IList<XPathItem> X31ResultDocument(string href, IList<XPathItem> document, string baseUri, double formatId, string formatName,
            double formatsId, string parameters, double namesId)
        {
            bool raw = parameters.Contains("#raw" + SerializationParameters.NameEnd, System.StringComparison.Ordinal);
            var doc = raw ? null! : (XPathNavigator)document[0];
            XsltNameContext names = (XsltNameContext)ConstantPool.Get((int)namesId);
            var format = (ResultFormat)ConstantPool.Get((int)formatId);
            if (formatName.Length > 0)
            {
                var (ns, local) = names.Resolve(formatName, "", "XTDE1460");
                var formats = (Dictionary<(string, string), ResultFormat>)ConstantPool.Get((int)formatsId);
                if (!formats.TryGetValue((ns, local), out format!))
                {
                    throw new XdmException("XTDE1460", $"There is no output definition named '{formatName}'");
                }
            }
            var (settings, extras, text) = SerializationParameters.Apply(format, parameters, names);
            if (raw && href.Length == 0)
            {
                // The principal result: the items, separated by the item-separator (or, without one, a space
                // between adjacent atomic values); json and adaptive apply to secondary results only.
                PrincipalResult? target = XmlQueryRuntime.Principal;
                if (target == null)
                {
                    return document;
                }
                if (extras?.RawMethod != null && target.WriteItems(document, settings, extras))
                {
                    return Sequences.Empty;
                }
                target.BeginExplicit(settings, extras);
                bool previousAtomic = false;
                for (int i = 0; i < document.Count; i++)
                {
                    bool atomic = document[i] is not XPathNavigator;
                    if (i > 0 && (extras?.ItemSeparator != null || atomic && previousAtomic))
                    {
                        target.WriteString(extras?.ItemSeparator ?? " ");
                    }
                    if (document[i] is XPathNavigator { NodeType: XPathNodeType.Root } root)
                    {
                        WriteChildren(target, root);
                    }
                    else if (document[i] is XPathNavigator node)
                    {
                        target.WriteNode(node, defattr: true);
                    }
                    else
                    {
                        target.WriteString(document[i].Value);
                    }
                    previousAtomic = atomic;
                }
                target.EndExplicit();
                return Sequences.Empty;
            }
            if (href.Length == 0)
            {
                // The principal result.
                PrincipalResult? principal = XmlQueryRuntime.Principal;
                if (principal == null)
                {
                    return X31ContentItems(document);
                }
                principal.BeginExplicit(settings, extras);
                WriteChildren(principal, doc);
                principal.EndExplicit();
                return Sequences.Empty;
            }
            string uri = X31OutputUri(href, baseUri);
            if (!_runtime.ResultDocumentUris.Add(uri))
            {
                throw new XdmException("XTDE1490", $"Two final result trees can't have the same URI: {uri}");
            }
            System.Func<string, string, System.IO.TextWriter>? handler = XmlQueryRuntime.ResultDocumentHandler;
            if (handler == null)
            {
                throw new XdmException("XTDE1480", $"Secondary results are not enabled; can't write {uri}");
            }
            System.IO.TextWriter output = handler(uri, extras?.RawMethod is "json" or "adaptive" ? extras.RawMethod : text ? "text" : settings.OutputMethod.ToString().ToLowerInvariant());
            if (raw)
            {
                if (extras?.RawMethod is "json" or "adaptive")
                {
                    ItemSerializer.Write(document, extras, output);
                }
                else
                {
                    System.Xml.XmlWriterSettings s = settings.Clone();
                    s.CloseOutput = false;
                    ItemSerializer.WriteSequence(document, s, extras ?? new OutputExtras(), ws => System.Xml.XmlWriter.Create(output, ws));
                }
                output.Dispose();
                return Sequences.Empty;
            }
            if (text)
            {
                output.Write(doc.Value);
                output.Dispose();
                return Sequences.Empty;
            }
            settings.CloseOutput = true;
            using (System.Xml.XmlWriter w = PrincipalResult.Create(s => System.Xml.XmlWriter.Create(output, s), settings, extras))
            {
                WriteChildren(w, doc);
                w.Flush();
            }
            return Sequences.Empty;
        }

        private static void WriteChildren(System.Xml.XmlWriter w, XPathNavigator doc)
        {
            XPathNavigator child = doc.Clone();
            if (child.MoveToFirstChild())
            {
                do
                {
                    w.WriteNode(child, defattr: true);
                }
                while (child.MoveToNext());
            }
        }

        // ------------------------------------------------------------ xsl:number

        public XPathItem X31IntegerItem(double value) => AtomicValue.OfInteger(new System.Numerics.BigInteger(value));

        /// <summary>
        /// xsl:number's formatting (XSLT 3.0 §12.4): the format string is split into format tokens and the
        /// separators between them; the nth number uses the nth token (the last one again when there are fewer
        /// tokens) and the separator before it. Each number is formatted as format-integer formats it.
        /// </summary>
        public string X31NumberFormat(IList<XPathItem> values, string format, string groupingSeparator, string groupingSize, string ordinal, string lang, string startAt)
        {
            if (lang.Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(lang, "^[a-zA-Z]{1,8}(-[a-zA-Z0-9]{1,8})*$"))
            {
                throw new XdmException("XTDE0030", $"xsl:number: lang=\"{lang}\" is not a language code");
            }
            var numbers = new List<System.Numerics.BigInteger>();
            foreach (XPathItem item in Sequences.Atomize(values))
            {
                var a = (AtomicValue)item;
                double d = a.Type.IsNumeric ? Numeric.ToDouble(a) : ToNumber(a);
                if (double.IsNaN(d) || double.IsInfinity(d) || d < 0)
                {
                    // XSLT 2.0: a value that isn't a non-negative integer is a dynamic error.
                    throw new XdmException("XTDE0980", $"xsl:number can't format {a.Value}: the value must be a non-negative integer");
                }
                numbers.Add(new System.Numerics.BigInteger(System.Math.Floor(d + 0.5))); // rounded as fn:round rounds
            }

            if (startAt.Length > 0)
            {
                // start-at (XSLT 3.0 §12.1): the numbers are re-based, the last integer serving for the rest.
                if (!System.Text.RegularExpressions.Regex.IsMatch(startAt, @"^\s*-?[0-9]+(\s+-?[0-9]+)*\s*$"))
                {
                    throw new XdmException("XTDE0030", $"xsl:number: start-at=\"{startAt}\" is not a list of integers");
                }
                string[] starts = startAt.Split((char[]?)null, System.StringSplitOptions.RemoveEmptyEntries);
                for (int s = 0; s < numbers.Count; s++)
                {
                    numbers[s] += System.Numerics.BigInteger.Parse(starts[System.Math.Min(s, starts.Length - 1)], System.Globalization.CultureInfo.InvariantCulture) - 1;
                }
            }

            // Split the format into prefix, tokens and separators.
            var tokens = new List<string>();
            var separators = new List<string>();
            int i = 0;
            string prefix = ReadNonAlphanumeric(format, ref i);
            while (i < format.Length)
            {
                tokens.Add(ReadAlphanumeric(format, ref i));
                string sep = ReadNonAlphanumeric(format, ref i);
                separators.Add(sep);
            }
            string suffix = "";
            if (tokens.Count > 0 && separators.Count == tokens.Count)
            {
                suffix = separators[^1];
                separators.RemoveAt(separators.Count - 1);
            }
            if (tokens.Count == 0)
            {
                // No format token: "1", with the format as both prefix and suffix.
                tokens.Add("1");
                suffix = prefix;
            }

            var sb = new StringBuilder(prefix);
            for (int n = 0; n < numbers.Count; n++)
            {
                if (n > 0)
                {
                    sb.Append(separators.Count == 0 ? "." : separators[System.Math.Min(n - 1, separators.Count - 1)]);
                }
                string token = tokens[System.Math.Min(n, tokens.Count - 1)];
                sb.Append(FormatNumberToken(numbers[n], token, groupingSeparator, groupingSize, ordinal, lang));
            }
            sb.Append(suffix);
            return sb.ToString();
        }

        private static string FormatNumberToken(System.Numerics.BigInteger n, string token, string groupingSeparator, string groupingSize, string ordinal, string lang)
        {
            string picture = token;
            if (groupingSeparator.Length > 0 && int.TryParse(groupingSize, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int size) && size > 0)
            {
                // A decimal token gets the grouping: build a picture with separators every `size` digits.
                IntegerFormatter plain;
                try
                {
                    plain = new IntegerFormatter(token, "XTDE0030");
                }
                catch (XdmException)
                {
                    plain = new IntegerFormatter("1");
                }
                if (plain.IsDecimal)
                {
                    string digits = plain.Format(n);
                    var grouped = new StringBuilder();
                    for (int i = 0; i < digits.Length; i++)
                    {
                        if (i > 0 && (digits.Length - i) % size == 0) grouped.Append(groupingSeparator);
                        grouped.Append(digits[i]);
                    }
                    return grouped.ToString();
                }
            }
            string modifier = ordinal.Length > 0 && ordinal != "no" ? ";o" : "";
            try
            {
                return new IntegerFormatter(picture + modifier, "XTDE0030", allowEmpty: true).Format(n, lang.Length > 0 ? lang : null);
            }
            catch (XdmException)
            {
                // An unrecognised token formats as "1" does.
                return new IntegerFormatter("1" + modifier).Format(n);
            }
        }

        private static string ReadAlphanumeric(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && (char.IsLetterOrDigit(s, i) || char.GetUnicodeCategory(s, i) is System.Globalization.UnicodeCategory.OtherNumber or System.Globalization.UnicodeCategory.LetterNumber))
            {
                i += char.IsSurrogatePair(s, i) ? 2 : 1;
            }
            return s[start..i];
        }

        private static string ReadNonAlphanumeric(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && !(char.IsLetterOrDigit(s, i) || char.GetUnicodeCategory(s, i) is System.Globalization.UnicodeCategory.OtherNumber or System.Globalization.UnicodeCategory.LetterNumber))
            {
                i += char.IsSurrogatePair(s, i) ? 2 : 1;
            }
            return s[start..i];
        }

        // ------------------------------------------------------------ tunnel parameters

        /// <summary>The tunnel parameters of a call: an immutable list of name/value frames.</summary>
        internal sealed class TunnelFrame : AtomicValue
        {
            public TunnelFrame(TunnelFrame? outer, string ns, string local, IList<XPathItem> value) : base(AtomicTypes.String)
            {
                Outer = outer;
                Ns = ns;
                Local = local;
                ParamValue = value;
            }

            public TunnelFrame? Outer { get; }

            public string Ns { get; }

            public string Local { get; }

            public IList<XPathItem> ParamValue { get; }

            public override string Value => "";

            public override object ClrValue => this;

            public TunnelFrame? Find(string ns, string local)
            {
                for (TunnelFrame? f = this; f != null; f = f.Outer)
                {
                    if (f.Local == local && f.Ns == ns) return f;
                }
                return null;
            }
        }

        // The value is copied: QIL may pass it in a list it reuses (navigators are cloned for the same reason).
        public IList<XPathItem> X31TunnelAdd(IList<XPathItem> frame, string ns, string local, IList<XPathItem> value) =>
            new XPathItem[] { new TunnelFrame(frame.Count == 0 ? null : (TunnelFrame)frame[0], ns, local, CopyItems(value)) };

        private static XPathItem[] CopyItems(IList<XPathItem> items)
        {
            var copy = new XPathItem[items.Count];
            for (int i = 0; i < copy.Length; i++)
            {
                copy[i] = items[i] is XPathNavigator n ? n.Clone() : items[i];
            }
            return copy;
        }

        public bool X31TunnelHas(IList<XPathItem> frame, string ns, string local) =>
            frame.Count != 0 && ((TunnelFrame)frame[0]).Find(ns, local) != null;

        /// <summary>
        /// A result document's URI: its href resolved against the base output URI (or, without one, the static base
        /// URI); the base output URI itself for the principal result (href="").
        /// </summary>
        public string X31OutputUri(string href, string staticBaseUri)
        {
            string? baseUri = XmlQueryRuntime.BaseOutputUri ?? (staticBaseUri.Length == 0 ? null : staticBaseUri);
            if (href.Length == 0)
            {
                return XmlQueryRuntime.BaseOutputUri ?? "";
            }
            return UriReference.IsAbsolute(href) || baseUri == null || !UriReference.IsAbsolute(baseUri) ? href : UriReference.Resolve(href, baseUri);
        }

        // The current template rule travels with the tunnel parameters too, for xsl:next-match in a named template.
        private const string RuleEntry = "\u0001xsl3-rule";

        /// <summary>The tunnel frame with template rule <paramref name="rule"/> as the current template rule.</summary>
        public IList<XPathItem> X31TunnelSetRule(IList<XPathItem> frame, double rule) =>
            new XPathItem[] { new TunnelFrame(frame.Count == 0 ? null : (TunnelFrame)frame[0], RuleEntry, "", Sequences.One(AtomicValue.OfDouble(rule))) };

        /// <summary>The current template rule a tunnel frame carries, or -1 (absent).</summary>
        public double X31TunnelRule(IList<XPathItem> frame) =>
            frame.Count != 0 && ((TunnelFrame)frame[0]).Find(RuleEntry, "") is { } entry ? Numeric.ToDouble((AtomicValue)entry.ParamValue[0]) : -1;

        // The current output URI travels with the tunnel parameters, set by xsl:result-document.
        private const string OutputEntry = "\u0001xsl3-output";

        /// <summary>The tunnel frame with <paramref name="uri"/> as the current output URI.</summary>
        public IList<XPathItem> X31TunnelSetOutput(IList<XPathItem> frame, string uri) =>
            new XPathItem[] { new TunnelFrame(frame.Count == 0 ? null : (TunnelFrame)frame[0], OutputEntry, "", Sequences.One(AtomicValue.OfString(uri))) };

        /// <summary>current-output-uri() (XSLT 3.0 §20.5.2): the result document's URI, else the base output URI.</summary>
        public IList<XPathItem> X31CurrentOutputUri(IList<XPathItem> frame)
        {
            string? uri = frame.Count != 0 && ((TunnelFrame)frame[0]).Find(OutputEntry, "") is { } entry ? entry.ParamValue[0].Value : XmlQueryRuntime.BaseOutputUri;
            return string.IsNullOrEmpty(uri) ? Sequences.Empty : Sequences.One(new StringValue(AtomicTypes.AnyUri, uri));
        }

        public IList<XPathItem> X31TunnelGet(IList<XPathItem> frame, string ns, string local) =>
            frame.Count == 0 ? Sequences.Empty : ((TunnelFrame)frame[0]).Find(ns, local)?.ParamValue ?? Sequences.Empty;

        // The current mode travels with the tunnel parameters, as an entry of this namespace whose local name is the
        // mode, for mode="#current" where the template's mode isn't known when it's compiled (XSLT 3.0 §6.6).
        private const string ModeEntry = "\u0001xsl3-mode";

        /// <summary>The tunnel frame with <paramref name="mode"/> as the current mode.</summary>
        public IList<XPathItem> X31TunnelSetMode(IList<XPathItem> frame, string mode)
        {
            var head = frame.Count == 0 ? null : (TunnelFrame)frame[0];
            if (head?.Ns == ModeEntry)
            {
                return head.Local == mode ? frame : new XPathItem[] { new TunnelFrame(head.Outer, ModeEntry, mode, Sequences.Empty) };
            }
            return new XPathItem[] { new TunnelFrame(head, ModeEntry, mode, Sequences.Empty) };
        }

        /// <summary>The current mode a tunnel frame carries, or null.</summary>
        public string X31TunnelMode(IList<XPathItem> frame)
        {
            for (var f = frame.Count == 0 ? null : (TunnelFrame)frame[0]; f != null; f = f.Outer)
            {
                if (f.Ns == ModeEntry) return f.Local;
            }
            return "\u0002";
        }
    }
}
