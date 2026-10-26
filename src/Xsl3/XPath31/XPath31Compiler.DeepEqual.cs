// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.XPath;
using Xsl3.Qil;
using Xsl3.Runtime;
using Xsl3.Xdm;
using T = Xsl3.XmlQueryTypeFactory;

namespace Xsl3.XPath31;

internal sealed partial class XPath31Compiler
{
    private bool GroundedComparisonOperand(Expr expression) => expression switch {
        AxisStep { Axis: Axis.Attribute, Predicates.Count: 0 } => true, // locally retained owner headers
        NamedFunctionRef or InlineFunctionExpr => true, // compared functions fail without invoking their bodies
        MapConstructor map => map.Entries.All(e => GroundedComparisonOperand(e.Key) && GroundedComparisonOperand(e.Value)),
        SquareArrayConstructor array => array.Members.All(GroundedComparisonOperand),
        CurlyArrayConstructor array => array.Content == null || GroundedComparisonOperand(array.Content),
        SequenceExpr sequence => sequence.Items.All(GroundedComparisonOperand),
        _ => GroundedSetOperand(expression),
    };

    private QilNode? ForwardDeepEqual(IReadOnlyList<Expr> args)
    {
        bool leftGrounded = GroundedComparisonOperand(args[0]), rightGrounded = GroundedComparisonOperand(args[1]);
        if (leftGrounded == rightGrounded) return null;
        Expr input = args[leftGrounded ? 1 : 0];
        QilNode reference = Items(Build(args[leftGrounded ? 0 : 1]));
        AxisStep? step = ClosedDescendantStep(input);
        NodeKindTest? projection = null;
        if (step is { Predicates.Count: 0 })
        {
            if (step.Test is NameTest { LocalName: not null, AnyNamespace: false } name)
            {
                QName expanded = _types.Expand(new LexicalQName(name.Prefix, name.LocalName, name.BracedUri), _sc.DefaultElementNamespace);
                projection = new(XPathNodeType.Element, expanded.NamespaceUri, expanded.LocalName);
            }
            else if (step.Test is NameTest { LocalName: null, Prefix: null, BracedUri: null }) projection = new(XPathNodeType.Element);
            else if (step.Test is KindNodeTest kind)
            {
                NodeKindTest resolved = _types.ResolveKind(kind.Kind);
                if (resolved.Kind == XPathNodeType.Element && resolved.TypeName == null && resolved.Names == null
                    && resolved.LocalName == null && resolved.NamespaceUri == null) projection = resolved;
            }
        }
        QilNode source;
        if (projection == null) source = Items(Build(input));
        else
        {
            QilNode Descendants() => StepFor(step!.Axis == Axis.Child ? Axis.Descendant : step.Axis, step.Test, Array.Empty<Expr>(), topmost: true);
            if (input is PathExpr path)
            {
                var (anchor, body, binding) = WithFocus(Nodes(Build(path.Left), "XPTY0019"), false, Descendants);
                source = Items(WrapLet(binding, _f.Loop(anchor, body)));
            }
            else source = Items(Descendants());
        }
        QilNode type = _f.Double(projection == null ? -1 : ConstantPool.Add(new SequenceTypeTest(projection, Cardinality.ExactlyOne)));
        QilIterator state = _f.Let(StatefulCall(nameof(XsltLibrary.X31DeepEqualNew), T.Item, StaticId(), reference,
            args.Count == 3 ? AtomicOperand(args[2]) : _f.Sequence(), _f.Boolean(args.Count == 3), type));
        if (source.XmlType!.MaybeMany && _env?.LiftPopulation(source) is { } producer)
            return _f.Loop(state, StatefulCall(nameof(XsltLibrary.X31DeepEqualRun), T.BooleanX, state, producer));
        QilIterator item = _f.For(source);
        QilNode compare = _f.Loop(item, StatefulCall(nameof(XsltLibrary.X31DeepEqualItem), T.Empty, state, item));
        return _f.Loop(state, _f.Sequence(compare, StatefulCall(nameof(XsltLibrary.X31DeepEqualEnd), T.BooleanX, state)));
    }
}
