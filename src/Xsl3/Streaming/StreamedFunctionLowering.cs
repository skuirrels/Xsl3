// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using Xsl3.IlGen;
using Xsl3.Qil;
using Xsl3.Runtime;
using Xsl3.XPath31;
using Xsl3.Xdm;
using T = Xsl3.XmlQueryTypeFactory;

namespace Xsl3.Streaming;

/// <summary>
/// Keeps single-use streamed parameters and node results in iterator form. The ordinary function ABI
/// takes and returns an IList, collecting selected nodes before the next operation could consume them.
/// This lowering runs before the streamed optimizer's read-order contract is captured.
/// </summary>
internal sealed partial class StreamedFunctionLowering : QilReplaceVisitor
{
    private readonly HashSet<QilFunction> _functions;
    private readonly Dictionary<QilFunction, (QilParameter Parameter, QilNode Body)?> _bodies = new();
    private readonly HashSet<QilFunction> _active = new();
    private readonly Stack<QilNode> _inputs = new();
    private readonly IReadOnlyDictionary<QilFunction, (SequenceTypeTest Type, bool Grounded)> _singletons;
    private readonly QilPatternFactory _patterns;

    internal StreamedFunctionLowering(QilFactory factory, HashSet<QilFunction> functions,
        IReadOnlyDictionary<QilFunction, (SequenceTypeTest Type, bool Grounded)>? singletons = null,
        FunctionItemRegistry? registry = null, HashSet<QilFunction>? nodeResults = null,
        HashSet<QilFunction>? templates = null, System.Func<QilNode, XmlQueryType, QilNode>? unbox = null,
        System.Action<QilFunction, QilFunction>? copyFrame = null) : base(factory)
    {
        _functions = functions;
        _singletons = singletons ?? new Dictionary<QilFunction, (SequenceTypeTest, bool)>();
        _patterns = new QilPatternFactory(factory, debug: false);
        _registry = registry;
        _nodeResults = nodeResults ?? new();
        _templates = templates ?? new();
        _unbox = unbox;
        _copyFrame = copyFrame;
    }

    internal void Lower(QilExpression expression)
    {
        // Snapshot every candidate before rewriting any definition. Otherwise declaration order can
        // cause a later expansion to copy a body that has already expanded independent calls.
        foreach (QilFunction function in _functions)
        {
            _originals.Add(function, new BodyClone(f).Clone(function.Definition));
            _bodies.Add(function, Prepare(function));
        }
        foreach (QilFunction function in _templates)
            if (!_originals.ContainsKey(function)) _originals.Add(function, new BodyClone(f).Clone(function.Definition));
        Visit(expression);
        new StreamedPathOrder(f).Rewrite(expression);
    }

    private (QilParameter Parameter, QilNode Body)? Prepare(QilFunction function)
    {
        // Recursive functions retain their call boundary. The compiler supplies iterator-backed
        // parameters where supported; finite expansion applies to the remaining call shapes.
        if (function.Arguments.Count == 0 || Calls.Reaches(function.Definition, function)) return null;
        QilParameter parameter = f.Parameter(T.NodeNotRtfS);
        QilNode body = new BodyClone(f).Clone(function.Definition);
        body = new Substitute(f, function.Arguments[0], parameter).Rewrite(body);
        body = new IteratorBindings(f).Rewrite(body);
        return Use.IsOnce(body, parameter) ? (parameter, body) : null;
    }

    protected override QilNode VisitInvoke(QilInvoke call)
    {
        base.VisitInvoke(call);
        QilFunction function = call.Function;
        if (_registry != null && _templates.Contains(function)) return TemplatePullCall(call);
        bool singleton = _singletons.TryGetValue(function, out var contract) && call.Arguments.Count != 0
            && !call.Arguments[0].XmlType!.IsSingleton && call.Arguments[0].XmlType!.IsSubtypeOf(T.NodeNotRtfS);
        if (singleton && contract.Grounded) return SingletonCall(call, contract.Type);
        // The source guard establishes cardinality at run time, but a QIL for still has its source's
        // static cardinality. Keep a narrower native return contract at its existing call boundary;
        // changing it to a sequence would violate the caller's generated-code contract.
        if (singleton && call.XmlType!.Cardinality != XmlQueryCardinality.ZeroOrMore) return call;
        if (!_functions.Contains(function) || _active.Contains(function) || call.Arguments.Count == 0
            || !call.Arguments[0].XmlType!.IsSubtypeOf(T.NodeNotRtfS)) return call;
        // Follow the one streamed operand through a call chain. Expanding unrelated grounded calls
        // as well could multiply a small acyclic call graph into an exponential expression tree.
        if (_inputs.TryPeek(out QilNode? input) && !DependsOn(call.Arguments[0], input)) return call;

        if (_bodies[function] is not { } implementation)
            return _registry != null && _nodeResults.Contains(function)
                && _singletons.TryGetValue(function, out contract) && !contract.Grounded
                ? PullCall(call, singleton, contract.Type) : call;

        var substitutions = new SubstitutionList();
        var bindings = new List<QilIterator>();
        // Other operands keep one evaluation each, independent of how often the body uses them.
        for (int i = 1; i < call.Arguments.Count; i++)
        {
            QilIterator binding = f.Let(call.Arguments[i]);
            bindings.Add(binding);
            substitutions.AddSubstitutionPair(function.Arguments[i], binding);
        }
        QilNode Expand(QilNode input)
        {
            QilNode body = new BodyClone(f, substitutions).Clone(implementation.Body);
            body = new Substitute(f, implementation.Parameter, input).Rewrite(body);
            _active.Add(function);
            _inputs.Push(input);
            try { return Visit(body); }
            finally { _inputs.Pop(); _active.Remove(function); }
        }
        QilNode result = singleton ? TransmittingSingleton(call, contract.Type, bindings, Expand) : Expand(call.Arguments[0]);
        for (int i = bindings.Count - 1; i >= 0; i--) result = f.Loop(bindings[i], result);
        return result;
    }

    private QilNode TransmittingSingleton(QilInvoke call, SequenceTypeTest parameter, IReadOnlyList<QilIterator> bindings,
        System.Func<QilNode, QilNode> expand)
    {
        QilNode Stateful(string method, XmlQueryType type, params QilNode[] arguments)
        {
            QilNode result = XPath31Compiler.Invoke(_patterns, method, type, arguments);
            OptimizerPatterns.Write(result).AddPattern(OptimizerPatternName.MaybeSideEffects);
            return result;
        }
        // Results can themselves be source nodes. Let their consumer run before advancing the
        // argument selection. Only a per-invocation seen flag crosses those evaluations; it holds no
        // source nodes. Full consumption still checks the complete argument's cardinality.
        QilIterator state = f.Let(Stateful(nameof(XsltLibrary.X31SingletonNew), T.Item));
        QilIterator source = f.For(call.Arguments[0]);
        QilIterator checkedNode = f.Let(Stateful(nameof(XsltLibrary.X31SingletonNode), T.NodeNotRtf, state, source));
        QilList emptyArguments = f.ActualParameterList();
        emptyArguments.Add(f.Sequence());
        foreach (QilIterator binding in bindings) emptyArguments.Add(binding);
        // Empty input is grounded and can keep the original call. Expanding an optional-empty body
        // as well would duplicate every nested call chain even when its consuming branch is unused.
        QilNode empty = parameter.AllowsEmpty ? f.Invoke(call.Function, emptyArguments)
            : f.TypeAssert(XPath31Compiler.Invoke(_patterns, nameof(XsltLibrary.X31Raise), T.ItemS,
                f.LiteralString("XPTY0004"), f.LiteralString("A required singleton streamed function argument is empty")), call.XmlType!);
        QilNode result = f.Sequence(new QilNode[] {
            f.Loop(source, f.Loop(checkedNode, expand(checkedNode))),
            f.Conditional(Stateful(nameof(XsltLibrary.X31SingletonSeen), T.BooleanX, state), f.Sequence(), empty),
        });
        result = f.Loop(state, result);
        result.SourceLine = call.SourceLine;
        return result;
    }

    private QilNode SingletonCall(QilInvoke call, SequenceTypeTest parameter)
    {
        var bindings = new List<QilIterator>();
        for (int i = 1; i < call.Arguments.Count; i++) bindings.Add(f.Let(call.Arguments[i]));
        QilList Arguments(QilNode first)
        {
            QilList arguments = f.ActualParameterList();
            arguments.Add(first);
            foreach (QilIterator binding in bindings) arguments.Add(binding);
            return arguments;
        }
        QilNode Call(string method, XmlQueryType type, params QilNode[] arguments) =>
            XPath31Compiler.Invoke(_patterns, method, type, arguments);

        // An absorbing/inspection function's result is grounded. Evaluate it while the first node
        // is current, then check for a second node before exposing any result. Cache only that result,
        // never the source selection. The real invocation retains recursion, tracing and depth checks.
        QilIterator source = f.For(call.Arguments[0]);
        QilNode result = Call(nameof(XsltLibrary.X31Wrap), T.Item, f.Invoke(call.Function, Arguments(source)));
        QilNode excess = Call(nameof(XsltLibrary.X31RaiseItem), T.Item,
            f.LiteralString("XPTY0004"), f.LiteralString("A singleton streamed function argument contains more than one node"));
        QilIterator collected = f.Let(f.Loop(source, f.Conditional(f.Eq(f.PositionOf(source), f.LiteralInt32(1)), result, excess)));
        QilNode empty = parameter.AllowsEmpty ? f.Invoke(call.Function, Arguments(f.Sequence()))
            : Call(nameof(XsltLibrary.X31Raise), T.ItemS, f.LiteralString("XPTY0004"), f.LiteralString("A required singleton streamed function argument is empty"));
        QilNode unwrapped = Call(nameof(XsltLibrary.X31Unwrap), T.ItemS, Call(nameof(XsltLibrary.X31SingleItemOf), T.Item, collected));
        result = f.Loop(collected, f.Conditional(f.IsEmpty(collected), empty, unwrapped));
        for (int i = bindings.Count - 1; i >= 0; i--) result = f.Loop(bindings[i], result);
        result.SourceLine = call.SourceLine;
        return result;
    }

    private static bool DependsOn(QilNode expression, QilNode input)
    {
        var pending = new Stack<QilNode>();
        var visited = new HashSet<QilNode>();
        pending.Push(expression);
        while (pending.TryPop(out QilNode? node))
        {
            if (ReferenceEquals(node, input)) return true;
            if (!visited.Add(node) || node is QilFunction) continue;
            // Includes iterator bindings, but not the bodies of invoked functions.
            for (int i = 0; i < node.Count; i++) if (node[i] != null) pending.Push(node[i]);
        }
        return false;
    }

    // Code generation attaches mutable iterator storage and construction facts to these annotations.
    // Each expansion needs its own annotations; compiler pruning and side-effect policies survive cloning.
    private sealed class BodyClone(QilFactory factory, SubstitutionList substitutions) : QilCloneVisitor(factory, substitutions)
    {
        internal BodyClone(QilFactory factory) : this(factory, new SubstitutionList()) { }
        protected override QilNode Visit(QilNode node)
        {
            QilNode clone = base.Visit(node);
            if (clone?.Annotation is XmlILAnnotation annotation)
            {
                bool topmost = OptimizerPatterns.Read(clone).MatchesPattern(OptimizerPatternName.Topmost);
                bool sideEffects = OptimizerPatterns.Read(clone).MatchesPattern(OptimizerPatternName.MaybeSideEffects);
                bool loopGate = OptimizerPatterns.Read(clone).MatchesPattern(OptimizerPatternName.LoopGate);
                bool pull = OptimizerPatterns.Read(clone).MatchesPattern(OptimizerPatternName.PullSequence);
                bool simpleContent = OptimizerPatterns.Read(clone).MatchesPattern(OptimizerPatternName.StreamedSimpleContent);
                bool depthLimit = OptimizerPatterns.Read(clone).MatchesPattern(OptimizerPatternName.DepthLimit);
                object? maximumDepth = depthLimit ? OptimizerPatterns.Read(clone).GetArgument(OptimizerPatternArgument.MaximumDepth) : null;
                clone.Annotation = annotation.Count == 3 ? annotation[0] : null;
                if (topmost) OptimizerPatterns.Write(clone).AddPattern(OptimizerPatternName.Topmost);
                if (sideEffects) OptimizerPatterns.Write(clone).AddPattern(OptimizerPatternName.MaybeSideEffects);
                if (loopGate) OptimizerPatterns.Write(clone).AddPattern(OptimizerPatternName.LoopGate);
                // A pull call returns one item per invocation. Dropping this protocol
                // turns an inlined population into its first one-item batch.
                if (pull) OptimizerPatterns.Write(clone).AddPattern(OptimizerPatternName.PullSequence);
                if (simpleContent) OptimizerPatterns.Write(clone).AddPattern(OptimizerPatternName.StreamedSimpleContent);
                if (depthLimit)
                {
                    OptimizerPatterns.Write(clone).AddPattern(OptimizerPatternName.DepthLimit);
                    OptimizerPatterns.Write(clone).AddArgument(OptimizerPatternArgument.MaximumDepth, maximumDepth!);
                }
            }
            return clone!;
        }
    }

    private sealed class Substitute(QilFactory factory, QilNode find, QilNode replacement) : QilReplaceVisitor(factory)
    {
        internal QilNode Rewrite(QilNode node) => VisitAssumeReference(node);
        protected override QilNode VisitReference(QilNode node) => ReferenceEquals(node, find) ? replacement : node;
    }

    private sealed class IteratorBindings(QilFactory factory) : QilReplaceVisitor(factory)
    {
        internal QilNode Rewrite(QilNode node) => VisitAssumeReference(node);

        protected override QilNode VisitTypeAssert(QilTargetType assertion)
        {
            base.VisitTypeAssert(assertion);
            return assertion.Source.XmlType!.IsSubtypeOf(T.NodeNotRtfS) && assertion.Source.XmlType.IsSubtypeOf(assertion.TargetType)
                ? assertion.Source : assertion;
        }

        protected override QilNode VisitXsltInvokeEarlyBound(QilInvokeEarlyBound call)
        {
            base.VisitXsltInvokeEarlyBound(call);
            if (call.Arguments.Count == 0 || !call.Arguments[0].XmlType!.IsSubtypeOf(T.NodeNotRtfS)) return call;
            if (call.ClrMethod.DeclaringType != typeof(XsltLibrary)) return call;
            if (call.ClrMethod.Name == nameof(XsltLibrary.X31Nodes)) return call.Arguments[0];
            if (call.ClrMethod.Name == nameof(XsltLibrary.X31Coerce)
                && ConstantPool.Get((int)(double)((QilLiteral)call.Arguments[1]).Value!) is SequenceTypeTest required
                && required.Item is AnyItemTest or NodeKindTest { DocumentElement: null })
            {
                bool identity = required.Item is AnyItemTest || required.Item is NodeKindTest
                    { Kind: null, NamespaceUri: null, LocalName: null, AnyType: true, DocumentElement: null,
                        TypeName: null, SchemaType: null, Names: null, Nillable: true };
                if (required.Cardinality == Cardinality.ZeroOrMore && identity) return call.Arguments[0];
                // Node and item return types never atomize or promote their source nodes.
                // Validate each original node and the complete cardinality with the original
                // conversion error code, without collecting the function result first.
                return XPath31Compiler.TypeCheckedPopulation(call.Arguments[0], call.Arguments[1], call.Arguments[2],
                    new QilPatternFactory(f, debug: false));
            }
            return call;
        }

        protected override QilNode VisitLoop(QilLoop loop)
        {
            base.VisitLoop(loop);
            if (loop.Variable.NodeType == QilNodeType.Let && !loop.Variable.XmlType!.IsSingleton
                && loop.Variable.XmlType.IsSubtypeOf(T.NodeNotRtfS) && Use.IsOnce(loop.Body, loop.Variable))
                return new Substitute(f, loop.Variable, loop.Variable.Binding!).Rewrite(loop.Body);
            return loop;
        }
    }

    // One syntactic reference is insufficient if it occurs inside a repeated loop body.
    private sealed class Use(QilNode target) : QilVisitor
    {
        private int _count, _repeated;
        internal static bool IsOnce(QilNode body, QilNode target)
        {
            var use = new Use(target);
            use.VisitAssumeReference(body);
            return use._count == 1;
        }
        protected override QilNode VisitReference(QilNode node)
        {
            if (ReferenceEquals(node, target)) _count += _repeated == 0 ? 1 : 2;
            return node;
        }
        protected override QilNode VisitChildren(QilNode parent)
        {
            for (int i = 0; i < parent.Count; i++)
            {
                bool repeated = i == 1 && parent is QilLoop loop && loop.Variable.NodeType == QilNodeType.For;
                if (repeated) _repeated++;
                if (IsReference(parent, i)) VisitReference(parent[i]);
                else Visit(parent[i]);
                if (repeated) _repeated--;
            }
            return parent;
        }
    }

    private sealed class Calls(QilFunction target) : QilVisitor
    {
        private readonly HashSet<QilFunction> _visited = new();
        private bool _found;
        internal static bool Reaches(QilNode body, QilFunction target)
        {
            var calls = new Calls(target);
            calls.Visit(body);
            return calls._found;
        }
        protected override QilNode VisitInvoke(QilInvoke call)
        {
            if (ReferenceEquals(call.Function, target)) _found = true;
            else if (_visited.Add(call.Function)) Visit(call.Function.Definition);
            return base.VisitInvoke(call);
        }
    }
}
