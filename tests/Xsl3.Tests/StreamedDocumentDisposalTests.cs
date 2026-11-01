// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Xml;
using System.Xml.XPath;
using Xsl3.Streaming;
using Xunit;

namespace Xsl3.Tests;

[Collection("Process-wide state")]
public class StreamedDocumentDisposalTests
{
    private const string BaseUri = "file:///disposal/input.xml";
    private const string Input = "<root xmlns:p='urn:disposal' xml:lang='en'><p:row code='value'><value>payload</value></p:row><after/></root>";

    public static IEnumerable<object[]> States()
    {
        foreach (string state in new[] { "start", "text", "ended" })
        foreach (bool owned in new[] { false, true })
        foreach (bool observerFailure in new[] { false, true })
            yield return new object[] { state, owned, observerFailure };
    }

    [Theory]
    [MemberData(nameof(States))]
    public void DisposedDocumentDoesNotOwnItsUnreferencedCurrentRow(string state, bool owned, bool observerFailure)
    {
        var closed = ClosePartialSource(state, owned, observerFailure);
        Collect();
        foreach (WeakReference<StreamedDocument.Node> frame in closed.Frames) Assert.False(frame.TryGetTarget(out _));
        Assert.Equal("", closed.Document.Root.LocalName);
        closed.Document.Dispose(); // cleanup remains idempotent after a callback failure
        GC.KeepAlive(closed.Document);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (StreamedDocument Document, WeakReference<StreamedDocument.Node>[] Frames) ClosePartialSource(
        string state, bool owned, bool observerFailure)
    {
        using var reader = XmlReader.Create(new StringReader(Input), new(), BaseUri);
        var document = new StreamedDocument(reader, ownsReader: owned);
        if (observerFailure) document.Observe(new FailingObserver());
        var nav = (StreamedNavigator)document.CreateNavigator();
        Assert.True(nav.MoveToFirstChild()); Assert.True(nav.MoveToFirstChild());
        var frames = new List<WeakReference<StreamedDocument.Node>> { new(nav.PositionNode) };
        if (state != "start")
        {
            Assert.True(nav.MoveToFirstChild()); frames.Add(new(nav.PositionNode));
            Assert.True(nav.MoveToFirstChild()); frames.Add(new(nav.PositionNode));
            if (state == "ended")
            {
                Assert.False(nav.MoveToNext()); Assert.True(nav.MoveToParent());
                Assert.False(nav.MoveToNext());
            }
        }
        if (observerFailure) Assert.Throws<DisposalFailure>(document.Dispose);
        else document.Dispose();
        Assert.Equal(owned, reader.ReadState == ReadState.Closed);
        return (document, frames.ToArray());
    }

    [Theory]
    [InlineData("element", false)]
    [InlineData("element", true)]
    [InlineData("attribute", false)]
    [InlineData("attribute", true)]
    [InlineData("namespace", false)]
    [InlineData("namespace", true)]
    [InlineData("text", false)]
    [InlineData("text", true)]
    public void RetainedPositionKeepsItsHeaderUntilTheCallerReleasesIt(string kind, bool owned)
    {
        var closed = CheckRetainedPosition(kind, owned);
        Collect();
        Assert.False(closed.Frame.TryGetTarget(out _));
        GC.KeepAlive(closed.Document);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (StreamedDocument Document, WeakReference<StreamedDocument.Node> Frame) CheckRetainedPosition(string kind, bool owned)
    {
        using var reader = XmlReader.Create(new StringReader(Input), new(), BaseUri);
        var document = new StreamedDocument(reader, ownsReader: owned);
        var nav = (StreamedNavigator)document.CreateNavigator();
        Assert.True(nav.MoveToFirstChild()); Assert.True(nav.MoveToFirstChild());
        if (kind == "attribute") Assert.True(nav.MoveToFirstAttribute());
        else if (kind == "namespace") Assert.True(nav.MoveToNamespace("p"));
        else if (kind == "text") { Assert.True(nav.MoveToFirstChild()); Assert.True(nav.MoveToFirstChild()); }
        string name = nav.Name, value = kind is "attribute" or "namespace" ? nav.Value : "";
        XPathNodeType nodeType = nav.NodeType;
        string id = nav.UniqueId;
        var frame = new WeakReference<StreamedDocument.Node>(nav.PositionNode);
        document.Dispose();
        Collect(); Assert.True(frame.TryGetTarget(out _));
        Assert.Equal(nodeType, nav.NodeType); Assert.Equal(name, nav.Name); Assert.Equal(id, nav.UniqueId);
        Assert.Equal(BaseUri, nav.BaseURI); Assert.Equal("en", nav.XmlLang);
        Assert.Equal("urn:disposal", nav.LookupNamespace("p"));
        Assert.True(nav.IsSamePosition(nav.Clone()));
        if (kind is "attribute" or "namespace")
        {
            Assert.Equal(value, nav.Value);
            Assert.True(nav.MoveToParent());
        }
        Assert.Throws<ObjectDisposedException>(() => nav.MoveToFirstChild());
        Assert.Equal(owned, reader.ReadState == ReadState.Closed);
        GC.KeepAlive(nav);
        return (document, frame);
    }

    private sealed class DisposalFailure : Exception;
    private sealed class FailingObserver : IStreamedNodeObserver
    {
        public void Start(StreamedNavigator node) { }
        public void End(StreamedNavigator node) { }
        public void Dispose() => throw new DisposalFailure();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Collect() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
}
