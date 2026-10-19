// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Xml.XPath;
using Xsl3.Qil;
using Xsl3.Runtime;
using Xsl3.XPath31;
using Xsl3.Xdm;
using T = Xsl3.XmlQueryTypeFactory;

namespace Xsl3.Xslt
{
    /// <summary>
    /// Xsl3: [xsl:]use-when (XSLT 3.0 §3.13.1). The expression is evaluated when the stylesheet is read, in a
    /// static context with the namespaces in scope and the functions that need no dynamic context, among them
    /// system-property, function-available, element-available and type-available. There is no context item and
    /// there are no variables; stylesheet functions aren't available.
    /// </summary>
    internal static class UseWhen
    {
        public static bool Evaluate(string expression, IReadOnlyDictionary<string, string> namespaces, string defaultElementNamespace, string? baseUri)
        {
            var sc = new XPathStaticContext { BaseUri = baseUri, DefaultElementNamespace = defaultElementNamespace };
            sc.Namespaces.Clear();
            foreach (var (prefix, uri) in namespaces)
            {
                sc.Namespaces[prefix] = uri;
            }
            sc.Namespaces["xml"] = XmlReservedNs.NsXml;
            int names = ConstantPool.Add(new XsltNameContext(namespaces, new HashSet<(string, string, int)>()));
            XPath31Executable executable = XPath31Compiler.CompileHosted(Parser.ParseXPath(expression), sc, f => new Environment(f, names));
            return Sequences.EffectiveBooleanValue(executable.Evaluate());
        }

        private sealed class Environment(QilPatternFactory f, int names) : IXPath31Environment
        {
            private QilNode Raise(string code, string message, XmlQueryType type) =>
                XPath31Compiler.Invoke(f, type == T.DoubleX ? nameof(XsltLibrary.X31RaiseDouble) : nameof(XsltLibrary.X31RaiseItem), type, f.String(code), f.String(message));

            public FunctionItemRegistry? FunctionItems => null;

            public QilNode? ContextItem() => null;

            public QilNode ContextPosition() => Raise("XPDY0002", "There is no focus in a use-when expression", T.DoubleX);

            public QilNode ContextLast() => Raise("XPDY0002", "There is no focus in a use-when expression", T.DoubleX);

            public QilNode? ResolveVariable(QName name) => null;

            public QilNode? ResolveFunction(QName name, IReadOnlyList<Expr> args, System.Func<Expr, QilNode> compile)
            {
                if (name.NamespaceUri != QName.Ns.Fn)
                {
                    return null;
                }
                QilNode Name() => XPath31Compiler.Invoke(f, nameof(XsltLibrary.X31String), T.StringX, compile(args[0]));
                switch (name.LocalName, args.Count)
                {
                    case ("system-property", 1):
                        return XPath31Compiler.Invoke(f, nameof(XsltLibrary.X31SystemProperty), T.Item, Name(), f.Double(names));
                    case ("element-available", 1):
                        return XPath31Compiler.Invoke(f, nameof(XsltLibrary.X31ElementAvailable), T.Item, Name(), f.Double(names));
                    case ("type-available", 1):
                        return XPath31Compiler.Invoke(f, nameof(XsltLibrary.X31TypeAvailable), T.Item, Name(), f.Double(names));
                    case ("function-available", 1):
                    case ("function-available", 2):
                        return XPath31Compiler.Invoke(f, nameof(XsltLibrary.X31FunctionAvailable), T.Item, Name(),
                            args.Count > 1 ? XPath31Compiler.Invoke(f, nameof(XsltLibrary.X31Integer), T.DoubleX, compile(args[1])) : f.Double(-1), f.Double(names));
                    case ("current" or "current-group" or "current-grouping-key" or "regex-group" or "key" or "document" or "unparsed-entity-uri" or "unparsed-entity-public-id", _):
                        // XSLT functions that need a dynamic context aren't available in use-when (XSLT 3.0 §3.13.1).
                        throw new XPathStaticException("XPST0017", $"{name.LocalName}() isn't available in a use-when expression", 0);
                }
                return null;
            }
        }
    }
}
