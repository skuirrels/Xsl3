// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Xml.XPath;
using Xsl3.Streaming;
using Xsl3.XPath31.Functions;
using Xsl3.Xdm;

namespace Xsl3.Runtime;

public sealed partial class XsltLibrary
{
    private static bool ForwardGroupMember(XPathItem item) =>
        item is StreamedNavigator { NodeType: not (XPathNodeType.Attribute or XPathNodeType.Namespace) };

    private sealed class ForwardGroups(XsltLibrary owner, XPathItem population, int kind, FunctionContext context, bool composite) : FunctionItem
    {
        internal readonly XsltLibrary Owner = owner;
        internal readonly XPathItem Population = population;
        internal readonly int Kind = kind;
        internal readonly FunctionContext Context = context;
        internal readonly bool Composite = composite;
        internal GroupTuple? Pending;
        internal ForwardGroup? Current;
        public override int Arity => 0;
        public override SequenceTypeTest ParameterType(int index) => SequenceTypeTest.AnySequence;
        public override SequenceTypeTest ReturnType => SequenceTypeTest.AnySequence;
        public override string Describe() => "a forward grouping population";

        internal GroupTuple? NextTuple()
        {
            IList<XPathItem> next = Owner.X31PullNext(Population);
            if (next.Count == 0) return null;
            var tuple = (GroupTuple)next[0];
            if (Kind == 1 && !Composite && tuple.Key.Count != 1)
                throw new XdmException("XTTE1100", "A group-adjacent key must be a single atomic value");
            return tuple;
        }
    }

    private sealed class ForwardGroup(ForwardGroups owner, GroupTuple first) : AtomicValue(AtomicTypes.String)
    {
        internal readonly ForwardGroups Owner = owner;
        internal readonly GroupTuple First = first;
        // Grounded members, including attribute/namespace headers, may be used repeatedly.
        // Streamed parent/child nodes remain forward iterators. Explicit copies are grounded.
        internal List<XPathItem>? Cache = ForwardGroupMember(first.Item) ? null : new();
        internal bool FirstTaken, Complete, EndPrevious;
        public override string Value => First.Item.Value;
        public override object ClrValue => this;

        internal XPathItem? ReadNew()
        {
            if (Complete) return null;
            GroupTuple? tuple;
            if (!FirstTaken) { FirstTaken = true; tuple = First; }
            else
            {
                if (Owner.Kind == 3 && EndPrevious) { Complete = true; return null; }
                tuple = Owner.NextTuple();
                if (tuple == null || Owner.Kind == 1 && !KeysEqual(First.Key, tuple.Key, Owner.Context)
                    || Owner.Kind == 2 && Sequences.EffectiveBooleanValue(tuple.Key))
                {
                    Owner.Pending = tuple;
                    Complete = true;
                    return null;
                }
            }
            EndPrevious = Owner.Kind == 3 && Sequences.EffectiveBooleanValue(tuple.Key);
            if (ForwardGroupMember(tuple.Item)) Cache = null;
            Cache?.Add(tuple.Item);
            return tuple.Item;
        }
    }

    private sealed class GroupCursor(XPathItem group) : FunctionItem
    {
        internal readonly XPathItem Group = group;
        internal int Index;
        public override int Arity => 0;
        public override SequenceTypeTest ParameterType(int index) => SequenceTypeTest.AnySequence;
        public override SequenceTypeTest ReturnType => SequenceTypeTest.AnySequence;
        public override string Describe() => "a current-group iterator";
    }

    // Only fork capture requests this explicit group buffer. Finish on the invoking
    // thread; a worker must never resume the parent's forward population continuation.
    private Group BufferForkGroup(ForwardGroup group)
    {
        if (group.FirstTaken && group.Cache == null) throw StreamedDocument.PositionError();
        var members = group.Cache == null ? new List<XPathItem>() : new List<XPathItem>(group.Cache);
        for (int i = 0; i < members.Count; i++) members[i] = X31BufferForkItem(members[i]);
        while (group.ReadNew() is { } item)
        {
            XmlQueryRuntime.CheckCancellation();
            members.Add(X31BufferForkItem(item));
        }
        group.Cache = members;
        var result = new Group(group.Owner.Kind < 2 ? group.First.Key : null);
        result.Members.AddRange(members);
        return result;
    }

    public XPathItem X31ForwardGroupsNew(XPathItem population, double kind, string collation, bool composite) =>
        new ForwardGroups(this, population, (int)kind, GroupingContext(collation), composite);

    public IList<XPathItem> X31ForwardGroupNext(XPathItem state)
    {
        var groups = (ForwardGroups)state;
        if (groups.Current is { } previous)
            while (previous.ReadNew() != null) { }
        GroupTuple? first = groups.Pending ?? groups.NextTuple();
        groups.Pending = null;
        if (first == null) { groups.Current = null; return Sequences.Empty; }
        return Sequences.One(groups.Current = new ForwardGroup(groups, first));
    }

    public XPathItem X31GroupCursor(XPathItem group)
    {
        if (group is ForwardGroup { Cache: null, FirstTaken: true }) throw StreamedDocument.PositionError();
        return new GroupCursor(group);
    }

    public XPathItem X31TunnelGroupCursor(IList<XPathItem> frame) =>
        frame.Count != 0 && ((TunnelFrame)frame[0]).Find(GroupEntry, "") is { ParamValue.Count: 2 } entry
            ? X31GroupCursor(entry.ParamValue[0])
            : throw new XdmException("XTDE1061", "current-group() is absent");

    public IList<XPathItem> X31GroupMemberNext(XPathItem state)
    {
        var cursor = (GroupCursor)state;
        if (cursor.Group is Group grounded)
            return cursor.Index < grounded.Members.Count ? Sequences.One(grounded.Members[cursor.Index++]) : Sequences.Empty;
        var group = (ForwardGroup)cursor.Group;
        if (group.Cache == null && group.Owner.Current != group) throw StreamedDocument.PositionError();
        XPathItem? member = group.Cache is { } cache && cursor.Index < cache.Count ? cache[cursor.Index] : group.ReadNew();
        if (member == null) return Sequences.Empty;
        cursor.Index++;
        return Sequences.One(member);
    }

    public IList<XPathNavigator> X31GroupMemberNodeNext(XPathItem state)
    {
        IList<XPathItem> item = X31GroupMemberNext(state);
        return item.Count == 0 ? Array.Empty<XPathNavigator>() : new[] { (XPathNavigator)item[0] };
    }

    private FunctionContext GroupingContext(string collation)
    {
        try { return collation.Length == 0 ? X31Context : X31Context.WithDefaultCollation(Collation.Resolve(collation)); }
        catch (XdmException error) when (error.Code == "FOCH0002") { throw new XdmException("XTDE1110", error.Message); }
    }
}
