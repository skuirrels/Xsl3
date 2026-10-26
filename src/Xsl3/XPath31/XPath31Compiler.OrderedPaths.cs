// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using Xsl3.Qil;
using Xsl3.Xdm;
using T = Xsl3.XmlQueryTypeFactory;

namespace Xsl3.XPath31;

internal sealed partial class XPath31Compiler
{
    private HashSet<PathExpr>? _unfusedSequencePaths;

    private QilNode? ForwardSequencePath(PathExpr path)
    {
        if (!_sc.ForwardOnly || _env == null || _unfusedSequencePaths?.Contains(path) == true) return null;
        var steps = new List<AxisStep>();
        var paths = new List<PathExpr>();
        QilNode? Unfused()
        {
            _unfusedSequencePaths ??= new(System.Collections.Generic.ReferenceEqualityComparer.Instance);
            _unfusedSequencePaths.Add(path);
            foreach (PathExpr prefix in paths) _unfusedSequencePaths.Add(prefix);
            return null;
        }
        Expr remaining = path;
        while (remaining is PathExpr { Descendants: false, Right: AxisStep { Axis: Axis.Child, Predicates.Count: 0 } step } prefix)
        {
            paths.Add(prefix);
            if (!HeaderTest(step.Test)) return Unfused();
            steps.Add(step);
            remaining = prefix.Left;
        }
        IReadOnlyList<Expr>? InsertionArguments(Expr expression)
        {
            if (expression is not FunctionCall { Arguments.Count: 3 } insertion
                || insertion.Arguments[0] is ArgumentPlaceholder || insertion.Arguments[1] is ArgumentPlaceholder || insertion.Arguments[2] is ArgumentPlaceholder)
                return null;
            QName name = _types.Expand(insertion.Name, _sc.DefaultFunctionNamespace);
            return name.NamespaceUri == QName.Ns.Fn && name.LocalName == "insert-before" ? CompatArguments(name, insertion.Arguments) : null;
        }
        if (steps.Count == 0 || remaining is not SequenceExpr && InsertionArguments(remaining) == null) return Unfused();
        var leaves = new List<Expr>();
        var positions = new List<Expr>();
        void Expand(Expr expression)
        {
            if (expression is SequenceExpr sequence)
            {
                foreach (Expr item in sequence.Items) Expand(item);
            }
            else if (InsertionArguments(expression) is { } arguments)
            {
                positions.Add(arguments[1]);
                Expand(arguments[0]);
                Expand(arguments[2]);
            }
            else leaves.Add(expression);
        }
        Expand(remaining);
        QilList inputs = _qf.Sequence();
        foreach (Expr anchors in leaves)
        {
            Expr fullPath = anchors;
            for (int i = steps.Count - 1; i >= 0; i--) fullPath = new PathExpr(fullPath, steps[i], false);
            QilNode population;
            QilNode ordered;
            if (ForwardDescendantChildren((PathExpr)fullPath) is { } descendants)
            {
                population = descendants;
                ordered = _f.True();
            }
            else
            {
                QilNode source = Build(anchors);
                ordered = DisjointChildAnchors(anchors) || source.XmlType!.IsSingleton || s_sameDepth.TryGetValue(source, out _)
                    ? _f.True() : s_orderedChildren.TryGetValue(source, out QilNode? proof) ? new QilCloneVisitor(_qf).Clone(proof) : _f.False();
                population = source;
                if (!source.XmlType!.IsSubtypeOf(T.NodeNotRtfS))
                {
                    QilIterator item = _f.For(Items(source));
                    population = _f.Loop(item, Call(nameof(Xsl3.Runtime.XsltLibrary.X31NodeItem), T.NodeNotRtf, item, _f.String("XPTY0019")));
                }
                for (int i = steps.Count - 1; i >= 0; i--)
                {
                    AxisStep step = steps[i];
                    var (item, body, binding) = WithFocus(population, false, () => Build(step));
                    population = WrapLet(binding, _f.Loop(item, body));
                }
            }
            QilNode? producer = _env.LiftPopulation(population);
            if (producer == null) return Unfused();
            inputs.Add(StatefulCall(nameof(Xsl3.Runtime.XsltLibrary.X31NodeMergeInputNew), T.Item, producer, ordered));
        }
        QilIterator state = _f.Let(StatefulCall(nameof(Xsl3.Runtime.XsltLibrary.X31NodeMergeNew), T.Item, inputs));
        QilNode next = StatefulCall(nameof(Xsl3.Runtime.XsltLibrary.X31NodeMergeNext), T.NodeNotRtfS, state);
        Xsl3.IlGen.OptimizerPatterns.Write(next).AddPattern(Xsl3.IlGen.OptimizerPatternName.PullSequence);
        // After a node-only child path, insertion position affects argument validation, but
        // not the canonical set of result nodes. Keep validation ahead of the merged cursor.
        QilList result = _qf.Sequence();
        foreach (Expr position in positions)
            result.Add(StatefulCall(nameof(Xsl3.Runtime.XsltLibrary.X31InsertValidate), T.NodeNotRtfS, AtomicOperand(position)));
        result.Add(_f.Loop(state, next));
        // A child path canonicalizes the sequence by node identity and document
        // order, independently of its original comma/insertion ordering. Preserve
        // that contract for subsequent attribute paths without another eager sort.
        return _f.TypeAssert(result, T.NodeSDod);
    }
}
