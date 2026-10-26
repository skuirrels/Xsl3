// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Xml;
using System.Xml.Schema;
using System.Xml.XPath;
using Xsl3.Streaming;
using Xunit;

namespace Xsl3.Tests;

public class StreamedReplayTests
{
    [Theory]
    [InlineData("snapshot(//a)")]
    [InlineData("copy-of(//a)")]
    public void ExplicitGroundingKeepsNestedMatchesAndFollowingInput(string select)
    {
        string sheet = $$"""
            <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:mode streamable="yes"/><xsl:output method="text"/>
              <xsl:template match="/"><xsl:iterate select="{{select}}"><xsl:value-of select="@id, string(.)" separator=":"/><xsl:text>;</xsl:text></xsl:iterate></xsl:template>
            </xsl:stylesheet>
            """;
        var transform = new XsltCompiler { Settings = new() { StreamedExecution = true } }.Compile(new StringReader(sheet)).CreateTransformer();
        using var input = XmlReader.Create(new StringReader("<doc><a id='outer'>one<a id='inner'>two</a>three</a><a id='next'>four</a></doc>"));
        using var output = new StringWriter();
        transform.ApplyTemplates(input, output);
        Assert.Equal("outer:onetwothree;inner:two;next:four;", output.ToString());
        Assert.NotEqual(ReadState.Closed, input.ReadState);
    }

    [Theory]
    [InlineData("snapshot(//a)")]
    [InlineData("copy-of(//a)")]
    public void NestedSelectionsProduceIndependentCopies(string select)
    {
        string sheet = $$"""
            <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:mode streamable="yes"/><xsl:template match="/"><xsl:sequence select="{{select}}"/></xsl:template>
            </xsl:stylesheet>
            """;
        var transform = new XsltCompiler { Settings = new() { StreamedExecution = true } }.Compile(new StringReader(sheet)).CreateTransformer();
        using var input = XmlReader.Create(new StringReader("<doc><a id='outer'><a id='inner'/></a></doc>"));
        var copies = transform.ApplyTemplates(input);
        Assert.Equal(2, copies.Count);
        var childOfFirst = ((XPathNavigator)copies.Items[0]).Clone();
        Assert.True(childOfFirst.MoveToFirstChild());
        var second = (XPathNavigator)copies.Items[1];
        Assert.Equal(childOfFirst.GetAttribute("id", ""), second.GetAttribute("id", ""));
        Assert.False(childOfFirst.IsSamePosition(second));
        Assert.Equal(XmlNodeOrder.Unknown, childOfFirst.ComparePosition(second));
    }

    [Fact]
    public void ReplayedSourcePositionsKeepTheirOriginalIdentityAndAncestorHeaders()
    {
        using var input = XmlReader.Create(new StringReader("<doc xml:lang='en'><a id='outer'><a id='inner'>two</a></a><after/></doc>"));
        using var document = new StreamedDocument(input);
        var iterator = new Runtime.DescendantIterator();
        iterator.Create(document.CreateNavigator(), Runtime.XmlNavNameFilter.Create("a", ""), false);
        Assert.True(iterator.MoveNext());
        var outer = (StreamedNavigator)iterator.Current.Clone();
        Assert.Equal("two", outer.Ground().Value);
        Assert.True(iterator.MoveNext());
        var inner = iterator.Current.Clone();
        Assert.Equal("inner", inner.GetAttribute("id", ""));
        Assert.Equal("two", inner.Value);
        Assert.Equal("en", inner.XmlLang);
        Assert.True(outer.IsDescendant(inner));
        Assert.Equal(XmlNodeOrder.Before, outer.ComparePosition(inner));
        Assert.True(inner.IsSamePosition(inner.Clone()));
        Assert.True(inner.MoveToParent());
        Assert.True(inner.IsSamePosition(outer));
        Assert.False(iterator.MoveNext());
    }

    [Fact]
    public void ReplayedChildAndSiblingAxesRemainInsideTheCopiedSubtree()
    {
        using var input = XmlReader.Create(new StringReader("<doc><a><b>one</b><!--c--><?p x?><b>two</b></a><a><b>three</b></a></doc>"));
        using var document = new StreamedDocument(input);
        var iterator = new Runtime.DescendantIterator();
        iterator.Create(document.CreateNavigator(), Runtime.XmlNavNameFilter.Create("a", ""), false);
        Assert.True(iterator.MoveNext());
        var first = (StreamedNavigator)iterator.Current.Clone();
        first.Ground();
        var values = new List<string>();
        var child = first.Clone();
        Assert.True(child.MoveToFirstChild());
        do { values.Add(child.Value); } while (child.MoveToNext());
        Assert.Equal(new[] { "one", "c", "x", "two" }, values);
        Assert.True(iterator.MoveNext());
        Assert.Equal("three", iterator.Current.Value);
        Assert.False(iterator.MoveNext());
    }

    [Fact]
    public void ExplicitCopiesWithoutAnOverlappingScanDoNotCreateReplayBuffers()
    {
        using var input = XmlReader.Create(new StringReader("<doc><a><a/></a><a/></doc>"));
        using var document = new StreamedDocument(input);
        var iterator = new Runtime.DescendantIterator();
        iterator.CreateTopmost(document.CreateNavigator(), Runtime.XmlNavNameFilter.Create("a", ""), false);
        Assert.True(iterator.MoveNext());
        var first = (StreamedNavigator)iterator.Current;
        first.Ground();
        Assert.Null(first.PositionNode.Replay);
        Assert.True(iterator.MoveNext());
        Assert.False(iterator.MoveNext());
    }

    [Fact]
    public void AReplayedEmptyElementHasNoChildren()
    {
        using var input = XmlReader.Create(new StringReader("<doc><a/></doc>"));
        using var document = new StreamedDocument(input);
        var iterator = new Runtime.DescendantIterator();
        iterator.Create(document.CreateNavigator(), Runtime.XmlNavNameFilter.Create("a", ""), false);
        Assert.True(iterator.MoveNext());
        var first = (StreamedNavigator)iterator.Current;
        first.Ground();
        Assert.False(first.HasChildren);
        Assert.False(first.MoveToFirstChild());
        Assert.Equal("", first.Value);
        Assert.False(iterator.MoveNext());
    }

    [Fact]
    public void ReplayedNodesPreserveValidatedAttributesAndFinalTypeAnnotations()
    {
        const string schema = """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
              <xs:complexType name="item" mixed="true"><xs:sequence><xs:element name="a" type="item" minOccurs="0" maxOccurs="unbounded"/></xs:sequence><xs:attribute name="id" type="xs:integer"/></xs:complexType>
              <xs:element name="doc"><xs:complexType><xs:sequence><xs:element name="a" type="item"/></xs:sequence></xs:complexType></xs:element>
            </xs:schema>
            """;
        var schemas = new XmlSchemaSet();
        using (var xsd = XmlReader.Create(new StringReader(schema))) schemas.Add(null, xsd);
        using var input = XmlReader.Create(new StringReader("<doc><a id='1'><a id='2'>value</a></a></doc>"),
            new() { ValidationType = ValidationType.Schema, Schemas = schemas });
        using var document = new StreamedDocument(input);
        var iterator = new Runtime.DescendantIterator();
        iterator.Create(document.CreateNavigator(), Runtime.XmlNavNameFilter.Create("a", ""), false);
        Assert.True(iterator.MoveNext());
        ((StreamedNavigator)iterator.Current).Ground();
        Assert.True(iterator.MoveNext());
        XPathNavigator inner = iterator.Current.Clone();
        Assert.Equal("item", inner.SchemaInfo?.SchemaType?.Name);
        Assert.Equal(XmlSchemaValidity.Valid, inner.SchemaInfo?.Validity);
        Assert.Equal("value", inner.Value);
        Assert.True(inner.MoveToFirstAttribute());
        Assert.Equal(XmlTypeCode.Integer, inner.SchemaInfo?.SchemaType?.TypeCode);
        Assert.Equal(2, inner.ValueAsInt);
        Assert.False(iterator.MoveNext());
    }

    [Fact]
    public void AHeaderPeekBeforeGroundingIsIncludedInReplay()
    {
        using var input = XmlReader.Create(new StringReader("<doc><a><a>value</a></a></doc>"));
        using var document = new StreamedDocument(input);
        var iterator = new Runtime.DescendantIterator();
        iterator.Create(document.CreateNavigator(), Runtime.XmlNavNameFilter.Create("a", ""), false);
        Assert.True(iterator.MoveNext());
        var first = (StreamedNavigator)iterator.Current;
        Assert.True(first.HasChildren);
        first.Ground();
        Assert.True(iterator.MoveNext());
        Assert.Equal("value", iterator.Current.Value);
        Assert.False(iterator.MoveNext());
    }

    [Theory]
    [InlineData("strip", false)]
    [InlineData("strip", true)]
    [InlineData("preserve", false)]
    [InlineData("preserve", true)]
    public void CopyInstructionsReplayIndependentlyOfNamespaceAndAnnotationOptions(string validation, bool namespaces)
    {
        string sheet = $$"""
            <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:mode streamable="yes"/><xsl:output omit-xml-declaration="yes"/>
              <xsl:template match="/"><out><xsl:copy-of select="//a" validation="{{validation}}" copy-namespaces="{{(namespaces ? "yes" : "no")}}"/></out></xsl:template>
            </xsl:stylesheet>
            """;
        var transform = new XsltCompiler { Settings = new() { StreamedExecution = true } }.Compile(new StringReader(sheet)).CreateTransformer();
        using var input = XmlReader.Create(new StringReader("<doc><a id='outer'><a id='inner'/></a><a id='next'/></doc>"));
        using var output = new StringWriter();
        transform.ApplyTemplates(input, output);
        Assert.Equal("<out><a id=\"outer\"><a id=\"inner\"/></a><a id=\"inner\"/><a id=\"next\"/></out>", output.ToString());
    }

#if NET10_0_OR_GREATER
    [Fact]
    public void SavedAssembliesReplayOverlappingSnapshots()
    {
        const string sheet = """
            <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:mode streamable="yes"/><xsl:output method="text"/>
              <xsl:template match="/"><xsl:iterate select="snapshot(//a)"><xsl:value-of select="@id"/></xsl:iterate></xsl:template>
            </xsl:stylesheet>
            """;
        var settings = new XsltSettings { StreamedExecution = true };
        using var assembly = new MemoryStream();
        new XslCompiledTransform().CompileToAssembly(XmlReader.Create(new StringReader(sheet)), settings, null,
            "Xsl3.Tests.StreamedReplay", "Saved.Replay", assembly);
        var transform = new XslCompiledTransform();
        transform.Load(System.Reflection.Assembly.Load(assembly.ToArray()).GetType("Saved.Replay", true)!, settings);
        using var input = XmlReader.Create(new StringReader("<doc><a id='1'><a id='2'/></a><a id='3'/></doc>"));
        using var output = new StringWriter();
        transform.Transform(input, null, output);
        Assert.Equal("123", output.ToString());
    }
#endif

    [Theory]
    [InlineData("<a><a/></a>")]
    [InlineData("text<a/>")]
    [InlineData("<b/><a/>")]
    public void AbortedGroundingCannotSilentlyLoseMatchingDescendants(string content)
    {
        using var input = XmlReader.Create(new StringReader("<doc><a>" + content + "</a></doc>"));
        using var document = new StreamedDocument(input);
        var iterator = new Runtime.DescendantIterator();
        iterator.Create(document.CreateNavigator(), Runtime.XmlNavNameFilter.Create("a", ""), false);
        Assert.True(iterator.MoveNext());
        var first = (StreamedNavigator)iterator.Current.Clone();
        using (first.BeginConsumption(replay: true)) Assert.True(first.MoveToFirstChild());
        Assert.Equal("stream-position", Assert.Throws<Xsl3.Xdm.XdmException>(() => iterator.MoveNext()).Code);
    }

    [Fact]
    public void ReachingTheEndAfterSkippingAnUnreadSubtreeDoesNotCompleteReplay()
    {
        using var input = XmlReader.Create(new StringReader("<doc><a><b><a/></b></a></doc>"));
        using var document = new StreamedDocument(input);
        var iterator = new Runtime.DescendantIterator();
        iterator.Create(document.CreateNavigator(), Runtime.XmlNavNameFilter.Create("a", ""), false);
        Assert.True(iterator.MoveNext());
        var root = (StreamedNavigator)iterator.Current.Clone();
        var child = (StreamedNavigator)root.Clone();
        using (root.BeginConsumption(replay: true))
        {
            Assert.True(child.MoveToFirstChild());
            Assert.False(child.MoveToNext()); // skips b without reading its nested a
        }
        Assert.False(StreamedDocument.CanReplay(root.PositionNode));
        Assert.Equal("stream-position", Assert.Throws<Xsl3.Xdm.XdmException>(() => iterator.MoveNext()).Code);
    }

    [Fact]
    public void AdvancingPastACopiedSubtreeReleasesItsReplayBuffer()
    {
        using var input = XmlReader.Create(new StringReader("<doc><a><a/></a><a id='next'/></doc>"));
        using var document = new StreamedDocument(input);
        var iterator = new Runtime.DescendantIterator();
        iterator.Create(document.CreateNavigator(), Runtime.XmlNavNameFilter.Create("a", ""), false);
        WeakReference replay = GroundFirst(ref iterator);
        Assert.True(iterator.MoveNext()); // nested match in the buffer
        Assert.True(iterator.MoveNext()); // following source match, outside the buffer
        Assert.Equal("next", iterator.Current.GetAttribute("id", ""));
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(replay.IsAlive);
        GC.KeepAlive(iterator);
        GC.KeepAlive(document);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference GroundFirst(ref Runtime.DescendantIterator iterator)
    {
        Assert.True(iterator.MoveNext());
        var first = (StreamedNavigator)iterator.Current;
        first.Ground();
        Assert.Equal(2, first.PositionNode.Replay!.Count);
        return new(first.PositionNode.Replay!);
    }
}
