// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using Xsl3.Xdm;
using Xsl3.Qil;
using T = Xsl3.XmlQueryTypeFactory;

namespace Xsl3.XPath31;

internal sealed partial class XPath31Compiler
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<QilNode, QilNode> s_functionParameters = new();
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<QilNode, QilNode> s_orderedChildren = new();
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<QilNode, object> s_groundedFunctionArguments = new();
    internal static bool IsGroundedFunctionArgument(QilNode value) => AtomicItems(value) || s_groundedFunctionArguments.TryGetValue(value, out _);
    internal static void GroundedFunctionResult(QilNode value) => s_groundedFunctionArguments.AddOrUpdate(value, GroundedSetBindings.Marker);
    internal static QilNode? FunctionParameterSource(QilNode value) => s_functionParameters.TryGetValue(value, out QilNode? parameter) ? parameter : null;
    internal static QilNode FunctionParameterPopulation(QilNode population, QilNode parameter, QilNode orderedChildren)
    {
        s_functionParameters.AddOrUpdate(population, parameter);
        s_orderedChildren.AddOrUpdate(population, orderedChildren);
        return population;
    }

    internal static QilNode OrderedChildArgument(Expr expression, QilNode value, QilPatternFactory factory)
    {
        if (s_orderedChildren.TryGetValue(value, out QilNode? condition)) return new QilCloneVisitor(factory.BaseFactory).Clone(condition);
        // These populations are distinct, ordered and disjoint. Arbitrary grounded sequences,
        // unions and descendant paths do not acquire that contract merely by entering a function.
        static bool Disjoint(Expr input) => input switch
        {
            ContextItemExpr or RootExpr or AxisStep { Axis: Axis.Child or Axis.Attribute or Axis.Self } => true,
            PathExpr { Descendants: false, Right: AxisStep { Axis: Axis.Child or Axis.Attribute or Axis.Self }, Left: var left } => Disjoint(left),
            FilterExpr filter => Disjoint(filter.Base),
            _ => false,
        };
        return factory.Boolean(value.XmlType!.IsSingleton || s_sameDepth.TryGetValue(value, out _) || Disjoint(expression));
    }

    private QilNode? ParameterChildPath(PathExpr path)
    {
        if (!_sc.ForwardOnly) return null;
        var steps = new List<AxisStep>();
        Expr root = path;
        while (root is PathExpr { Descendants: false, Right: AxisStep { Axis: Axis.Child or Axis.Attribute } step } childPath)
        {
            steps.Add(step);
            root = childPath.Left;
        }
        if (root is not VarRef) return null;
        QilNode source = Build(root);
        if (!s_orderedChildren.TryGetValue(source, out QilNode? ordered)) return null;

        // Build the two complete chains once. Branching and cloning at each step would grow
        // a long path exponentially; a grounded chain also needs normalization after every step.
        QilNode forward = source, canonical = new QilCloneVisitor(_qf).Clone(source);
        for (int i = steps.Count - 1; i >= 0; i--)
        {
            AxisStep step = steps[i];
            var (item, body, binding) = WithFocus(forward, UsesLast(step), () => Build(step));
            forward = WrapLet(binding, _f.Loop(item, body));
            canonical = Join(canonical, () => Build(step), UsesLast(step));
        }
        QilNode result = _f.Conditional(new QilCloneVisitor(_qf).Clone(ordered), forward, canonical);
        if (steps[0].Axis == Axis.Child) s_orderedChildren.AddOrUpdate(result, ordered);
        return result;
    }
    internal static QilNode AtomicArgument(QilNode value, ItemTest? required, XPathStaticContext sc, QilPatternFactory factory, IXPath31Environment environment)
    {
        var compiler = new XPath31Compiler(sc, factory, environment);
        return compiler.AtomicArgument(new ContextItemExpr(), compiler.Items(value), required);
    }

    private QilNode AtomicArgument(Expr expression, QilNode value, ItemTest? required) =>
        _sc.ForwardOnly && required is AtomicTypeTest or NumericTest or UnionTypeTest && !AtomicItems(value)
            ? AtomizeEach(expression, value) : value;

    private QilNode AtomicOperand(Expr expression)
    {
        QilNode value = Items(_sc.ForwardOnly ? BuildValueSource(expression) : Build(expression));
        return _sc.ForwardOnly && !AtomicItems(value) ? AtomizeEach(expression, value) : value;
    }

    private QilNode FunctionArgument(Expr expression, ItemTest? required)
    {
        QilNode value = Items(Build(expression));
        bool Grounded(Expr input) => input switch
        {
            MapConstructor or SquareArrayConstructor or CurlyArrayConstructor or InlineFunctionExpr or NamedFunctionRef => true,
            SequenceExpr sequence => System.Linq.Enumerable.All(sequence.Items, Grounded),
            _ => GroundedSetOperand(input),
        };
        if (_sc.ForwardOnly && Grounded(expression)) s_groundedFunctionArguments.AddOrUpdate(value, GroundedSetBindings.Marker);
        return AtomicArgument(expression, value, required);
    }

    private QilNode Cast(Expr operand, QilNode target, bool allowEmpty)
    {
        // The list-valued cast interface must receive atomic values, before advancing a forward
        // selection to its end can invalidate the selected nodes. The canonical cast retains its
        // cardinality, schema-list and conversion rules.
        QilNode result = Call(nameof(Xsl3.Runtime.XsltLibrary.X31Cast), T.ItemS, AtomicOperand(operand), target, _f.Boolean(allowEmpty));
        if (_sc.ForwardOnly) s_atomicItems.AddOrUpdate(result, AtomicTypes.AnyAtomicType);
        return result;
    }

    private QilNode ForwardUntypedAggregate(string name, QilNode nodes)
    {
        QilNode values = UntypedDoubles(nodes);
        QilNode aggregate = name switch
        {
            "avg" => _qf.Average(values),
            "min" => _qf.Minimum(values),
            _ => _qf.Maximum(values),
        };
        // The aggregate is optional: no input yields no item, not an integer zero.
        QilIterator result = _f.For(aggregate);
        QilNode boxed = Call(nameof(Xsl3.Runtime.XsltLibrary.X31DoubleItem), T.Item, result);
        s_atomicItems.AddOrUpdate(boxed, AtomicTypes.Double);
        return _f.Loop(result, boxed);
    }

    private QilNode StatefulCall(string method, XmlQueryType type, params QilNode[] arguments)
    {
        QilNode call = Call(method, type, arguments);
        Xsl3.IlGen.OptimizerPatterns.Write(call).AddPattern(Xsl3.IlGen.OptimizerPatternName.MaybeSideEffects);
        return call;
    }

    private QilNode ForwardBoolean(QilNode source)
    {
        QilIterator state = _f.Let(StatefulCall(nameof(Xsl3.Runtime.XsltLibrary.X31EbvNew), T.Item));
        QilNode gated = Streaming.StreamedLoopGate.Create(_qf, Items(source),
            StatefulCall(nameof(Xsl3.Runtime.XsltLibrary.X31EbvActive), T.BooleanX, state));
        QilIterator item = _f.For(gated);
        QilIterator seen = _f.Let(_f.Length(_f.Loop(item, StatefulCall(nameof(Xsl3.Runtime.XsltLibrary.X31EbvItem), T.IntX, state, item))));
        return _f.Loop(state, _f.Loop(seen, StatefulCall(nameof(Xsl3.Runtime.XsltLibrary.X31EbvResult), T.BooleanX, state)));
    }

    private QilNode ForwardCardinality(Expr expression, string function)
    {
        QilNode source = Build(expression);
        QilIterator state = _f.Let(StatefulCall(nameof(Xsl3.Runtime.XsltLibrary.X31CardinalityNew), T.Item, _f.String(function)));
        QilIterator item = _f.For(source);
        QilNode checkedItems = _f.Filter(item, StatefulCall(nameof(Xsl3.Runtime.XsltLibrary.X31CardinalityAccept), T.BooleanX, state));
        QilNode result = _f.Loop(state, _f.Sequence(checkedItems,
            StatefulCall(nameof(Xsl3.Runtime.XsltLibrary.X31CardinalityEnd), T.Empty, state)));
        return PreserveForwardSequenceProofs(source, result, function is "zero-or-one" or "exactly-one" || DisjointChildAnchors(expression));
    }

    private QilNode ForwardTrace(Expr call, QName name, Expr expression, Expr? labelExpression)
    {
        QilNode source = Build(expression);
        QilIterator? label = labelExpression == null ? null : _f.Let(StatefulCall(
            nameof(Xsl3.Runtime.XsltLibrary.X31TraceLabel), T.StringX, AtomicOperand(labelExpression)));
        QilNode population = Items(source);
        QilIterator? cursor = null;
        if (!population.XmlType!.IsEmpty && !population.XmlType.IsSingleton && _env?.LiftPopulation(population) is { } producer)
        {
            // Both branches consume the same suspended population. Lifting its body once
            // prevents exponential QIL growth when trace calls are nested.
            cursor = _f.Let(StatefulCall(nameof(Xsl3.Runtime.XsltLibrary.X31PullNew), T.Item, producer));
            population = StatefulCall(nameof(Xsl3.Runtime.XsltLibrary.X31PullNext), T.ItemS, cursor);
            Xsl3.IlGen.OptimizerPatterns.Write(population).AddPattern(Xsl3.IlGen.OptimizerPatternName.PullSequence);
        }
        QilIterator item = _f.For(population);
        QilNode prepared = _f.Loop(item, StatefulCall(nameof(Xsl3.Runtime.XsltLibrary.X31TraceItem), T.Item, item));
        var arguments = new List<QilNode> { prepared };
        if (label != null) arguments.Add(Items(label));
        QilNode diagnostic = CallLibrary(call, name, arguments);
        // trace#1 has an absorbing operand; preserve readable original nodes even without
        // a handler. trace#2 transmits its operand and needs no buffering on that path.
        QilNode transparent = new QilCloneVisitor(_qf).Clone(label == null ? prepared : population);
        if (source.XmlType!.IsSubtypeOf(T.NodeNotRtfS))
        {
            diagnostic = _f.TypeAssert(diagnostic, T.NodeNotRtfS);
            transparent = _f.TypeAssert(transparent, T.NodeNotRtfS);
        }
        QilNode result = _f.Conditional(StatefulCall(nameof(Xsl3.Runtime.XsltLibrary.X31HasTraceHandler), T.BooleanX), diagnostic, transparent);
        if (cursor != null) result = _f.Loop(cursor, result);
        if (label != null) result = _f.Loop(label, result);
        return PreserveForwardSequenceProofs(source, result, DisjointChildAnchors(expression));
    }

    private QilNode ForwardIntegerPredicate(QilNode source, QilNode position)
    {
        QilIterator state = _f.Let(StatefulCall(nameof(Xsl3.Runtime.XsltLibrary.X31IntegerWindowNew), T.Item, position));
        QilNode gated = Streaming.StreamedLoopGate.Create(_qf, source,
            StatefulCall(nameof(Xsl3.Runtime.XsltLibrary.X31IntegerWindowActive), T.BooleanX, state));
        QilIterator item = _f.For(gated);
        QilNode selected = _f.Filter(item, StatefulCall(nameof(Xsl3.Runtime.XsltLibrary.X31IntegerWindowAccept), T.BooleanX, state));
        QilNode result = _f.Loop(state, _f.TypeAssert(selected,
            source.XmlType!.IsSubtypeOf(T.NodeNotRtfS) ? T.NodeNotRtfQ : T.PrimeProduct(T.Item, XmlQueryCardinality.ZeroOrOne)));
        return PreserveForwardSequenceProofs(source, result, disjoint: true);
    }

    private static QilNode PreserveForwardSequenceProofs(QilNode source, QilNode result, bool disjoint)
    {
        if (AtomicItems(source)) s_atomicItems.AddOrUpdate(result, AtomicTypes.AnyAtomicType);
        if (disjoint || source.XmlType!.IsSingleton || s_sameDepth.TryGetValue(source, out _)) SameDepth(result);
        else if (s_orderedChildren.TryGetValue(source, out QilNode? ordered)) s_orderedChildren.AddOrUpdate(result, ordered);
        return result;
    }

    private QilNode ForwardInsertBefore(QilNode source, QilNode position, QilNode inserts, bool disjoint)
    {
        if (inserts.XmlType!.IsEmpty)
        {
            // Keep an opaque empty iterator so FoldNone cannot extract validation from
            // surrounding empty iterator bindings and violate the read-region contract.
            QilNode identity = _f.Sequence(StatefulCall(nameof(Xsl3.Runtime.XsltLibrary.X31InsertValidate), T.NodeNotRtfS, position), source, inserts);
            return PreserveForwardSequenceProofs(source, identity, disjoint);
        }
        QilIterator state = _f.Let(StatefulCall(nameof(Xsl3.Runtime.XsltLibrary.X31InsertNew), T.Item, position));
        QilIterator item = _f.For(source);
        QilNode before = _f.Conditional(StatefulCall(nameof(Xsl3.Runtime.XsltLibrary.X31InsertBefore), T.BooleanX, state), inserts, _f.Sequence());
        QilNode tail = _f.Conditional(StatefulCall(nameof(Xsl3.Runtime.XsltLibrary.X31InsertTail), T.BooleanX, state),
            new QilCloneVisitor(_qf).Clone(inserts), _f.Sequence());
        QilNode result = _f.Loop(state, _f.Sequence(_f.Loop(item, _f.Sequence(before, item)), tail));
        if (AtomicItems(source) && AtomicItems(inserts)) s_atomicItems.AddOrUpdate(result, AtomicTypes.AnyAtomicType);
        return result;
    }

    private QilNode ForwardRemove(QilNode source, QilNode position)
    {
        QilIterator state = _f.Let(StatefulCall(nameof(Xsl3.Runtime.XsltLibrary.X31RemoveNew), T.Item, position));
        QilIterator item = _f.For(Items(source));
        QilNode result = _f.Loop(state, _f.Filter(item, StatefulCall(nameof(Xsl3.Runtime.XsltLibrary.X31RemoveAccept), T.BooleanX, state)));
        if (AtomicItems(source)) s_atomicItems.AddOrUpdate(result, AtomicTypes.AnyAtomicType);
        return result;
    }

    private QilNode ForwardSubsequence(QilNode source, QilNode start, QilNode? length, bool head = false)
    {
        QilIterator state = _f.Let(StatefulCall(nameof(Xsl3.Runtime.XsltLibrary.X31SubsequenceNew), T.Item,
            Items(start), length == null ? _f.Sequence() : Items(length), _f.Boolean(length != null)));
        QilNode gated = Streaming.StreamedLoopGate.Create(_qf, source,
            StatefulCall(nameof(Xsl3.Runtime.XsltLibrary.X31SubsequenceActive), T.BooleanX, state));
        QilNode selected = _qf.Filter(_qf.For(gated), StatefulCall(nameof(Xsl3.Runtime.XsltLibrary.X31SubsequenceAccept), T.BooleanX, state));
        if (head) selected = _f.TypeAssert(selected, source.XmlType!.IsSubtypeOf(T.NodeNotRtfS) ? T.NodeNotRtfQ : T.PrimeProduct(T.Item, XmlQueryCardinality.ZeroOrOne));
        return _f.Loop(state, selected);
    }

    private QilNode ForwardDynamicCall(QilNode function, IReadOnlyList<QilNode> arguments)
    {
        var bindings = new List<QilIterator>();
        QilIterator state = _f.Let(StatefulCall(nameof(Xsl3.Runtime.XsltLibrary.X31CallNew), T.Item, function, _f.Double(arguments.Count)));
        bindings.Add(state);
        foreach (QilNode argument in arguments)
        {
            QilIterator item = _f.For(argument);
            QilNode seen = _f.Length(_f.Loop(item, StatefulCall(nameof(Xsl3.Runtime.XsltLibrary.X31CallArgumentItem), T.IntX, state, item)));
            state = _f.Let(StatefulCall(nameof(Xsl3.Runtime.XsltLibrary.X31CallArgumentComplete), T.Item, state, _f.XsltConvert(seen, T.DoubleX)));
            bindings.Add(state);
        }
        QilNode result = _f.Invoke(Registry.Dispatcher, _f.ActualParameterList(state));
        for (int i = bindings.Count - 1; i >= 0; i--) result = _f.Loop(bindings[i], result);
        return result;
    }

    private QilNode StringArguments(QilNode value)
    {
        QilIterator item = _f.For(Items(value));
        QilNode text = Call(nameof(Xsl3.Runtime.XsltLibrary.X31StringItem), T.Item,
            _f.XPathNodeValue(ValueReplayOperand(_f.TypeAssert(item, T.NodeNotRtf), value)));
        // fn:string uses the node's string value, including for schema-typed nodes. Preserve other
        // items so the canonical function still enforces cardinality and FOTY0014 for function items.
        if (!value.XmlType!.IsSubtypeOf(T.NodeNotRtfS)) text = _f.Conditional(_f.IsType(item, T.NodeNotRtf), text, item);
        return _f.Loop(item, text);
    }

    private QilNode AtomizeEach(Expr call, QilNode value, bool preserveTextNodes = false)
    {
        QilIterator item = _f.For(Items(value));
        QilNode operand = ValueReplayOperand(item, value);
        if (preserveTextNodes)
            return _f.Loop(item, Call(nameof(Xsl3.Runtime.XsltLibrary.X31SimpleContentItem), T.Item, operand));
        QilNode atomized = CallLibrary(call, new QName(QName.Ns.Fn, "data"), new List<QilNode> { operand });
        return _f.Loop(item, atomized);
    }

    // Fuse a complete name test into the descendant iterator, so it knows exactly which accepted
    // subtrees outermost excludes. Predicates and partial name tests need acceptance after filtering;
    // pruning their rejected candidates would incorrectly omit nested matches.
    private QilNode? ForwardOutermost(Expr input)
    {
        // Canonicalize arbitrary grounded arguments, while ordered forward populations can retain
        // their iterator proof after function lowering. Only the last accepted ancestor is retained.
        QilNode nodes = _f.DocOrderDistinct(Nodes(Build(input), "XPTY0004"));
        if (_env?.LiftPopulation(Items(nodes)) is not { } producer) return null;
        QilIterator state = _f.Let(StatefulCall(nameof(Xsl3.Runtime.XsltLibrary.X31OutermostNew), T.Item, producer));
        QilNode next = StatefulCall(nameof(Xsl3.Runtime.XsltLibrary.X31OutermostNext), T.NodeSDod, state);
        Xsl3.IlGen.OptimizerPatterns.Write(next).AddPattern(Xsl3.IlGen.OptimizerPatternName.PullSequence);
        return _f.Loop(state, next);
    }

    private QilNode? OutermostStep(Expr input)
    {
        static bool CompleteTest(NodeTest test) => test is NameTest name &&
            (name.LocalName != null && !name.AnyNamespace || name.LocalName == null && name.Prefix == null && name.BracedUri == null);

        if (DisjointChildAnchors(input))
        {
            // A singleton or a child-only path is already ordered and cannot contain overlapping nodes.
            // Keep it as an iterator rather than collecting it for an identity outermost call.
            QilNode disjoint = Build(input);
            if (disjoint.XmlType!.IsSubtypeOf(T.NodeNotRtfS)) return disjoint;
        }

        QilNode Descendants(AxisStep step, Axis axis) => StepFor(axis, step.Test, Array.Empty<Expr>(), topmost: true);
        if (input is AxisStep { Axis: Axis.Descendant or Axis.DescendantOrSelf, Predicates.Count: 0 } direct && CompleteTest(direct.Test))
            return Descendants(direct, direct.Axis);
        if (input is PathExpr { Right: AxisStep { Predicates.Count: 0 } step } path && DisjointChildAnchors(path.Left) && CompleteTest(step.Test))
        {
            if (path.Descendants && step.Axis == Axis.Child)
                return Join(Build(path.Left), () => Descendants(step, Axis.Descendant), needsLast: false);
            if (!path.Descendants && step.Axis is Axis.Descendant or Axis.DescendantOrSelf)
                return Join(Build(path.Left), () => Descendants(step, step.Axis), needsLast: false);
        }

        // A//P/C can overlap through nested P nodes. Scan C once and test its parent while it is
        // available, pruning only candidates that match the complete P/C test. Collecting the path
        // first would retain navigators whose content has already passed the forward reader.
        if (input is PathExpr { Descendants: false, Right: AxisStep { Axis: Axis.Child, Predicates.Count: 0,
                Test: NameTest { LocalName: not null, AnyNamespace: false } child },
                Left: PathExpr { Descendants: true, Right: AxisStep { Axis: Axis.Child, Predicates.Count: 0,
                    Test: NameTest { LocalName: not null, AnyNamespace: false } parent } } scan }
            && DisjointChildAnchors(scan.Left))
        {
            QName Expand(NameTest name) => _types.Expand(new LexicalQName(name.Prefix, name.LocalName!, name.BracedUri), _sc.DefaultElementNamespace);
            QName p = Expand(parent), c = Expand(child);
            var (anchor, body, binding) = WithFocus(Nodes(Build(scan.Left), "XPTY0019"), needsLast: false, () =>
            {
                QilIterator state = _f.Let(StatefulCall(nameof(Xsl3.Runtime.XsltLibrary.X31OutermostChildNew), T.Item,
                    FocusNode(), _f.String(p.LocalName), _f.String(p.NamespaceUri), _f.String(c.LocalName), _f.String(c.NamespaceUri)));
                QilNode next = StatefulCall(nameof(Xsl3.Runtime.XsltLibrary.X31OutermostChildNext), T.NodeNotRtfS, state);
                Xsl3.IlGen.OptimizerPatterns.Write(next).AddPattern(Xsl3.IlGen.OptimizerPatternName.PullSequence);
                return _f.Loop(state, next);
            });
            // Child-only anchors are disjoint and ordered; each cursor is also ordered and distinct.
            // A general path-result sort here would collect the live nodes before their consumer.
            return WrapLet(binding, _f.Loop(anchor, body));
        }
        return null;
    }
}
