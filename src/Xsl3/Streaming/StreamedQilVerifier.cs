// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xsl3.Qil;
using Xsl3.IlGen;

namespace Xsl3.Streaming;

/// <summary>
/// Checks the conservative optimizer's contract, independently of its enabled rewrite table.
/// This is a preservation check, not a substitute for source streamability analysis or runtime position checks.
/// </summary>
internal sealed class StreamedQilVerifier
{
    private readonly record struct Region(QilNode Owner, QilNodeType Kind, int Part);
    private sealed record Read(QilNode Operation, QilNodeType Kind, MethodInfo? Method, bool Topmost, bool LoopGate, bool PullSequence, Region[] Regions);
    private readonly List<Read> _before;
    private readonly Dictionary<QilFunction, int> _pullFunctions;

    private StreamedQilVerifier(QilExpression expression)
    {
        _before = Scan.Reads(expression);
        _pullFunctions = expression.FunctionList.OfType<QilFunction>().Where(function => function.PullArgument >= 0)
            .ToDictionary(function => function, function => function.PullArgument);
    }

    internal static StreamedQilVerifier Capture(QilExpression expression) => new(expression);

    internal void Verify(QilExpression expression)
    {
        foreach (QilFunction function in expression.FunctionList)
            if (_pullFunctions.TryGetValue(function, out int argument) && function.PullArgument != argument)
                throw Failure(function, "changed a population's continuation argument");
        int next = 0;
        foreach (Read read in Scan.Reads(expression))
        {
            // Elimination is safe for this contract. Insertion, duplication and reordering are not.
            while (next < _before.Count && !ReferenceEquals(_before[next].Operation, read.Operation)) next++;
            if (next == _before.Count)
                throw Failure(read.Operation, "introduced, duplicated or reordered an operation that can read or buffer streamed input");
            Read before = _before[next++];
            if (before.Kind != read.Kind || before.Method != read.Method)
                throw Failure(read.Operation, "changed the kind of a read operation");
            if (before.Topmost != read.Topmost)
                throw Failure(read.Operation, "changed the subtree-pruning policy of a read operation");
            if (before.LoopGate != read.LoopGate)
                throw Failure(read.Operation, "changed the before-next loop condition policy");
            if (before.PullSequence != read.PullSequence)
                throw Failure(read.Operation, "changed a runtime pull operation into a cached sequence");
            if (!before.Regions.SequenceEqual(read.Regions))
                throw Failure(read.Operation, "moved a read operation into a different function, binding, loop or branch "
                    + $"({read.Method?.Name}; before: {DescribeRegions(before.Regions)}; after: {DescribeRegions(read.Regions)})");
        }
    }

    private static string DescribeRegions(Region[] regions) => string.Join(", ", regions.Select(r => $"{r.Kind}[{r.Part}]"));

    private static InvalidOperationException Failure(QilNode operation, string reason) =>
        new($"Streamed QIL optimizer {reason}: {operation.NodeType} at {operation.SourceLine}");

    private sealed class Scan : QilVisitor
    {
        private readonly List<Read> _reads = new();
        private readonly List<Region> _regions = new();
        private readonly HashSet<QilNode> _active = new(ReferenceEqualityComparer.Instance);

        internal static List<Read> Reads(QilExpression expression)
        {
            var scan = new Scan();
            scan.Visit(expression);
            return scan._reads;
        }

        protected override QilNode Visit(QilNode node)
        {
            if (!_active.Add(node)) throw Failure(node, "created a cyclic expression");
            try
            {
                if (ReadsOrBuffers(node)) _reads.Add(new(node, node.NodeType, (node as QilInvokeEarlyBound)?.ClrMethod,
                    OptimizerPatterns.Read(node).MatchesPattern(OptimizerPatternName.Topmost),
                    OptimizerPatterns.Read(node).MatchesPattern(OptimizerPatternName.LoopGate),
                    OptimizerPatterns.Read(node).MatchesPattern(OptimizerPatternName.PullSequence), _regions.ToArray()));
                return base.Visit(node);
            }
            finally { _active.Remove(node); }
        }

        protected override QilNode VisitChildren(QilNode parent)
        {
            for (int child = 0; child < parent.Count; child++)
            {
                if (parent[child] == null) continue; // A parameter's default value/name can be absent.
                if (IsReference(parent, child)) VisitReference(parent[child]);
                else
                {
                    // Keep the binding outside its loop's repeated body, and keep short-circuit branches distinct.
                    QilNode? owner = parent is QilLoop loop ? loop.Variable
                        : parent.NodeType is QilNodeType.For or QilNodeType.Let or QilNodeType.Parameter
                            or QilNodeType.Function or QilNodeType.Conditional or QilNodeType.Choice
                            or QilNodeType.And or QilNodeType.Or or QilNodeType.BranchList ? parent : null;
                    if (owner != null) _regions.Add(new(owner, owner.NodeType, child));
                    Visit(parent[child]);
                    if (owner != null) _regions.RemoveAt(_regions.Count - 1);
                }
            }
            return parent;
        }

        private static bool ReadsOrBuffers(QilNode node) => node.NodeType is
            QilNodeType.DataSource or QilNodeType.Content or QilNodeType.Descendant or QilNodeType.DescendantOrSelf
            or QilNodeType.Preceding or QilNodeType.FollowingSibling or QilNodeType.PrecedingSibling
            or QilNodeType.NodeRange or QilNodeType.Deref or QilNodeType.XPathFollowing or QilNodeType.XPathPreceding
            or QilNodeType.XPathNodeValue or QilNodeType.XsltCopy or QilNodeType.XsltCopyOf
            or QilNodeType.Invoke or QilNodeType.XsltInvokeEarlyBound or QilNodeType.XsltInvokeLateBound
            or QilNodeType.Sort or QilNodeType.DocOrderDistinct
            or QilNodeType.Filter
            || node.NodeType == QilNodeType.XsltConvert && node[0].XmlType!.IsNode && !node.XmlType!.IsNode;
    }
}
