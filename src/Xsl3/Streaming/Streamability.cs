// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Linq;
using System.Xml.XPath;
using Xsl3.Xdm;

namespace Xsl3.Streaming;

/// <summary>XSLT 3.0 §19.1: a set of the 28 fundamental item types; cardinality is deliberately absent.</summary>
[Flags]
internal enum UType
{
    Empty = 0,
    Document = 1 << 0,
    Element = 1 << 1,
    Attribute = 1 << 2,
    Text = 1 << 3,
    Comment = 1 << 4,
    ProcessingInstruction = 1 << 5,
    Namespace = 1 << 6,
    Boolean = 1 << 7,
    Double = 1 << 8,
    Decimal = 1 << 9,
    Float = 1 << 10,
    String = 1 << 11,
    DateTime = 1 << 12,
    Date = 1 << 13,
    Time = 1 << 14,
    GYear = 1 << 15,
    GYearMonth = 1 << 16,
    GMonth = 1 << 17,
    GMonthDay = 1 << 18,
    GDay = 1 << 19,
    AnyUri = 1 << 20,
    QName = 1 << 21,
    Notation = 1 << 22,
    Base64Binary = 1 << 23,
    HexBinary = 1 << 24,
    Duration = 1 << 25,
    Function = 1 << 26,
    UntypedAtomic = 1 << 27,

    ParentNodes = Document | Element,
    ChildNodes = Element | Text | Comment | ProcessingInstruction,
    ChildlessNodes = Attribute | Text | Comment | ProcessingInstruction | Namespace,
    Nodes = ParentNodes | ChildlessNodes,
    Numeric = Decimal | Double | Float,
    Atomic = Boolean | Numeric | String | DateTime | Date | Time | GYear | GYearMonth | GMonth |
        GMonthDay | GDay | AnyUri | QName | Notation | Base64Binary | HexBinary | Duration | UntypedAtomic,
    Any = Nodes | Atomic | Function,
}

internal static class UTypes
{
    internal static UType FromSequence(SequenceTypeTest type) => FromItem(type.Item);

    internal static UType FromItem(ItemTest? type) => type switch
    {
        null => UType.Empty,
        AnyItemTest => UType.Any,
        AtomicTypeTest atomic => FromAtomic(atomic.Type),
        UnionTypeTest union => union.Members.Aggregate(UType.Empty, (types, member) => types | FromAtomic(member)),
        NumericTest => UType.Numeric,
        FunctionItemTest or MapTest or ArrayTest => UType.Function,
        NodeKindTest node => node.Kind switch
        {
            null or XPathNodeType.All => UType.Nodes,
            XPathNodeType.Root => UType.Document,
            XPathNodeType.Element => UType.Element,
            XPathNodeType.Attribute => UType.Attribute,
            XPathNodeType.Namespace => UType.Namespace,
            XPathNodeType.Text or XPathNodeType.Whitespace or XPathNodeType.SignificantWhitespace => UType.Text,
            XPathNodeType.Comment => UType.Comment,
            XPathNodeType.ProcessingInstruction => UType.ProcessingInstruction,
            _ => throw new NotSupportedException($"No U-type for {node}"),
        },
        _ => throw new NotSupportedException($"No U-type for {type}"),
    };

    // Reuse the XDM type hierarchy, including user-defined restrictions. Integer and the duration
    // subtypes are primitives in the evaluator but not fundamental types in XSLT §19.1.
    internal static UType FromAtomic(AtomicType type) => type.Primitive switch
    {
        Primitive.AnyAtomic => UType.Atomic,
        Primitive.UntypedAtomic => UType.UntypedAtomic,
        Primitive.String => UType.String,
        Primitive.Boolean => UType.Boolean,
        Primitive.Decimal or Primitive.Integer => UType.Decimal,
        Primitive.Float => UType.Float,
        Primitive.Double => UType.Double,
        Primitive.Duration or Primitive.YearMonthDuration or Primitive.DayTimeDuration => UType.Duration,
        Primitive.DateTime => UType.DateTime,
        Primitive.Time => UType.Time,
        Primitive.Date => UType.Date,
        Primitive.GYearMonth => UType.GYearMonth,
        Primitive.GYear => UType.GYear,
        Primitive.GMonthDay => UType.GMonthDay,
        Primitive.GDay => UType.GDay,
        Primitive.GMonth => UType.GMonth,
        Primitive.HexBinary => UType.HexBinary,
        Primitive.Base64Binary => UType.Base64Binary,
        Primitive.AnyUri => UType.AnyUri,
        Primitive.QName => UType.QName,
        Primitive.Notation => UType.Notation,
        _ => throw new NotSupportedException($"No fundamental type for {type}"),
    };
}

/// <summary>XSLT 3.0 §19.4. Postures are states, rather than an ordered measure of difficulty.</summary>
internal enum Posture { Grounded, Climbing, Striding, Crawling, Roaming }

/// <summary>XSLT 3.0 §19.6, in increasing width.</summary>
internal enum Sweep { Motionless, Consuming, FreeRanging }

internal readonly record struct StreamabilityContext(UType Type, Posture Posture)
{
    internal ItemTest? ItemType { get; init; }
}

/// <summary>The static result and the rule that produced it. This does not imply streamed execution.</summary>
internal readonly record struct Streamability(UType Type, Posture Posture, Sweep Sweep, string Rule)
{
    // §19.1: ancillary function/map/array signature information is not part of the U-type lattice.
    internal ItemTest? ItemType { get; init; }
}

internal enum OperandUsage { Absorption, Inspection, Transmission, Navigation }

internal readonly record struct AnalysisOperand(Streamability Value, OperandUsage Usage, bool HigherOrder = false, int ChoiceGroup = 0);
