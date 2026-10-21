// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.XPath;
using Xsl3.Qil;
using Xsl3.XPath31;
using Xsl3.XPath31.Functions;
using Xsl3.Xdm;
using T = Xsl3.XmlQueryTypeFactory;

namespace Xsl3.Runtime
{
    /// <summary>Xsl3: the parameters that supply the initial function's arguments (XSLT 3.0 §2.3.4).</summary>
    internal static class InitialFunctionArguments
    {
        public const string Namespace = "urn:xsl3:initial-function";

        /// <summary>The name of the parameter that supplies argument <paramref name="position"/> (from 1).</summary>
        public static System.Xml.XmlQualifiedName Name(int position) => new("arg" + position, Namespace);
    }

    /// <summary>Xsl3: names shared by the parts of xsl:evaluate (docs/plan/p5-evaluate.md).</summary>
    internal static class DynamicEvaluation
    {
        /// <summary>The namespace of the internal function the loader lowers xsl:evaluate to, and of its variables.</summary>
        public const string Namespace = "urn:xsl3:evaluate";

        /// <summary>How many compiled target expressions each xsl:evaluate instruction keeps.</summary>
        public const int CacheSize = 64;
    }

    /// <summary>
    /// The static context of one xsl:evaluate instruction (XSLT 3.0 §10.4.1), made when the stylesheet is compiled,
    /// and the target expressions compiled for it, most recently used first.
    /// </summary>
    internal sealed class EvaluateContext(Dictionary<string, string> namespaces, string defaultElementNamespace, string? baseUri, string defaultCollation,
        XPathDecimalFormat defaultDecimalFormat, Dictionary<QName, XPathDecimalFormat> decimalFormats)
    {
        public Dictionary<string, string> Namespaces { get; } = namespaces;

        public string DefaultElementNamespace { get; } = defaultElementNamespace;

        public string? BaseUri { get; } = baseUri;

        private readonly LinkedList<(string Key, object Compiled)> _cache = new();
        private readonly Dictionary<string, LinkedListNode<(string Key, object Compiled)>> _index = new(System.StringComparer.Ordinal);

        /// <summary>The compiled expression for a key, or the static error compiling it raised; compiled on a miss.</summary>
        public XPath31Executable Get(string key, System.Func<XPath31Executable> compile)
        {
            lock (_cache)
            {
                if (_index.TryGetValue(key, out var hit))
                {
                    _cache.Remove(hit);
                    _cache.AddFirst(hit);
                    return hit.Value.Compiled is XPath31Executable e ? e : throw (XdmException)hit.Value.Compiled;
                }
            }
            object compiled;
            try
            {
                compiled = compile();
            }
            catch (XdmException e)
            {
                compiled = e;
            }
            lock (_cache)
            {
                if (!_index.ContainsKey(key))
                {
                    _index[key] = _cache.AddFirst((key, compiled));
                    if (_cache.Count > DynamicEvaluation.CacheSize)
                    {
                        _index.Remove(_cache.Last!.Value.Key);
                        _cache.RemoveLast();
                    }
                }
            }
            return compiled is XPath31Executable executable ? executable : throw (XdmException)compiled;
        }

        /// <summary>The static context of a target expression.</summary>
        public XPathStaticContext StaticContext(IReadOnlyDictionary<string, string> namespaces, string defaultElementNamespace, string? baseUri)
        {
            var sc = new XPathStaticContext
            {
                BaseUri = baseUri,
                DefaultElementNamespace = defaultElementNamespace,
                DefaultCollation = defaultCollation,
                DefaultDecimalFormat = defaultDecimalFormat,
            };
            sc.Namespaces.Clear();
            foreach (var (prefix, uri) in namespaces)
            {
                if (prefix.Length > 0)
                {
                    sc.Namespaces[prefix] = uri;
                }
            }
            sc.Namespaces["xml"] = XmlReservedNs.NsXml;
            foreach (var (name, format) in decimalFormats)
            {
                sc.DecimalFormats[name] = format;
            }
            return sc;
        }
    }

    public sealed partial class XsltLibrary
    {
        /// <summary>
        /// xsl:evaluate (XSLT 3.0 §10.4): compiles the target expression in the instruction's static context (or the
        /// namespace context's namespaces), with the parameters as its variables and the stylesheet functions as
        /// function items, and evaluates it with the context item as a singleton focus.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("AOT", "IL3050",
            Justification = "Checked: without dynamic code, xsl:evaluate fails with PlatformNotSupportedException (docs/plan/p4-sink-b.md decision 7).")]
        public IList<XPathItem> X31Evaluate(IList<XPathItem> xpath, IList<XPathItem> contextItem, IList<XPathItem> namespaceContext, IList<XPathItem> namespaceGiven,
            IList<XPathItem> baseUri, IList<XPathItem> withParams, IList<XPathItem> withParamValues, IList<XPathItem> functions, double contextId)
        {
            var context = (EvaluateContext)Constant((int)contextId);
            string target = TargetExpression(xpath);
            if (contextItem.Count > 1)
            {
                throw new XdmException("XTTE3210", $"The context-item of xsl:evaluate must be at most one item, not {Coercion.Describe(contextItem)}");
            }

            // The statically known namespaces and the default element namespace (§10.4.1).
            IReadOnlyDictionary<string, string> namespaces = context.Namespaces;
            string defaultElementNamespace = context.DefaultElementNamespace;
            bool ownNamespaces = Sequences.EffectiveBooleanValue(namespaceGiven);
            if (ownNamespaces)
            {
                if (namespaceContext.Count != 1 || namespaceContext[0] is not XPathNavigator node)
                {
                    throw new XdmException("XTTE3170", $"The namespace-context of xsl:evaluate must be a single node, not {Coercion.Describe(namespaceContext)}");
                }
                var inScope = new Dictionary<string, string>(System.StringComparer.Ordinal);
                XPathNavigator element = node.Clone();
                if (element.NodeType != XPathNodeType.Element)
                {
                    element.MoveToParent();
                }
                if (element.NodeType == XPathNodeType.Element)
                {
                    foreach (var (prefix, uri) in element.GetNamespacesInScope(XmlNamespaceScope.All))
                    {
                        inScope[prefix] = uri;
                    }
                }
                namespaces = inScope;
                defaultElementNamespace = inScope.GetValueOrDefault("", "");
            }

            string? staticBase = context.BaseUri;
            if (baseUri.Count > 0)
            {
                string value = baseUri[0].Value.Trim();
                staticBase = staticBase != null && System.Uri.TryCreate(staticBase, System.UriKind.Absolute, out System.Uri? b) && System.Uri.TryCreate(b, value, out System.Uri? r)
                    ? r.OriginalString : value;
            }

            // The variables: the xsl:with-param values, then the with-params map, which takes precedence.
            var variables = new Dictionary<QName, IList<XPathItem>>();
            void Bind(IList<XPathItem> map, string what)
            {
                if (map.Count != 1 || map[0] is not XdmMap m)
                {
                    throw Err.XPTY0004($"{what} of xsl:evaluate must be a map, not {Coercion.Describe(map)}");
                }
                foreach (var (key, value) in m.Entries)
                {
                    if (key is not QNameValue q)
                    {
                        throw Err.XPTY0004($"The keys of {what} of xsl:evaluate must be xs:QName values, not {key.Type.Name}");
                    }
                    variables[new QName(q.QName.NamespaceUri, q.QName.LocalName)] = Copy(value);
                }
            }
            Bind(withParamValues, "the xsl:with-param values");
            Bind(withParams, "with-params");

            // The stylesheet functions, each a hidden variable the target expression calls dynamically.
            var functionTable = new Dictionary<(string, string, int), QName>();
            for (int i = 0; i < functions.Count; i++)
            {
                if (functions[i] is XdmFunction { Name: QName fn } f)
                {
                    var hidden = new QName(DynamicEvaluation.Namespace, "f" + i);
                    functionTable[(fn.NamespaceUri, fn.LocalName, f.Arity)] = hidden;
                    variables[hidden] = Sequences.One(f);
                }
            }

            var key = new StringBuilder(target).Append('\u0001').Append(staticBase).Append('\u0001').Append(defaultElementNamespace);
            if (ownNamespaces)
            {
                foreach (var (prefix, uri) in namespaces.OrderBy(kv => kv.Key, System.StringComparer.Ordinal))
                {
                    key.Append('\u0001').Append(prefix).Append('=').Append(uri);
                }
            }
            key.Append('\u0002');
            foreach (QName name in variables.Keys.Where(n => n.NamespaceUri != DynamicEvaluation.Namespace).OrderBy(n => n.EQName, System.StringComparer.Ordinal))
            {
                key.Append(name.EQName).Append('\u0001');
            }

            var names = new HashSet<QName>(variables.Keys);
            XPathStaticContext sc = context.StaticContext(namespaces, defaultElementNamespace, staticBase);
            if (!System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported)
            {
                throw new System.PlatformNotSupportedException("xsl:evaluate compiles its target expression at run time, which needs dynamic code; NativeAOT doesn't have it.");
            }
            XPath31Executable executable = context.Get(key.ToString(), () =>
            {
                try
                {
                    Expr ast = Parser.ParseXPath(target);
                    // In the stylesheet's constant table: the target refers to the stylesheet's constants by number.
                    using var constants = ConstantPool.Use(_runtime.Constants);
                    return XPath31Compiler.CompileHosted(ast, sc, f => new EvaluateEnvironment(f, names, functionTable));
                }
                catch (XPathStaticException e)
                {
                    // XTDE3160: a static error in the target expression, reported as a dynamic error (§10.4).
                    throw new XdmException("XTDE3160", $"The target expression of xsl:evaluate is not valid: {e.Code}: {e.Message}");
                }
            });

            FunctionContext host = X31Context;
            return Copy(executable.Evaluate(contextItem.Count == 1 ? contextItem[0] : null, variables, host.ImplicitTimezoneMinutes,
                _runtime.ExternalContext.DataSources, host.CurrentDateTime));
        }

        // The target expression: the xpath attribute's value converted to xs:string by the function conversion rules.
        private static string TargetExpression(IList<XPathItem> xpath)
        {
            if (xpath.Count == 1)
            {
                AtomicValue? value = Sequences.AtomizeOptional(xpath, "the xpath attribute of xsl:evaluate");
                if (value != null && value.Type.Primitive is Primitive.String or Primitive.UntypedAtomic or Primitive.AnyUri)
                {
                    return value.Value;
                }
            }
            throw Err.XPTY0004($"The xpath attribute of xsl:evaluate must be a single string, not {Coercion.Describe(xpath)}");
        }

        /// <summary>
        /// What a target expression sees of its host (XSLT 3.0 §10.4.1–2): the supplied context item as a singleton
        /// focus (XPDY0002 when absent), the parameters as variables, and the stylesheet functions, called through
        /// the function items that stand for them. XSLT's own functions (current, key, …) aren't available.
        /// </summary>
        private sealed class EvaluateEnvironment(QilPatternFactory f, IReadOnlySet<QName> variables, IReadOnlyDictionary<(string, string, int), QName> functions)
            : IXPath31Environment
        {
            public FunctionItemRegistry? FunctionItems => null;

            public QilNode? ContextItem() => XPath31Compiler.Invoke(f, nameof(X31ContextItem), T.Item);

            public QilNode ContextPosition() => XPath31Compiler.Invoke(f, nameof(X31TopLevelFocus), T.DoubleX);

            public QilNode ContextLast() => XPath31Compiler.Invoke(f, nameof(X31TopLevelFocus), T.DoubleX);

            public QilNode? ResolveVariable(QName name) =>
                variables.Contains(name) ? XPath31Compiler.Invoke(f, nameof(X31Variable), T.ItemS, f.String(name.LocalName), f.String(name.NamespaceUri)) : null;

            public QilNode? ResolveFunction(QName name, IReadOnlyList<Expr> args, System.Func<Expr, QilNode> compile) =>
                functions.TryGetValue((name.NamespaceUri, name.LocalName, args.Count), out QName? hidden)
                    ? compile(new DynamicFunctionCall(new VarRef(new LexicalQName(null, hidden.LocalName, hidden.NamespaceUri)), args))
                    : null;
        }
    }
}
