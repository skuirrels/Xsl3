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
