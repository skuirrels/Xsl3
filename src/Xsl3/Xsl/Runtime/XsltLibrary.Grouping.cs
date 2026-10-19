// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.XPath;
using Xsl3.XPath31.Functions;
using Xsl3.Xdm;

namespace Xsl3.Runtime
{
    /// <summary>
    /// Xsl3: run-time support for XSLT 2.0 sorting (xsl:sort, xsl:perform-sort), grouping (xsl:for-each-group)
    /// and xsl:analyze-string. Groups, sort tuples and regex segments travel through QIL as opaque items.
    /// </summary>
    public sealed partial class XsltLibrary
    {
        // ------------------------------------------------------------ sorting

        /// <summary>
        /// An xsl:sort key as a string whose ordinal order is the key's order (XSLT 3.0 §13.1.2): empty sequences
        /// first, then NaN, then numbers, strings under the collation, and so on; values of different kinds sort
        /// by kind. <paramref name="dataType"/> is "text", "number" or empty for the typed values.
        /// </summary>
        // Per sort instruction: the kind of the keys seen so far in the current sort (XTDE1030).
        private readonly Dictionary<int, string> _sortKeyKinds = new();

        public string X31SortKey(IList<XPathItem> items, string dataType, string collation, string lang, double site, double position)
        {
            IList<XPathItem> atoms = Sequences.Atomize(items);
            if (position == 1)
            {
                _sortKeyKinds.Remove((int)site);
            }
            if (atoms.Count == 0)
            {
                return "0";
            }
            if (atoms.Count > 1)
            {
                throw new XdmException("XTTE1020", "A sort key must be a single atomic value, not a sequence of " + atoms.Count + " items");
            }
            var v = (AtomicValue)atoms[0];
            if (dataType.Length == 0)
            {
                // Without data-type, the keys are compared as their types are: they must be comparable.
                if (v.Type.Primitive is Primitive.Duration or Primitive.GYear or Primitive.GYearMonth or Primitive.GMonth or Primitive.GMonthDay
                    or Primitive.GDay or Primitive.QName or Primitive.Notation)
                {
                    throw new XdmException("XTDE1030", $"Values of type {v.Type.Name} have no ordering for sorting");
                }
                string kind = v.Type.IsNumeric ? "numeric" : v.Type.Primitive is Primitive.String or Primitive.UntypedAtomic or Primitive.AnyUri ? "string"
                    : v.Type.Primitive.ToString();
                if (_sortKeyKinds.TryGetValue((int)site, out string? previous))
                {
                    if (previous != kind)
                    {
                        throw new XdmException("XTDE1030", $"Sort keys of types {previous} and {kind} can't be compared");
                    }
                }
                else
                {
                    _sortKeyKinds[(int)site] = kind;
                }
            }
            Collation coll = SortCollation(collation, lang);
            switch (dataType)
            {
                case "number":
                    return NumberKey(ToNumber(v));
                case "text":
                    return "3" + Hex(coll.KeyBytes(v.Value));
            }
            if (v.Type.IsNumeric)
            {
                return NumberKey(Numeric.ToDouble(v));
            }
            switch (v)
            {
                case DateTimeValue dt:
                    return "4" + DecimalKey(dt.DateTime.ToTimeline(dt.Type.Primitive, X31Context.ImplicitTimezoneMinutes));
                case DurationValue du:
                    return "6" + DecimalKey(du.Duration.Months * 2_678_400m + du.Duration.Seconds);
                case BooleanValue b:
                    return b.Value == "true" ? "51" : "50";
                default:
                    return "3" + Hex(coll.KeyBytes(v.Value));
            }
        }

        private readonly Dictionary<(string, string), Collation> _sortCollations = new();

        private Collation SortCollation(string collation, string lang)
        {
            if (collation.Length == 0 && lang.Length == 0)
            {
                return Collation.Codepoint;
            }
            if (!_sortCollations.TryGetValue((collation, lang), out Collation? c))
            {
                _sortCollations[(collation, lang)] = c = collation.Length > 0
                    ? Collation.Resolve(collation)
                    : Collation.Resolve(Collation.UcaPrefix + "?lang=" + lang + ";fallback=yes");
            }
            return c;
        }

        private static string NumberKey(double d)
        {
            if (double.IsNaN(d))
            {
                return "1";
            }
            if (d == 0)
            {
                d = 0; // -0 sorts as 0
            }
            long bits = System.BitConverter.DoubleToInt64Bits(d);
            ulong ordered = bits < 0 ? ~(ulong)bits : (ulong)bits ^ 0x8000000000000000UL;
            return "2" + ordered.ToString("X16", CultureInfo.InvariantCulture);
        }

        private static string DecimalKey(decimal d) => NumberKey((double)d).Substring(1);

        private static string Hex(byte[] bytes) => System.Convert.ToHexString(bytes);

        // fn:number: NaN for values that aren't numbers.
        private static double ToNumber(AtomicValue v)
        {
            if (v.Type.IsNumeric)
            {
                return Numeric.ToDouble(v);
            }
            try
            {
                return Numeric.ToDouble(Casting.Cast(v, AtomicTypes.Double));
            }
            catch (XdmException)
            {
                return double.NaN;
            }
        }

        // ------------------------------------------------------------ grouping

        /// <summary>An item of the population with its grouping key, before grouping.</summary>
        internal sealed class GroupTuple : AtomicValue
        {
            public GroupTuple(XPathItem item, IList<XPathItem> key) : base(AtomicTypes.String)
            {
                Item = item;
                Key = key;
            }

            public XPathItem Item { get; }

            public IList<XPathItem> Key { get; }

            public override string Value => Item.Value;

            public override object ClrValue => Item;
        }

        /// <summary>A group of xsl:for-each-group: its members in population order and its grouping key.</summary>
        internal sealed class Group : AtomicValue
        {
            public Group(IList<XPathItem>? key) : base(AtomicTypes.String)
            {
                Key = key;
            }

            public List<XPathItem> Members { get; } = new();

            public IList<XPathItem>? Key { get; }

            public override string Value => Members.Count > 0 ? Members[0].Value : "";

            public override object ClrValue => Members;
        }

        // QIL loops reuse one navigator per iterator, and pass single items to IList parameters in a reused list
        // (XmlQueryItemSequence.CreateOrReuse): anything kept must be copied.
        public XPathItem X31GroupTuple(XPathItem item, IList<XPathItem> key) =>
            new GroupTuple(item is XPathNavigator n ? n.Clone() : item, new List<XPathItem>(Sequences.Atomize(key)));

        /// <summary>
        /// Forms the groups (XSLT 3.0 §14.5): kind 0 group-by, 1 group-adjacent, 2 group-starting-with, 3
        /// group-ending-with (for the last two the key is whether the item matches the pattern).
        /// </summary>
        public IList<XPathItem> X31Groups(IList<XPathItem> tuples, double kind, string collation, bool composite)
        {
            var groups = new List<XPathItem>();
            FunctionContext c = collation.Length == 0 ? X31Context : X31Context.WithDefaultCollation(Collation.Resolve(collation));
            switch ((int)kind)
            {
                case 0:
                {
                    var buckets = new Dictionary<object, List<Group>>();
                    foreach (GroupTuple t in tuples)
                    {
                        IList<XPathItem> keys = Sequences.Atomize(t.Key);
                        if (composite)
                        {
                            AddToGroup(new List<XPathItem>(keys), t.Item);
                            continue;
                        }
                        var seen = new List<AtomicValue>();
                        foreach (AtomicValue k in keys)
                        {
                            // An item goes into each group of its distinct keys, once.
                            if (seen.Exists(s => SequenceFunctions.EqualLenient(s, k, c)))
                            {
                                continue;
                            }
                            seen.Add(k);
                            AddToGroup(new XPathItem[] { k }, t.Item);
                        }
                    }
                    void AddToGroup(IList<XPathItem> key, XPathItem item)
                    {
                        object hash = key.Count == 1 ? SequenceFunctions.HashKey((AtomicValue)key[0], c) : key.Count;
                        if (!buckets.TryGetValue(hash, out List<Group>? bucket))
                        {
                            buckets[hash] = bucket = new List<Group>();
                        }
                        Group? g = bucket.Find(b => KeysEqual(b.Key!, key, c));
                        if (g == null)
                        {
                            bucket.Add(g = new Group(key));
                            groups.Add(g);
                        }
                        g.Members.Add(item);
                    }
                    break;
                }
                case 1:
                {
                    Group? current = null;
                    foreach (GroupTuple t in tuples)
                    {
                        IList<XPathItem> key = Sequences.Atomize(t.Key);
                        if (!composite && key.Count != 1)
                        {
                            throw new XdmException("XTTE1100", "A group-adjacent key must be a single atomic value");
                        }
                        if (current == null || !KeysEqual(current.Key!, key, c))
                        {
                            groups.Add(current = new Group(key));
                        }
                        current.Members.Add(t.Item);
                    }
                    break;
                }
                default:
                {
                    bool starting = (int)kind == 2;
                    Group? current = null;
                    bool endPrevious = false;
                    foreach (GroupTuple t in tuples)
                    {
                        bool matches = Sequences.EffectiveBooleanValue(t.Key);
                        if (current == null || (starting && matches) || (!starting && endPrevious))
                        {
                            groups.Add(current = new Group(null));
                        }
                        current.Members.Add(t.Item);
                        endPrevious = matches;
                    }
                    break;
                }
            }
            return groups;
        }

        private static bool KeysEqual(IList<XPathItem> a, IList<XPathItem> b, FunctionContext c)
        {
            if (a.Count != b.Count)
            {
                return false;
            }
            for (int i = 0; i < a.Count; i++)
            {
                if (!SequenceFunctions.EqualLenient((AtomicValue)a[i], (AtomicValue)b[i], c))
                {
                    return false;
                }
            }
            return true;
        }

        public IList<XPathItem> X31GroupMembers(XPathItem group) => ((Group)group).Members;

        public IList<XPathItem> X31GroupKey(XPathItem group) => ((Group)group).Key ?? Sequences.Empty;

        public XPathItem X31GroupFirst(XPathItem group) => ((Group)group).Members[0];

        // ------------------------------------------------------------ xsl:analyze-string

        /// <summary>A matching or non-matching substring of xsl:analyze-string.</summary>
        internal sealed class Segment : AtomicValue
        {
            public Segment(string text, Match? match) : base(AtomicTypes.String)
            {
                Text = text;
                Match = match;
            }

            public string Text { get; }

            public Match? Match { get; }

            public override string Value => Text;

            public override object ClrValue => Text;
        }

        /// <summary>The input split into matching and non-matching substrings, in order (XSLT 3.0 §15.1).</summary>
        public IList<XPathItem> X31AnalyzeString(IList<XPathItem> input, string regex, string flags)
        {
            // select is an xs:string? (function conversion rules: untypedAtomic and anyURI become strings).
            AtomicValue? a = Sequences.AtomizeOptional(input, "xsl:analyze-string/@select");
            if (a != null && a.Type.Primitive is not (Primitive.String or Primitive.UntypedAtomic or Primitive.AnyUri))
            {
                throw new XdmException("XPTY0004", $"xsl:analyze-string requires a string, not {a.Type.Name}");
            }
            string s = a?.Value ?? "";
            XPathRegex re;
            try
            {
                re = XPathRegex.Get(regex, flags);
            }
            catch (XdmException e) when (e.Code is "FORX0001" or "FORX0002")
            {
                throw new XdmException(e.Code == "FORX0001" ? "XTDE1145" : "XTDE1140", e.Message);
            }
            if (re.Regex.IsMatch(""))
            {
                throw new XdmException("XTDE1150", $"The regex '{regex}' matches a zero-length string");
            }
            var segments = new List<XPathItem>();
            int pos = 0;
            for (Match m = re.Regex.Match(s); m.Success; m = m.NextMatch())
            {
                if (m.Index > pos)
                {
                    segments.Add(new Segment(s[pos..m.Index], null));
                }
                segments.Add(new Segment(m.Value, m));
                pos = m.Index + m.Length;
            }
            if (pos < s.Length)
            {
                segments.Add(new Segment(s[pos..], null));
            }
            return segments;
        }

        public bool X31SegmentIsMatch(XPathItem segment) => ((Segment)segment).Match != null;

        public XPathItem X31SegmentString(XPathItem segment) => AtomicValue.OfString(((Segment)segment).Text);

        /// <summary>regex-group(n) inside xsl:matching-substring: the nth captured group, "" if absent.</summary>
        public XPathItem X31SegmentGroup(XPathItem segment, IList<XPathItem> n)
        {
            Match? m = ((Segment)segment).Match;
            AtomicValue? number = Sequences.AtomizeOptional(n, "the argument of regex-group()");
            if (m == null || number == null)
            {
                return AtomicValue.OfString("");
            }
            double d = ToNumber(number);
            if (double.IsNaN(d) || d < 0 || d >= m.Groups.Count || d != System.Math.Floor(d))
            {
                return AtomicValue.OfString("");
            }
            System.Text.RegularExpressions.Group g = m.Groups[(int)d];
            return AtomicValue.OfString(g.Success ? g.Value : "");
        }
    }
}
