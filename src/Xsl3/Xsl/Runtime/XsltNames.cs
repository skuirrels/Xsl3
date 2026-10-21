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

        public bool FunctionAvailable(string name, int? arity)
        {
            var (ns, local) = Resolve(name, QName.Ns.Fn, "XTDE1400");
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
            // xs:T#1 constructor functions
            return ns == QName.Ns.Xs && arity is null or 1 && AtomicTypes.Find(ns, local) is { IsAbstract: false };
        }

        public bool ElementAvailable(string name)
        {
            var (ns, local) = Resolve(name, "", "XTDE1440");
            return ns == XsltNs && (Instructions.Contains(local) || local == "evaluate" && EvaluateEnabled);
        }

        public bool TypeAvailable(string name)
        {
            var (ns, local) = Resolve(name, "", "XTDE1428");
            return ns == QName.Ns.Xs && (AtomicTypes.Find(ns, local) != null || local is "anyType" or "anySimpleType" or "untyped");
        }
    }

    public sealed partial class XsltLibrary
    {
        private static XsltNameContext NameContext(double id) => (XsltNameContext)ConstantPool.Get((int)id);

        public XPathItem X31SystemProperty(string name, double context) => AtomicValue.OfString(NameContext(context).SystemProperty(name));

        public XPathItem X31FunctionAvailable(string name, double arity, double context) =>
            AtomicValue.OfBoolean(NameContext(context).FunctionAvailable(name, arity < 0 ? null : (int)arity));

        public XPathItem X31ElementAvailable(string name, double context) => AtomicValue.OfBoolean(NameContext(context).ElementAvailable(name));

        public XPathItem X31TypeAvailable(string name, double context) => AtomicValue.OfBoolean(NameContext(context).TypeAvailable(name));

        /// <summary>The value of a static variable or parameter, computed when the stylesheet was compiled.</summary>
        public IList<XPathItem> X31StaticValue(double id) => (IList<XPathItem>)ConstantPool.Get((int)id);

        /// <summary>The context item a template called from a function body gets: none (an empty string stands in).</summary>
        public XPathItem X31AbsentFocus() => AtomicValue.OfString("");

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
