// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Linq;
using Xsl3.Xdm;

namespace Xsl3.Streaming;

/// <summary>XSLT 3.0 §19.8.5: the category belongs to the declaration, not inferred from its body.</summary>
internal enum FunctionStreamabilityCategory
{
    Unclassified, Absorbing, Inspection, Filter, ShallowDescent, DeepDescent, Ascent,
}

internal static class FunctionStreamability
{
    internal static AnalysisFunction Describe(FunctionItemTest signature, FunctionStreamabilityCategory category)
    {
        if (signature.Parameters == null || signature.ReturnType == null)
            throw new ArgumentException("A stylesheet function needs a complete signature", nameof(signature));
        if (category != FunctionStreamabilityCategory.Unclassified && signature.Parameters.Count == 0)
            throw new ArgumentException("XTSE3155: a declared-streamable function must have a parameter", nameof(signature));
        OperandUsage[] usages = signature.Parameters.Select(p => ExpressionAnalysis.TypeDeterminedUsage(p.Item)).ToArray();
        if (usages.Length > 0)
            usages[0] = category switch
            {
                FunctionStreamabilityCategory.Absorbing => OperandUsage.Absorption,
                FunctionStreamabilityCategory.Inspection => OperandUsage.Inspection,
                FunctionStreamabilityCategory.Filter or FunctionStreamabilityCategory.Ascent
                    or FunctionStreamabilityCategory.ShallowDescent or FunctionStreamabilityCategory.DeepDescent => OperandUsage.Transmission,
                _ => usages[0],
            };
        return new(signature, Array.AsReadOnly(usages), DeclaredStreamable: category != FunctionStreamabilityCategory.Unclassified) { Category = category };
    }

    internal static Streamability StreamingParameter(SequenceTypeTest type, FunctionStreamabilityCategory category) =>
        new(UTypes.FromSequence(type), category == FunctionStreamabilityCategory.Ascent ? Posture.Climbing : Posture.Striding,
            category == FunctionStreamabilityCategory.Absorbing && type.AllowsMany ? Sweep.Consuming : Sweep.Motionless,
            "19.8.5");

    internal static bool IsGuaranteed(FunctionItemTest signature, FunctionStreamabilityCategory category, Streamability result)
    {
        if (category == FunctionStreamabilityCategory.Unclassified) return true;
        if (signature.Parameters is not { Count: > 0 } parameters || signature.ReturnType == null) return false;
        if (category != FunctionStreamabilityCategory.Absorbing && parameters[0].AllowsMany) return false;
        Streamability adjusted = AdjustType(result, signature.ReturnType);
        return category switch
        {
            FunctionStreamabilityCategory.Absorbing => adjusted.Posture == Posture.Grounded && adjusted.Sweep != Sweep.FreeRanging,
            FunctionStreamabilityCategory.Inspection => adjusted.Posture == Posture.Grounded && adjusted.Sweep == Sweep.Motionless,
            FunctionStreamabilityCategory.Filter => adjusted.Posture == Posture.Striding && adjusted.Sweep == Sweep.Motionless,
            FunctionStreamabilityCategory.ShallowDescent => adjusted.Posture == Posture.Striding && adjusted.Sweep != Sweep.FreeRanging,
            FunctionStreamabilityCategory.DeepDescent => adjusted.Posture == Posture.Crawling && adjusted.Sweep != Sweep.FreeRanging,
            FunctionStreamabilityCategory.Ascent => adjusted.Posture is Posture.Grounded or Posture.Climbing && adjusted.Sweep == Sweep.Motionless,
            _ => false,
        };
    }

    // The conversion of a result to a node/item return type preserves its nodes; it does not
    // navigate them. Atomic and function return types apply their type-determined usage.
    internal static Streamability AdjustType(Streamability value, SequenceTypeTest required)
    {
        OperandUsage usage = ExpressionAnalysis.TypeDeterminedUsage(required.Item);
        if (usage == OperandUsage.Navigation) usage = OperandUsage.Transmission;
        return ExpressionAnalysis.General_19_8_1(UTypes.FromSequence(required), "19.3", new AnalysisOperand(value, usage))
            with { ItemType = required.Item };
    }

    internal static Streamability Call(AnalysisFunction function, IReadOnlyList<Streamability> values)
    {
        FunctionStreamabilityCategory category = function.Category;
        FunctionItemTest signature = function.Signature;
        UType type = UTypes.FromSequence(signature.ReturnType!);
        string rule = category switch
        {
            FunctionStreamabilityCategory.Absorbing => "19.8.5.2", FunctionStreamabilityCategory.Inspection => "19.8.5.3",
            FunctionStreamabilityCategory.Filter => "19.8.5.4", FunctionStreamabilityCategory.ShallowDescent => "19.8.5.5",
            FunctionStreamabilityCategory.DeepDescent => "19.8.5.6", FunctionStreamabilityCategory.Ascent => "19.8.5.7",
            _ => "19.8.5.1",
        };
        Streamability Free() => new(type, Posture.Roaming, Sweep.FreeRanging, rule);
        if (category == FunctionStreamabilityCategory.Absorbing && values[0].Posture == Posture.Crawling) return Free();
        if (category is FunctionStreamabilityCategory.ShallowDescent or FunctionStreamabilityCategory.DeepDescent)
        {
            Streamability first = AdjustType(values[0], signature.Parameters![0]);
            Streamability rest = ExpressionAnalysis.General_19_8_1(UType.Any, rule,
                values.Skip(1).Select((v, i) => new AnalysisOperand(v, function.Usages[i + 1])).ToArray());
            if (first.Posture is not (Posture.Grounded or Posture.Striding) || rest.Posture != Posture.Grounded
                || first.Sweep == Sweep.FreeRanging || rest.Sweep == Sweep.FreeRanging
                || first.Sweep == Sweep.Consuming && rest.Sweep == Sweep.Consuming) return Free();
            Sweep sweep = first.Sweep > rest.Sweep ? first.Sweep : rest.Sweep;
            if (first.Posture == Posture.Grounded) return new(type, Posture.Grounded, sweep, rule);
            if ((UTypes.FromSequence(signature.Parameters[0]) & values[0].Type & UType.ParentNodes) != 0) sweep = Sweep.Consuming;
            return new(type, category == FunctionStreamabilityCategory.DeepDescent ? Posture.Crawling : Posture.Striding, sweep, rule);
        }
        // Follow proposed XT30.E31 / Bug 30289: ascent's first argument uses transmission.
        // This differs from the published Recommendation's inspection rule; see docs/divergences.md.
        // https://github.com/w3c/qtspecs/blob/6cef5545618cd2edfd94f32af488a16bb19c598c/errata/xslt-30/errata.xml
        Streamability result = ExpressionAnalysis.General_19_8_1(type, rule,
            values.Select((v, i) => new AnalysisOperand(v, function.Usages[i])).ToArray());
        if (category != FunctionStreamabilityCategory.Ascent) return result;
        if (result.Sweep != Sweep.Motionless) return Free();
        if (result.Posture == Posture.Grounded || (type & UType.Nodes) == 0) return new(type, Posture.Grounded, Sweep.Motionless, rule);
        return new(type, result.Posture == Posture.Roaming ? Posture.Roaming : Posture.Climbing,
            result.Posture == Posture.Roaming ? Sweep.FreeRanging : Sweep.Motionless, rule);
    }

    internal static bool TryParse(string value, out FunctionStreamabilityCategory category)
    {
        FunctionStreamabilityCategory? parsed = value switch
        {
            "unclassified" => FunctionStreamabilityCategory.Unclassified,
            "absorbing" => FunctionStreamabilityCategory.Absorbing,
            "inspection" => FunctionStreamabilityCategory.Inspection,
            "filter" => FunctionStreamabilityCategory.Filter,
            "shallow-descent" => FunctionStreamabilityCategory.ShallowDescent,
            "deep-descent" => FunctionStreamabilityCategory.DeepDescent,
            "ascent" => FunctionStreamabilityCategory.Ascent,
            _ => null,
        };
        category = parsed ?? FunctionStreamabilityCategory.Unclassified;
        return parsed.HasValue;
    }
}
