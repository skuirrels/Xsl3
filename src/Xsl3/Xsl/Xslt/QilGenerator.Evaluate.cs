// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using Xsl3.Qil;
using Xsl3.Runtime;
using Xsl3.XPath31;
using Xsl3.XPath31.Functions;
using Xsl3.Xdm;
using T = Xsl3.XmlQueryTypeFactory;

namespace Xsl3.Xslt
{
    // Xsl3: xsl:evaluate (XSLT 3.0 §10.4; docs/plan/p5-evaluate.md). The loader lowers the instruction to a call of
    // Q{urn:xsl3:evaluate}evaluate; this compiles that call.
    internal sealed partial class QilGenerator
    {
        /// <summary>
        /// The internal evaluate function. Its arguments: the target expression, the context item, the namespace
        /// context node, whether namespace-context was given, the base URI, the with-params map and the map of the
        /// xsl:with-param values. The static context of the instruction becomes an <see cref="EvaluateContext"/>, and
        /// the stylesheet functions the target expression may call are passed as function items.
        /// </summary>
        private QilNode CompileEvaluate31(QName name, IReadOnlyList<XPath31.Expr> args, System.Func<XPath31.Expr, QilNode> compile)
        {
            if (name.LocalName != "evaluate" || args.Count != 7)
            {
                return ReportXPath31Error("XPST0017", $"There is no function {name.EQName}#{args.Count}");
            }
            XPathStaticContext sc = StaticContext31();
            var context = new EvaluateContext(new Dictionary<string, string>(sc.Namespaces), sc.DefaultElementNamespace, sc.BaseUri, sc.DefaultCollation,
                sc.DefaultDecimalFormat, new Dictionary<QName, XPathDecimalFormat>(sc.DecimalFormats));
            var references = new List<XPath31.Expr>();
            foreach (var (ns, local, arity) in EvaluateFunctions31())
            {
                references.Add(new XPath31.NamedFunctionRef(new XPath31.LexicalQName(null, local, ns), arity));
            }
            QilNode functions = references.Count == 0 ? _f.TypeAssert(_f.Sequence(), T.ItemS) : Box(compile(new XPath31.SequenceExpr(references)));
            var actual = new List<QilNode>();
            foreach (XPath31.Expr arg in args)
            {
                actual.Add(Box(compile(arg)));
            }
            actual.Add(functions);
            actual.Add(_f.Double(ConstantPool.Add(context)));
            return X31(nameof(XsltLibrary.X31Evaluate), T.ItemS, actual.ToArray());
        }

        /// <summary>
        /// The stylesheet functions a target expression may call (XSLT 3.0 §10.4.1): the package's own functions that
        /// aren't private, and those of used packages it sees with a visibility other than private or hidden.
        /// </summary>
        private IEnumerable<(string Ns, string Local, int Arity)> EvaluateFunctions31()
        {
            var seen = new HashSet<(string, string, int)>();
            foreach (var ((functionName, arity), (function, _)) in _compiler.Functions)
            {
                var (package, ns) = PackageComponents.Demangle(functionName.NamespaceUri);
                if (package == _package31 && function.PackageId == _package31 && function.Visibility is "public" or "final" && seen.Add((ns, functionName.LocalName, arity)))
                {
                    yield return (ns, functionName.LocalName, arity);
                }
            }
            if (_package31 < _compiler.PackageInfos.Count)
            {
                foreach (var (key, component) in _compiler.PackageInfos[_package31].Visible)
                {
                    if (key.Kind == 'F' && component.Owner != _package31 && component.Visibility is not ("private" or "hidden") && seen.Add((key.Ns, key.Local, key.Arity)))
                    {
                        yield return (key.Ns, key.Local, key.Arity);
                    }
                }
            }
        }

        /// <summary>
        /// The initial function (XSLT 3.0 §2.3.4): a public or final stylesheet function of the principal package, called
        /// with the arguments the caller supplies as parameters; XTDE0041 if there is no such function.
        /// </summary>
        private QilNode CompileInitialFunction31()
        {
            XmlQualifiedName name = _compiler.InitialFunction!;
            int arity = _compiler.InitialFunctionArity;
            QilName functionName = _f.QName(name.Name, name.Namespace);
            if (!_compiler.Functions.TryGetValue((functionName, arity), out var entry) || entry.Function.PackageId != 0 ||
                entry.Function.Visibility is not ("public" or "final"))
            {
                return X31(nameof(XsltLibrary.X31Raise), T.ItemS, _f.String("XTDE0041"),
                    _f.String($"There is no public stylesheet function Q{{{name.Namespace}}}{name.Name}#{arity} to call as the initial function"));
            }
            QilList actual = _f.ActualParameterList();
            for (int i = 1; i <= arity; i++)
            {
                XmlQualifiedName argument = XslCompiledTransform.InitialFunctionArgument(i);
                actual.Add(X31(nameof(XsltLibrary.X31Variable), T.ItemS, _f.String(argument.Name), _f.String(argument.Namespace)));
            }
            return _f.Invoke(entry.Function.Function!, actual);
        }
    }
}
