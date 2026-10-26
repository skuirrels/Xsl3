// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Linq;
using Xsl3.Qil;
using Xsl3.Runtime;
using T = Xsl3.XmlQueryTypeFactory;

namespace Xsl3.XPath31;

internal sealed partial class XPath31Compiler
{
    // Do not register a cursor for kinds that no path prefix can select. Consuming an
    // element value may absorb text; that text is irrelevant to an element-only scan.
    private QilNode HeaderSetScan(Axis axis, IEnumerable<(Axis Axis, NodeTest Test)> tests, int? maximumDepth = null)
    {
        XmlNodeKindFlags kinds = 0;
        foreach (var test in tests) kinds |= NodeTestKinds(test.Axis, test.Test);
        kinds &= AxisKinds(axis);
        QilNode context = FocusNode();
        QilNode nodes = axis switch {
            Axis.Attribute or Axis.Child => _f.Content(context), Axis.Self => context,
            Axis.Descendant => _f.Descendant(context), _ => _f.DescendantOrSelf(context),
        };
        if (maximumDepth is { } maximum)
        {
            var patterns = Xsl3.IlGen.OptimizerPatterns.Write(nodes);
            patterns.AddPattern(Xsl3.IlGen.OptimizerPatternName.DepthLimit);
            patterns.AddArgument(Xsl3.IlGen.OptimizerPatternArgument.MaximumDepth, maximum);
        }
        QilIterator item = _f.For(nodes);
        return _f.Filter(item, _f.IsType(item, T.NodeChoice(kinds)));
    }

    private QilNode HeaderPredicates(QilNode condition, QilNode node, Axis axis, IReadOnlyList<Expr> predicates,
        QilNode? state, bool siblings, QilNode root, ref int counterSlot)
    {
        foreach (Expr predicate in predicates)
        {
            if (HeaderSetPredicate(predicate, axis))
            {
                var (focus, selected, binding) = WithFocus(node, false, () => Bool(Build(predicate)));
                condition = _f.And(condition, WrapLet(binding, _f.Loop(focus, selected)));
                continue;
            }
            QilNode owner = siblings ? _f.TypeAssert(_f.Parent(node), T.NodeNotRtf) : root;
            QilIterator position = _f.Let(axis == Axis.Self ? Constant(Xsl3.Xdm.AtomicValue.OfInteger(1))
                : StatefulCall(nameof(XsltLibrary.X31NodeSetPositionNext), T.Item, state!, owner, _f.Double(counterSlot++)));
            Focus? saved = _focus;
            _focus = new(node, () => Call(nameof(XsltLibrary.X31CapturedNumber), T.DoubleX, Items(position)), null, position);
            QilNode accepted;
            try { accepted = Call(nameof(XsltLibrary.X31PredicateAt), T.BooleanX, Items(Build(predicate)), position); }
            finally { _focus = saved; }
            condition = _f.And(condition, _f.Loop(position, accepted));
        }
        return condition;
    }

    private sealed record HeaderStep(AxisStep Step, bool SiblingPositions)
    {
        internal Axis Axis => Step.Axis;
        internal NodeTest Test => Step.Test;
        internal IReadOnlyList<Expr> Predicates => Step.Predicates;
    }
    private sealed record HeaderPath(Expr Anchor, List<HeaderStep> Steps, string ContextError);

    private HashSet<Expr>? _invalidHeaderPaths;

    private HeaderPath? ParseHeaderPath(Expr expression)
    {
        HeaderPath? Invalid(Expr value)
        {
            (_invalidHeaderPaths ??= new(System.Collections.Generic.ReferenceEqualityComparer.Instance)).Add(value);
            return null;
        }
        HeaderPath? Parse(Expr value)
        {
            if (_invalidHeaderPaths?.Contains(value) == true) return null;
            if (value is RootExpr or VarRef or ContextItemExpr) return new(value, new(), "XPTY0004");
            if (value is AxisStep direct)
                return Allowed(direct) ? new(new ContextItemExpr(), new() { new(direct, direct.Axis == Axis.Child) }, "XPTY0020") : Invalid(value);
            if (value is not PathExpr { Right: AxisStep step } path || !Allowed(step)) return Invalid(value);
            HeaderPath? prefix = Parse(path.Left);
            if (prefix == null) return Invalid(value);
            bool siblings = step.Axis == Axis.Child;
            if (path.Descendants)
            {
                if (step.Axis != Axis.Child) return Invalid(value);
                step = step with { Axis = Axis.Descendant };
            }
            prefix.Steps.Add(new(step, siblings));
            return prefix with { ContextError = path.Left is ContextItemExpr or VarRef ? "XPTY0019" : prefix.ContextError };
        }
        bool Allowed(AxisStep step) => step.Axis is Axis.Child or Axis.Self or Axis.Descendant or Axis.DescendantOrSelf
            && HeaderTest(step.Test) && step.Predicates.All(predicate => HeaderSetPredicate(predicate, step.Axis, positional: step.Axis is Axis.Child or Axis.Self));
        return Parse(expression);
    }

    private (List<QilIterator> Matches, int CounterSlot) HeaderPathMatches(HeaderPath path,
        QilNode candidate, QilNode root, QilNode state, int firstSlot, int counterSlot)
    {
        QilNode Match(int index, QilNode node)
        {
            AxisStep step = path.Steps[index].Step;
            // Every earlier header, and every earlier self prefix on this header,
            // already has its facts. Read those facts instead of recursively
            // emitting all preceding paths again for every prefix.
            QilNode Previous(QilNode parent) => index == 0 ? _f.Is(parent, root)
                : _f.Eq(StatefulCall(nameof(XsltLibrary.X31NodeSetMatchGet), T.IntX,
                    state, parent, _f.Double(firstSlot + index - 1)), _f.Int32(1));
            QilNode previous;
            if (step.Axis == Axis.Self) previous = Previous(node);
            else
            {
                QilNode parents = step.Axis switch {
                    Axis.Child => _f.Parent(node), Axis.Descendant => _f.Ancestor(node), _ => _f.AncestorOrSelf(node),
                };
                QilIterator parent = _f.For(parents);
                previous = _f.Not(_f.IsEmpty(_f.Filter(parent, Previous(parent))));
            }
            QilNode condition = _f.And(previous, MatchesNode(node, step.Axis, step.Test));
            condition = HeaderPredicates(condition, node, step.Axis, step.Predicates, state,
                path.Steps[index].SiblingPositions, root, ref counterSlot);
            return StatefulCall(nameof(XsltLibrary.X31NodeSetMatchPut), T.BooleanX,
                state, node, _f.Double(firstSlot + index), condition);
        }
        var matches = new List<QilIterator>();
        for (int i = 0; i < path.Steps.Count; i++) matches.Add(_f.Let(Match(i, candidate)));
        return (matches, counterSlot);
    }

    private QilNode? ForwardHeaderSet(Expr expression,
        System.Func<QilNode, IReadOnlyList<QilNode>, QilNode>? projection = null)
    {
        if (!_sc.ForwardOnly) return null;
        var paths = new List<HeaderPath>();
        bool Add(Expr value)
        {
            if (value is BinaryExpr binary && SetOperator(binary.Op)) return Add(binary.Left) && Add(binary.Right);
            HeaderPath? path = ParseHeaderPath(value);
            if (path == null) return false;
            if (path.Steps.Count == 0) path.Steps.Add(new(new AxisStep(Axis.Self, new KindNodeTest(new AnyKindTest()), System.Array.Empty<Expr>()), false));
            paths.Add(path); return true;
        }
        if (!Add(expression) || paths.Count == 0) return null;
        Expr common = paths[0].Anchor;
        if (paths.Any(path => path.Anchor.ToString() != common.ToString())) return null;

        // Hoist only predicate-free peer prefixes. Header predicates retain their own
        // call sites and are evaluated once when each prefix node is encountered.
        int hoisted = 0;
        while (paths.All(path => path.Steps.Count > hoisted && path.Steps[hoisted] is { Axis: Axis.Child or Axis.Self, Predicates.Count: 0 })
            && paths.All(path => path.Steps[hoisted].ToString() == paths[0].Steps[hoisted].ToString())) hoisted++;
        QilNode source = Nodes(Build(common), paths[0].ContextError);
        if (projection != null && !DisjointChildAnchors(common) && source.XmlType!.MaybeMany
            && !s_sameDepth.TryGetValue(source, out _)) return null;
        QilNode roots = source;
        for (int i = 0; i < hoisted; i++)
        {
            AxisStep step = paths[0].Steps[i].Step;
            var (item, body, binding) = WithFocus(roots, false, () => Build(step));
            roots = WrapLet(binding, _f.Loop(item, body));
        }
        PreserveForwardSequenceProofs(source, roots, DisjointChildAnchors(common));
        foreach (HeaderPath path in paths) path.Steps.RemoveRange(0, hoisted);
        foreach (HeaderPath path in paths)
            if (path.Steps.Count == 0) path.Steps.Add(new(new AxisStep(Axis.Self, new KindNodeTest(new AnyKindTest()), System.Array.Empty<Expr>()), false));

        var (anchor, bodyResult, inputBinding) = WithFocus(roots, false, () =>
        {
            QilNode root = FocusNode();
            QilIterator state = _f.Let(StatefulCall(nameof(XsltLibrary.X31NodeSetMatchNew), T.Item));
            QilIterator candidate = _f.For(HeaderSetScan(Axis.DescendantOrSelf, paths.SelectMany(path => path.Steps).Select(step => (step.Axis, step.Test)),
                paths.All(path => path.Steps.All(step => step.Axis is Axis.Child or Axis.Self))
                    ? paths.Max(path => path.Steps.Count(step => step.Axis == Axis.Child)) : null));
            int offset = 0, counterSlot = 0;
            var results = new List<QilNode>(); var evaluations = new List<QilIterator>();
            foreach (HeaderPath path in paths)
            {
                int firstSlot = offset; offset += path.Steps.Count;
                var evaluated = HeaderPathMatches(path, candidate, root, state, firstSlot, counterSlot);
                counterSlot = evaluated.CounterSlot;
                evaluations.AddRange(evaluated.Matches);
                results.Add(evaluated.Matches[^1]);
            }
            int leaf = 0;
            QilNode Combine(Expr value)
            {
                if (value is not BinaryExpr binary || !SetOperator(binary.Op)) return results[leaf++];
                QilNode left = Combine(binary.Left), right = Combine(binary.Right);
                return binary.Op switch { BinaryOp.Union => _f.Or(left, right), BinaryOp.Intersect => _f.And(left, right), _ => _f.And(left, _f.Not(right)) };
            }
            QilNode selectedResult = projection != null ? projection(candidate, results)
                : _f.Conditional(Combine(expression), candidate, _f.Sequence());
            for (int i = evaluations.Count - 1; i >= 0; i--) selectedResult = _f.Loop(evaluations[i], selectedResult);
            return _f.Loop(state, _f.Loop(candidate, selectedResult));
        });
        QilNode result = WrapLet(inputBinding, _f.Loop(anchor, bodyResult));
        if (projection != null) return _f.TypeAssert(result, T.NodeSDod);
        // Only a fixed peer depth proves disjoint output. Mixed depths remain crawling.
        int leafIndex = 0;
        List<int> Depths(Expr value)
        {
            if (value is not BinaryExpr binary || !SetOperator(binary.Op))
            {
                var path = paths[leafIndex++];
                return path.Steps.Any(step => step.Axis is Axis.Descendant or Axis.DescendantOrSelf) ? new() { -1 }
                    : new() { path.Steps.Count(step => step.Axis == Axis.Child) };
            }
            var left = Depths(binary.Left); var right = Depths(binary.Right);
            if (binary.Op == BinaryOp.Union) left.AddRange(right);
            return left;
        }
        var depths = Depths(expression);
        bool disjoint = depths[0] >= 0 && depths.All(depth => depth == depths[0]);
        if (DisjointChildAnchors(common) || source.XmlType!.IsSingleton || s_sameDepth.TryGetValue(source, out _))
        {
            result = _f.TypeAssert(result, T.NodeSDod);
            return disjoint ? SameDepth(result) : result;
        }
        QilNode ordered = s_orderedChildren.TryGetValue(source, out var proof) ? new QilCloneVisitor(_qf).Clone(proof) : _f.False();
        QilNode canonical = CanonicalSetPopulation(result, ordered);
        if (disjoint) s_orderedChildren.AddOrUpdate(canonical, ordered);
        return canonical;
    }

    private QilNode CanonicalSetPopulation(QilNode population, QilNode ordered)
    {
        if (population.XmlType!.IsEmpty) return _f.TypeAssert(population, T.NodeSDod);
        if (_env?.LiftPopulation(Items(population)) is not { } producer) return _f.DocOrderDistinct(population);
        QilNode left = StatefulCall(nameof(XsltLibrary.X31NodeSetInputNew), T.Item, Items(producer), ordered);
        QilNode right = StatefulCall(nameof(XsltLibrary.X31NodeSetInputNew), T.Item, _f.Sequence(), _f.False());
        QilIterator state = _f.Let(StatefulCall(nameof(XsltLibrary.X31NodeSetNew), T.Item, left, right, _f.Double(0), _f.False(), _f.True()));
        QilNode next = StatefulCall(nameof(XsltLibrary.X31NodeSetNext), T.NodeSDod, state);
        Xsl3.IlGen.OptimizerPatterns.Write(next).AddPattern(Xsl3.IlGen.OptimizerPatternName.PullSequence);
        return _f.Loop(state, next);
    }
}
