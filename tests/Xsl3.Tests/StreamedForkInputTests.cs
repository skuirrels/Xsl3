// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Xml;
using System.Xml.Schema;
using System.Xml.XPath;
using Xsl3.Streaming;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

[Collection("Process-wide state")]
public class StreamedForkInputTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    private static void Run(StreamedNavigator focus, params Action<StreamedNavigator>[] bodies)
    {
        using var cancellation = new CancellationTokenSource(Deadline);
        using var input = focus.Document.Fork(focus);
        input.Run(bodies.Select<Action<StreamedNavigator>, Action<StreamedNavigator, CancellationToken>>(body =>
            (view, token) => body(view)).ToArray(), capacity: 2, cancellation.Token);
    }

    private static string ReadXml(XPathNavigator position)
    {
        using XmlReader input = position.ReadSubtree();
        var result = new XmlDocument();
        result.Load(input);
        return result.OuterXml;
    }

    [Fact]
    public void LogicalEventsKeepCoalescedTextAndPreservedWhitespace()
    {
        const string xml = "<doc xml:space='preserve'> a<![CDATA[b]]>c<!--note--><?pi value?><n/> \t</doc>";
        using var baselineInput = XmlReader.Create(new StringReader(xml));
        using var baseline = new StreamedDocument(baselineInput);
        string[] expected = Trace(baseline.CreateNavigator());
        Assert.Equal("Text: abc", expected[0]);
        Assert.Contains("SignificantWhitespace: \t", expected);
        using var input = XmlReader.Create(new StringReader(xml));
        using var document = new StreamedDocument(input);
        Run((StreamedNavigator)document.CreateNavigator(),
            view => Assert.Equal(expected, Trace(view)), view => Assert.Equal(expected, Trace(view)));

        static string[] Trace(XPathNavigator view)
        {
            Assert.True(view.MoveToFirstChild());
            Assert.Same(view.NameTable.Get("doc"), view.LocalName);
            Assert.True(view.MoveToFirstChild());
            var result = new List<string>();
            do
            {
                if (view.NodeType == XPathNodeType.Element)
                    Assert.Same(view.NameTable.Get(view.LocalName), view.LocalName);
                result.Add(view.NodeType + ":" + (view.NodeType == XPathNodeType.Element ? view.LocalName : view.Value));
            } while (view.MoveToNext());
            return result.ToArray();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProngsIndependentlyReadTheSameMixedContent(bool element)
    {
        const string xml = "<doc xmlns:p='urn:p'>one<![CDATA[two]]><p:n/>three<!--c--><?pi q?></doc>";
        using var input = XmlReader.Create(new StringReader(xml));
        using var document = new StreamedDocument(input);
        var focus = (StreamedNavigator)document.CreateNavigator();
        if (element) Assert.True(focus.MoveToFirstChild());
        string? first = null, second = null, text = null;
        Run(focus, view => first = ReadXml(view), view => second = ReadXml(view), view => text = view.Value);
        Assert.Equal(first, second);
        Assert.Contains("<p:n", first);
        Assert.Contains("<!--c-->", first);
        Assert.Contains("<?pi q?>", first);
        Assert.Equal("onetwothree", text);
        Assert.NotEqual(ReadState.Closed, input.ReadState);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(64)]
    public void SkippingNestedContentKeepsTheFollowingSiblingAndOtherProng(int capacity)
    {
        const string xml = "<doc><row id='1'><value>one<nested>two</nested>three</value><!--c--></row>"
            + "<row id='2'/><row id='3'><value/></row></doc>";
        using var cancellation = new CancellationTokenSource(Deadline);
        using var input = XmlReader.Create(new StringReader(xml));
        using var document = new StreamedDocument(input);
        using var fork = document.Fork((StreamedNavigator)document.CreateNavigator());
        var ids = new List<string>();
        string? copy = null;
        fork.Run(new Action<StreamedNavigator, CancellationToken>[] {
            (view, _) => {
                Assert.True(view.MoveToFirstChild());
                Assert.True(view.MoveToFirstChild());
                do { ids.Add(view.GetAttribute("id", "")); } while (view.MoveToNext());
                Assert.False(view.MoveToNext());
            },
            (view, _) => copy = ReadXml(view),
        }, capacity, cancellation.Token);
        Assert.Equal(new[] { "1", "2", "3" }, ids);
        var expected = new XmlDocument(); expected.LoadXml(xml);
        Assert.Equal(expected.OuterXml, copy);
    }

    [Fact]
    public void ViewsPreserveNodeAttributeNamespaceAndDocumentIdentity()
    {
        using var input = XmlReader.Create(new StringReader("<doc xmlns:p='urn:p' id='a'><p:n/></doc>"));
        using var document = new StreamedDocument(input);
        var focus = (StreamedNavigator)document.CreateNavigator();
        Assert.True(focus.MoveToFirstChild());
        Assert.Equal("urn:p", focus.GetNamespacesInScope(XmlNamespaceScope.All)["p"]);
        var nodes = new XPathNavigator[2];
        var attributes = new XPathNavigator[2];
        var namespaces = new XPathNavigator[2];
        var names = new XmlNameTable[2];
        Run(focus, view => Capture(view, 0), view => Capture(view, 1));
        Assert.True(focus.IsSamePosition(nodes[0]));
        Assert.True(nodes[0].IsSamePosition(nodes[1]));
        Assert.True(attributes[0].IsSamePosition(attributes[1]));
        Assert.True(namespaces[0].IsSamePosition(namespaces[1]));
        Assert.Equal(XmlNodeOrder.Same, nodes[0].ComparePosition(focus));
        Assert.NotSame(names[0], names[1]);
        var comparer = new Runtime.DocumentOrderComparer();
        Assert.Equal(comparer.GetDocumentIndex(focus), comparer.GetDocumentIndex(nodes[0]));
        Assert.Equal(comparer.GetDocumentIndex(focus), comparer.GetDocumentIndex(nodes[1]));

        void Capture(StreamedNavigator view, int index)
        {
            nodes[index] = view.Clone(); names[index] = view.NameTable;
            XPathNavigator attribute = view.Clone(), ns = view.Clone();
            Assert.True(attribute.MoveToAttribute("id", ""));
            Assert.True(ns.MoveToNamespace("p"));
            Assert.Same(view.NameTable.Get("p"), ns.LocalName);
            Assert.Same(view.NameTable.Get("urn:p"), ns.Value);
            attributes[index] = attribute; namespaces[index] = ns;
            Assert.True(view.MoveToFirstChild());
            Assert.True(focus.IsDescendant(view));
            Assert.Equal(XmlNodeOrder.Before, focus.ComparePosition(view));
        }
    }

    [Fact]
    public void NestedElementForkPreservesAncestorHeadersAndTheFollowingSibling()
    {
        using var input = XmlReader.Create(new StringReader("<doc xmlns:p='urn:p' xml:lang='en'><a id='a'><p:n>value</p:n></a><b/></doc>"), new(), "file:///fork/input.xml");
        using var document = new StreamedDocument(input);
        var focus = (StreamedNavigator)document.CreateNavigator();
        Assert.True(focus.MoveToFirstChild()); Assert.True(focus.MoveToFirstChild());
        string? text = null;
        Run(focus, view => {
            Assert.Equal("a", view.GetAttribute("id", ""));
            Assert.Equal("en", view.XmlLang);
            Assert.Equal("file:///fork/input.xml", view.BaseURI);
            Assert.Equal("urn:p", view.LookupNamespace("p"));
            XPathNavigator parent = view.Clone(); Assert.True(parent.MoveToParent());
            Assert.Equal("doc", parent.LocalName);
            text = view.Value;
        }, view => Assert.Contains("value", ReadXml(view)));
        Assert.Equal("value", text);
        Assert.True(focus.MoveToNext());
        Assert.Equal("b", focus.LocalName);
    }

    [Fact]
    public void AnAlreadyInspectedFirstChildIsDeliveredToEveryProng()
    {
        using var input = XmlReader.Create(new StringReader("<doc>text<n/></doc>"));
        using var document = new StreamedDocument(input);
        var focus = (StreamedNavigator)document.CreateNavigator();
        Assert.True(focus.MoveToFirstChild());
        Assert.True(focus.HasChildren);
        string? text = null, xml = null;
        Run(focus, view => text = view.Value, view => xml = ReadXml(view));
        Assert.Equal("text", text);
        Assert.Contains("text<n", xml);
    }

    [Fact]
    public void HeaderOnlyProngsDoNotAdvanceTheOriginalSource()
    {
        using var input = XmlReader.Create(new StringReader("<doc><n/></doc>"));
        using var document = new StreamedDocument(input);
        var focus = (StreamedNavigator)document.CreateNavigator();
        Run(focus, view => Assert.Equal(XPathNodeType.Root, view.NodeType), view => Assert.Equal("", view.LocalName));
        Assert.Equal(ReadState.Initial, input.ReadState);
        Assert.True(focus.MoveToFirstChild());
        Assert.Equal("doc", focus.LocalName);
    }

    [Fact]
    public void NestedForksShareOneOriginalDocument()
    {
        using var input = XmlReader.Create(new StringReader("<doc><n>one</n><n>two</n></doc>"));
        using var document = new StreamedDocument(input);
        var focus = (StreamedNavigator)document.CreateNavigator();
        var results = new string?[3];
        Run(focus, view => Run(view, nested => results[0] = nested.Value, nested => results[1] = nested.Value), view => results[2] = view.Value);
        Assert.All(results, result => Assert.Equal("onetwo", result));
    }

    [Fact]
    public void NestedEventsDoNotRetainConsumedProngFrameState()
    {
        using var input = XmlReader.Create(new StringReader("<doc><n/><n/></doc>"));
        using var document = new StreamedDocument(input);
        Run((StreamedNavigator)document.CreateNavigator(), view =>
        {
            Assert.True(view.MoveToFirstChild());
            Assert.True(view.MoveToFirstChild());
            WeakReference state = AttachProngState(view);
            Assert.True(view.MoveToParent());
            using var firstRead = new ManualResetEventSlim();
            using var advanced = new ManualResetEventSlim();
            using var inspected = new ManualResetEventSlim();
            Run(view, slow =>
            {
                try
                {
                    Assert.True(slow.MoveToFirstChild());
                    firstRead.Set();
                    Assert.True(advanced.Wait(Deadline));
                    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                    Assert.False(state.IsAlive);
                    // The slow reader still owns the first published event here.
                    Assert.Equal("n", slow.LocalName);
                    GC.KeepAlive(slow);
                }
                finally { inspected.Set(); }
            }, fast =>
            {
                Assert.True(firstRead.Wait(Deadline));
                Assert.True(fast.MoveToFirstChild());
                Assert.True(fast.MoveToNext());
                advanced.Set();
                Assert.True(inspected.Wait(Deadline));
            });
        }, view => _ = view.Value);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference AttachProngState(StreamedNavigator view)
    {
        // Per-prong state must not become part of a nested fork's immutable payload.
        object state = new();
        view.PositionNode.RuntimeFacts = state;
        return new WeakReference(state);
    }

    private sealed class Observer(int thread) : IStreamedNodeObserver
    {
        internal int Starts, Ends;
        internal bool Closed;
        public void Start(StreamedNavigator node) { Assert.Equal(thread, Environment.CurrentManagedThreadId); Starts++; }
        public void End(StreamedNavigator node) { Assert.Equal(thread, Environment.CurrentManagedThreadId); Ends++; }
        public void Dispose() => Closed = true;
    }

    [Fact]
    public void OnlyTheOriginalSourceRunsObserverCallbacks()
    {
        using var input = XmlReader.Create(new StringReader("<doc><n>v</n><n/></doc>"));
        using var document = new StreamedDocument(input);
        var observer = new Observer(Environment.CurrentManagedThreadId);
        document.Observe(observer);
        Run((StreamedNavigator)document.CreateNavigator(), view => Assert.Equal("v", view.Value), view => Assert.Equal("v", view.Value));
        Assert.Equal(5, observer.Starts); // document, doc, two n elements and one text node
        Assert.Equal(5, observer.Ends);
        Assert.False(observer.Closed);
        document.Dispose(); Assert.True(observer.Closed);
    }

    [Fact]
    public void ValidationAnnotationsSurviveEventsAndGrounding()
    {
        var schemas = new XmlSchemaSet();
        schemas.Add(null, XmlReader.Create(new StringReader("<xs:schema xmlns:xs='http://www.w3.org/2001/XMLSchema'><xs:element name='doc'><xs:complexType><xs:sequence><xs:element name='n' type='xs:integer'/></xs:sequence><xs:attribute name='k' type='xs:integer' default='7'/></xs:complexType></xs:element></xs:schema>")));
        using var input = XmlReader.Create(new StringReader("<doc><n>12</n></doc>"), new() { ValidationType = ValidationType.Schema, Schemas = schemas });
        using var document = new StreamedDocument(input);
        Run((StreamedNavigator)document.CreateNavigator(), view => Verify(view.Ground()), view => Verify(view.Ground()));
        static void Verify(XPathNavigator copy)
        {
            Assert.True(copy.MoveToFirstChild());
            XPathNavigator attribute = copy.Clone(); Assert.True(attribute.MoveToAttribute("k", ""));
            Assert.Equal(XmlTypeCode.Integer, attribute.SchemaInfo!.SchemaType!.TypeCode);
            Assert.Equal("7", attribute.Value);
            Assert.True(copy.MoveToFirstChild());
            Assert.Equal(XmlTypeCode.Integer, copy.SchemaInfo!.SchemaType!.TypeCode);
            Assert.Equal("12", copy.Value);
        }
    }

    [Fact]
    public void AProngCanStopEarlyWhileAnotherConsumesTheRest()
    {
        string xml = "<doc>" + string.Concat(Enumerable.Range(1, 1000).Select(i => $"<n>{i}</n>")) + "</doc>";
        using var input = XmlReader.Create(new StringReader(xml));
        using var document = new StreamedDocument(input);
        int count = 0;
        Run((StreamedNavigator)document.CreateNavigator(), view => Assert.True(view.MoveToFirstChild()), view => {
            using XmlReader reader = view.ReadSubtree();
            while (reader.Read()) if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "n") count++;
        });
        Assert.Equal(1000, count);
        Assert.NotEqual(ReadState.Closed, input.ReadState);
    }

    [Fact]
    public void ViewsReportAnAttemptToLeaveTheirSubtree()
    {
        using var input = XmlReader.Create(new StringReader("<doc><a/><b/></doc>"));
        using var document = new StreamedDocument(input);
        var focus = (StreamedNavigator)document.CreateNavigator();
        Assert.True(focus.MoveToFirstChild()); Assert.True(focus.MoveToFirstChild());
        Run(focus, view => Assert.Contains("stream-position", Assert.Throws<XdmException>(() => view.MoveToNext()).Message));
        Assert.True(focus.MoveToNext());
        Assert.Equal("b", focus.LocalName);
    }

    [Fact]
    public void OriginalSourceCancellationReachesProngsEvenWithoutFurtherReads()
    {
        using var cancellation = new CancellationTokenSource();
        using var entered = new CountdownEvent(2);
        using var input = XmlReader.Create(new StringReader("<doc/>"));
        using var document = new StreamedDocument(input, cancellation: cancellation.Token);
        using var fork = document.Fork((StreamedNavigator)document.CreateNavigator());
        var canceller = new Thread(() => { if (entered.Wait(Deadline)) cancellation.Cancel(); }) { IsBackground = true };
        canceller.Start();
        try
        {
            Assert.Throws<OperationCanceledException>(() => fork.Run(new Action<StreamedNavigator, CancellationToken>[] {
                (view, token) => { entered.Signal(); Assert.True(token.WaitHandle.WaitOne(Deadline)); token.ThrowIfCancellationRequested(); },
                (view, token) => { entered.Signal(); Assert.True(token.WaitHandle.WaitOne(Deadline)); token.ThrowIfCancellationRequested(); },
            }, 1));
        }
        finally { cancellation.Cancel(); Assert.True(canceller.Join(Deadline)); }
        Assert.NotEqual(ReadState.Closed, input.ReadState);
    }

    [Fact]
    public void MalformedSourceErrorsDoNotCloseTheBorrowedReader()
    {
        using var input = XmlReader.Create(new StringReader("<doc><n></doc>"));
        using var document = new StreamedDocument(input);
        Assert.Throws<XmlException>(() => Run((StreamedNavigator)document.CreateNavigator(), view => ReadXml(view), view => ReadXml(view)));
        Assert.NotEqual(ReadState.Closed, input.ReadState);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SecondaryPositionsAreNotSilentlyChangedToTheirParentElement(bool ns)
    {
        using var input = XmlReader.Create(new StringReader("<doc xmlns:p='urn:p' id='a'/>"));
        using var document = new StreamedDocument(input);
        var focus = (StreamedNavigator)document.CreateNavigator();
        Assert.True(focus.MoveToFirstChild());
        Assert.True(ns ? focus.MoveToNamespace("p") : focus.MoveToAttribute("id", ""));
        Assert.Throws<XdmException>(() => document.Fork(focus));
        Assert.Equal(ns ? "urn:p" : "a", focus.Value);
    }
}
