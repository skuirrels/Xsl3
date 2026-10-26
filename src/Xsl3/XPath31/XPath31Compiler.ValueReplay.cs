// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Linq;
using Xsl3.Qil;
using Xsl3.Runtime;
using T = Xsl3.XmlQueryTypeFactory;

namespace Xsl3.XPath31;

internal sealed partial class XPath31Compiler
{
    private static readonly ConditionalWeakTable<QilNode, object> s_valueReplaySources = new();

    private QilNode BuildValueSource(Expr expression)
    {
        bool replay = ValueReplaySource(expression);
        QilNode value = replay && expression is SequenceExpr sequence
            ? _f.Sequence(sequence.Items.Select(item => Items(BuildValueSource(item))).ToArray())
            : replay && _sc.ForwardOnly ? ForwardValuePath(expression) ?? Build(expression) : Build(expression);
        if (replay) s_valueReplaySources.AddOrUpdate(value, GroundedSetBindings.Marker);
        return value;
    }

    // These populations have no deferred predicates or source-dependent metadata
    // consumers. Only their values need to survive an overlapping ancestor value.
    private static bool ValuePath(Expr expression) => expression switch
    {
        AxisStep { Predicates.Count: 0, Axis: Axis.Child or Axis.Self or Axis.Descendant or Axis.DescendantOrSelf or Axis.Attribute } => true,
        RootExpr or ContextItemExpr => true,
        PathExpr { Left: var left, Right: AxisStep { Predicates.Count: 0, Axis: Axis.Child or Axis.Self or Axis.Descendant or Axis.DescendantOrSelf or Axis.Attribute } }
            => ValuePath(left),
        _ => false,
    };

    private static bool HasDescendant(Expr expression) => expression switch
    {
        AxisStep { Axis: Axis.Descendant or Axis.DescendantOrSelf } => true,
        PathExpr path => path.Descendants || HasDescendant(path.Left) || HasDescendant(path.Right),
        _ => false,
    };

    private bool ValueReplaySource(Expr expression) => expression is SequenceExpr sequence
        ? System.Linq.Enumerable.Any(sequence.Items, ValueReplaySource)
            && System.Linq.Enumerable.All(sequence.Items, item => ValuePath(item) || GroundedSetOperand(item))
        : ValuePath(expression) && HasDescendant(expression);

    // A compound value path must attach replay to its final-node cursor. A nested
    // child iterator instead inherits the ancestor cursor, which cannot preserve
    // its selected children when the first overlapping value absorbs that subtree.
    // Match the predicate-free path backwards using immutable ancestor headers;
    // runtime prefix facts are not available on replayed historical headers.
    private QilNode? ForwardValuePath(Expr expression)
    {
        var steps = new List<AxisStep>();
        Expr? anchorExpression = null;
        string contextError = "XPTY0020";
        bool Parse(Expr value)
        {
            if (value is RootExpr or ContextItemExpr) { anchorExpression = value; return true; }
            if (value is AxisStep direct)
            {
                anchorExpression = new ContextItemExpr();
                return Add(direct);
            }
            if (value is not PathExpr { Right: AxisStep step } path || !Parse(path.Left)) return false;
            if (path.Left is ContextItemExpr) contextError = "XPTY0019";
            if (path.Descendants)
            {
                if (step.Axis != Axis.Child) return false;
                step = step with { Axis = Axis.Descendant };
            }
            return Add(step);
        }
        bool Add(AxisStep step)
        {
            if (step.Predicates.Count != 0 || step.Axis is not (Axis.Child or Axis.Self or Axis.Descendant or Axis.DescendantOrSelf)
                || !HeaderTest(step.Test)) return false;
            steps.Add(step); return true;
        }
        if (!Parse(expression) || anchorExpression == null) return null;
        int hoisted = 0;
        while (hoisted < steps.Count && steps[hoisted].Axis is Axis.Child or Axis.Self) hoisted++;
        // Keep the direct descendant case on its existing optimized path.
        if (steps.Count - hoisted < 2) return null;

        QilNode source = Nodes(Build(anchorExpression), contextError);
        QilNode roots = source;
        for (int i = 0; i < hoisted; i++)
        {
            AxisStep step = steps[i];
            var (item, body, binding) = WithFocus(roots, false, () => Build(step));
            roots = WrapLet(binding, _f.Loop(item, body));
        }
        PreserveForwardSequenceProofs(source, roots, disjoint: true);
        steps.RemoveRange(0, hoisted);
        var (anchor, selected, inputBinding) = WithFocus(roots, false, () =>
        {
            QilNode root = FocusNode();
            QilIterator candidate = _f.For(StepFor(Axis.DescendantOrSelf, steps[^1].Test, System.Array.Empty<Expr>()));
            QilNode Match(int index, QilNode node)
            {
                AxisStep step = steps[index];
                QilNode previous;
                if (step.Axis == Axis.Self) previous = index == 0 ? _f.Is(node, root) : Match(index - 1, node);
                else
                {
                    QilNode relatives = step.Axis switch {
                        Axis.Child => _f.Parent(node), Axis.Descendant => _f.Ancestor(node), _ => _f.AncestorOrSelf(node),
                    };
                    QilIterator relative = _f.For(relatives);
                    previous = _f.Not(_f.IsEmpty(_f.Filter(relative,
                        index == 0 ? _f.Is(relative, root) : Match(index - 1, relative))));
                }
                return _f.And(MatchesNode(node, step.Axis, step.Test), previous);
            }
            return _f.Filter(candidate, Match(steps.Count - 1, candidate));
        });
        return _f.TypeAssert(WrapLet(inputBinding, _f.Loop(anchor, selected)), T.NodeSDod);
    }

    private QilNode ValueReplayOperand(QilNode item, QilNode source)
    {
        if (!_sc.ForwardOnly || !s_valueReplaySources.TryGetValue(source, out _)) return item;
        QilNode prepared = StatefulCall(nameof(XsltLibrary.X31ValueOperand), T.Item, item);
        return item.XmlType!.IsSubtypeOf(T.NodeNotRtfS) ? _f.TypeAssert(prepared, item.XmlType) : prepared;
    }
}
