// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Xsl3.Qil;
using Xsl3.Runtime;
using Xsl3.Xdm;
using Xsl3.XPath31.Functions;
using T = Xsl3.XmlQueryTypeFactory;

namespace Xsl3.XPath31;

internal sealed partial class XPath31Compiler
{
    private static class GroundedSetBindings
    {
        internal static readonly ConditionalWeakTable<QilNode, object> Values = new();
        internal static readonly object Marker = new();
    }
    private HashSet<PathExpr>? _unfusedSetPaths;
    private static bool SetOperator(BinaryOp operation) => operation is BinaryOp.Union or BinaryOp.Intersect or BinaryOp.Except;

    private bool GroundedSetOperand(Expr expression)
    {
        switch (expression)
        {
            case EmptySequenceExpr or IntegerLiteral or DecimalLiteral or DoubleLiteral or StringLiteral: return true;
            case VarRef variable:
                QName name = _types.Expand(variable.Name, "");
                for (int i = _scope.Count - 1; i >= 0; i--)
                    if (_scope[i].Name == name) return GroundedSetBindings.Values.TryGetValue(_scope[i].Value, out _);
                return _env?.IsGroundedVariable(name) == true;
            case SequenceExpr sequence: return sequence.Items.All(GroundedSetOperand);
            case FilterExpr filter: return GroundedSetOperand(filter.Base);
            case PathExpr { Right: AxisStep, Left: var left }: return GroundedSetOperand(left);
            case BinaryExpr binary when SetOperator(binary.Op): return GroundedSetOperand(binary.Left) && GroundedSetOperand(binary.Right);
            case FunctionCall call:
                QName function = _types.Expand(call.Name, _sc.DefaultFunctionNamespace);
                if (function.NamespaceUri != QName.Ns.Fn) return false;
                if (function.LocalName is "copy-of" or "snapshot" or "doc" or "document") return true;
                if (function.LocalName is "trace" or "reverse" or "head" or "tail" or "subsequence" or "remove"
                    or "unordered" or "exactly-one" or "zero-or-one" or "one-or-more")
                    return call.Arguments.Count > 0 && GroundedSetOperand(call.Arguments[0]);
                if (function.LocalName == "insert-before" && call.Arguments.Count == 3)
                    return GroundedSetOperand(call.Arguments[0]) && GroundedSetOperand(call.Arguments[2]);
                return FunctionLibrary.Find(function.NamespaceUri, function.LocalName, call.Arguments.Count)?.ReturnType.Item
                    is AtomicTypeTest or NumericTest or UnionTypeTest or FunctionItemTest or MapTest or ArrayTest;
            default: return false;
        }
    }

    private static bool SyntacticNodeOrder(Expr expression) => expression switch
    {
        RootExpr or ContextItemExpr or PathExpr or AxisStep { Axis: not Axis.Namespace } => true,
        FilterExpr filter => SyntacticNodeOrder(filter.Base),
        _ => false,
    };

    private QilNode SetInputOrder(Expr expression, QilNode value)
    {
        if (!value.XmlType!.MaybeMany || value.XmlType.IsDod || s_sameDepth.TryGetValue(value, out _) || SyntacticNodeOrder(expression)) return _f.True();
        return s_orderedChildren.TryGetValue(value, out var ordered) ? new QilCloneVisitor(_qf).Clone(ordered) : _f.False();
    }

    private QilNode? ForwardIndependentSet(BinaryExpr expression, IReadOnlyList<AxisStep>? children = null)
    {
        if (!_sc.ForwardOnly || _env == null) return null;
        bool leftGrounded = GroundedSetOperand(expression.Left), rightGrounded = GroundedSetOperand(expression.Right);
        if (!leftGrounded && !rightGrounded) return null;
        QilNode? leftSource = null;
        QilNode? Input(Expr operand, bool grounded, bool left)
        {
            QilNode population, ordered;
            Expr full = operand;
            if (children != null)
                for (int i = children.Count - 1; i >= 0; i--) full = new PathExpr(full, children[i], false);
            if (children != null && operand is BinaryExpr binary && SetOperator(binary.Op))
            {
                population = Build(full);
                ordered = _f.True();
            }
            else if (children != null && operand is AxisStep or PathExpr && ForwardDescendantChildren((PathExpr)full) is { } descendants)
            {
                population = descendants;
                ordered = _f.True();
            }
            else
            {
                QilNode source = Build(operand);
                if (left) leftSource = source;
                ordered = children == null || !source.XmlType!.MaybeMany || s_sameDepth.TryGetValue(source, out _) || DisjointChildAnchors(operand)
                    ? SetInputOrder(operand, source)
                    : s_orderedChildren.TryGetValue(source, out var proof) ? new QilCloneVisitor(_qf).Clone(proof) : _f.False();
                population = source;
                if (children != null)
                {
                    if (!source.XmlType!.IsSubtypeOf(T.NodeNotRtfS))
                    {
                        QilIterator item = _f.For(Items(source));
                        population = _f.Loop(item, StatefulCall(nameof(XsltLibrary.X31NodeItem), T.NodeNotRtf, item, _f.String("XPTY0004")));
                    }
                    for (int i = children.Count - 1; i >= 0; i--)
                    {
                        AxisStep step = children[i];
                        var (item, body, binding) = WithFocus(population, false, () => Build(step));
                        population = WrapLet(binding, _f.Loop(item, body));
                    }
                }
            }
            QilNode producer = population.XmlType!.IsEmpty ? _f.Sequence() : _env.LiftPopulation(Items(population))!;
            if (producer == null) return null;
            return StatefulCall(nameof(XsltLibrary.X31NodeSetInputNew), T.Item, Items(producer), grounded ? _f.False() : ordered);
        }
        QilNode? left = Input(expression.Left, leftGrounded, true), right = Input(expression.Right, rightGrounded, false);
        if (left == null || right == null) return null;
        QilIterator state = _f.Let(StatefulCall(nameof(XsltLibrary.X31NodeSetNew), T.Item, left, right,
            _f.Double(expression.Op - BinaryOp.Union), _f.Boolean(leftGrounded), _f.Boolean(rightGrounded)));
        QilNode next = StatefulCall(nameof(XsltLibrary.X31NodeSetNext), T.NodeSDod, state);
        Xsl3.IlGen.OptimizerPatterns.Write(next).AddPattern(Xsl3.IlGen.OptimizerPatternName.PullSequence);
        QilNode result = _f.Loop(state, next);
        return children == null && expression.Op != BinaryOp.Union && leftSource != null
            ? PreserveForwardSequenceProofs(leftSource, result, !leftSource.XmlType!.MaybeMany || DisjointChildAnchors(expression.Left)) : result;
    }

    private QilNode? ForwardSetPath(PathExpr path)
    {
        if (!_sc.ForwardOnly || _env == null || _unfusedSetPaths?.Contains(path) == true) return null;
        var steps = new List<AxisStep>(); var prefixes = new List<PathExpr>();
        QilNode? Unfused()
        {
            _unfusedSetPaths ??= new(System.Collections.Generic.ReferenceEqualityComparer.Instance);
            _unfusedSetPaths.Add(path);
            foreach (var prefix in prefixes) _unfusedSetPaths.Add(prefix);
            return null;
        }
        Expr remaining = path;
        while (remaining is PathExpr { Descendants: false, Right: AxisStep { Axis: Axis.Child, Predicates.Count: 0 } step } prefix)
        {
            prefixes.Add(prefix);
            if (!HeaderTest(step.Test)) return Unfused();
            steps.Add(step); remaining = prefix.Left;
        }
        if (steps.Count == 0 || remaining is not BinaryExpr set || !SetOperator(set.Op)) return Unfused();
        if (ForwardIndependentSet(set, steps) is { } independent) return independent;
        Expr Distribute(Expr value)
        {
            if (value is BinaryExpr binary && SetOperator(binary.Op)) return new BinaryExpr(binary.Op, Distribute(binary.Left), Distribute(binary.Right));
            for (int i = steps.Count - 1; i >= 0; i--) value = new PathExpr(value, steps[i], false);
            return value;
        }
        var distributed = (BinaryExpr)Distribute(set);
        return ForwardSharedSet(distributed) ?? ForwardHeaderSet(distributed) ?? Unfused();
    }
}
