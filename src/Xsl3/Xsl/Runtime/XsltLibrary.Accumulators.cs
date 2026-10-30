// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System;
using System.Xml;
using System.Xml.XPath;
using Xsl3.XPath31;
using Xsl3.XPath31.Functions;
using Xsl3.Xdm;

namespace Xsl3.Runtime
{
    /// <summary>
    /// Xsl3: where an accumulator's compiled functions are among the accumulator items: the initial value's at
    /// <paramref name="Offset"/>, then a pattern and a value function per rule (most preferred first), whose
    /// phases (end: true) are in <paramref name="EndPhases"/>. <paramref name="OnPrincipal"/>: whether it is
    /// applicable to the principal source document (the initial mode's use-accumulators, §18.2.2).
    /// </summary>
    internal sealed record AccumulatorInfo(int Offset, bool[] EndPhases, SequenceTypeTest? Type, bool OnPrincipal = true, bool Streamable = false);

    public sealed partial class XsltLibrary
    {
        private GroundedAccumulatorValues? _groundedAccumulators;

        /// <summary>
        /// accumulator-before and accumulator-after (XSLT 3.0 §18.2.4): the accumulator's value before or after the
        /// context node, computed for the node's whole tree the first time it is asked for.
        /// </summary>
        public IList<XPathItem> X31AccumulatorValue(IList<XPathItem> items, double tableId, string name, double nameContext, IList<XPathItem> context, bool after, double package)
        {
            var (ns, local) = NameContext(nameContext).Resolve(name, "", "XTDE3340");
            // The accumulators of the package the call is in (their names carry its id).
            string key = new QName(Xsl3.Xslt.PackageComponents.MangleNamespace((int)package, ns), local).EQName;
            var table = (Dictionary<string, AccumulatorInfo>)Constant((int)tableId);
            if (!table.TryGetValue(key, out AccumulatorInfo? info))
            {
                throw new XdmException("XTDE3340", $"There is no accumulator named {name}");
            }
            if (context.Count == 0 || context.Count == 1 && context[0] is AbsentItem)
                throw new XdmException("XTDE3350", $"{(after ? "accumulator-after" : "accumulator-before")}() needs a context item");
            if (context.Count != 1 || context[0] is not XPathNavigator node)
            {
                throw new XdmException("XTTE3360", $"{(after ? "accumulator-after" : "accumulator-before")}() needs a node as the context item");
            }
            if (node.NodeType is XPathNodeType.Attribute or XPathNodeType.Namespace)
            {
                throw new XdmException("XTTE3360", $"Accumulators have no value at {node.NodeType.ToString().ToLowerInvariant()} nodes");
            }
            XPathNavigator root = node.Clone();
            (long Before, long After)? phases = (root as Tree.CompactNavigator)?.TraversalPhases;
            root.MoveToRoot();
            bool validatedCopy = false;
            while (_runtime.Documents.TryGetCopy(root, out var copied) && Original(node, copied.Top, copied.Original) is XPathNavigator original)
            {
                // A copy made with copy-accumulators="yes": the original node's values. Applicable as they are to the
                // original's tree (copy-3002, XTDE3362), unless validation made the copy: a new temporary tree, to which
                // every accumulator applies (accumulator-073).
                validatedCopy |= copied.Validated;
                node = original;
                root = node.Clone();
                phases = (root as Tree.CompactNavigator)?.TraversalPhases;
                root.MoveToRoot();
            }
            // Validation does not replace copied accumulator values with a new traversal. Streamed
            // origins may already be closed; their observer owns the recorded before/after values.
            if (node is Streaming.StreamedNavigator streamed)
            {
                if (streamed.Document.Identity.Observer is StreamedAccumulators observer) return observer.Value(key, streamed, after);
                throw new XdmException("XTDE3362", $"The accumulator {name} isn't applicable to the document (use-accumulators)");
            }
            var cache = _groundedAccumulators ??= new();
            string rootId = _runtime.GenerateId(root);
            if (!validatedCopy && !cache.Applies(root, rootId, new QName(ns, local).EQName))
            {
                throw new XdmException("XTDE3362", $"The accumulator {name} isn't applicable to the document (use-accumulators)");
            }
            if (!validatedCopy && !info.OnPrincipal && _runtime.ExternalContext.IsInitialSelectionDocument(root))
            {
                throw new XdmException("XTDE3362", $"The accumulator {name} isn't applicable to the source document (the initial mode's use-accumulators)");
            }
            var position = phases is { } native
                ? new GroundedAccumulatorValues.Position(null, native.Before, native.After)
                : new GroundedAccumulatorValues.Position(_runtime.GenerateId(node));
            var state = cache.Demand(root, rootId, key, position, after, _runtime.EvaluationDependencies, XmlQueryRuntime.Cancellation, out var cached);
            if (state == null) return cache.IsShared ? Copy(cached!) : cached!;
            var previous = XmlQueryRuntime.Cancellation;
            var output = _runtime.SaveOutput();
            int sourceMark = _runtime.StreamedSourceMark, traceMark = _runtime.TraceStack.Count, lineMark = _runtime.TryLines.Count;
            bool pendingType = _runtime.PendingAnyType;
            Stack<bool>? annotations = _runtime.CopyAnnotationScopes();
            if (cache.IsShared) XmlQueryRuntime.Cancellation = cache.EvaluationCancellation;
            bool completed = false;
            Exception? failure = null;
            try
            {
                IList<XPathItem> initial = AccumulatorCoerce(info, CallFunction((FunctionItem)items[info.Offset], Sequences.One(root.Clone())), name);
                AccumulatorVisit(root, initial, items, info, name, cache, state);
                completed = true;
            }
            catch (Exception error) when (XmlException.IsCatchableException(error))
            {
                _runtime.RecordFailureStack(error);
                if (error is OperationCanceledException) throw;
                failure = error;
                completed = true;
            }
            finally
            {
                try { cache.Finish(state, completed, failure); }
                finally
                {
                    _runtime.CloseStreamedSources(sourceMark);
                    _runtime.RestoreOutput(output);
                    _runtime.AnnotationScopes = annotations;
                    _runtime.PendingAnyType = pendingType;
                    if (_runtime.TraceStack.Count > traceMark) _runtime.TraceStack.RemoveRange(traceMark, _runtime.TraceStack.Count - traceMark);
                    if (_runtime.TryLines.Count > lineMark) _runtime.TryLines.RemoveRange(lineMark, _runtime.TryLines.Count - lineMark);
                    XmlQueryRuntime.Cancellation = previous;
                }
            }
            var result = cache.Result(state, position, after);
            return cache.IsShared ? Copy(result) : result;
        }

        internal static bool IsAccumulatorCycle(Exception error)
        {
            for (Exception? current = error; current != null; current = current.InnerException)
                if (current is XdmException { AccumulatorCycle: true }) return true;
            return false;
        }

        /// <summary>The documents of xsl:source-document or xsl:merge-source, recorded with their use-accumulators.</summary>
        public IList<XPathItem> X31SourceDocuments(IList<XPathItem> documents, string names)
        {
            foreach (XPathItem item in documents)
            {
                if (item is XPathNavigator node)
                {
                    XPathNavigator root = node.Clone();
                    root.MoveToRoot();
                    (_groundedAccumulators ??= new()).Select(root, _runtime.GenerateId(root),
                        names == "#all" ? null : new HashSet<string>(names.Split(' ', System.StringSplitOptions.RemoveEmptyEntries)));
                }
            }
            return documents;
        }

        /// <summary>fn:copy-of: copies preserve annotations in a schema-aware processor and inherit accumulator values.</summary>
        public IList<XPathItem> X31CopyWithAccumulators(IList<XPathItem> items) =>
            Copies(items, copyNamespaces: true, keepTypes: _runtime.SchemaAware);

        // The node of the original tree at the same place as `node` in the copy of it rooted at `top`.
        private static XPathNavigator? Original(XPathNavigator node, XPathNavigator top, XPathNavigator original)
        {
            var path = new List<int>();
            XPathNavigator n = node.Clone();
            while (!n.IsSamePosition(top))
            {
                int index = 0;
                XPathNavigator sibling = n.Clone();
                while (sibling.MoveToPrevious())
                {
                    index++;
                }
                path.Add(index);
                if (!n.MoveToParent())
                {
                    return null; // not within the copy
                }
            }
            if (original is Streaming.StreamedNavigator streamed) return streamed.OriginalAtPath(path);
            XPathNavigator result = original.Clone();
            for (int i = path.Count - 1; i >= 0; i--)
            {
                if (!result.MoveToFirstChild())
                {
                    return null;
                }
                for (int k = 0; k < path[i]; k++)
                {
                    if (!result.MoveToNext())
                    {
                        return null;
                    }
                }
            }
            return result;
        }

        // Iterative document-order traversal: XML depth consumes heap frames, not the CLR stack.
        private IList<XPathItem> AccumulatorVisit(XPathNavigator node, IList<XPathItem> value, IList<XPathItem> items, AccumulatorInfo info, string name,
            GroundedAccumulatorValues cache, GroundedAccumulatorValues.State state)
        {
            var open = new Stack<(XPathNavigator Node, string? Id, IList<XPathItem> Before)>();
            XPathNavigator current = node.Clone();
            while (true)
            {
                XmlQueryRuntime.CheckCancellation();
                IList<XPathItem> before = AccumulatorApply(current, value, end: false, items, info, name);
                string? id = state.IsCompact ? null : _runtime.GenerateId(current);
                cache.Record(state, id, before, null);
                open.Push((current.Clone(), id, before));
                value = before;
                if (current.MoveToFirstChild()) continue;
                do
                {
                    XmlQueryRuntime.CheckCancellation();
                    var frame = open.Pop();
                    value = AccumulatorApply(frame.Node, value, end: true, items, info, name);
                    cache.Record(state, frame.Id, frame.Before, value);
                    if (open.Count == 0) return value;
                    current = frame.Node;
                    if (current.MoveToNext()) break;
                }
                while (true);
            }
        }

        // The rule of the phase that matches the node with the highest priority (the first of equal ones), applied.
        private IList<XPathItem> AccumulatorApply(XPathNavigator node, IList<XPathItem> value, bool end, IList<XPathItem> items, AccumulatorInfo info, string name)
        {
            int best = -1;
            double bestPriority = double.NegativeInfinity;
            for (int r = 0; r < info.EndPhases.Length; r++)
            {
                if (info.EndPhases[r] != end)
                {
                    continue;
                }
                IList<XPathItem> priority;
                var output = _runtime.SaveOutput();
                int sourceMark = _runtime.StreamedSourceMark;
                try { priority = CallFunction((FunctionItem)items[info.Offset + 1 + 2 * r], Sequences.One(node.Clone())); }
                catch (Exception error) when (CanIgnorePatternError(error, sourceMark))
                {
                    _runtime.RestoreOutput(output);
                    continue;
                }
                if (priority.Count == 1)
                {
                    double p = CapturedNumber(priority[0]);
                    if (best < 0 || p > bestPriority)
                    {
                        best = r;
                        bestPriority = p;
                    }
                }
            }
            if (best < 0)
            {
                return value;
            }
            return AccumulatorCoerce(info, CallFunction((FunctionItem)items[info.Offset + 2 + 2 * best], Sequences.One(node.Clone()), value), name);
        }

        private static IList<XPathItem> AccumulatorCoerce(AccumulatorInfo info, IList<XPathItem> value, string name) =>
            info.Type == null ? Copy(value) : Coercion.CoerceTo(Copy(value), info.Type, "XPTY0004", () => $"the value of accumulator {name}");
    }
}
