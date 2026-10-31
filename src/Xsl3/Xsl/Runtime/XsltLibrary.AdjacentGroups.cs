// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Xml.XPath;
using Xsl3.Xdm;

namespace Xsl3.Runtime;

public sealed partial class XsltLibrary
{
    private sealed class AdjacentGroups(IList<XPathItem> tuples, IReadOnlyList<int> starts) : FunctionItem
    {
        internal IList<XPathItem> Tuples = tuples;
        internal IReadOnlyList<int> Starts = starts;
        internal readonly int Count = starts.Count;
        internal int Position;
        internal ExceptionDispatchInfo? Failure;
        public override int Arity => 0;
        public override SequenceTypeTest ParameterType(int index) => SequenceTypeTest.AnySequence;
        public override SequenceTypeTest ReturnType => SequenceTypeTest.AnySequence;
        public override string Describe() => "a grounded adjacent-group cursor";

        internal void Release()
        {
            Tuples = Sequences.Empty;
            Starts = Array.Empty<int>();
            Position = Count;
        }
    }

    /// <summary>Compute adjacent boundaries and their total count without retaining every group frame.</summary>
    public XPathItem X31AdjacentGroupsNew(IList<XPathItem> tuples, string collation, bool composite)
    {
        XmlQueryRuntime.CheckCancellation();
        var starts = new List<int>();
        var context = GroupingContext(collation);
        GroupTuple? current = null;
        for (int i = 0; i < tuples.Count; i++)
        {
            XmlQueryRuntime.CheckCancellation();
            var tuple = (GroupTuple)tuples[i];
            IList<XPathItem>? key = tuple.KeyString == null ? Sequences.Atomize(tuple.Key) : null;
            if (!composite && key != null && key.Count != 1)
                throw new XdmException("XTTE1100", "A group-adjacent key must be a single atomic value");
            // Untyped node keys are already held as strings. Compare them with the same
            // collation without filling every tuple's atom cache; frames own their keys.
            bool equal = current != null && (current.KeyString is string a && tuple.KeyString is string b
                ? context.DefaultCollation.Compare(a, b) == 0
                : KeysEqual(AdjacentKey(current), key ?? AdjacentKey(tuple), context));
            if (!equal)
            {
                starts.Add(i);
                current = tuple;
            }
        }
        return new AdjacentGroups(tuples, starts);
    }

    private static IList<XPathItem> AdjacentKey(GroupTuple tuple) => tuple.KeyString is string value
        ? Sequences.One(AtomicValue.OfUntyped(value)) : Sequences.Atomize(tuple.Key);

    public double X31AdjacentGroupCount(XPathItem state)
    {
        var groups = (AdjacentGroups)state;
        groups.Failure?.Throw();
        XmlQueryRuntime.CheckCancellation();
        return groups.Count;
    }

    public IList<XPathItem> X31AdjacentGroupNext(XPathItem state)
    {
        var groups = (AdjacentGroups)state;
        groups.Failure?.Throw();
        try
        {
            XmlQueryRuntime.CheckCancellation();
            if (groups.Position == groups.Count) return Sequences.Empty;
            int start = groups.Starts[groups.Position];
            int end = groups.Position + 1 == groups.Count ? groups.Tuples.Count : groups.Starts[groups.Position + 1];
            var first = (GroupTuple)groups.Tuples[start];
            var group = new Group(AdjacentKey(first));
            for (int i = start; i < end; i++)
            {
                XmlQueryRuntime.CheckCancellation();
                group.Members.Add(((GroupTuple)groups.Tuples[i]).Item);
            }
            groups.Position++;
            if (groups.Position == groups.Count) groups.Release();
            return Sequences.One(group);
        }
        catch (Exception error) when (System.Xml.XmlException.IsCatchableException(error))
        {
            groups.Failure = ExceptionDispatchInfo.Capture(error);
            groups.Release();
            throw;
        }
    }
}
