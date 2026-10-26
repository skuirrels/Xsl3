// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;

namespace Xsl3.XPath31;

[Flags]
internal enum FocusDependency
{
    None = 0,
    Position = 1,
    Size = 2,
}

/// <summary>Numeric focus captured from an expression's incoming context, before local focus bindings.</summary>
internal static class FocusAnalysis
{
    internal static FocusDependency Analyze(Expr expression, Func<LexicalQName, bool> isBuiltin)
    {
        FocusDependency Visit(Expr? value)
        {
            if (value == null) return FocusDependency.None;
            switch (value)
            {
                case FunctionCall call:
                    return Sum(call.Arguments) | Function(call.Name, call.Arguments.Count);
                case NamedFunctionRef reference:
                    return reference.Arity.IsZero ? Function(reference.Name, 0)
                        : reference.Arity == 2 ? Function(reference.Name, 2) : FocusDependency.None;
                // Path/map right operands and predicates use the focus bound by their own population.
                case PathExpr path: return Visit(path.Left);
                case SimpleMapExpr map: return Visit(map.Left);
                case FilterExpr filter: return Visit(filter.Base);
                case AxisStep: return FocusDependency.None;
                // Inline function bodies start with absent focus, not the creator's focus.
                case InlineFunctionExpr: return FocusDependency.None;
                case DynamicFunctionCall call: return Visit(call.Function) | Sum(call.Arguments);
                case ArrowExpr arrow:
                    return Visit(arrow.Target) | Visit(arrow.FunctionExpr) | Sum(arrow.Arguments)
                        | (arrow.FunctionName is { } name ? Function(name, arrow.Arguments.Count + 1) : FocusDependency.None);
                case BinaryExpr binary: return Visit(binary.Left) | Visit(binary.Right);
                case UnaryExpr unary: return Visit(unary.Operand);
                case SequenceExpr sequence: return Sum(sequence.Items);
                case IfExpr conditional: return Visit(conditional.Condition) | Visit(conditional.Then) | Visit(conditional.Else);
                case ForExpr loop:
                {
                    FocusDependency result = Visit(loop.Return);
                    foreach (Binding binding in loop.Bindings) result |= Visit(binding.Value);
                    return result;
                }
                case LetExpr let:
                {
                    FocusDependency result = Visit(let.Return);
                    foreach (Binding binding in let.Bindings) result |= Visit(binding.Value);
                    return result;
                }
                case QuantifiedExpr quantified:
                {
                    FocusDependency result = Visit(quantified.Satisfies);
                    foreach (Binding binding in quantified.Bindings) result |= Visit(binding.Value);
                    return result;
                }
                case InstanceOfExpr test: return Visit(test.Operand);
                case TreatExpr test: return Visit(test.Operand);
                case CastExpr cast: return Visit(cast.Operand);
                case CastableExpr cast: return Visit(cast.Operand);
                case MapConstructor map:
                {
                    FocusDependency result = FocusDependency.None;
                    foreach (MapConstructorEntry entry in map.Entries) result |= Visit(entry.Key) | Visit(entry.Value);
                    return result;
                }
                case SquareArrayConstructor array: return Sum(array.Members);
                case CurlyArrayConstructor array: return Visit(array.Content);
                case PostfixLookup lookup: return Visit(lookup.Base) | Visit(lookup.Key.Expr);
                case UnaryLookup lookup: return Visit(lookup.Key.Expr);
                case IntegerLiteral or DecimalLiteral or DoubleLiteral or StringLiteral or VarRef or ContextItemExpr
                    or RootExpr or EmptySequenceExpr or ArgumentPlaceholder:
                    return FocusDependency.None;
                default:
                    // Future expressions must explicitly describe their focus binding before pruning arguments.
                    return FocusDependency.Position | FocusDependency.Size;
            }
        }

        FocusDependency Sum(IEnumerable<Expr> values)
        {
            FocusDependency result = FocusDependency.None;
            foreach (Expr value in values) result |= Visit(value);
            return result;
        }

        FocusDependency Function(LexicalQName name, int arity)
        {
            if (name.LocalName is not ("position" or "last" or "function-lookup") || !isBuiltin(name)) return FocusDependency.None;
            return name.LocalName switch
            {
                "position" when arity == 0 => FocusDependency.Position,
                "last" when arity == 0 => FocusDependency.Size,
                "function-lookup" when arity == 2 => FocusDependency.Position | FocusDependency.Size,
                _ => FocusDependency.None,
            };
        }

        return Visit(expression);
    }
}
