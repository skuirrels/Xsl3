// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Xml.XPath;
using Xsl3.Xdm;

namespace Xsl3.XPath31.Functions
{
    // The core of the F&O 3.1 function library. Signatures are written exactly as in the specification; the
    // function conversion rules (Coercion) have already been applied to the arguments when a body runs.
    internal static partial class FunctionLibrary
    {
        private static readonly IList<XPathItem> Empty = Sequences.Empty;

        private static IList<XPathItem> One(XPathItem item) => new[] { item };

        private static IList<XPathItem> Bool(bool b) => One(AtomicValue.OfBoolean(b));

        private static IList<XPathItem> Str(string s) => One(AtomicValue.OfString(s));

        private static IList<XPathItem> Int(BigInteger i) => One(AtomicValue.OfInteger(i));

        private static AtomicValue? Opt(IList<XPathItem> arg) => arg.Count == 0 ? null : (AtomicValue)arg[0];

        /// <summary>An xs:string? argument as a string, the empty sequence as "".</summary>
        private static string S(IList<XPathItem> arg) => arg.Count == 0 ? "" : ((AtomicValue)arg[0]).Value;

        private static double D(IList<XPathItem> arg) => Numeric.ToDouble((AtomicValue)arg[0]);

        private static XPathNavigator? Node(IList<XPathItem> arg) => arg.Count == 0 ? null : (XPathNavigator)arg[0];

        private static void RegisterCore()
        {
            // ---------------------------------------------------------- booleans (F&O §7)
            Define("fn:true() as xs:boolean", (c, a) => Bool(true));
            Define("fn:false() as xs:boolean", (c, a) => Bool(false));
            Define("fn:not($arg as item()*) as xs:boolean", (c, a) => Bool(!Sequences.EffectiveBooleanValue(a[0])));
            Define("fn:boolean($arg as item()*) as xs:boolean", (c, a) => Bool(Sequences.EffectiveBooleanValue(a[0])));

            // ---------------------------------------------------------- accessors (F&O §2)
            Define("fn:string($arg as item()?) as xs:string", (c, a) => Str(a[0].Count == 0 ? "" : Sequences.StringValue(a[0][0])));
            Define("fn:data($arg as item()*) as xs:anyAtomicType*", (c, a) => Sequences.Atomize(a[0]));
            Define("fn:node-name($arg as node()?) as xs:QName?", (c, a) => Node(a[0]) is XPathNavigator n && NodeName(n) is QName q ? One(new QNameValue(AtomicTypes.QName, q)) : Empty);
            Define("fn:name($arg as node()?) as xs:string", (c, a) => Str(Node(a[0]) is XPathNavigator n && NodeName(n) is QName q ? q.ToString() : ""));
            Define("fn:local-name($arg as node()?) as xs:string", (c, a) => Str(Node(a[0]) is XPathNavigator n && NodeName(n) is QName q ? q.LocalName : ""));
            Define("fn:namespace-uri($arg as node()?) as xs:anyURI", (c, a) => One(new StringValue(AtomicTypes.AnyUri, Node(a[0]) is XPathNavigator n && n.NodeType is XPathNodeType.Element or XPathNodeType.Attribute ? n.NamespaceURI : "")));
            Define("fn:root($arg as node()?) as node()?", (c, a) =>
            {
                if (Node(a[0]) is not XPathNavigator n) return Empty;
                XPathNavigator root = n.Clone();
                while (root.MoveToParent()) { }
                return One(root);
            });
            Define("fn:has-children($node as node()?) as xs:boolean", (c, a) => Bool(Node(a[0]) is XPathNavigator n && n.HasChildren));

            // ---------------------------------------------------------- numerics (F&O §4)
            Define("fn:number($arg as xs:anyAtomicType?) as xs:double", (c, a) =>
                One(Opt(a[0]) is AtomicValue v && Casting.IsCastable(v, AtomicTypes.Double) ? Casting.Cast(v, AtomicTypes.Double) : AtomicValue.OfDouble(double.NaN)));
            Define("fn:abs($arg as xs:numeric?) as xs:numeric?", (c, a) => Opt(a[0]) is AtomicValue v ? One(NumericFunctions.Abs(v)) : Empty);
            Define("fn:ceiling($arg as xs:numeric?) as xs:numeric?", (c, a) => Opt(a[0]) is AtomicValue v ? One(NumericFunctions.Ceiling(v)) : Empty);
            Define("fn:floor($arg as xs:numeric?) as xs:numeric?", (c, a) => Opt(a[0]) is AtomicValue v ? One(NumericFunctions.Floor(v)) : Empty);
            Define("fn:round($arg as xs:numeric?) as xs:numeric?", (c, a) => Opt(a[0]) is AtomicValue v ? One(NumericFunctions.Round(v, 0, halfToEven: false)) : Empty);
            Define("fn:round($arg as xs:numeric?, $precision as xs:integer) as xs:numeric?", (c, a) => Opt(a[0]) is AtomicValue v ? One(NumericFunctions.Round(v, Precision(a[1]), halfToEven: false)) : Empty);
            Define("fn:round-half-to-even($arg as xs:numeric?) as xs:numeric?", (c, a) => Opt(a[0]) is AtomicValue v ? One(NumericFunctions.Round(v, 0, halfToEven: true)) : Empty);
            Define("fn:round-half-to-even($arg as xs:numeric?, $precision as xs:integer) as xs:numeric?", (c, a) => Opt(a[0]) is AtomicValue v ? One(NumericFunctions.Round(v, Precision(a[1]), halfToEven: true)) : Empty);

            // ---------------------------------------------------------- strings (F&O §5)
            Define("fn:concat($arg1 as xs:anyAtomicType?, $arg2 as xs:anyAtomicType?, ...) as xs:string", (c, a) =>
            {
                var sb = new StringBuilder();
                foreach (IList<XPathItem> arg in a)
                {
                    sb.Append(S(arg));
                }
                return Str(sb.ToString());
            });
            Define("fn:string-join($arg1 as xs:anyAtomicType*) as xs:string", (c, a) => Str(string.Concat(a[0].Select(i => ((AtomicValue)i).Value))));
            Define("fn:string-join($arg1 as xs:anyAtomicType*, $arg2 as xs:string) as xs:string", (c, a) => Str(string.Join(S(a[1]), a[0].Select(i => ((AtomicValue)i).Value))));
            Define("fn:string-length($arg as xs:string?) as xs:integer", (c, a) => Int(CodePoints.Length(S(a[0]))));
            Define("fn:normalize-space($arg as xs:string?) as xs:string", (c, a) => Str(Lexical.Collapse(S(a[0]))));
            Define("fn:upper-case($arg as xs:string?) as xs:string", (c, a) => Str(CaseMapping.ToUpper(S(a[0]))));
            Define("fn:lower-case($arg as xs:string?) as xs:string", (c, a) => Str(CaseMapping.ToLower(S(a[0]))));
            Define("fn:substring($sourceString as xs:string?, $start as xs:double) as xs:string", (c, a) => Str(CodePoints.Substring(S(a[0]), D(a[1]), double.PositiveInfinity)));
            Define("fn:substring($sourceString as xs:string?, $start as xs:double, $length as xs:double) as xs:string", (c, a) => Str(CodePoints.Substring(S(a[0]), D(a[1]), D(a[2]))));
            Define("fn:translate($arg as xs:string?, $mapString as xs:string, $transString as xs:string) as xs:string", (c, a) => Str(CodePoints.Translate(S(a[0]), S(a[1]), S(a[2]))));
            Define("fn:contains($arg1 as xs:string?, $arg2 as xs:string?) as xs:boolean", (c, a) => Bool(StringMatch.Contains(S(a[0]), S(a[1]), c.DefaultCollation)));
            Define("fn:starts-with($arg1 as xs:string?, $arg2 as xs:string?) as xs:boolean", (c, a) => Bool(StringMatch.StartsWith(S(a[0]), S(a[1]), c.DefaultCollation)));
            Define("fn:ends-with($arg1 as xs:string?, $arg2 as xs:string?) as xs:boolean", (c, a) => Bool(StringMatch.EndsWith(S(a[0]), S(a[1]), c.DefaultCollation)));
            Define("fn:substring-before($arg1 as xs:string?, $arg2 as xs:string?) as xs:string", (c, a) => Str(StringMatch.Before(S(a[0]), S(a[1]), c.DefaultCollation)));
            Define("fn:substring-after($arg1 as xs:string?, $arg2 as xs:string?) as xs:string", (c, a) => Str(StringMatch.After(S(a[0]), S(a[1]), c.DefaultCollation)));
            Define("fn:string-to-codepoints($arg as xs:string?) as xs:integer*", (c, a) => CodePoints.Of(S(a[0])).Select(cp => (XPathItem)AtomicValue.OfInteger(cp)).ToList());
            Define("fn:codepoints-to-string($arg as xs:integer*) as xs:string", (c, a) => Str(CodePoints.ToStringChecked(a[0].Select(i => ((IntegerValue)i).Integer))));
            Define("fn:compare($comparand1 as xs:string?, $comparand2 as xs:string?) as xs:integer?", (c, a) =>
                a[0].Count == 0 || a[1].Count == 0 ? Empty : Int(System.Math.Sign(c.DefaultCollation.Compare(S(a[0]), S(a[1])))));
            Define("fn:codepoint-equal($comparand1 as xs:string?, $comparand2 as xs:string?) as xs:boolean?", (c, a) =>
                a[0].Count == 0 || a[1].Count == 0 ? Empty : Bool(S(a[0]) == S(a[1])));
            Define("fn:normalize-unicode($arg as xs:string?) as xs:string", (c, a) => Str(S(a[0]).Normalize(NormalizationForm.FormC)));
            Define("fn:normalize-unicode($arg as xs:string?, $normalizationForm as xs:string) as xs:string", (c, a) => Str(CodePoints.Normalize(S(a[0]), S(a[1]))));

            // ---------------------------------------------------------- sequences (F&O §14)
            Define("fn:empty($arg as item()*) as xs:boolean", (c, a) => Bool(a[0].Count == 0));
            Define("fn:exists($arg as item()*) as xs:boolean", (c, a) => Bool(a[0].Count != 0));
            Define("fn:count($arg as item()*) as xs:integer", (c, a) => Int(a[0].Count));
            Define("fn:head($arg as item()*) as item()?", (c, a) => a[0].Count == 0 ? Empty : One(a[0][0]));
            Define("fn:tail($arg as item()*) as item()*", (c, a) => a[0].Skip(1).ToList());
            Define("fn:reverse($arg as item()*) as item()*", (c, a) => a[0].Reverse().ToList());
            Define("fn:unordered($sourceSeq as item()*) as item()*", (c, a) => a[0]);
            Define("fn:subsequence($sourceSeq as item()*, $startingLoc as xs:double) as item()*", (c, a) => SequenceFunctions.Subsequence(a[0], D(a[1]), null));
            Define("fn:subsequence($sourceSeq as item()*, $startingLoc as xs:double, $length as xs:double) as item()*", (c, a) => SequenceFunctions.Subsequence(a[0], D(a[1]), D(a[2])));
            Define("fn:insert-before($target as item()*, $position as xs:integer, $inserts as item()*) as item()*", (c, a) =>
            {
                BigInteger p = ((IntegerValue)a[1][0]).Integer;
                int at = p < 1 ? 0 : p > a[0].Count ? a[0].Count : (int)p - 1;
                var result = new List<XPathItem>(a[0]);
                result.InsertRange(at, a[2]);
                return result;
            });
            Define("fn:remove($target as item()*, $position as xs:integer) as item()*", (c, a) =>
            {
                BigInteger p = ((IntegerValue)a[1][0]).Integer;
                if (p < 1 || p > a[0].Count) return a[0];
                var result = new List<XPathItem>(a[0]);
                result.RemoveAt((int)p - 1);
                return result;
            });
            Define("fn:index-of($seq as xs:anyAtomicType*, $search as xs:anyAtomicType) as xs:integer*", (c, a) =>
            {
                var search = (AtomicValue)a[1][0];
                var result = new List<XPathItem>();
                for (int i = 0; i < a[0].Count; i++)
                {
                    if (SequenceFunctions.EqualForIndexOf((AtomicValue)a[0][i], search, c))
                    {
                        result.Add(AtomicValue.OfInteger(i + 1));
                    }
                }
                return result;
            });
            Define("fn:distinct-values($arg as xs:anyAtomicType*) as xs:anyAtomicType*", (c, a) => SequenceFunctions.DistinctValues(a[0], c));
            Define("fn:deep-equal($parameter1 as item()*, $parameter2 as item()*) as xs:boolean", (c, a) => Bool(DeepEqual.Sequences(a[0], a[1], c)));
            Define("fn:zero-or-one($arg as item()*) as item()?", (c, a) => a[0].Count <= 1 ? a[0] : throw new XdmException("FORG0003", "fn:zero-or-one called with a sequence of more than one item"));
            Define("fn:one-or-more($arg as item()*) as item()+", (c, a) => a[0].Count >= 1 ? a[0] : throw new XdmException("FORG0004", "fn:one-or-more called with an empty sequence"));
            Define("fn:exactly-one($arg as item()*) as item()", (c, a) => a[0].Count == 1 ? a[0] : throw new XdmException("FORG0005", "fn:exactly-one called with a sequence of zero or more than one items"));

            // ---------------------------------------------------------- aggregates (F&O §14.4)
            Define("fn:sum($arg as xs:anyAtomicType*) as xs:anyAtomicType", (c, a) => One(Aggregates.Sum(a[0], AtomicValue.OfInteger(0), c)));
            Define("fn:sum($arg as xs:anyAtomicType*, $zero as xs:anyAtomicType?) as xs:anyAtomicType?", (c, a) => a[0].Count == 0 ? a[1] : One(Aggregates.Sum(a[0], null, c)));
            Define("fn:avg($arg as xs:anyAtomicType*) as xs:anyAtomicType?", (c, a) => Aggregates.Avg(a[0], c));
            Define("fn:min($arg as xs:anyAtomicType*) as xs:anyAtomicType?", (c, a) => Aggregates.MinMax(a[0], c, max: false));
            Define("fn:max($arg as xs:anyAtomicType*) as xs:anyAtomicType?", (c, a) => Aggregates.MinMax(a[0], c, max: true));

            // ---------------------------------------------------------- errors and diagnostics (F&O §3)
            Define("fn:error() as item()*", (c, a) => throw new XdmException("FOER0000", "fn:error() called"));
            Define("fn:error($code as xs:QName?) as item()*", (c, a) => throw ErrorFrom(a[0], null));
            Define("fn:error($code as xs:QName?, $description as xs:string) as item()*", (c, a) => throw ErrorFrom(a[0], S(a[1])));
            Define("fn:error($code as xs:QName?, $description as xs:string, $error-object as item()*) as item()*", (c, a) => throw ErrorFrom(a[0], S(a[1])));
            Define("fn:trace($value as item()*) as item()*", (c, a) => a[0]);
            Define("fn:trace($value as item()*, $label as xs:string) as item()*", (c, a) => a[0]);

            // ---------------------------------------------------------- QNames (F&O §10)
            Define("fn:QName($paramURI as xs:string?, $paramQName as xs:string) as xs:QName", (c, a) => One(QNameFunctions.Construct(S(a[0]), S(a[1]))));
            Define("fn:local-name-from-QName($arg as xs:QName?) as xs:NCName?", (c, a) => Opt(a[0]) is QNameValue q ? One(new StringValue(AtomicTypes.NCName, q.QName.LocalName)) : Empty);
            Define("fn:namespace-uri-from-QName($arg as xs:QName?) as xs:anyURI?", (c, a) => Opt(a[0]) is QNameValue q ? One(new StringValue(AtomicTypes.AnyUri, q.QName.NamespaceUri)) : Empty);
            Define("fn:prefix-from-QName($arg as xs:QName?) as xs:NCName?", (c, a) => Opt(a[0]) is QNameValue q && q.QName.Prefix.Length > 0 ? One(new StringValue(AtomicTypes.NCName, q.QName.Prefix)) : Empty);

            RegisterDateTime();
            RegisterRegex();
            RegisterMisc();
            RegisterNodes();
            RegisterFormatting();
            Define("fn:parse-ietf-date($value as xs:string?) as xs:dateTime?", (c, a) => a[0].Count == 0 ? Empty : One(IetfDate.Parse(S(a[0]))));
        }

        private static long Precision(IList<XPathItem> arg)
        {
            BigInteger p = ((IntegerValue)arg[0]).Integer;
            return p > 10000 ? 10000 : p < -10000 ? -10000 : (long)p;
        }

        private static QName? NodeName(XPathNavigator n) => n.NodeType switch
        {
            XPathNodeType.Element or XPathNodeType.Attribute => new QName(n.NamespaceURI, n.LocalName, n.Prefix),
            XPathNodeType.ProcessingInstruction or XPathNodeType.Namespace when n.LocalName.Length > 0 => new QName("", n.LocalName),
            _ => null,
        };

        private static XdmException ErrorFrom(IList<XPathItem> code, string? description)
        {
            if (Opt(code) is QNameValue q)
            {
                return new XdmException(q.QName.LocalName, description ?? $"Error {q.QName}");
            }
            return new XdmException("FOER0000", description ?? "fn:error called");
        }
    }
}
