// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Linq;
using Xsl3.Xdm;

namespace Xsl3.Streaming;

internal sealed partial class ExpressionAnalysis
{
    internal static OperandUsage TypeDeterminedUsage(ItemTest? type) => type switch
    {
        FunctionItemTest or MapTest or ArrayTest => OperandUsage.Inspection,
        AtomicTypeTest or UnionTypeTest or NumericTest => OperandUsage.Absorption,
        _ => OperandUsage.Navigation,
    };

    private static Streamability Grounded(UType type, string rule, Sweep sweep = Sweep.Motionless) => new(type, Posture.Grounded, sweep, rule);
    private static Streamability Roaming(UType type, string rule) => new(type, Posture.Roaming, Sweep.FreeRanging, rule);

    /// <summary>XSLT 3.0 §19.8.1. Shared by expressions, function calls, and (later) instructions.</summary>
    internal static Streamability General_19_8_1(UType type, string rule, params AnalysisOperand[] operands)
    {
        var potentiallyConsuming = new List<(AnalysisOperand Operand, Sweep Adjusted)>();
        foreach (AnalysisOperand operand in operands)
        {
            Streamability value = operand.Value;
            if (value.Sweep == Sweep.FreeRanging || value.Posture == Posture.Roaming)
            {
                return Roaming(type, rule);
            }
            Sweep adjusted = value.Sweep;
            OperandUsage usage = operand.Usage;
            if (usage == OperandUsage.Absorption && (value.Type & UType.ParentNodes) == 0)
            {
                usage = OperandUsage.Inspection;
            }
            if (value.Posture != Posture.Grounded)
            {
                adjusted = usage switch
                {
                    OperandUsage.Navigation => Sweep.FreeRanging,
                    OperandUsage.Absorption when value.Posture == Posture.Climbing => Sweep.FreeRanging,
                    OperandUsage.Absorption => Sweep.Consuming,
                    _ => value.Sweep,
                };
            }
            if (adjusted == Sweep.FreeRanging) return Roaming(type, rule);
            if (adjusted == Sweep.Consuming || operand.Usage == OperandUsage.Transmission && value.Posture != Posture.Grounded)
            {
                potentiallyConsuming.Add((operand, adjusted));
            }
        }
        if (potentiallyConsuming.Count > 1)
        {
            int group = potentiallyConsuming[0].Operand.ChoiceGroup;
            if (group != 0 && potentiallyConsuming.All(o => o.Operand.ChoiceGroup == group))
            {
                Posture posture = CombinedPosture(potentiallyConsuming.Select(o => o.Operand.Value.Posture));
                return posture == Posture.Roaming ? Roaming(type, rule)
                    : new(type, posture, potentiallyConsuming.Max(o => o.Operand.Value.Sweep), rule);
            }
            Posture same = potentiallyConsuming[0].Operand.Value.Posture;
            if (potentiallyConsuming.All(o => o.Adjusted == Sweep.Motionless && o.Operand.Value.Posture == same))
            {
                return new(type, same, Sweep.Motionless, rule);
            }
            return Roaming(type, rule);
        }
        if (potentiallyConsuming.Count == 1)
        {
            var (operand, sweep) = potentiallyConsuming[0];
            if (operand.HigherOrder) return Roaming(type, rule);
            return operand.Usage is OperandUsage.Absorption or OperandUsage.Inspection
                ? Grounded(type, rule, Sweep.Consuming) : new(type, operand.Value.Posture, sweep, rule);
        }
        return Grounded(type, rule);
    }

    private static Posture CombinedPosture(IEnumerable<Posture> postures)
    {
        var kinds = postures.Where(p => p != Posture.Grounded).ToHashSet();
        if (kinds.Count == 0) return Posture.Grounded;
        if (kinds.Contains(Posture.Roaming)) return Posture.Roaming;
        if (kinds.Count == 1) return kinds.Single();
        return kinds.SetEquals(new[] { Posture.Striding, Posture.Crawling }) ? Posture.Crawling : Posture.Roaming;
    }
}
