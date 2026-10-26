// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using Xsl3.IlGen;
using Xsl3.Qil;
using T = Xsl3.XmlQueryTypeFactory;

namespace Xsl3.Streaming;

/// <summary>Remove unused implicit context sizes without evaluating a forward selection to count it.</summary>
internal sealed class StreamedFocusPruning(QilFactory factory) : QilReplaceVisitor(factory)
{
    internal void Prune(QilExpression expression)
    {
        var parameters = new Dictionary<QilFunction, (QilParameter Parameter, int Index)>();
        foreach (QilFunction function in expression.FunctionList)
            for (int i = 0; i < function.Arguments.Count; i++)
                if (function.Arguments[i] is QilParameter { Name: { LocalName: "last", NamespaceUri: XmlReservedNs.NsXslDebug } } parameter
                    && parameter.XmlType!.Equals(T.DoubleX))
                    parameters.Add(function, (parameter, i));

        var demands = new Demands(parameters);
        demands.Scan(expression);
        demands.Propagate();
        // Adjust every call before changing the declarations' argument positions.
        foreach (var (call, parameter, index) in demands.Calls)
            if (!demands.Needed.Contains(parameter)) call.Arguments.RemoveAt(index);
        foreach (var (function, (parameter, index)) in parameters)
            if (!demands.Needed.Contains(parameter)) function.Arguments.RemoveAt(index);
        Visit(expression);
    }

    protected override QilNode VisitLoop(QilLoop loop)
    {
        base.VisitLoop(loop);
        if (loop.Variable.NodeType != QilNodeType.Let) return loop;
        OptimizerPatterns patterns = OptimizerPatterns.Read(loop.Variable);
        if (patterns.MatchesPattern(OptimizerPatternName.ContextSize) && !References.Any(loop.Body, loop.Variable))
            return loop.Body;
        // ConstructLoop wraps the cache around its for iterator. Sorting caches have a separate
        // purpose and are never marked ContextSizeCache. Do not remove an alias with other users.
        if (patterns.MatchesPattern(OptimizerPatternName.ContextSizeCache)
            && loop.Body is QilLoop { Variable.NodeType: QilNodeType.For } iteration
            && !References.Any(iteration.Body, loop.Variable))
        {
            if (ReferenceEquals(iteration.Variable.Binding, loop.Variable))
            {
                iteration.Variable.Binding = loop.Variable.Binding;
                return iteration;
            }
            if (iteration.Variable.Binding is QilLoop gate
                && OptimizerPatterns.Read(gate).MatchesPattern(OptimizerPatternName.LoopGate)
                && ReferenceEquals(gate.Variable.Binding, loop.Variable) && !References.Any(gate.Body, loop.Variable))
            {
                gate.Variable.Binding = loop.Variable.Binding;
                return iteration;
            }
        }
        return loop;
    }

    private sealed class References(QilNode target) : QilVisitor
    {
        private bool _found;
        internal static bool Any(QilNode body, QilNode target)
        {
            var references = new References(target);
            references.VisitAssumeReference(body);
            return references._found;
        }
        protected override QilNode VisitReference(QilNode node)
        {
            _found |= ReferenceEquals(node, target);
            return node;
        }
    }

    private sealed class Demands(Dictionary<QilFunction, (QilParameter Parameter, int Index)> parameters) : QilVisitor
    {
        private readonly HashSet<QilParameter> _parameters = new();
        private readonly Dictionary<QilParameter, HashSet<QilParameter>> _callers = new();
        private readonly HashSet<QilInvoke> _calls = new();
        private QilParameter? _forwardTo;
        internal readonly HashSet<QilParameter> Needed = new();
        internal readonly List<(QilInvoke Call, QilParameter Parameter, int Index)> Calls = new();

        internal void Scan(QilExpression expression)
        {
            foreach (var (parameter, _) in parameters.Values) _parameters.Add(parameter);
            Visit(expression);
        }

        internal void Propagate()
        {
            var pending = new Queue<QilParameter>(Needed);
            while (pending.TryDequeue(out QilParameter? parameter))
                if (_callers.TryGetValue(parameter, out var callers))
                    foreach (QilParameter caller in callers)
                        if (Needed.Add(caller)) pending.Enqueue(caller);
        }

        protected override QilNode VisitReference(QilNode node)
        {
            if (node is QilParameter parameter && _parameters.Contains(parameter))
            {
                if (_forwardTo == null) Needed.Add(parameter);
                else
                {
                    if (!_callers.TryGetValue(_forwardTo, out var callers)) _callers.Add(_forwardTo, callers = new());
                    callers.Add(parameter);
                }
            }
            return node;
        }

        protected override QilNode VisitInvoke(QilInvoke call)
        {
            bool known = parameters.TryGetValue(call.Function, out var target);
            if (known && _calls.Add(call)) Calls.Add((call, target.Parameter, target.Index));
            QilParameter? outer = _forwardTo;
            for (int i = 0; i < call.Arguments.Count; i++)
            {
                _forwardTo = known && i == target.Index ? target.Parameter : outer;
                VisitAssumeReference(call.Arguments[i]);
            }
            _forwardTo = outer;
            return call;
        }
    }
}
