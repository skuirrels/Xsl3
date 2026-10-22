// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.Schema;
using System.Xml.XPath;
using Xsl3.Streaming;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

public class StreamedNavigatorTests
{
    [Fact]
    public void DocumentOrderIdsDoNotRetainClosedStreamedDocuments()
    {
        var comparer = new Runtime.DocumentOrderComparer();
        WeakReference reference = RegisterDocument(comparer);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(reference.IsAlive);
        GC.KeepAlive(comparer);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference RegisterDocument(Runtime.DocumentOrderComparer comparer)
    {
        using var document = new StreamedDocument(Reader("<doc/>"), ownsReader: true);
        XPathNavigator nav = document.CreateNavigator();
        int id = comparer.GetDocumentIndex(nav);
        GC.Collect();
        Assert.Equal(id, comparer.GetDocumentIndex(nav.Clone()));
        return new WeakReference(document);
    }

    private static XmlReader Reader(string xml) => XmlReader.Create(new StringReader(xml), new() { DtdProcessing = DtdProcessing.Parse }, "file:///stream/input.xml");

    [Fact]
    public void DescendantCursorDoesNotSeekAheadAndStaysInsideItsElement()
    {
        using var reader = Reader("<doc><group><record>1</record><record><value>2</value></record></group><record>outside</record></doc>");
        using var document = new StreamedDocument(reader);
        var nav = document.CreateNavigator();
        nav.MoveToFirstChild();
        nav.MoveToFirstChild();
        var iterator = new Runtime.DescendantIterator();
        iterator.Create(nav, Runtime.XmlNavNameFilter.Create("record", ""), orSelf: false);
        Assert.Equal("group", reader.LocalName);
        var values = new List<string>();
        while (iterator.MoveNext()) values.Add(iterator.Current.Value);
        Assert.Equal(new[] { "1", "2" }, values);
        Assert.Equal(XmlNodeType.EndElement, reader.NodeType);
        Assert.Equal("group", reader.LocalName);
        Assert.True(nav.MoveToNext());
        Assert.Equal("outside", nav.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AttributesHaveNoDescendants(bool orSelf)
    {
        using var document = new StreamedDocument(Reader("<doc id='value'><child/></doc>"), true);
        var nav = document.CreateNavigator();
        nav.MoveToFirstChild();
        nav.MoveToFirstAttribute();
        var iterator = new Runtime.DescendantIterator();
        iterator.Create(nav, Runtime.XmlNavNeverFilter.Create(), orSelf);
        Assert.Equal(orSelf, iterator.MoveNext());
        if (orSelf) Assert.Equal("value", iterator.Current.Value);
        Assert.False(iterator.MoveNext());
    }

    [Fact]
    public void ConsumingAMatchingNestedDescendantFailsInsteadOfDroppingIt()
    {
        using var document = new StreamedDocument(Reader("<doc><record>1<record>2</record></record></doc>"), true);
        var iterator = new Runtime.DescendantIterator();
        iterator.Create(document.CreateNavigator(), Runtime.XmlNavNameFilter.Create("record", ""), false);
        Assert.True(iterator.MoveNext());
        Assert.Equal("12", iterator.Current.Value);
        Assert.Equal("stream-position", Assert.Throws<XdmException>(() => iterator.MoveNext()).Code);
    }

    [Fact]
    public void AFinishedOrAbandonedDescendantCursorIsNotKeptAliveByTheDocument()
    {
        using var document = new StreamedDocument(Reader("<doc/>"), true);
        WeakReference cursor = MakeCursor(document);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(cursor.IsAlive);
        GC.KeepAlive(document);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference MakeCursor(StreamedDocument document) =>
        new(new StreamedDescendantCursor((StreamedNavigator)document.CreateNavigator(), Runtime.XmlNavNameFilter.Create("record", "")));

    [Theory]
    [InlineData("<doc/>")]
    [InlineData("<?pi test?><doc><a>one</a><b/><a>two</a></doc><!--end-->")]
    [InlineData("<doc>one<![CDATA[two]]> three<a>four</a>five</doc>")]
    [InlineData("<doc> <a/>\n<b xml:space='preserve'>  </b>\t</doc>")]
    [InlineData("<doc xmlns='urn:doc' xmlns:a='urn:a' x='1' a:y='2'><a:child xmlns:a='urn:b'><leaf xmlns='' xml:lang='en'>text</leaf></a:child></doc>")]
    public void ForwardTraversalMatchesXPathDocument(string xml)
    {
        using var stream = new StreamedDocument(Reader(xml), ownsReader: true);
        XPathNavigator actual = stream.CreateNavigator();
        using XmlReader input = Reader(xml);
        XPathNavigator expected = new XPathDocument(input, XmlSpace.Preserve).CreateNavigator();
        Compare(actual, expected);
        Assert.True(actual.IsSamePosition(stream.CreateNavigator()));
    }

    private static void Compare(XPathNavigator actual, XPathNavigator expected)
    {
        Assert.Equal(expected.NodeType, actual.NodeType);
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.LocalName, actual.LocalName);
        Assert.Equal(expected.NamespaceURI, actual.NamespaceURI);
        Assert.Equal(expected.BaseURI, actual.BaseURI);
        Assert.Equal(expected.XmlLang, actual.XmlLang);
        Assert.Equal(expected.IsEmptyElement, actual.IsEmptyElement);
        if (actual.NodeType is not (XPathNodeType.Root or XPathNodeType.Element)) Assert.Equal(expected.Value, actual.Value);
        Assert.Equal(expected.HasAttributes, actual.HasAttributes);
        XPathNavigator a = actual.Clone(), e = expected.Clone();
        if (e.MoveToFirstAttribute())
        {
            Assert.True(a.MoveToFirstAttribute());
            while (true)
            {
                Assert.Equal(e.Name, a.Name);
                Assert.Equal(e.Value, a.Value);
                bool next = e.MoveToNextAttribute();
                Assert.Equal(next, a.MoveToNextAttribute());
                if (!next) break;
            }
        }
        foreach (var scope in new[] { XmlNamespaceScope.All, XmlNamespaceScope.Local, XmlNamespaceScope.ExcludeXml })
        {
            var es = expected.GetNamespacesInScope(scope).OrderBy(n => n.Key).ToArray();
            Assert.Equal(es, actual.GetNamespacesInScope(scope).OrderBy(n => n.Key).ToArray());
        }
        Assert.Equal(expected.HasChildren, actual.HasChildren);
        bool child = expected.MoveToFirstChild();
        Assert.Equal(child, actual.MoveToFirstChild());
        if (!child) return;
        do
        {
            Compare(actual, expected);
            bool next = expected.MoveToNext();
            Assert.Equal(next, actual.MoveToNext());
            if (!next) break;
        } while (true);
        Assert.True(expected.MoveToParent());
        Assert.True(actual.MoveToParent());
    }

    [Fact]
    public void AttributesNamespacesAndAncestorsDoNotAdvanceTheReader()
    {
        using XmlReader reader = Reader("<root xmlns:p='urn:p' id='r'><child xml:lang='en' p:value='3'><leaf/></child></root>");
        using var document = new StreamedDocument(reader);
        XPathNavigator nav = document.CreateNavigator();
        Assert.Equal(ReadState.Initial, reader.ReadState);
        Assert.True(nav.MoveToFirstChild());
        Assert.True(nav.MoveToFirstChild());
        Assert.Equal("child", reader.LocalName);
        var original = nav.Clone();
        Assert.True(nav.MoveToAttribute("value", "urn:p"));
        Assert.Equal("3", nav.Value);
        Assert.True(nav.MoveToParent());
        Assert.True(nav.MoveToNamespace("p"));
        Assert.Equal("urn:p", nav.Value);
        Assert.True(nav.MoveToParent());
        Assert.True(nav.IsSamePosition(original));
        Assert.True(nav.MoveToParent());
        Assert.Equal("r", nav.GetAttribute("id", ""));
        Assert.Equal("child", reader.LocalName);
    }

    [Theory]
    [InlineData("<r>a<b>b</b>c<![CDATA[d]]>e<!--ignore--><?pi ignore?></r>", "abcde")]
    [InlineData("<r/>", "")]
    [InlineData("<r></r>", "")]
    [InlineData("<r> </r>", " ")]
    public void StringValueConsumesOnlyItsSubtree(string subtree, string value)
    {
        using var document = new StreamedDocument(Reader("<doc>" + subtree + "<next>remaining</next></doc>"), true);
        XPathNavigator nav = document.CreateNavigator();
        nav.MoveToFirstChild();
        nav.MoveToFirstChild();
        Assert.Equal(value, nav.Value);
        Assert.Equal("r", nav.Name);
        Assert.True(nav.MoveToNext());
        Assert.Equal("next", nav.Name);
        Assert.Equal("remaining", nav.Value);
        Assert.False(nav.MoveToNext());
        Assert.False(nav.MoveToNext());
    }

    [Fact]
    public void ClonesShareConsumptionAndRejectRevisitingPassedContent()
    {
        using var document = new StreamedDocument(Reader("<doc><a>one</a><b>two</b></doc>"), true);
        XPathNavigator parent = document.CreateNavigator();
        parent.MoveToFirstChild();
        XPathNavigator nav = parent.Clone();
        nav.MoveToFirstChild();
        XPathNavigator copy = nav.Clone();
        Assert.Equal("one", nav.Value);
        AssertPositionError(() => _ = copy.Value);
        Assert.True(nav.MoveToNext());
        AssertPositionError(() => parent.MoveToFirstChild());
        Assert.Equal("two", nav.Value);
        AssertPositionError(() => nav.MoveToPrevious());
        Assert.True(copy.MoveToParent());
        Assert.Equal("doc", copy.Name);
    }

    [Fact]
    public void ValueAfterTraversingChildrenRaisesAnErrorInsteadOfReturningEmpty()
    {
        using var document = new StreamedDocument(Reader("<doc><a>content</a></doc>"), true);
        XPathNavigator nav = document.CreateNavigator();
        nav.MoveToFirstChild();
        var parent = nav.Clone();
        nav.MoveToFirstChild();
        Assert.False(nav.MoveToNext());
        AssertPositionError(() => _ = parent.Value);
    }

    [Fact]
    public void UnselectedSubtreesAreSkippedAndIdsKeepDocumentOrder()
    {
        using var document = new StreamedDocument(Reader("<doc><skip><nested>unused</nested></skip><wanted id='v'>42</wanted></doc>"), true);
        XPathNavigator nav = document.CreateNavigator();
        nav.MoveToFirstChild();
        nav.MoveToFirstChild();
        var skipped = nav.Clone();
        Assert.True(nav.MoveToNext());
        Assert.Equal("wanted", nav.Name);
        Assert.Equal(XmlNodeOrder.Before, skipped.ComparePosition(nav));
        Assert.Equal("42", nav.Value);
        AssertPositionError(() => _ = skipped.Value);
        Assert.Equal(skipped.UniqueId, skipped.Clone().UniqueId);
        Assert.NotEqual(skipped.UniqueId, nav.UniqueId);
    }

    [Fact]
    public void GroundingUsesACompactSubtreeAndLeavesTheNextSiblingUnread()
    {
        using var document = new StreamedDocument(Reader("<doc xmlns:p='urn:p' xmlns:unused='urn:unused'><p:record id='1'>a<b>b</b>c</p:record><record>next</record></doc>"), true);
        var nav = (StreamedNavigator)document.CreateNavigator();
        nav.MoveToFirstChild();
        nav.MoveToFirstChild();
        XPathNavigator grounded = nav.Ground();
        Assert.IsType<Xsl3.Tree.CompactNavigator>(grounded);
        Assert.Equal(XPathNodeType.Element, grounded.NodeType);
        Assert.Equal("abc", grounded.Value);
        Assert.Equal("1", grounded.GetAttribute("id", ""));
        Assert.Equal("urn:unused", grounded.LookupNamespace("unused"));
        Assert.False(grounded.MoveToParent());
        Assert.True(nav.MoveToNext());
        Assert.Equal("next", nav.Value);
    }

    [Fact]
    public void RootCanBeGroundedWithoutDroppingItsFirstElement()
    {
        using var document = new StreamedDocument(Reader("<?p v?><doc><a>value</a></doc>"), true);
        var nav = (StreamedNavigator)document.CreateNavigator();
        XPathNavigator grounded = nav.Ground();
        Assert.Equal(XPathNodeType.Root, grounded.NodeType);
        Assert.Equal("value", grounded.Value);
        Assert.True(grounded.MoveToFirstChild());
        Assert.Equal(XPathNodeType.ProcessingInstruction, grounded.NodeType);
    }

    [Fact]
    public void HeaderLookaheadDoesNotPreventStringValueOrGrounding()
    {
        using var document = new StreamedDocument(Reader("<doc><a>one<b>two</b></a><a>three</a></doc>"), true);
        var nav = (StreamedNavigator)document.CreateNavigator();
        nav.MoveToFirstChild();
        nav.MoveToFirstChild();
        Assert.True(nav.HasChildren);
        Assert.Equal("onetwo", nav.Ground().Value);
        Assert.True(nav.MoveToNext());
        Assert.True(nav.HasChildren);
        Assert.Equal("three", nav.Value);
    }

    [Theory]
    [InlineData("count(/doc/record)", "3")]
    [InlineData("sum(/doc/record/@n)", "6")]
    [InlineData("sum(/doc/record)", "6")]
    [InlineData("string(document-uri(copy-of(.)))", "")]
    public void ExistingCompiledExpressionsCanConsumeTheForwardNavigator(string select, string expected)
    {
        using var document = new StreamedDocument(Reader("<doc><record n='1'>1</record><record n='2'>2</record><record n='3'>3</record></doc>"), true);
        string sheet = "<xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform'><xsl:output method='text'/>"
            + $"<xsl:template match='/'><xsl:value-of select='{select}'/></xsl:template></xsl:stylesheet>";
        XsltExecutable executable = new XsltCompiler().Compile(new StringReader(sheet));
        var output = new StringWriter();
        executable.CreateTransformer().ApplyTemplates(new XdmNode(document.CreateNavigator()), output);
        Assert.Equal(expected, output.ToString());
    }

    [Theory]
    [InlineData("copy-of(.)")]
    [InlineData("snapshot(.)")]
    public void CompiledGroundingRunsOncePerRecord(string expression)
    {
        using var document = new StreamedDocument(Reader("<doc xmlns:p='urn:p'><record n='1'><p:value>one</p:value></record><record n='2'><p:value>two</p:value></record></doc>"), true);
        string sheet = "<xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform'>"
            + $"<xsl:template match='/'><out><xsl:for-each select='/doc/record'><xsl:sequence select='{expression}'/></xsl:for-each></out></xsl:template></xsl:stylesheet>";
        XsltExecutable executable = new XsltCompiler().Compile(new StringReader(sheet));
        var output = new StringWriter();
        executable.CreateTransformer().ApplyTemplates(new XdmNode(document.CreateNavigator()), output);
        var result = new XPathDocument(new StringReader(output.ToString())).CreateNavigator();
        Assert.Equal(2d, result.Evaluate("count(/out/record)"));
        Assert.Equal("onetwo", result.Value);
        Assert.Equal("urn:p", result.SelectSingleNode("/out/record")!.LookupNamespace("p"));
    }

    [Fact]
    public void CancellationAndReaderOwnershipAreExplicit()
    {
        using XmlReader reader = Reader("<doc/>");
        using var cancel = new CancellationTokenSource();
        var document = new StreamedDocument(reader, cancellation: cancel.Token);
        cancel.Cancel();
        Assert.Throws<OperationCanceledException>(() => document.CreateNavigator().MoveToFirstChild());
        document.Dispose();
        Assert.Equal(ReadState.Initial, reader.ReadState);
        using var ownedReader = Reader("<doc/>");
        new StreamedDocument(ownedReader, ownsReader: true).Dispose();
        Assert.Equal(ReadState.Closed, ownedReader.ReadState);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(10000)]
    public void RetainedFramesAreBoundedByDepthRatherThanRecordCount(int count)
    {
        string xml = "<doc>" + string.Concat(Enumerable.Repeat("<record id='v'><value>text</value></record>", count)) + "</doc>";
        using var document = new StreamedDocument(Reader(xml), true);
        XPathNavigator nav = document.CreateNavigator();
        nav.MoveToFirstChild();
        nav.MoveToFirstChild();
        int visited = 0, maximum = 0;
        do
        {
            Assert.Equal("text", nav.Value);
            visited++;
            maximum = Math.Max(maximum, document.RetainedNodeCount);
        } while (nav.MoveToNext());
        Assert.Equal(count, visited);
        Assert.InRange(maximum, 1, 4); // root, doc, record; one leaf of lookahead at most
    }

    [Fact]
    public void ValidatingReaderAnnotationsSurviveGrounding()
    {
        const string schema = "<xs:schema xmlns:xs='http://www.w3.org/2001/XMLSchema'><xs:element name='doc'><xs:complexType><xs:sequence><xs:element name='n' type='xs:integer'/></xs:sequence></xs:complexType></xs:element></xs:schema>";
        var schemas = new XmlSchemaSet();
        using (XmlReader xsd = XmlReader.Create(new StringReader(schema))) schemas.Add(null, xsd);
        using XmlReader reader = XmlReader.Create(new StringReader("<doc><n>42</n></doc>"), new() { ValidationType = ValidationType.Schema, Schemas = schemas });
        using var document = new StreamedDocument(reader);
        var nav = (StreamedNavigator)document.CreateNavigator();
        nav.MoveToFirstChild();
        nav.MoveToFirstChild();
        Assert.Equal(XmlTypeCode.Integer, nav.SchemaInfo!.SchemaType!.TypeCode);
        XPathNavigator grounded = nav.Ground();
        Assert.Equal(XmlTypeCode.Integer, grounded.SchemaInfo!.SchemaType!.TypeCode);
        Assert.Equal(42, grounded.ValueAsInt);
    }

    [Fact]
    public void TypedWhitespaceUsesTheSameFacetsAsTheCompactTree()
    {
        const string schema = "<xs:schema xmlns:xs='http://www.w3.org/2001/XMLSchema'><xs:element name='doc'><xs:complexType><xs:sequence><xs:element name='n' type='xs:token'/></xs:sequence><xs:attribute name='a' type='xs:token'/></xs:complexType></xs:element></xs:schema>";
        var schemas = new XmlSchemaSet();
        using (XmlReader xsd = XmlReader.Create(new StringReader(schema))) schemas.Add(null, xsd);
        using XmlReader reader = XmlReader.Create(new StringReader("<doc a=' x   y '>\n<n> one   two </n>\n</doc>"), new() { ValidationType = ValidationType.Schema, Schemas = schemas });
        using var document = new StreamedDocument(reader);
        XPathNavigator nav = document.CreateNavigator();
        nav.MoveToFirstChild();
        Assert.Equal("x y", nav.GetAttribute("a", ""));
        Assert.True(nav.MoveToFirstChild());
        Assert.Equal("n", nav.Name); // element-only whitespace is absent
        Assert.Equal("one two", nav.Value);
        Assert.False(nav.MoveToNext());
    }

    [Fact]
    public void XmlIdAndMalformedInputKeepTheirDefinedSemantics()
    {
        using var document = new StreamedDocument(Reader("<doc xml:id='  a  '><incomplete>"), true);
        XPathNavigator nav = document.CreateNavigator();
        nav.MoveToFirstChild();
        Assert.Equal("a", nav.GetAttribute("id", "http://www.w3.org/XML/1998/namespace"));
        Assert.Throws<XmlException>(() => _ = nav.Value);
    }

    [Fact]
    public void ReaderPositionedInsideADocumentKeepsItsNamespaceContext()
    {
        using XmlReader reader = Reader("<outer xmlns:p='urn:p' xmlns:u='urn:unused'><p:inner>text</p:inner></outer>");
        Assert.True(reader.ReadToFollowing("inner", "urn:p"));
        using var document = new StreamedDocument(reader);
        XPathNavigator nav = document.CreateNavigator();
        Assert.True(nav.MoveToFirstChild());
        Assert.Equal("urn:unused", nav.LookupNamespace("u"));
        Assert.Equal("p:inner", nav.Name);
        Assert.Equal("text", nav.Value);
        Assert.False(nav.MoveToNext());
        Assert.Equal("outer", reader.LocalName); // no reads beyond the caller's reader scope
    }

    [Fact]
    public void EntityBoundariesDoNotSplitAdjacentTextNodes()
    {
        using var reader = new XmlTextReader(new StringReader("<!DOCTYPE doc [<!ENTITY e 'two'>]><doc>one&e;three</doc>"))
            { DtdProcessing = DtdProcessing.Parse, EntityHandling = EntityHandling.ExpandCharEntities };
        using var document = new StreamedDocument(reader);
        XPathNavigator nav = document.CreateNavigator();
        Assert.True(nav.MoveToFirstChild());
        Assert.True(nav.MoveToFirstChild());
        Assert.Equal("onetwothree", nav.Value);
        Assert.False(nav.MoveToNext());
    }

    private static void AssertPositionError(Action action)
    {
        XdmException error = Assert.Throws<XdmException>(action);
        Assert.Equal(new QName("http://xsl3/err", "stream-position"), error.Name);
    }
}
