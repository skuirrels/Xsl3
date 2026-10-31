// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Xml.XPath;
using Xsl3.Streaming;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

public class StreamedObservationTests
{
    private sealed class Observer : IStreamedNodeObserver
    {
        internal readonly List<string> Events = new();
        internal Action<StreamedNavigator>? OnStart;
        internal Action<StreamedNavigator>? OnEnd;
        internal bool Closed;
        public void Start(StreamedNavigator node)
        {
            Events.Add("+" + node.NodeType + ":" + node.LocalName);
            OnStart?.Invoke(node);
        }
        public void End(StreamedNavigator node)
        {
            Events.Add("-" + node.NodeType + ":" + node.LocalName);
            OnEnd?.Invoke(node);
        }
        public void Dispose() => Closed = true;
    }

    [Fact]
    public void ObserverCallbacksKeepIndependentPositionsAfterTheSourceAdvances()
    {
        using var input = XmlReader.Create(new StringReader("<doc><first/><second/></doc>"));
        using var document = new StreamedDocument(input);
        var starts = new List<StreamedNavigator>();
        var ends = new List<StreamedNavigator>();
        document.Observe(new Observer { OnStart = starts.Add, OnEnd = ends.Add });
        document.CompleteSubtree(document.Root);
        Assert.Equal(new[] { "", "doc", "first", "second" }, starts.ConvertAll(node => node.LocalName));
        Assert.Equal(new[] { "first", "second", "doc", "" }, ends.ConvertAll(node => node.LocalName));
        Assert.True(starts[2].IsSamePosition(ends[0]));
        Assert.True(starts[3].IsSamePosition(ends[1]));
        Assert.All(starts, start => Assert.All(ends, end => Assert.NotSame(start, end)));
    }

    [Fact]
    public void SkippingTemplateContentStillObservesEverySourceNode()
    {
        using var input = XmlReader.Create(new StringReader("<doc><skip><empty/>one<!--c--><?p x?></skip><keep/></doc>"));
        using var document = new StreamedDocument(input);
        var observer = new Observer();
        document.Observe(observer);
        XPathNavigator nav = document.CreateNavigator();
        Assert.True(nav.MoveToFirstChild());
        Assert.True(nav.MoveToFirstChild());
        Assert.Equal("skip", nav.LocalName);
        Assert.True(nav.MoveToNext());
        Assert.Equal("keep", nav.LocalName);
        Assert.False(nav.MoveToNext());
        document.CompleteSubtree(document.Root);
        Assert.Equal(new[] {
            "+Root:", "+Element:doc", "+Element:skip", "+Element:empty", "-Element:empty",
            "+Text:", "-Text:", "+Comment:", "-Comment:", "+ProcessingInstruction:p", "-ProcessingInstruction:p",
            "-Element:skip", "+Element:keep", "-Element:keep", "-Element:doc", "-Root:"
        }, observer.Events);
        Assert.False(observer.Closed);
        document.Dispose();
        Assert.True(observer.Closed);
        Assert.NotEqual(ReadState.Closed, input.ReadState);
    }

    [Fact]
    public void MotionlessObserversCanReadHeadersButCannotAdvanceInput()
    {
        using var input = XmlReader.Create(new StringReader("<doc id='header'><child/></doc>"));
        using var document = new StreamedDocument(input);
        bool checkedHeader = false;
        var observer = new Observer { OnStart = node => {
            if (node.LocalName != "doc") return;
            Assert.Equal("header", node.GetAttribute("id", ""));
            Assert.Equal("stream-position", Assert.Throws<XdmException>(() => node.MoveToFirstChild()).Code);
            Assert.Equal("stream-position", Assert.Throws<XdmException>(() => node.Value).Code);
            Assert.False(node.PositionNode.Consumed);
            Assert.Equal(XmlNodeType.Element, input.NodeType);
            Assert.Equal("doc", input.LocalName);
            checkedHeader = true;
        }};
        document.Observe(observer);
        document.CompleteSubtree(document.Root);
        Assert.True(checkedHeader);
    }

    [Fact]
    public void AnObserverMustBeAttachedBeforeAnyInputIsConsumed()
    {
        using var input = XmlReader.Create(new StringReader("<doc/>"));
        using var document = new StreamedDocument(input);
        Assert.True(document.CreateNavigator().MoveToFirstChild());
        Assert.Equal("stream-position", Assert.Throws<XdmException>(() => document.Observe(new Observer())).Code);
    }

    [Fact]
    public void ReplayingAnExplicitCopyDoesNotObserveItsNodesAgain()
    {
        using var input = XmlReader.Create(new StringReader("<doc><a><a/></a><a/></doc>"));
        using var document = new StreamedDocument(input);
        var observer = new Observer();
        document.Observe(observer);
        var iterator = new Runtime.DescendantIterator();
        iterator.Create(document.CreateNavigator(), Runtime.XmlNavNameFilter.Create("a", ""), false);
        Assert.True(iterator.MoveNext());
        ((StreamedNavigator)iterator.Current).Ground();
        int events = observer.Events.Count;
        Assert.True(iterator.MoveNext());
        Assert.Equal(events, observer.Events.Count);
        Assert.True(iterator.MoveNext());
        Assert.Equal(events + 2, observer.Events.Count); // the following empty a, start and end
        document.CompleteSubtree(document.Root);
        Assert.Equal(10, observer.Events.Count); // document root, doc, three a nodes
    }
}
