// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using Xsl3.XPath31;
using Xsl3.Xdm;

namespace Xsl3.Streaming;

/// <summary>Static context supplied by the containing declaration or the analysis-only harness.</summary>
internal sealed class ExpressionAnalysisOptions
{
    internal StaticTypeResolver Types { get; init; } = new(StandardNamespace);
    internal string DefaultFunctionNamespace { get; init; } = QName.Ns.Fn;
    internal IReadOnlyDictionary<QName, AnalysisVariable> Variables { get; init; } = new Dictionary<QName, AnalysisVariable>();
    internal Func<QName, int, AnalysisFunction?>? ResolveFunction { get; init; }
    internal bool InPattern { get; init; }
    internal AccumulatorPhase AccumulatorPhase { get; init; }
    internal bool InSequenceConstructor { get; init; }
    internal bool PrecedingConsumingInstruction { get; init; }
    internal Streamability? CurrentGroupSelection { get; init; }
    // Compiler optimization (§19.10); the analysis-only harness leaves source syntax unchanged.
    internal bool AllowStreamabilityRewrites { get; init; }
    internal bool StreamingParameterRepeated { get; init; }

    internal static string? StandardNamespace(string prefix) => prefix switch
    {
        "xs" => QName.Ns.Xs, "fn" => QName.Ns.Fn, "map" => QName.Ns.Map,
        "array" => QName.Ns.Array, "math" => QName.Ns.Math, "xml" => QName.Ns.Xml,
        _ => null,
    };
}

internal enum AccumulatorPhase { None, Start, End }

internal sealed record AnalysisVariable(SequenceTypeTest Type)
{
    internal QName? ComponentName { get; init; }
    // Only the host's streaming parameter may refer to streamed nodes (§19.8.8.12).
    internal Streamability? StreamingParameter { get; init; }
}

/// <summary>Explicit host policy for stylesheet/extension calls; no assumed streamability for unknown functions.</summary>
internal sealed record AnalysisFunction(FunctionItemTest Signature, IReadOnlyList<OperandUsage> Usages, bool FocusDependent = false, bool DeclaredStreamable = false)
{
    internal FunctionStreamabilityCategory Category { get; init; }
}
