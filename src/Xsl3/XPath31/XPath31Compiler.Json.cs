// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using Xsl3.Qil;
using Xsl3.Runtime;
using Xsl3.Xdm;
using T = Xsl3.XmlQueryTypeFactory;

namespace Xsl3.XPath31;

internal sealed partial class XPath31Compiler
{
    private QilNode ForwardXmlToJson(Expr input, Expr? options)
    {
        QilIterator state = _f.Let(StatefulCall(nameof(XsltLibrary.X31XmlToJsonNew), T.Item, StaticId(),
            options == null ? _f.Sequence() : Items(Build(options)), _f.Boolean(options != null)));
        if (ForwardXmlToJsonHeaderPath(input, state) is { } headers)
        {
            QilNode value = _f.Loop(state, _f.Sequence(headers, StatefulCall(nameof(XsltLibrary.X31XmlToJsonEnd), T.ItemS, state)));
            s_atomicItems.AddOrUpdate(value, AtomicTypes.String);
            s_stringItems.AddOrUpdate(value, AtomicTypes.String);
            return value;
        }
        QilNode source = Items(Build(input));
        QilNode result;
        if (source.XmlType!.MaybeMany && _env?.LiftPopulation(source) is { } producer)
        {
            AxisStep? descendant = ClosedDescendantStep(input);
            NodeKindTest? test = null;
            if (descendant is { Predicates.Count: 0 })
            {
                if (descendant.Test is NameTest { AnyNamespace: false, LocalName: not null } name)
                {
                    QName expanded = _types.Expand(new LexicalQName(name.Prefix, name.LocalName, name.BracedUri), _sc.DefaultElementNamespace);
                    test = new NodeKindTest(System.Xml.XPath.XPathNodeType.Element, expanded.NamespaceUri, expanded.LocalName);
                }
                else if (descendant.Test is KindNodeTest kind)
                {
                    NodeKindTest resolved = _types.ResolveKind(kind.Kind);
                    // These tests are fully represented by the underlying axis filter.
                    // Tests requiring an additional name/schema predicate need a stronger proof.
                    if (resolved.TypeName == null && resolved.DocumentElement == null && resolved.Names == null
                        && resolved.NamespaceUri == null && resolved.LocalName == null) test = resolved;
                }
            }
            QilNode type = _f.Double(test == null ? -1 : ConstantPool.Add(new SequenceTypeTest(test, Cardinality.ExactlyOne)));
            result = _f.Loop(state, StatefulCall(nameof(XsltLibrary.X31XmlToJsonRun), T.ItemS, state, producer, type));
        }
        else
        {
            QilIterator item = _f.For(source);
            QilNode convert = _f.Loop(item, StatefulCall(nameof(XsltLibrary.X31XmlToJsonItem), T.Empty, state, item));
            result = _f.Loop(state, _f.Sequence(convert, StatefulCall(nameof(XsltLibrary.X31XmlToJsonEnd), T.ItemS, state)));
        }
        s_atomicItems.AddOrUpdate(result, AtomicTypes.String);
        s_stringItems.AddOrUpdate(result, AtomicTypes.String);
        return result;
    }
    private QilNode? ForwardXmlToJsonHeaderPath(Expr input, QilNode conversion)
    {
        HeaderPath? path = ParseHeaderPath(input);
        if (_env == null || path == null || path.Anchor is not (ContextItemExpr or RootExpr)
            || path.Steps.Count < 2 || path.Steps[^1].Axis != Axis.Child
            || !System.Linq.Enumerable.Any(path.Steps, step => step.Axis is Axis.Descendant or Axis.DescendantOrSelf)
            || !System.Linq.Enumerable.Any(path.Steps, step => step.Predicates.Count != 0)) return null;
        QilNode source = Nodes(Build(path.Anchor), path.ContextError);
        if (source.XmlType!.MaybeMany) return null;
        bool supported = true;
        var (anchor, body, binding) = WithFocus(source, false, () =>
        {
            QilNode root = FocusNode();
            QilIterator matches = _f.Let(StatefulCall(nameof(XsltLibrary.X31NodeSetMatchNew), T.Item));
            QilNode? selector = _env.LiftHeaderTest(node =>
            {
                var evaluated = HeaderPathMatches(path, node, root, matches, 0, 0).Matches;
                QilNode result = evaluated[^1];
                for (int i = evaluated.Count - 1; i >= 0; i--) result = _f.Loop(evaluated[i], result);
                return result;
            });
            if (selector == null) { supported = false; return _f.Sequence(); }
            return _f.Loop(matches, StatefulCall(nameof(XsltLibrary.X31XmlToJsonHeaders), T.Empty, conversion, root, selector));
        });
        return supported ? WrapLet(binding, _f.Loop(anchor, body)) : null;
    }

}
