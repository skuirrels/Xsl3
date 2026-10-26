// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Xsl3.Qil;
using Xsl3.Runtime;
using T = Xsl3.XmlQueryTypeFactory;

namespace Xsl3.XPath31;

internal sealed partial class XPath31Compiler
{
    private sealed record ClimbingLeaf(Expr Prefix, IReadOnlyList<AxisStep> Projection);

    private QilNode PotentialClimbingHeaders(QilNode candidate, IReadOnlyList<ClimbingLeaf> leaves)
    {
        QilNode Potential(QilNode node, Axis axis, NodeTest test) => test is NameTest
            ? MatchesNode(node, axis, test) : _f.IsType(node, T.NodeChoice(NodeTestKinds(axis, test)));
        var populations = new List<QilNode>();
        foreach (var leaf in leaves)
        {
            AxisStep ascent = leaf.Projection[0];
            QilIterator owner = _f.For(_f.AncestorOrSelf(candidate));
            QilNode selected = owner;
            if (leaf.Projection.Count > 1)
            {
                AxisStep attribute = leaf.Projection[1];
                var broad = attribute with { Predicates = System.Array.Empty<Expr>(),
                    Test = attribute.Test is NameTest ? attribute.Test : new KindNodeTest(new AnyKindTest()) };
                var (item, body, binding) = WithFocus(owner, false, () => Build(broad));
                selected = WrapLet(binding, _f.Loop(item, body));
            }
            populations.Add(_f.Loop(owner, _f.Conditional(Potential(owner, Axis.AncestorOrSelf, ascent.Test), selected, _f.Sequence())));
        }
        // Only the bounded current ancestor/attribute headers are sorted. Projection
        // predicates run later, at their original matched-prefix call sites.
        return _f.DocOrderDistinct(_f.Sequence(populations.ToArray()));
    }

    private QilNode? ForwardClimbingSet(BinaryExpr expression)
    {
        if (!_sc.ForwardOnly) return null;
        var leaves = new List<ClimbingLeaf>();
        var plan = new List<string>();
        bool deferred = false;
        Expr? Prefixes(Expr value)
        {
            if (value is BinaryExpr binary && SetOperator(binary.Op))
            {
                if (binary.Op == BinaryOp.Except) deferred = true;
                Expr? left = Prefixes(binary.Left), right = Prefixes(binary.Right);
                if (left == null || right == null) return null;
                plan.Add(binary.Op == BinaryOp.Union ? "U" : binary.Op == BinaryOp.Intersect ? "I" : "E");
                return new BinaryExpr(binary.Op, left, right);
            }
            var projection = new List<AxisStep>();
            Expr prefix = value;
            if (prefix is PathExpr { Descendants: false, Right: AxisStep { Axis: Axis.Attribute } attribute } attr)
            {
                if (!HeaderTest(attribute.Test)) return null;
                foreach (Expr predicate in attribute.Predicates)
                    if (!HeaderSetPredicate(predicate, Axis.Attribute, positional: true)) return null;
                projection.Add(attribute); prefix = attr.Left;
            }
            AxisStep ascent;
            if (prefix is PathExpr { Descendants: false, Right: AxisStep { Axis: Axis.Ancestor or Axis.AncestorOrSelf } ancestor } path)
            { ascent = ancestor; prefix = path.Left; }
            else if (prefix is AxisStep { Axis: Axis.Ancestor or Axis.AncestorOrSelf } direct)
            { ascent = direct; prefix = new ContextItemExpr(); }
            else return null;
            if (!HeaderTest(ascent.Test)) return null;
            foreach (Expr predicate in ascent.Predicates)
            {
                if (!HeaderSetPredicate(predicate, ascent.Axis, positional: true)) return null;
                if (!HeaderSetPredicate(predicate, ascent.Axis)) deferred = true;
            }
            projection.Insert(0, ascent);
            plan.Add(leaves.Count.ToString(CultureInfo.InvariantCulture));
            if (leaves.Count != 0 && !projection.Select(step => step.ToString())
                .SequenceEqual(leaves[0].Projection.Select(step => step.ToString()))) deferred = true;
            leaves.Add(new(prefix, projection));
            return prefix;
        }
        Expr? prefixes = Prefixes(expression);
        if (prefixes == null) return null;
        bool SingleSource(Expr value) => value is RootExpr or ContextItemExpr or AxisStep
            || value is PathExpr path && SingleSource(path.Left);
        QilIterator state = _f.Let(deferred
            ? StatefulCall(nameof(XsltLibrary.X31DeferredClimbingSetNew), T.Item, _f.String(string.Join(" ", plan)),
                leaves.All(leaf => SingleSource(leaf.Prefix)) ? _f.True() : _f.False())
            : StatefulCall(nameof(XsltLibrary.X31ClimbingSetNew), T.Item, _f.String(string.Join(" ", plan))));
        QilNode? scan = ForwardHeaderSet(prefixes, (candidate, matches) =>
        {
            var actions = new List<QilIterator>();
            if (deferred) actions.Add(_f.Let(StatefulCall(nameof(XsltLibrary.X31DeferredClimbingSetBegin), T.BooleanX,
                state, candidate, PotentialClimbingHeaders(candidate, leaves))));
            for (int i = 0; i < leaves.Count; i++)
            {
                Expr projection = new ContextItemExpr();
                foreach (AxisStep step in leaves[i].Projection) projection = new PathExpr(projection, step, false);
                var (item, body, binding) = WithFocus(candidate, false, () => Nodes(Build(projection), "XPTY0019"));
                QilNode nodes = WrapLet(binding, _f.Loop(item, body));
                actions.Add(_f.Let(_f.Conditional(matches[i],
                    StatefulCall(deferred ? nameof(XsltLibrary.X31DeferredClimbingSetAdd) : nameof(XsltLibrary.X31ClimbingSetAdd),
                        T.BooleanX, state, _f.Double(i), nodes), _f.False())));
            }
            QilNode result = deferred
                ? StatefulCall(nameof(XsltLibrary.X31DeferredClimbingSetDrain), T.NodeNotRtfS, state, _f.False())
                : StatefulCall(nameof(XsltLibrary.X31ClimbingSetDrain), T.NodeNotRtfS, state);
            if (deferred) Xsl3.IlGen.OptimizerPatterns.Write(result).AddPattern(Xsl3.IlGen.OptimizerPatternName.PullSequence);
            for (int i = actions.Count - 1; i >= 0; i--) result = _f.Loop(actions[i], result);
            return result;
        });
        if (scan == null) return null;
        QilNode completed = deferred
            ? StatefulCall(nameof(XsltLibrary.X31DeferredClimbingSetDrain), T.NodeNotRtfS, state, _f.True())
            : StatefulCall(nameof(XsltLibrary.X31ClimbingSetEnd), T.NodeNotRtfS, state);
        if (deferred) Xsl3.IlGen.OptimizerPatterns.Write(completed).AddPattern(Xsl3.IlGen.OptimizerPatternName.PullSequence);
        return _f.Loop(state, _f.TypeAssert(_f.Sequence(scan, completed), T.NodeSDod));
    }
}
