// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Linq;
using Xsl3.XPath31;
using Xsl3.Xdm;

namespace Xsl3.Streaming;

internal sealed partial class ExpressionAnalysis
{
    private Streamability FocusVisit(Expr expression, StreamabilityContext context, bool predicate = false)
    {
        _focusDepth++;
        _higherOrderDepth++;
        try { return Visit(expression, context, predicate); }
        finally { _focusDepth--; _higherOrderDepth--; }
    }

    private UType SequenceType(IReadOnlyList<Expr> expressions, StreamabilityContext context, out AnalysisOperand[] operands)
    {
        operands = expressions.Select(e => new AnalysisOperand(Visit(e, context), OperandUsage.Transmission)).ToArray();
        return operands.Aggregate(UType.Empty, (type, operand) => type | operand.Value.Type);
    }

    private Streamability Binary_19_8_8(BinaryExpr expression, StreamabilityContext context, bool predicate)
    {
        Streamability left = Visit(expression.Left, context), right = Visit(expression.Right, context);
        BinaryOp op = expression.Op;
        if (op is BinaryOp.Union or BinaryOp.Intersect or BinaryOp.Except)
        {
            UType resultType = op == BinaryOp.Union ? left.Type | right.Type : op == BinaryOp.Intersect ? left.Type & right.Type : left.Type;
            const string rule = "19.8.8.4";
            if (left.Sweep == Sweep.FreeRanging || right.Sweep == Sweep.FreeRanging) return Roaming(resultType, rule);
            if (left.Posture == Posture.Grounded && left.Sweep == Sweep.Motionless) return right with { Type = resultType, Rule = rule };
            if (right.Posture == Posture.Grounded && right.Sweep == Sweep.Motionless) return left with { Type = resultType, Rule = rule };
            if (left.Posture == Posture.Climbing && right.Posture == Posture.Climbing)
                return new(resultType, Posture.Climbing, Wider(left.Sweep, right.Sweep), rule);
            if (left.Posture is Posture.Striding or Posture.Crawling && right.Posture is Posture.Striding or Posture.Crawling)
            {
                if (_options.AllowStreamabilityRewrites && op == BinaryOp.Except && left.Posture == Posture.Striding
                    && expression.Right is ContextItemExpr && right.Sweep == Sweep.Motionless)
                    return left with { Type = resultType, Rule = rule };
                // Children of the same peer selection remain peers when united. This is a
                // compiler extension; the syntax-only harness retains the general crawling rule.
                if (_options.AllowStreamabilityRewrites && op == BinaryOp.Union && left.Posture == Posture.Striding && right.Posture == Posture.Striding
                    && expression.Left is PathExpr { Descendants: false, Right: AxisStep { Axis: Axis.Child } } lp
                    && expression.Right is PathExpr { Descendants: false, Right: AxisStep { Axis: Axis.Child } } rp
                    && lp.Left.ToString() == rp.Left.ToString())
                    return new(resultType, Posture.Striding, Wider(left.Sweep, right.Sweep), rule);
                return new(resultType, Posture.Crawling, Wider(left.Sweep, right.Sweep), rule);
            }
            return Roaming(resultType, rule);
        }
        bool inspection = op is BinaryOp.Or or BinaryOp.And or BinaryOp.Is or BinaryOp.Precedes or BinaryOp.Follows;
        UType type = op switch
        {
            BinaryOp.StringConcat => UType.String,
            BinaryOp.Range => UType.Decimal,
            BinaryOp.Add or BinaryOp.Subtract or BinaryOp.Multiply or BinaryOp.Divide or BinaryOp.IntegerDivide or BinaryOp.Modulo =>
                predicate ? UType.Numeric : UType.Atomic,
            _ => UType.Boolean,
        };
        OperandUsage usage = inspection ? OperandUsage.Inspection : OperandUsage.Absorption;
        return General_19_8_1(type, "19.8.1", new(left, usage), new(right, usage));
    }

    private Streamability If_19_8_8_3(IfExpr expression, StreamabilityContext context)
    {
        Streamability condition = Visit(expression.Condition, context), then = Visit(expression.Then, context), otherwise = Visit(expression.Else, context);
        return General_19_8_1(then.Type | otherwise.Type, "19.8.8.3", new(condition, OperandUsage.Inspection),
            new(then, OperandUsage.Transmission, ChoiceGroup: 1), new(otherwise, OperandUsage.Transmission, ChoiceGroup: 1))
            with { ItemType = Equals(then.ItemType, otherwise.ItemType) ? then.ItemType : null };
    }

    private Streamability Variable_19_8_8_12(VarRef variable)
    {
        QName name = _options.Types.Expand(variable.Name, "");
        if (!_variables.TryGetValue(name, out Streamability value))
            throw new XPathStaticException("XPST0008", $"Variable {name.EQName} is not declared", variable.Span.Start);
        // A captured streamed node in a repeated operand can be inspected as an ancestor, but
        // cannot be consumed again or returned repeatedly as a peer sequence (§19.8.4.18-22 notes).
        if ((_higherOrderDepth != 0 || _options.StreamingParameterRepeated) && value.Posture is Posture.Striding or Posture.Crawling)
            return value.Sweep == Sweep.Consuming ? Roaming(value.Type, "19.8.8.12")
                : value with { Posture = Posture.Climbing, Rule = "19.8.8.12" };
        return value with { Rule = "19.8.8.12" };
    }

    private enum BindingKind { Let, For, Quantified }

    private Streamability Bindings_19_8_8(IReadOnlyList<Binding> bindings, int index, Expr body, StreamabilityContext context, BindingKind kind)
    {
        if (index == bindings.Count) return Visit(body, context);
        Binding binding = bindings[index];
        Streamability value = Visit(binding.Value, context);
        QName name = _options.Types.Expand(binding.Variable, "");
        bool hadOld = _variables.TryGetValue(name, out Streamability old);
        if (_options.AllowStreamabilityRewrites && kind == BindingKind.Quantified && index == bindings.Count - 1
            && value.Posture is Posture.Striding or Posture.Crawling && value.Sweep != Sweep.FreeRanging
            && !UsesOuterFocus(body) && !CapturesStreamedVariable(body, new() { name }))
        {
            // A focus-independent, motionless predicate can be evaluated once per selected node,
            // just as exists(S[P]) / empty(S[not(P)]), without retaining the streamed binding.
            _variables[name] = value with { Sweep = Sweep.Motionless };
            Streamability predicate;
            try { predicate = Visit(body, context); }
            finally { if (hadOld) _variables[name] = old; else _variables.Remove(name); }
            if (predicate.Sweep == Sweep.Motionless)
                return Grounded(UType.Boolean, "19.8.8.2", value.Sweep);
        }
        _variables[name] = Grounded(value.Type, "19.8.8.12") with { ItemType = value.ItemType };
        Streamability result;
        if (kind != BindingKind.Let) _higherOrderDepth++;
        try { result = Bindings_19_8_8(bindings, index + 1, body, context, kind); }
        finally
        {
            if (kind != BindingKind.Let) _higherOrderDepth--;
            if (hadOld) _variables[name] = old; else _variables.Remove(name);
        }
        string rule = kind == BindingKind.For ? "19.8.8.1" : kind == BindingKind.Quantified ? "19.8.8.2" : "19.8.1";
        UType type = kind == BindingKind.Quantified ? UType.Boolean : result.Type;
        if (kind == BindingKind.For && value.Posture != Posture.Grounded) return Roaming(type, rule);
        return General_19_8_1(type, rule, new AnalysisOperand(value, OperandUsage.Navigation),
            new(result, kind == BindingKind.Quantified ? OperandUsage.Inspection : OperandUsage.Transmission, kind != BindingKind.Let))
            with { ItemType = kind == BindingKind.Quantified ? null : result.ItemType };
    }

    private SequenceTypeTest ResolveType(SequenceType type) => ResolveType(_options.Types, type);

    internal static SequenceTypeTest ResolveType(StaticTypeResolver types, SequenceType type)
    {
        if (type.Item is AtomicOrUnionType atomic && types.Expand(atomic.Name, types.DefaultElementNamespace) == QName.Xs("error"))
            return SequenceTypeTest.EmptySequence;
        return types.Resolve(type);
    }

    private UType CastType(LexicalQName name)
    {
        QName expanded = _options.Types.Expand(name, _options.Types.DefaultElementNamespace);
        if (expanded == QName.Xs("error")) return UType.Empty;
        if (expanded == QName.Xs("numeric")) return UType.Numeric;
        if (StaticTypeResolver.FindCastTarget(expanded) is { } builtIn)
            return builtIn.Kind == CastKind.List ? UType.Atomic : UTypes.FromAtomic(builtIn.Type);
        // The resolver already owns schema restriction and union membership resolution.
        return UTypes.FromSequence(ResolveType(new(new AtomicOrUnionType(name), Occurrence.ZeroOrMore)));
    }

    private Streamability TypeTest_19_8_8(Expr operand, SequenceType type, StreamabilityContext context, bool treat)
    {
        Streamability value = Visit(operand, context);
        SequenceTypeTest resolved = ResolveType(type);
        bool documentContent = type.Item is DocumentTest { ElementTest: not null };
        string rule = treat ? "19.8.8.6" : "19.8.8.5";
        if (treat && documentContent) return Roaming(UTypes.FromSequence(resolved), rule);
        return General_19_8_1(treat ? UTypes.FromSequence(resolved) : UType.Boolean, rule,
            new AnalysisOperand(value, treat ? OperandUsage.Transmission : documentContent ? OperandUsage.Absorption : OperandUsage.Inspection))
            with { ItemType = treat ? resolved.Item : null };
    }

    private Streamability Filter_19_8_8_10(FilterExpr expression, StreamabilityContext context)
    {
        Streamability value = Visit(expression.Base, context);
        Streamability predicate = FocusVisit(expression.Predicate, new(value.Type, value.Posture) { ItemType = value.ItemType }, true);
        if (value.Posture == Posture.Crawling && IsFocusIndependentNumeric(expression.Predicate, predicate))
            return value with { Posture = Posture.Striding, Rule = "19.8.8.10" };
        return predicate.Sweep == Sweep.Motionless ? value with { Rule = "19.8.8.10" } : Roaming(value.Type, "19.8.8.10");
    }

    private bool IsFocusIndependentNumeric(Expr expression, Streamability value) => (value.Type & ~UType.Numeric) == 0 && !UsesOuterFocus(expression);

    private bool UsesOuterFocus(Expr expression) => expression switch
    {
        ContextItemExpr or AxisStep or RootExpr or UnaryLookup => true,
        FunctionCall call => IsFocusDependent(ExpandFunction(call.Name), call.Arguments.Count) || call.Arguments.Any(UsesOuterFocus),
        NamedFunctionRef reference => IsFocusDependent(ExpandFunction(reference.Name), CheckedArity(reference.Arity)),
        ArrowExpr arrow => arrow.FunctionName is { } name && IsFocusDependent(ExpandFunction(name), arrow.Arguments.Count + 1)
            || ChildrenOf(arrow).Any(UsesOuterFocus),
        PathExpr path => UsesOuterFocus(path.Left),
        SimpleMapExpr map => UsesOuterFocus(map.Left),
        FilterExpr filter => UsesOuterFocus(filter.Base),
        InlineFunctionExpr => false,
        _ => ChildrenOf(expression).Any(UsesOuterFocus),
    };

    private Streamability Map_19_8_8_17(MapConstructor map, StreamabilityContext context)
    {
        // Each entry is an independent single-pass consumer (§19.8.4.23); unlike comma,
        // multiple grounded consuming entries are permitted.
        Streamability[] entries = map.Entries.Select(e => General_19_8_1(UType.Function, "19.8.4.24",
            new(Visit(e.Key, context), OperandUsage.Absorption), new AnalysisOperand(Visit(e.Value, context), OperandUsage.Navigation))).ToArray();
        return (entries.Any(e => e.Posture == Posture.Roaming || e.Sweep == Sweep.FreeRanging)
            ? Roaming(UType.Function, "19.8.8.17")
            : Grounded(UType.Function, "19.8.8.17", entries.Aggregate(Sweep.Motionless, (s, e) => Wider(s, e.Sweep))))
            with { ItemType = MapTest.Any };
    }

    private Streamability Array_19_8_8(IReadOnlyList<Expr> members, StreamabilityContext context) =>
        General_19_8_1(UType.Function, "19.8.1", members.Select(e => new AnalysisOperand(Visit(e, context), OperandUsage.Navigation)).ToArray())
            with { ItemType = ArrayTest.Any };

    private Streamability Lookup_19_8_8_18(Expr expression, KeySpecifier key, StreamabilityContext context)
    {
        Streamability value = Visit(expression, context);
        SequenceTypeTest? member = value.ItemType switch { MapTest map => map.ValueType, ArrayTest array => array.MemberType, _ => null };
        UType type = member == null ? UType.Any : UTypes.FromSequence(member);
        var operands = new List<AnalysisOperand> { new(value, OperandUsage.Inspection) };
        if (key.Expr != null) operands.Add(new(Visit(key.Expr, context), OperandUsage.Absorption));
        return General_19_8_1(type, "19.8.8.18", operands.ToArray()) with { ItemType = member?.Item };
    }

    private bool ScanningPattern(Expr expression, StreamabilityContext context, bool allowLeadingDot = false)
    {
        // A leading slash has already reduced to the document context item (§19.8.9.18).
        // It may participate in a scanning path only in that case, never after climbing to a root.
        if (expression is RootExpr) return context.Type == UType.Document && context.Posture == Posture.Striding;
        if (expression is ContextItemExpr) return allowLeadingDot && context.Posture == Posture.Striding;
        if (expression is PathExpr path)
            return ScanningPattern(path.Left, context, allowLeadingDot) && ScanningPattern(path.Right, context, allowLeadingDot);
        if (expression is BinaryExpr { Op: BinaryOp.Union or BinaryOp.Intersect or BinaryOp.Except } binary)
            return ScanningPattern(binary.Left, context) && ScanningPattern(binary.Right, context);
        if (expression is FilterExpr filter)
        {
            if (!ScanningPattern(filter.Base, context)) return false;
            UType matched = Visit(filter.Base, new(UType.Nodes, Posture.Striding)).Type;
            Streamability predicate = FocusVisit(filter.Predicate, new(matched, Posture.Striding), true);
            return predicate.Sweep == Sweep.Motionless && (predicate.Type & UType.Numeric) == 0 && !HasPositionalFunction(filter.Predicate);
        }
        if (expression is not AxisStep step || step.Axis is not (Axis.Child or Axis.Descendant or Axis.Attribute or Axis.Self or Axis.DescendantOrSelf or Axis.Namespace)) return false;
        UType type = AxisType_19_1_1(step, UType.Nodes);
        foreach (Expr predicate in step.Predicates)
        {
            Streamability assessed = FocusVisit(predicate, new(type, Posture.Striding), true);
            if (assessed.Sweep != Sweep.Motionless || (assessed.Type & UType.Numeric) != 0 || HasPositionalFunction(predicate)) return false;
        }
        return true;
    }

    private bool MotionlessPredicatePattern(Expr expression)
    {
        if (expression is ContextItemExpr) return true;
        if (expression is not FilterExpr filter || !MotionlessPredicatePattern(filter.Base)) return false;
        Streamability value = FocusVisit(filter.Predicate, new(UType.Any, Posture.Striding), true);
        return value.Sweep == Sweep.Motionless && (value.Type & UType.Numeric) == 0 && !HasPositionalFunction(filter.Predicate);
    }

    private bool HasPositionalFunction(Expr expression)
    {
        QName? name = expression switch
        {
            FunctionCall call => ExpandFunction(call.Name),
            NamedFunctionRef reference => ExpandFunction(reference.Name),
            ArrowExpr { FunctionName: { } function } => ExpandFunction(function),
            _ => null,
        };
        if (name?.NamespaceUri == QName.Ns.Fn && name.LocalName is "position" or "last" or "function-lookup") return true;
        // Nested predicates establish their own position and do not make the containing predicate positional.
        return expression switch
        {
            AxisStep => false,
            FilterExpr filter => HasPositionalFunction(filter.Base),
            _ => ChildrenOf(expression).Any(HasPositionalFunction),
        };
    }

    private Streamability Castable(CastableExpr expression, StreamabilityContext context)
    {
        _ = CastType(expression.Type.TypeName);
        return General_19_8_1(UType.Boolean, "19.8.1", new AnalysisOperand(Visit(expression.Operand, context), OperandUsage.Absorption));
    }

    private static IEnumerable<Expr> ChildrenOf(Expr expression) => expression switch
    {
        BinaryExpr b => new[] { b.Left, b.Right }, UnaryExpr u => new[] { u.Operand },
        SequenceExpr s => s.Items, IfExpr c => new[] { c.Condition, c.Then, c.Else },
        PathExpr p => new[] { p.Left, p.Right }, SimpleMapExpr m => new[] { m.Left, m.Right },
        AxisStep s => s.Predicates, FilterExpr f => new[] { f.Base, f.Predicate },
        ForExpr f => f.Bindings.Select(b => b.Value).Append(f.Return),
        LetExpr l => l.Bindings.Select(b => b.Value).Append(l.Return),
        QuantifiedExpr q => q.Bindings.Select(b => b.Value).Append(q.Satisfies),
        InstanceOfExpr t => new[] { t.Operand }, TreatExpr t => new[] { t.Operand },
        CastExpr c => new[] { c.Operand }, CastableExpr c => new[] { c.Operand },
        FunctionCall c => c.Arguments, DynamicFunctionCall c => c.Arguments.Prepend(c.Function),
        ArrowExpr a => a.Arguments.Prepend(a.Target).Concat(a.FunctionExpr == null ? Array.Empty<Expr>() : new[] { a.FunctionExpr }),
        InlineFunctionExpr f => new[] { f.Body },
        MapConstructor m => m.Entries.SelectMany(e => new[] { e.Key, e.Value }),
        SquareArrayConstructor a => a.Members, CurlyArrayConstructor a => a.Content == null ? Array.Empty<Expr>() : new[] { a.Content },
        UnaryLookup l => l.Key.Expr == null ? Array.Empty<Expr>() : new[] { l.Key.Expr },
        PostfixLookup l => l.Key.Expr == null ? new[] { l.Base } : new[] { l.Base, l.Key.Expr },
        _ => Array.Empty<Expr>(),
    };
}
