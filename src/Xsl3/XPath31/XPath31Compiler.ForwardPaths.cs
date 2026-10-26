// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using Xsl3.Qil;
using T = Xsl3.XmlQueryTypeFactory;

namespace Xsl3.XPath31;

internal sealed partial class XPath31Compiler
{
    private HashSet<PathExpr>? _unfusedDescendantPaths;
    private Dictionary<Expr, int>? _descendantPathCounts;
    private bool NeedsHeaderPath(PathExpr path)
    {
        // A filtered child after a descendant selection must not resume each
        // ancestor's iterator after the child's population has advanced the reader.
        // Keep direct descendant selections on their absorption-aware iterator.
        bool Required(int count) => count > 1 || count > 0 && !path.Descendants
            && path.Right is AxisStep { Axis: Axis.Child, Predicates.Count: > 0 };
        _descendantPathCounts ??= new(System.Collections.Generic.ReferenceEqualityComparer.Instance);
        if (_descendantPathCounts.TryGetValue(path, out int cached)) return Required(cached);
        var prefixes = new List<(PathExpr Path, int Added)>();
        Expr value = path;
        while (value is PathExpr { Right: AxisStep step } current && !_descendantPathCounts.ContainsKey(value))
        {
            prefixes.Add((current, current.Descendants || step.Axis is Axis.Descendant or Axis.DescendantOrSelf ? 1 : 0));
            value = current.Left;
        }
        int count = _descendantPathCounts.TryGetValue(value, out cached) ? cached
            : value is AxisStep { Axis: Axis.Descendant or Axis.DescendantOrSelf } ? 1 : 0;
        for (int i = prefixes.Count - 1; i >= 0; i--)
        {
            count = System.Math.Min(2, count + prefixes[i].Added);
            _descendantPathCounts.Add(prefixes[i].Path, count);
        }
        return Required(count);
    }

    private static bool DisjointChildAnchors(Expr expression) => expression switch
    {
        ContextItemExpr or RootExpr or AxisStep { Axis: Axis.Child or Axis.Self } => true,
        PathExpr { Descendants: false, Right: AxisStep { Axis: Axis.Child or Axis.Self }, Left: var left } => DisjointChildAnchors(left),
        _ => false,
    };

    private static bool StableAnchor(Expr expression) => expression switch
    {
        ContextItemExpr or RootExpr or VarRef => true,
        AxisStep { Axis: Axis.Child or Axis.Self, Predicates.Count: 0 } => true,
        PathExpr { Descendants: false, Right: AxisStep { Axis: Axis.Child or Axis.Self, Predicates.Count: 0 }, Left: var left } => StableAnchor(left),
        _ => false,
    };

    // A predicate-free descendant step over disjoint stable anchors is closed
    // under matching descendants of each member. Absorbing callees can use this proof.
    private static AxisStep? ClosedDescendantStep(Expr input) => input switch {
        AxisStep { Axis: Axis.Descendant or Axis.DescendantOrSelf } step => step,
        PathExpr { Right: AxisStep { Axis: Axis.Descendant or Axis.DescendantOrSelf } step, Descendants: false, Left: var anchor }
            when StableAnchor(anchor) && DisjointChildAnchors(anchor) => step,
        PathExpr { Right: AxisStep { Axis: Axis.Child } step, Descendants: true, Left: var anchor }
            when StableAnchor(anchor) && DisjointChildAnchors(anchor) => step,
        _ => null,
    };

    private bool HeaderTest(NodeTest test) => test is not KindNodeTest kind || _types.ResolveKind(kind.Kind).DocumentElement == null;

    private QilNode MatchesNode(QilNode node, Axis principalAxis, NodeTest test) =>
        _f.Not(_f.IsEmpty(ApplyNodeTest(node, principalAxis, test)));

    private bool HeaderSetPredicate(Expr expression, Axis axis, bool positional = false)
    {
        FocusDependency focus = FocusAnalysis.Analyze(expression,
            name => _types.Expand(name, _sc.DefaultFunctionNamespace).NamespaceUri == Xsl3.Xdm.QName.Ns.Fn);
        if ((focus & FocusDependency.Size) != 0 || !positional && (focus & FocusDependency.Position) != 0) return false;
        bool Safe(Expr value)
        {
            if (value is VarRef) return false;
            if (value is AxisStep step && !HeaderTest(step.Test)) return false;
            if (value is FunctionCall call)
            {
                var name = _types.Expand(call.Name, _sc.DefaultFunctionNamespace);
                if (name.NamespaceUri is not (Xsl3.Xdm.QName.Ns.Fn or Xsl3.Xdm.QName.Ns.Xs)
                    && Functions.FunctionLibrary.Find(name.NamespaceUri, name.LocalName, call.Arguments.Count) == null
                    && _types.FindSchemaType(name) == null) return false;
            }
            return System.Linq.Enumerable.All(AllChildren(value), Safe);
        }
        if (!Safe(expression)) return false;
        var analysis = new Streaming.ExpressionAnalysis(new() { Types = _types, DefaultFunctionNamespace = _sc.DefaultFunctionNamespace });
        var value = analysis.Analyze(expression, new(axis == Axis.Attribute ? Streaming.UType.Attribute : Streaming.UType.Nodes, Streaming.Posture.Striding));
        return value.Sweep == Streaming.Sweep.Motionless && (positional || (value.Type & Streaming.UType.Numeric) == 0);
    }

    private QilNode? ForwardSharedSet(BinaryExpr union)
    {
        if (!_sc.ForwardOnly) return null;
        var leaves = new List<(Expr Anchor, Axis Axis, NodeTest Test, IReadOnlyList<Expr> Predicates, string ContextError)>();
        bool Add(Expr expression)
        {
            if (expression is BinaryExpr binary && SetOperator(binary.Op)) return Add(binary.Left) && Add(binary.Right);
            Expr anchor;
            AxisStep step;
            string contextError;
            if (expression is PathExpr { Right: AxisStep selected } path)
            {
                if (path.Descendants && selected.Axis != Axis.Child) return false;
                anchor = path.Left;
                step = path.Descendants ? selected with { Axis = Axis.Descendant } : selected;
                contextError = "XPTY0019";
            }
            else if (expression is AxisStep direct) { anchor = new ContextItemExpr(); step = direct; contextError = "XPTY0020"; }
            else if (expression is ContextItemExpr) { anchor = expression; step = new(Axis.Self, new KindNodeTest(new AnyKindTest()), System.Array.Empty<Expr>()); contextError = "XPTY0004"; }
            else return false;
            if (System.Linq.Enumerable.Any(step.Predicates, predicate => !HeaderSetPredicate(predicate, step.Axis, positional: step.Axis is Axis.Child or Axis.Attribute or Axis.Self)) || step.Axis is not (Axis.Child or Axis.Attribute or Axis.Self or Axis.Descendant or Axis.DescendantOrSelf)
                || !StableAnchor(anchor) || !HeaderTest(step.Test)) return false;
            leaves.Add((anchor, step.Axis, step.Test, step.Predicates, contextError));
            return true;
        }
        if (!Add(union) || leaves.Count == 0) return null;
        Expr common = leaves[0].Anchor;
        string identity = common.ToString();
        if (leaves.Exists(leaf => leaf.Anchor.ToString() != identity)) return null;
        bool attributes = leaves[0].Axis == Axis.Attribute;
        if (leaves.Exists(leaf => (leaf.Axis == Axis.Attribute) != attributes)) return null;
        Axis scan = attributes ? Axis.Attribute
            : leaves.TrueForAll(leaf => leaf.Axis == Axis.Self) ? Axis.Self
            : leaves.Exists(leaf => leaf.Axis is Axis.Self or Axis.DescendantOrSelf) ? Axis.DescendantOrSelf
            : leaves.Exists(leaf => leaf.Axis == Axis.Descendant) ? Axis.Descendant : Axis.Child;
        QilNode source = Nodes(Build(common), leaves[0].ContextError);
        var (anchor, body, binding) = WithFocus(source, false, () =>
        {
            QilNode root = FocusNode();
            bool positions = leaves.Exists(leaf => System.Linq.Enumerable.Any(leaf.Predicates, predicate => !HeaderSetPredicate(predicate, leaf.Axis)));
            QilIterator? state = positions ? _f.Let(StatefulCall(nameof(Xsl3.Runtime.XsltLibrary.X31NodeSetMatchNew), T.Item)) : null;
            int counterSlot = 0;
            QilIterator candidate = _f.For(HeaderSetScan(scan, System.Linq.Enumerable.Select(leaves, leaf => (leaf.Axis, leaf.Test)),
                scan == Axis.DescendantOrSelf && leaves.TrueForAll(leaf => leaf.Axis is Axis.Child or Axis.Self) ? 1 : null));
            var matches = new List<QilIterator>();
            foreach (var leaf in leaves)
            {
                QilNode match = MatchesNode(candidate, leaf.Axis, leaf.Test);
                if (leaf.Axis == Axis.Self) match = _f.And(_f.Is(candidate, root), match);
                else if (leaf.Axis == Axis.Descendant && scan == Axis.DescendantOrSelf)
                    match = _f.And(_f.Not(_f.Is(candidate, root)), match);
                else if (leaf.Axis == Axis.Child && scan != Axis.Child)
                {
                    QilIterator parent = _f.For(_f.Parent(candidate));
                    match = _f.And(_f.Not(_f.IsEmpty(_f.Filter(parent, _f.Is(parent, root)))), match);
                }
                match = HeaderPredicates(match, candidate, leaf.Axis, leaf.Predicates, state,
                    leaf.Axis is Axis.Child or Axis.Attribute, root, ref counterSlot);
                Xsl3.IlGen.OptimizerPatterns.Write(match).AddPattern(Xsl3.IlGen.OptimizerPatternName.MaybeSideEffects);
                matches.Add(_f.Let(match));
            }
            int index = 0;
            QilNode Combine(Expr expression)
            {
                if (expression is not BinaryExpr binary || !SetOperator(binary.Op)) return matches[index++];
                QilNode left = Combine(binary.Left), right = Combine(binary.Right);
                return binary.Op switch {
                    BinaryOp.Union => _f.Or(left, right),
                    BinaryOp.Intersect => _f.And(left, right),
                    _ => _f.And(left, _f.Not(right)),
                };
            }
            QilNode selectedResult = _f.Conditional(Combine(union), candidate, _f.Sequence());
            for (int i = matches.Count - 1; i >= 0; i--) selectedResult = _f.Loop(matches[i], selectedResult);
            return WrapLet(state, _f.Loop(candidate, selectedResult));
        });
        QilNode result = WrapLet(binding, _f.Loop(anchor, body));
        bool disjoint = scan is Axis.Child or Axis.Self;
        if (DisjointChildAnchors(common) || source.XmlType!.IsSingleton || s_sameDepth.TryGetValue(source, out _))
            return disjoint ? SameDepth(_f.TypeAssert(result, T.NodeSDod)) : _f.TypeAssert(result, T.NodeSDod);
        QilNode ordered = s_orderedChildren.TryGetValue(source, out QilNode? proof) ? new QilCloneVisitor(_qf).Clone(proof) : _f.False();
        QilNode canonical = CanonicalSetPopulation(result, ordered);
        if (disjoint) s_orderedChildren.AddOrUpdate(canonical, ordered);
        return canonical;
    }

    private QilNode? ForwardDescendantChildren(PathExpr path)
    {
        if (!_sc.ForwardOnly || _unfusedDescendantPaths?.Contains(path) == true) return null;
        var steps = new List<AxisStep>();
        var paths = new List<PathExpr>();
        QilNode? Unfused()
        {
            // A child-only chain that cannot use this rewrite must not be rescanned
            // at each recursive prefix. Keep the negative shape analysis linear.
            _unfusedDescendantPaths ??= new(System.Collections.Generic.ReferenceEqualityComparer.Instance);
            _unfusedDescendantPaths.Add(path);
            foreach (PathExpr prefix in paths) _unfusedDescendantPaths.Add(prefix);
            return null;
        }
        Expr remaining = path;
        while (remaining is PathExpr { Descendants: false, Right: AxisStep { Axis: Axis.Child, Predicates.Count: 0 } child } childPath)
        {
            paths.Add(childPath);
            if (!HeaderTest(child.Test)) return Unfused();
            steps.Add(child);
            remaining = childPath.Left;
        }
        if (steps.Count == 0) return Unfused();
        Expr sourceExpression;
        AxisStep first;
        string contextError;
        if (remaining is PathExpr { Right: AxisStep { Predicates.Count: 0 } selected } descendantPath
            && (descendantPath.Descendants && selected.Axis == Axis.Child || !descendantPath.Descendants && selected.Axis is Axis.Descendant or Axis.DescendantOrSelf))
        {
            sourceExpression = descendantPath.Left;
            first = descendantPath.Descendants ? selected with { Axis = Axis.Descendant } : selected;
            contextError = "XPTY0019";
        }
        else if (remaining is AxisStep { Axis: Axis.Descendant or Axis.DescendantOrSelf, Predicates.Count: 0 } direct)
        { sourceExpression = new ContextItemExpr(); first = direct; contextError = "XPTY0020"; }
        else return Unfused();
        if (!DisjointChildAnchors(sourceExpression) || !HeaderTest(first.Test)) return Unfused();
        steps.Add(first);
        var (anchor, body, binding) = WithFocus(Nodes(Build(sourceExpression), contextError), false, () =>
        {
            QilNode root = FocusNode();
            QilIterator candidate = _f.For(StepFor(Axis.Descendant, steps[0].Test, System.Array.Empty<Expr>()));
            QilNode MatchParents(int index, QilNode node)
            {
                if (index == steps.Count) return _f.True();
                QilIterator parent = _f.For(_f.Parent(node));
                QilNode test = MatchesNode(parent, index == steps.Count - 1 ? first.Axis : Axis.Child, steps[index].Test);
                if (index == steps.Count - 1 && first.Axis == Axis.Descendant)
                    test = _f.And(_f.Not(_f.Is(parent, root)), test);
                test = _f.And(test, MatchParents(index + 1, parent));
                return _f.And(_f.Not(_f.Is(node, root)), _f.Not(_f.IsEmpty(_f.Filter(parent, test))));
            }
            return _f.Filter(candidate, MatchParents(1, candidate));
        });
        return WrapLet(binding, _f.Loop(anchor, body));
    }
}
