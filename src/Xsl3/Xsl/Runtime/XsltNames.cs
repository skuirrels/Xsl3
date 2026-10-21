// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Xml.XPath;
using Xsl3.XPath31;
using Xsl3.XPath31.Functions;
using Xsl3.Xdm;

namespace Xsl3.Runtime
{
    /// <summary>
    /// Xsl3: what system-property, function-available, element-available and type-available need to answer for
    /// a name computed at run time: the namespaces in scope and the stylesheet's functions (XSLT 3.0 §20.4).
    /// </summary>
    internal sealed record XsltNameContext(IReadOnlyDictionary<string, string> Namespaces, IReadOnlySet<(string Ns, string Local, int Arity)> StylesheetFunctions)
    {
        public const string XsltNs = "http://www.w3.org/1999/XSL/Transform";

        /// <summary>Whether xsl:evaluate is enabled (XsltSettings.EnableEvaluate); otherwise it is statically disabled.</summary>
        public bool EvaluateEnabled { get; init; }

        public static readonly HashSet<string> Instructions = new(System.StringComparer.Ordinal)
        {
            "analyze-string", "apply-imports", "apply-templates", "attribute", "call-template", "choose", "comment", "copy",
            "copy-of", "document", "element", "fallback", "for-each", "for-each-group", "if", "message", "namespace", "next-match",
            "number", "perform-sort", "processing-instruction", "result-document", "sequence", "text", "value-of", "variable",
            // XSLT 3.0
            "where-populated", "on-empty", "on-non-empty", "try", "iterate", "next-iteration", "break", "fork", "source-document",
            "map", "map-entry", "merge", "assert",
            // In 3.0 element-available covers every XSLT element the processor implements, declarations and the
            // elements that only appear inside others too (XSLT 3.0 §20.4.2); not xsl:import-schema (no schema-awareness).
            "accept", "accumulator", "accumulator-rule", "attribute-set", "catch", "character-map", "context-item", "decimal-format",
            "expose", "function", "global-context-item", "import", "include", "key", "matching-substring", "merge-action", "merge-key",
            "merge-source", "mode", "namespace-alias", "non-matching-substring", "on-completion", "otherwise", "output",
            "output-character", "override", "package", "param", "preserve-space", "sort", "strip-space", "stylesheet", "template",
            "transform", "use-package", "when", "with-param",
        };

        public static readonly HashSet<(string, int)> XsltFunctions = new()
        {
            ("current", 0), ("current-group", 0), ("position", 0), ("last", 0), ("current-output-uri", 0), ("current-grouping-key", 0), ("document", 1), ("document", 2), ("element-available", 1),
            ("format-number", 2), ("format-number", 3), ("function-available", 1), ("function-available", 2), ("key", 2), ("key", 3),
            ("regex-group", 1), ("system-property", 1), ("type-available", 1), ("copy-of", 0), ("copy-of", 1), ("snapshot", 0), ("snapshot", 1), ("accumulator-before", 1), ("accumulator-after", 1), ("current-merge-group", 0), ("current-merge-group", 1), ("current-merge-key", 0), ("unparsed-entity-public-id", 1), ("unparsed-entity-uri", 1), ("unparsed-entity-public-id", 2), ("unparsed-entity-uri", 2),
        };

        /// <summary>
        /// An EQName or lexical QName, its prefix resolved against the namespaces in scope; an unprefixed name is
        /// in <paramref name="defaultNs"/>. <paramref name="code"/> is raised for a name that is neither, or whose
        /// prefix isn't declared.
        /// </summary>
        public (string Ns, string Local) Resolve(string name, string defaultNs, string code)
        {
            name = name.Trim();
            if (name.StartsWith("Q{", System.StringComparison.Ordinal) && name.IndexOf('}') is int close and > 0)
            {
                string local = name[(close + 1)..];
                if (!Casting.IsNCName(local))
                {
                    throw new XdmException(code, $"'{name}' is not a valid EQName");
                }
                return (name[2..close], local);
            }
            int colon = name.IndexOf(':');
            if (colon < 0)
            {
                return Casting.IsNCName(name) ? (defaultNs, name) : throw new XdmException(code, $"'{name}' is not a valid QName");
            }
            string prefix = name[..colon], rest = name[(colon + 1)..];
            if (!Casting.IsNCName(prefix) || !Casting.IsNCName(rest))
            {
                throw new XdmException(code, $"'{name}' is not a valid QName");
            }
            return Namespaces.TryGetValue(prefix, out string? ns) ? (ns, rest) : throw new XdmException(code, $"The prefix of '{name}' is not declared");
        }

        /// <summary>The system properties in the XSLT namespace (XSLT 3.0 §20.4.4), for fn:available-system-properties.</summary>
        public static readonly string[] SystemProperties =
        {
            "version", "vendor", "vendor-url", "product-name", "product-version", "is-schema-aware", "supports-serialization",
            "supports-backwards-compatibility", "supports-namespace-axis", "supports-streaming", "supports-dynamic-evaluation",
            "supports-higher-order-functions", "xpath-version", "xsd-version",
        };

        public string SystemProperty(string name)
        {
            var (ns, local) = Resolve(name, "", "XTDE1390");
            if (ns != XsltNs)
            {
                return "";
            }
            return local switch
            {
                "version" => "3.0",
                "vendor" => "Xsl3",
                "vendor-url" => "https://github.com/skuirrels/Xsl3",
                "product-name" => "Xsl3",
                "product-version" => typeof(XsltNameContext).Assembly.GetName().Version?.ToString() ?? "0",
                "supports-serialization" or "supports-backwards-compatibility" or "supports-namespace-axis" or "supports-higher-order-functions" => "yes",
                "supports-dynamic-evaluation" => EvaluateEnabled ? "yes" : "no",
                "is-schema-aware" or "supports-streaming" => "no",
                "xpath-version" => "3.1",
                "xsd-version" => "1.0",
                _ => "",
            };
        }

        /// <summary>exsl:node-set and msxsl:node-set, which 2.0+ stylesheets get as the identity (temporary trees are nodes).</summary>
        internal static bool IsNodeSetFunction(string ns, string local) =>
            local == "node-set" && ns is "http://exslt.org/common" or "urn:schemas-microsoft-com:xslt";

        public bool FunctionAvailable(string name, int? arity)
        {
            var (ns, local) = Resolve(name, QName.Ns.Fn, "XTDE1400");
            if (arity is null or 1 && IsNodeSetFunction(ns, local))
            {
                return true;
            }
            // With an arity, just that one (concat takes any number of arguments); without, any up to 10.
            for (int n = arity ?? 0; n <= (arity ?? 10); n++)
            {
                if (FunctionLibrary.Find(ns, local, n) != null || StylesheetFunctions.Contains((ns, local, n)) || (ns == QName.Ns.Fn && XsltFunctions.Contains((local, n))) ||
                    // The context-item forms (fn:name#0 and the like), compiled rather than in the library.
                    ns == QName.Ns.Fn && n == 0 && XPath31Compiler.s_contextFunctions.Contains(local) ||
                    XPath31Compiler.CompiledHigherOrderFunctions.Contains((ns, local, n)))
                {
                    return true;
                }
            }
            // The focus-dependent functions with their argument, compiled rather than in the library.
            if (ns == QName.Ns.Fn && arity is null or 1 && local is "lang" or "id" or "element-with-id" or "idref")
            {
                return true;
            }
            // xs:T#1 constructor functions, the list types and xs:error among them
            return ns == QName.Ns.Xs && arity is null or 1 && (local == "error"
                || XPath31.StaticTypeResolver.FindCastTarget(new QName(ns, local, "xs")) is var (type, _) && !type.IsAbstract);
        }

        public bool ElementAvailable(string name)
        {
            // An unprefixed name is in the default namespace in scope (XSLT 3.0 §20.4.2).
            var (ns, local) = Resolve(name, Namespaces.TryGetValue("", out string? defaultNs) ? defaultNs : "", "XTDE1440");
            return ns == XsltNs && (Instructions.Contains(local) || local == "evaluate" && EvaluateEnabled);
        }

        public bool TypeAvailable(string name)
        {
            var (ns, local) = Resolve(name, "", "XTDE1428");
            // xs:dateTimeStamp is XSD 1.1's; the xsd-version property is 1.0.
            return ns == QName.Ns.Xs && local != "dateTimeStamp" && (AtomicTypes.Find(ns, local) != null || local is "anyType" or "anySimpleType" or "untyped");
        }
    }

    public sealed partial class XsltLibrary
    {
        private XsltNameContext NameContext(double id) => (XsltNameContext)Constant((int)id);

        public XPathItem X31SystemProperty(string name, double context) => AtomicValue.OfString(NameContext(context).SystemProperty(name));

        // NaN: no arity given. No function has a negative arity or more than 255 parameters.
        public XPathItem X31FunctionAvailable(string name, double arity, double context) =>
            AtomicValue.OfBoolean(double.IsNaN(arity) ? NameContext(context).FunctionAvailable(name, null)
                : arity is >= 0 and <= 255 && NameContext(context).FunctionAvailable(name, (int)arity));

        public XPathItem X31ElementAvailable(string name, double context) => AtomicValue.OfBoolean(NameContext(context).ElementAvailable(name));

        public XPathItem X31TypeAvailable(string name, double context) => AtomicValue.OfBoolean(NameContext(context).TypeAvailable(name));

        /// <summary>The value of a static variable or parameter, computed when the stylesheet was compiled.</summary>
        public IList<XPathItem> X31StaticValue(double id) => (IList<XPathItem>)Constant((int)id);

        /// <summary>The context item a template called from a function body gets: none (an empty string stands in).</summary>
        public XPathItem X31AbsentFocus() => AbsentItem.Instance;

        /// <summary>xs:error($arg): () for (), FORG0001 for anything else (xs:error has no values).</summary>
        public IList<XPathItem> X31CastToError(IList<XPathItem> value) =>
            value.Count == 0 ? System.Array.Empty<XPathItem>() : throw new XdmException("FORG0001", "No value can be cast to xs:error");

        /// <summary>
        /// Xsl3: the focus of a named template called with no context item (XSLT 3.0 §10.1.2): its string value is ""
        /// (as the empty string stood in before), and using it as the context item is XPDY0002.
        /// </summary>
        internal sealed class AbsentItem : AtomicValue
        {
            public static readonly AbsentItem Instance = new();

            private AbsentItem() : base(AtomicTypes.String)
            {
            }

            public override string Value => "";

            public override object ClrValue => "";
        }

        /// <summary>The context item of a named template's body: XPDY0002 when it is absent.</summary>
        public XPathItem X31Focus(XPathItem item) =>
            item is AbsentItem ? throw new XdmException("XPDY0002", "The context item is absent") : item;

        /// <summary>A named template's xsl:context-item use="required": XTTE3090 when it is absent.</summary>
        public IList<XPathItem> X31RequireFocus(XPathItem item) =>
            item is AbsentItem ? throw new XdmException("XTTE3090", "The template requires a context item, and there is none") : System.Array.Empty<XPathItem>();

        public bool X31IsAbsent(XPathItem item) => item is AbsentItem;

        /// <summary>
        /// The global context item (XSLT 3.0 §9.7): the input's document node, or absent when the transformation was given
        /// no input (XslCompiledTransform.AbsentInput) or xsl:global-context-item says use="absent".
        /// </summary>
        public XPathItem X31GlobalContextItem(XPathNavigator root, bool absentDeclared) =>
            absentDeclared ? AbsentItem.Instance : X31InitialFocus(root);

        /// <summary>xsl:global-context-item use="required": XTDE3086 when the transformation was given no global context item.</summary>
        public IList<XPathItem> X31RequireGlobalContextItem(XPathItem item) =>
            X31InitialFocus(item) is AbsentItem ? throw new XdmException("XTDE3086", "The stylesheet requires a global context item, and none was supplied") : System.Array.Empty<XPathItem>();

        /// <summary>The initial context item: absent when the transformation was given XslCompiledTransform.AbsentInput.</summary>
        public XPathItem X31InitialFocus(XPathItem item) =>
            item is XPathNavigator node && XslCompiledTransform.AbsentInput.IsSamePosition(node) ? AbsentItem.Instance : item;


        /// <summary>The value of a yes/no attribute value template (XTDE0030 for other values).</summary>
        public bool X31YesNo(string value, string attribute) => value.Trim() switch
        {
            "yes" or "true" or "1" => true,
            "no" or "false" or "0" => false,
            _ => throw new XdmException("XTDE0030", $"{attribute}=\"{value}\" must be yes or no"),
        };

        /// <summary>unparsed-entity-uri and -public-id need a context node in a tree rooted at a document node.</summary>
        public bool X31EntityContext(XPathItem item)
        {
            if (item is XPathNavigator n)
            {
                XPathNavigator root = n.Clone();
                root.MoveToRoot();
                if (root.NodeType == XPathNodeType.Root)
                {
                    return true;
                }
            }
            throw new XdmException("XTDE1380", "The context item is not a node in a tree rooted at a document node");
        }

        /// <summary>A single numeric argument as a double (XPTY0004 for anything else).</summary>
        public double X31Integer(IList<XPathItem> items)
        {
            AtomicValue a = Sequences.AtomizeOptional(items, "the argument") ?? throw new XdmException("XPTY0004", "An integer is required, not an empty sequence");
            if (a.Type.Primitive == Primitive.UntypedAtomic)
            {
                // The function conversion rules cast an untyped value to the parameter's type (xs:integer).
                return double.TryParse(a.Value.Trim(), System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out double d)
                    ? d : throw new XdmException("FORG0001", $"'{a.Value}' can't be cast to xs:integer");
            }
            return a.Type.IsNumeric ? Numeric.ToDouble(a) : throw new XdmException("XPTY0004", $"An integer is required, not {a.Type.Name}");
        }
    }
}
