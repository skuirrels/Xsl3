// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Numerics;
using System.Xml.XPath;
using Xsl3.Streaming;
using Xsl3.Xdm;

namespace Xsl3.Runtime;

public sealed partial class XsltLibrary
{
    private readonly record struct HeaderSlot(int Slot, XPathNodeType Kind, string LocalName, string Namespace);
    private sealed class NodeSetMatches : FunctionItem
    {
        // Prefix facts and position counters expire with their source frames. Already-grounded
        // trees may retain their own identity map.
        internal readonly StreamedFrameFacts Streamed = new();
        internal readonly Dictionary<(string Id, int Slot), bool> Grounded = new();
        internal Dictionary<(string Id, int Slot), BigInteger>? GroundedPositions;
        internal DocumentOrderComparer? Documents;
        public override int Arity => 0;
        public override SequenceTypeTest ParameterType(int index) => SequenceTypeTest.AnySequence;
        public override SequenceTypeTest ReturnType => SequenceTypeTest.AnySequence;
        public override string Describe() => "memoized header path matches";
    }

    private sealed class HeaderFacts
    {
        internal readonly Dictionary<HeaderSlot, bool> Matches = new();
        internal Dictionary<int, BigInteger>? Positions;
    }

    private static string GroundedMatchId(NodeSetMatches matches, XPathNavigator node) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{(matches.Documents ??= new()).GetDocumentIndex(node)}:{node.UniqueId}");

    public XPathItem X31NodeSetMatchNew()
    {
        XmlQueryRuntime.CheckCancellation();
        return new NodeSetMatches();
    }

    public int X31NodeSetMatchGet(XPathItem state, XPathNavigator node, double slot)
    {
        XmlQueryRuntime.CheckCancellation();
        var matches = (NodeSetMatches)state;
        bool value;
        if (node is StreamedNavigator source)
        {
            var header = source.PositionNode.Original ?? source.PositionNode;
            if (!matches.Streamed.TryGet<HeaderFacts>(header, out var values)
                || !values!.Matches.TryGetValue(new((int)slot, node.NodeType, node.LocalName, node.NamespaceURI), out value)) return -1;
        }
        else if (!matches.Grounded.TryGetValue((GroundedMatchId(matches, node), (int)slot), out value)) return -1;
        return value ? 1 : 0;
    }

    public bool X31NodeSetMatchPut(XPathItem state, XPathNavigator node, double slot, bool value)
    {
        XmlQueryRuntime.CheckCancellation();
        var matches = (NodeSetMatches)state;
        if (node is StreamedNavigator source)
        {
            var header = source.PositionNode.Original ?? source.PositionNode;
            matches.Streamed.Get(header, static () => new HeaderFacts()).Matches[new((int)slot, node.NodeType, node.LocalName, node.NamespaceURI)] = value;
        }
        else matches.Grounded[(GroundedMatchId(matches, node), (int)slot)] = value;
        return value;
    }

    public XPathItem X31NodeSetPositionNext(XPathItem state, XPathNavigator parent, double slot)
    {
        XmlQueryRuntime.CheckCancellation();
        var matches = (NodeSetMatches)state;
        BigInteger position;
        if (parent is StreamedNavigator source)
        {
            var header = source.PositionNode.Original ?? source.PositionNode;
            var values = matches.Streamed.Get(header, static () => new HeaderFacts()).Positions ??= new();
            values.TryGetValue((int)slot, out position);
            values[(int)slot] = position += BigInteger.One;
        }
        else
        {
            var values = matches.GroundedPositions ??= new();
            var key = (GroundedMatchId(matches, parent), (int)slot);
            values.TryGetValue(key, out position);
            values[key] = position += BigInteger.One;
        }
        return AtomicValue.OfInteger(position);
    }

    public bool X31PredicateAt(IList<XPathItem> value, XPathItem position)
    {
        if (value.Count == 1 && value[0] is { IsNode: false } and not FunctionItem
            && Sequences.Atomize(value[0]) is { Type.IsNumeric: true } number)
            return Comparison.CompareNumeric(number, (IntegerValue)position) == 0;
        return Sequences.EffectiveBooleanValue(value);
    }
}
