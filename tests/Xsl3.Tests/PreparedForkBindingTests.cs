// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Xml;
using System.Xml.Schema;
using System.Xml.XPath;
using Xsl3.Runtime;
using Xsl3.Streaming;
using Xsl3.Xdm;
using Xunit;
using static Xsl3.Tests.ForkBindingTests;

namespace Xsl3.Tests;

public class PreparedForkBindingTests
{
    [Fact]
    public void PreparationFreezesCollectionsClosuresAndCursorPositions()
    {
        XmlQueryRuntime owner = CreateRuntime(), child = owner.CreateForkRuntime(new XmlCachedSequenceWriter());
        using var reader = XmlReader.Create(new StringReader("<doc><a id='first'/><b id='second'/></doc>"));
        using var document = new StreamedDocument(reader);
        var focus = (StreamedNavigator)document.CreateNavigator();
        Assert.True(focus.MoveToFirstChild()); Assert.True(focus.MoveToFirstChild());
        XPathNavigator attribute = focus.Clone(); Assert.True(attribute.MoveToAttribute("id", ""));
        var values = new List<XPathItem> { attribute };
        MapItem map = MapItem.Empty.Put(AtomicValue.OfString("value"), values);
        var function = new CompiledFunction(0, new(null, Array.Empty<SequenceTypeTest>(), SequenceTypeTest.AnySequence), owner);
        function.Captured.Add(new XPathItem[] { map });
        var group = new XsltLibrary.Group(null);
        group.Members.Add(function);
        var bindings = new List<XPathItem> { map, function, group };
        var prepared = owner.XsltFunctions.PrepareForkBindings(bindings);
        values.Clear(); function.Captured.Clear(); group.Members.Clear(); bindings.Clear();
        Assert.True(attribute.MoveToParent()); Assert.True(attribute.MoveToNext());
        Assert.True(attribute.MoveToAttribute("id", ""));
        Assert.True(focus.MoveToNext());

        Run(focus, view => child.RunFork(r => {
            IList<XPathItem> projected = r.XsltFunctions.ProjectForkBindings(prepared, view);
            var copied = Assert.IsType<MapItem>(projected[0]);
            Assert.Equal("first", Assert.Single(copied.GetString("value")!).Value);
            var closure = Assert.IsType<CompiledFunction>(projected[1]);
            Assert.Same(copied, Assert.Single(Assert.Single(closure.Captured)));
            Assert.Same(closure, Assert.Single(Assert.IsType<XsltLibrary.Group>(projected[2]).Members));
            Assert.Same(r, closure.Owner);
        }, default, null));
        Assert.Equal("second", attribute.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClosedSourceHeadersKeepIdentityInPrivateProngPositions(bool ns)
    {
        XmlQueryRuntime owner = CreateRuntime();
        XsltLibrary.PreparedForkBindings prepared;
        XPathNavigator original;
        using (var reader = XmlReader.Create(new StringReader("<doc xmlns:p='urn:p'><a id='first'/><b/></doc>")))
        using (var document = new StreamedDocument(reader))
        {
            original = document.CreateNavigator();
            Assert.True(original.MoveToFirstChild()); Assert.True(original.MoveToFirstChild());
            Assert.True(ns ? original.MoveToNamespace("p") : original.MoveToAttribute("id", ""));
            prepared = owner.XsltFunctions.PrepareForkBindings(new XPathItem[] { original, original });
        }
        using var input = XmlReader.Create(new StringReader("<input/>"));
        using var source = new StreamedDocument(input);
        Run((StreamedNavigator)source.CreateNavigator(), Check, Check);
        Assert.Equal(ReadState.Initial, input.ReadState);

        void Check(StreamedNavigator view)
        {
            IList<XPathItem> values = owner.XsltFunctions.ProjectForkBindings(prepared, view);
            var first = Assert.IsType<StreamedNavigator>(values[0]);
            var second = Assert.IsType<StreamedNavigator>(values[1]);
            Assert.NotSame(first, second);
            Assert.Same(first.Document, second.Document);
            Assert.True(first.IsSamePosition(original));
            Assert.True(first.MoveTo(second));
            Assert.Equal(ns ? "urn:p" : "first", first.Value);
            Assert.True(first.MoveToParent());
            Assert.Equal("a", first.LocalName);
            Assert.Equal(ns ? XPathNodeType.Namespace : XPathNodeType.Attribute, second.NodeType);
            Assert.NotSame(view.Document, first.Document);
            var order = new DocumentOrderComparer();
            Assert.Equal(order.GetDocumentIndex(original), order.GetDocumentIndex(second));
        }
    }

    [Fact]
    public void RetainedHeadersExposeMetadataWithoutOpeningContentAxes()
    {
        XmlQueryRuntime runtime = CreateRuntime();
        using var input = XmlReader.Create(new StringReader("<doc xmlns:p='urn:p' xml:lang='en'><a id='first'><n>value</n></a><b/></doc>"), new(), "urn:source");
        using var document = new StreamedDocument(input);
        var focus = (StreamedNavigator)document.CreateNavigator();
        Assert.True(focus.MoveToFirstChild()); Assert.True(focus.MoveToFirstChild());
        XPathNavigator attribute = focus.Clone(); Assert.True(attribute.MoveToAttribute("id", ""));
        var prepared = runtime.XsltFunctions.PrepareForkBindings(new XPathItem[] { attribute });
        Assert.True(focus.MoveToNext());
        Run(focus, view => {
            var header = Assert.IsType<StreamedNavigator>(Assert.Single(runtime.XsltFunctions.ProjectForkBindings(prepared, view)));
            Assert.True(header.MoveToParent());
            Assert.Equal("a", header.LocalName);
            Assert.Equal("urn:source", header.BaseURI);
            Assert.Equal("en", header.XmlLang);
            Assert.Equal("urn:p", header.LookupNamespace("p"));
            Assert.Same(header.LocalName, header.NameTable.Get(header.LocalName));
            Assert.Equal("first", header.GetAttribute("id", ""));
            Assert.True(header.MoveToNamespace("p"));
            Assert.Equal("urn:p", header.Value);
            Assert.True(header.MoveToParent());
            Assert.Throws<XdmException>(() => header.MoveToFirstChild());
            Assert.Throws<XdmException>(() => header.MoveToNext());
            Assert.Throws<XdmException>(() => header.Value);
            Assert.Throws<XdmException>(() => header.Ground());
            Assert.True(header.MoveToParent());
            Assert.Equal("doc", header.LocalName);
            header.MoveToRoot();
            Assert.Throws<XdmException>(() => header.MoveToFirstChild());
        });
    }

    [Fact]
    public void PreparationAfterSourceClosurePreservesTypedDefaultAttributes()
    {
        var schemas = new XmlSchemaSet();
        schemas.Add(null, XmlReader.Create(new StringReader("<xs:schema xmlns:xs='http://www.w3.org/2001/XMLSchema'><xs:element name='doc'><xs:complexType><xs:attribute name='n' type='xs:integer' default='12'/></xs:complexType></xs:element></xs:schema>")));
        XPathNavigator attribute;
        using (var input = XmlReader.Create(new StringReader("<doc/>"), new() { ValidationType = ValidationType.Schema, Schemas = schemas }))
        using (var document = new StreamedDocument(input))
        {
            attribute = document.CreateNavigator();
            Assert.True(attribute.MoveToFirstChild()); Assert.True(attribute.MoveToAttribute("n", ""));
        }
        XmlQueryRuntime runtime = CreateRuntime();
        var prepared = runtime.XsltFunctions.PrepareForkBindings(new XPathItem[] { attribute });
        using var reader = XmlReader.Create(new StringReader("<input/>"));
        using var source = new StreamedDocument(reader);
        Run((StreamedNavigator)source.CreateNavigator(), view => {
            var node = Assert.IsType<StreamedNavigator>(Assert.Single(runtime.XsltFunctions.ProjectForkBindings(prepared, view)));
            Assert.Equal(12, node.ValueAsInt);
            Assert.True(node.SchemaInfo!.IsDefault);
            Assert.Equal(XmlTypeCode.Integer, node.SchemaInfo.SchemaType!.TypeCode);
            Assert.True(node.IsSamePosition(attribute));
        });
    }

    [Fact]
    public void PreparationDoesNotMakeUnavailableContentConsumable()
    {
        XmlQueryRuntime runtime = CreateRuntime();
        using var input = XmlReader.Create(new StringReader("<source>value</source>"));
        using var document = new StreamedDocument(input);
        var prepared = runtime.XsltFunctions.PrepareForkBindings(new XPathItem[] { document.CreateNavigator() });
        using var otherInput = XmlReader.Create(new StringReader("<other/>"));
        using var other = new StreamedDocument(otherInput);
        Run((StreamedNavigator)other.CreateNavigator(), view => {
            var node = Assert.IsType<StreamedNavigator>(Assert.Single(runtime.XsltFunctions.ProjectForkBindings(prepared, view)));
            Assert.Equal(XPathNodeType.Root, node.NodeType);
            Assert.Equal("stream-position", Assert.Throws<XdmException>(() => node.Value).Code);
            Assert.Equal("stream-position", Assert.Throws<XdmException>(() => node.MoveToFirstChild()).Code);
        });
        Assert.Equal(ReadState.Initial, input.ReadState);
        Assert.Equal(ReadState.Initial, otherInput.ReadState);
    }

    [Fact]
    public void BufferedMembersKeepIdentityAndPrivateContentPositionsAfterSourceClosure()
    {
        XmlQueryRuntime runtime = CreateRuntime();
        XsltLibrary.PreparedForkBindings prepared;
        StreamedNavigator original;
        using (var input = XmlReader.Create(new StringReader("<doc><row id='a'><n>one</n></row><row id='b'>two</row></doc>")))
        using (var document = new StreamedDocument(input))
        {
            original = (StreamedNavigator)document.CreateNavigator();
            Assert.True(original.MoveToFirstChild()); Assert.True(original.MoveToFirstChild());
            runtime.XsltFunctions.X31BufferForkItem(original);
            prepared = runtime.XsltFunctions.PrepareForkBindings(new XPathItem[] { original, original });
            var next = original.Clone();
            Assert.True(next.MoveToNext()); Assert.Equal("b", next.GetAttribute("id", ""));
        }
        IList<XPathItem> first = runtime.XsltFunctions.ProjectForkBindings(prepared);
        IList<XPathItem> second = runtime.XsltFunctions.ProjectForkBindings(prepared);
        var row = Assert.IsType<StreamedNavigator>(first[0]);
        var alias = Assert.IsType<StreamedNavigator>(first[1]);
        var other = Assert.IsType<StreamedNavigator>(second[0]);
        Assert.NotSame(row, alias);
        Assert.Same(row.Document, alias.Document);
        Assert.NotSame(row.Document, other.Document);
        Assert.True(row.IsSamePosition(original));
        Assert.True(row.IsSamePosition(other));
        Assert.Equal("one", row.Value);
        Assert.True(row.MoveToFirstChild()); Assert.Equal("n", row.Name);
        Assert.Equal("row", alias.Name); Assert.Equal("row", other.Name);
        Assert.Equal("one", alias.Value); Assert.Equal("one", other.Value);
        Assert.True(row.MoveToFirstChild()); Assert.Equal("one", row.Value);
        Assert.True(row.MoveToParent()); Assert.True(row.MoveToParent());
        Assert.True(row.MoveToParent()); Assert.Equal("doc", row.Name);
        Assert.Throws<XdmException>(() => row.MoveToFirstChild());
    }

    [Fact]
    public void BufferedReplayPreservesSchemaAnnotationsAndDefaultAttributes()
    {
        var schemas = new XmlSchemaSet();
        schemas.Add(null, XmlReader.Create(new StringReader("<xs:schema xmlns:xs='http://www.w3.org/2001/XMLSchema'><xs:element name='row'><xs:complexType><xs:simpleContent><xs:extension base='xs:integer'><xs:attribute name='n' type='xs:integer' default='12'/></xs:extension></xs:simpleContent></xs:complexType></xs:element></xs:schema>")));
        XmlQueryRuntime runtime = CreateRuntime();
        XsltLibrary.PreparedForkBindings prepared;
        using (var input = XmlReader.Create(new StringReader("<row>42</row>"), new() { ValidationType = ValidationType.Schema, Schemas = schemas }))
        using (var document = new StreamedDocument(input))
        {
            var row = (StreamedNavigator)document.CreateNavigator();
            Assert.True(row.MoveToFirstChild());
            runtime.XsltFunctions.X31BufferForkItem(row);
            prepared = runtime.XsltFunctions.PrepareForkBindings(new XPathItem[] { row });
        }
        var value = Assert.IsType<StreamedNavigator>(Assert.Single(runtime.XsltFunctions.ProjectForkBindings(prepared)));
        Assert.Equal(42, value.ValueAsInt);
        Assert.Equal(XmlTypeCode.Integer, value.SchemaInfo!.SchemaType!.TypeCode);
        Assert.True(value.MoveToAttribute("n", ""));
        Assert.Equal(12, value.ValueAsInt);
        Assert.True(value.SchemaInfo!.IsDefault);
        Assert.Equal(XmlTypeCode.Integer, value.SchemaInfo.SchemaType!.TypeCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompactSourcesAndReplayViewsShareCreationOrder(bool compactFirst)
    {
        XPathNavigator? compact = compactFirst ? Xsl3.Tree.CompactDocument.Load(new StringReader("<constructed/>")).CreateNavigator() : null;
        using var input = XmlReader.Create(new StringReader("<source/>"));
        using var document = new StreamedDocument(input);
        compact ??= Xsl3.Tree.CompactDocument.Load(new StringReader("<constructed/>")).CreateNavigator();
        var source = (StreamedNavigator)document.CreateNavigator();
        var order = new DocumentOrderComparer();
        int expected = compactFirst ? 1 : -1;
        Assert.Equal(expected, order.Compare(source, compact));
        Assert.Equal(-expected, order.Compare(compact, source));
        XmlQueryRuntime runtime = CreateRuntime();
        runtime.XsltFunctions.X31BufferForkItem(source);
        var prepared = runtime.XsltFunctions.PrepareForkBindings(new XPathItem[] { source });
        var replay = Assert.IsType<StreamedNavigator>(Assert.Single(runtime.XsltFunctions.ProjectForkBindings(prepared)));
        Assert.Equal(0, order.Compare(source, replay));
        Assert.Equal(expected, order.Compare(replay, compact));
        Assert.Equal(-expected, order.Compare(compact, replay));
    }

    [Fact]
    public void SerializationBuffersStructuredNodeValuesWithoutInvokingForeignFunctions()
    {
        XmlQueryRuntime runtime = CreateRuntime();
        var function = new CompiledFunction(0, new(null, Array.Empty<SequenceTypeTest>(), SequenceTypeTest.AnySequence), CreateRuntime());
        XsltLibrary.PreparedForkBindings prepared;
        using (var input = XmlReader.Create(new StringReader("<doc><row>value</row></doc>")))
        using (var document = new StreamedDocument(input))
        {
            var source = (StreamedNavigator)document.CreateNavigator();
            Assert.True(source.MoveToFirstChild()); Assert.True(source.MoveToFirstChild());
            MapItem map = MapItem.Empty.Put(AtomicValue.OfString("node"), new XPathItem[] { source })
                .Put(AtomicValue.OfString("function"), new XPathItem[] { function });
            prepared = runtime.XsltFunctions.PrepareForkOutput(new XPathItem[] { ArrayItem.Of(new[] { new XPathItem[] { map } }) });
        }
        var array = Assert.IsType<ArrayItem>(Assert.Single(runtime.XsltFunctions.ProjectForkBindings(prepared)));
        var projected = Assert.IsType<MapItem>(Assert.Single(array[0]));
        Assert.Equal("value", Assert.Single(projected.GetString("node")!).Value);
        Assert.Same(function, Assert.Single(projected.GetString("function")!));
    }

    [Fact]
    public void DeepHeaderCaptureAndProjectionDoNotUseTheNativeStack()
    {
        const int depth = 20_000;
        string xml = string.Concat(System.Linq.Enumerable.Repeat("<n>", depth)) + "<leaf id='value'/>"
            + string.Concat(System.Linq.Enumerable.Repeat("</n>", depth));
        using var input = XmlReader.Create(new StringReader(xml));
        using var document = new StreamedDocument(input);
        XPathNavigator attribute = document.CreateNavigator();
        for (int i = 0; i <= depth; i++) Assert.True(attribute.MoveToFirstChild());
        Assert.True(attribute.MoveToAttribute("id", ""));
        XmlQueryRuntime runtime = CreateRuntime();
        var prepared = runtime.XsltFunctions.PrepareForkBindings(new XPathItem[] { attribute, attribute });
        using var otherInput = XmlReader.Create(new StringReader("<other/>"));
        using var other = new StreamedDocument(otherInput);
        Run((StreamedNavigator)other.CreateNavigator(), view => {
            IList<XPathItem> values = runtime.XsltFunctions.ProjectForkBindings(prepared, view);
            var node = Assert.IsType<StreamedNavigator>(values[0]);
            Assert.Equal("value", node.Value);
            Assert.Same(node.PositionNode, Assert.IsType<StreamedNavigator>(values[1]).PositionNode);
            int ancestors = 0;
            Assert.True(node.MoveToParent());
            while (node.MoveToParent()) ancestors++;
            Assert.Equal(depth + 1, ancestors);
        });
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (XsltLibrary.PreparedForkBindings Bindings, WeakReference<StreamedDocument.Node> Frame) RetainAttribute(XmlQueryRuntime runtime)
    {
        using var reader = XmlReader.Create(new StringReader("<doc><a id='first'/><b/></doc>"));
        using var document = new StreamedDocument(reader);
        var position = (StreamedNavigator)document.CreateNavigator();
        position.MoveToFirstChild(); position.MoveToFirstChild();
        var weak = new WeakReference<StreamedDocument.Node>(position.PositionNode);
        XPathNavigator attribute = position.Clone(); attribute.MoveToAttribute("id", "");
        var prepared = runtime.XsltFunctions.PrepareForkBindings(new XPathItem[] { attribute });
        position.MoveToNext();
        return (prepared, weak);
    }

    [Fact]
    public void PreparedHeadersDoNotRetainPastSourceFrames()
    {
        XmlQueryRuntime runtime = CreateRuntime();
        var retained = RetainAttribute(runtime);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.False(retained.Frame.TryGetTarget(out _));
        using var input = XmlReader.Create(new StringReader("<input/>"));
        using var source = new StreamedDocument(input);
        Run((StreamedNavigator)source.CreateNavigator(), view =>
            Assert.Equal("first", Assert.Single(runtime.XsltFunctions.ProjectForkBindings(retained.Bindings, view)).Value));
    }

    [Fact]
    public void ForeignFunctionsFailDuringPreparationBeforeInputStarts()
    {
        XmlQueryRuntime runtime = CreateRuntime();
        var foreign = new CompiledFunction(0, new(null, Array.Empty<SequenceTypeTest>(), SequenceTypeTest.AnySequence), CreateRuntime());
        Assert.Throws<InvalidOperationException>(() => runtime.XsltFunctions.PrepareForkBindings(new XPathItem[] { foreign }));
    }

    [Fact]
    public void PreparationChecksCancellationForEmptyBindings()
    {
        XmlQueryRuntime runtime = CreateRuntime();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        CancellationToken saved = XmlQueryRuntime.Cancellation;
        try
        {
            XmlQueryRuntime.Cancellation = cancellation.Token;
            Assert.Throws<OperationCanceledException>(() => runtime.XsltFunctions.PrepareForkBindings(Array.Empty<XPathItem>()));
        }
        finally { XmlQueryRuntime.Cancellation = saved; }
    }

    [Theory]
    [InlineData("p", false)]
    [InlineData("xml", false)]
    [InlineData("p", true)]
    [InlineData("xml", true)]
    public void NamespaceAndElementNamesUseTheProngsNameTable(string prefix, bool retained)
    {
        XmlQueryRuntime runtime = CreateRuntime();
        using var input = XmlReader.Create(new StringReader("<doc xmlns:p='urn:p'><a/><b/></doc>"));
        using var document = new StreamedDocument(input);
        var focus = (StreamedNavigator)document.CreateNavigator();
        Assert.True(focus.MoveToFirstChild()); Assert.True(focus.MoveToFirstChild());
        XPathNavigator ns = focus.Clone(); Assert.True(ns.MoveToNamespace(prefix));
        var prepared = runtime.XsltFunctions.PrepareForkBindings(new XPathItem[] { ns });
        if (retained) Assert.True(focus.MoveToNext());
        Run(focus, view => {
            var node = Assert.IsType<StreamedNavigator>(Assert.Single(runtime.XsltFunctions.ProjectForkBindings(prepared, view)));
            Assert.Same(node.LocalName, node.NameTable.Get(node.LocalName));
            Assert.True(node.MoveToParent());
            Assert.Same(node.LocalName, node.NameTable.Get(node.LocalName));
            Assert.True(node.MoveToParent());
            Assert.Same(node.LocalName, node.NameTable.Get(node.LocalName));
        });
    }
}
