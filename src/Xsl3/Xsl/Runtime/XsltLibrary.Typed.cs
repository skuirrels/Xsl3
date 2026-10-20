// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using Xsl3.XPath31;
using Xsl3.XPath31.Functions;
using Xsl3.Xdm;

namespace Xsl3.Runtime
{
    /// <summary>
    /// Xsl3 (P4): string functions on native values, for calls whose arguments are certainly single strings or nodes
    /// (XPath31Compiler.TypedCall). Each is the library function's own helper, without the item lists around it.
    /// </summary>
    public sealed partial class XsltLibrary
    {
        /// <summary>Whether a dynamic call's function is a single map (so that $m(key) can look the key up directly).</summary>
        public bool X31IsMap(System.Collections.Generic.IList<System.Xml.XPath.XPathItem> function) => function.Count == 1 && function[0] is XdmMap;

        /// <summary>
        /// $m(key) for a map: the key converted as a map's parameter type, xs:anyAtomicType (exactly one atomic value,
        /// XPTY0004 otherwise), then looked up, as the dispatcher's call of a map does.
        /// </summary>
        public System.Collections.Generic.IList<System.Xml.XPath.XPathItem> X31MapLookup(System.Collections.Generic.IList<System.Xml.XPath.XPathItem> function, System.Collections.Generic.IList<System.Xml.XPath.XPathItem> key)
        {
            System.Collections.Generic.IList<System.Xml.XPath.XPathItem> atoms = Sequences.Atomize(key);
            if (atoms.Count != 1)
            {
                throw Err.XPTY0004($"Argument 1 of {((XdmMap)function[0]).Describe()} must be one atomic value, not {Coercion.Describe(atoms)}");
            }
            return ((XdmMap)function[0]).Get((AtomicValue)atoms[0]) ?? Sequences.Empty;
        }

        /// <summary>
        /// The xs:double an xs:untypedAtomic value casts to: the exact fast parse for short decimals, the cast otherwise
        /// (FORG0001 for a string that isn't a double).
        /// </summary>
        internal static double UntypedDouble(string value) =>
            XPathConvert.TryParseShortDecimal(value, out double d) ? d : ((DoubleValue)Casting.Cast(new StringValue(AtomicTypes.UntypedAtomic, value), AtomicTypes.Double)).Double;

        /// <summary>
        /// The string an argument of type xs:string? (or xs:anyAtomicType?) gives for nodes: "" for none, the string
        /// value of one, XPTY0004 for more.
        /// </summary>
        public string X31OptionalNodeString(System.Collections.Generic.IList<System.Xml.XPath.XPathItem> nodes) => nodes.Count switch
        {
            0 => "",
            1 => nodes[0].Value,
            _ => throw Err.XPTY0004($"An argument that must be at most one item is a sequence of {nodes.Count} nodes"),
        };

        /// <summary>
        /// xsl:namespace (XSLT 3.0 §11.7.1): the name of the namespace node, checked with its value. The name must be
        /// "" or an NCName other than xmlns (XTDE0920); xml goes with the XML namespace and only with it (XTDE0925);
        /// the value can't be the xmlns namespace (XTDE0905) or empty (XTDE0930).
        /// </summary>
        public string X31NamespaceName(string name, string value)
        {
            const string XmlNs = "http://www.w3.org/XML/1998/namespace", XmlnsNs = "http://www.w3.org/2000/xmlns/";
            if ((name.Length != 0 && !Casting.IsNCName(name)) || name == "xmlns")
            {
                throw new XdmException("XTDE0920", $"'{name}' is not a namespace node name");
            }
            if ((name == "xml") != (value == XmlNs))
            {
                throw new XdmException("XTDE0925", name == "xml" ? "The prefix xml can only be bound to the XML namespace" : $"The XML namespace can only be bound to the prefix xml, not '{name}'");
            }
            if (value == XmlnsNs)
            {
                throw new XdmException("XTDE0905", "A namespace node can't have the xmlns namespace as its value");
            }
            if (value.Length == 0)
            {
                throw new XdmException("XTDE0930", "A namespace node can't have an empty value");
            }
            return name;
        }

        /// <summary>fn:number of a string or an untyped value: the xs:double it casts to, NaN if it doesn't.</summary>
        public System.Xml.XPath.XPathItem X31NumberOfString(string value)
        {
            if (XPathConvert.TryParseShortDecimal(value, out double d))
            {
                return AtomicValue.OfDouble(d);
            }
            try
            {
                return Casting.Cast(new StringValue(AtomicTypes.UntypedAtomic, value), AtomicTypes.Double);
            }
            catch (XdmException)
            {
                return AtomicValue.OfDouble(double.NaN);
            }
        }

        /// <summary>The xs:double an untyped value (a node's) casts to (FORG0001 if it isn't one).</summary>
        public double X31UntypedDouble(string value) => UntypedDouble(value);

        /// <summary>
        /// fn:sum of nodes' values, as doubles: added in order from the first, as X31SumNodes does; none is xs:integer 0.
        /// </summary>
        public System.Xml.XPath.XPathItem X31SumDoubles(System.Collections.Generic.IList<double> values)
        {
            if (values.Count == 0)
            {
                return AtomicValue.OfInteger(0);
            }
            double sum = values[0];
            for (int i = 1; i < values.Count; i++)
            {
                sum += values[i];
            }
            return AtomicValue.OfDouble(sum);
        }

        /// <summary>
        /// fn:sum of nodes (untyped, so each an xs:untypedAtomic cast to xs:double): the doubles added in order, from
        /// the first; the empty sequence is xs:integer 0.
        /// </summary>
        public System.Xml.XPath.XPathItem X31SumNodes(System.Collections.Generic.IList<System.Xml.XPath.XPathItem> nodes)
        {
            if (nodes.Count == 0)
            {
                return AtomicValue.OfInteger(0);
            }
            double sum = UntypedDouble(nodes[0].Value);
            for (int i = 1; i < nodes.Count; i++)
            {
                sum += UntypedDouble(nodes[i].Value);
            }
            return AtomicValue.OfDouble(sum);
        }

        private static readonly SequenceTypeTest s_formatNumberValue =
            FunctionLibrary.Find(Xdm.QName.Ns.Fn, "format-number", 2)!.ParameterType(0);

        /// <summary>
        /// fn:format-number with the unnamed decimal format: the value converted as the function's $value (xs:numeric?;
        /// a double as it is), then formatted.
        /// </summary>
        public string X31FormatNumber(System.Collections.Generic.IList<System.Xml.XPath.XPathItem> value, string picture, double sc)
        {
            AtomicValue? v = null;
            if (value.Count == 1 && value[0] is DoubleValue d)
            {
                v = d;
            }
            else if (value.Count != 0)
            {
                System.Collections.Generic.IList<System.Xml.XPath.XPathItem> converted =
                    Coercion.CoerceTo(value, s_formatNumberValue, "XPTY0004", () => "Argument 1 of fn:format-number#2");
                v = converted.Count == 0 ? null : (AtomicValue)converted[0];
            }
            return Xsl3.XPath31.Functions.NumberFormatter.Format(v, picture, X31ContextFor(sc).DecimalFormats.Unnamed);
        }

        /// <summary>An item known to be an xs:string (or a subtype), as a native string.</summary>
        public string X31ItemString(System.Xml.XPath.XPathItem item) => item.Value;

        public string X31UpperCase(string s) => CaseMapping.ToUpper(s);

        /// <summary>fn:tokenize with a literal pattern (no regex syntax, no flags): the input split where it occurs.</summary>
        public System.Collections.Generic.IList<System.Xml.XPath.XPathItem> X31TokenizeLiteral(string input, string pattern) =>
            FunctionLibrary.TokenizeLiteral(input, pattern);

        public string X31LowerCase(string s) => CaseMapping.ToLower(s);

        public string X31NormalizeSpace(string s) => Lexical.Collapse(s);

        /// <summary>fn:string-length: the number of code points (a double, which the compiler boxes as xs:integer).</summary>
        public double X31StringLength(string s) => CodePoints.Length(s);

        public string X31Translate(string s, string map, string trans) => CodePoints.Translate(s, map, trans);

        public bool X31Contains(string s, string t) => StringMatch.Contains(s, t, Collation.Codepoint);

        public bool X31StartsWith(string s, string t) => StringMatch.StartsWith(s, t, Collation.Codepoint);

        public bool X31EndsWith(string s, string t) => StringMatch.EndsWith(s, t, Collation.Codepoint);

        public string X31SubstringBefore(string s, string t) => StringMatch.Before(s, t, Collation.Codepoint);

        public string X31SubstringAfter(string s, string t) => StringMatch.After(s, t, Collation.Codepoint);
    }
}
