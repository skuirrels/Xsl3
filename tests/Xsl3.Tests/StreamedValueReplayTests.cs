// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.IO;
using System;
using System.Linq;
using System.Globalization;
using System.Xml;
using System.Xml.Schema;
using System.Xml.XPath;
using System.Collections.Generic;
using System.Threading;
using Xsl3.Streaming;
using Xunit;

namespace Xsl3.Tests;

public class StreamedValueReplayTests
{
    private const string Input = "<doc><n><n>1</n><n>2</n><n>3</n></n><n>4</n></doc>";
    private static string Sheet(string body) => "<xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform'>"
        + "<xsl:mode streamable='yes'/><xsl:output omit-xml-declaration='yes'/><xsl:template match='/'>" + body + "</xsl:template></xsl:stylesheet>";

    private static string Run(string body, bool forward, bool debug = false, bool modern = false, bool saved = false, string inputXml = Input)
    {
        var transform = new XslCompiledTransform(enableDebug: debug);
        using var style = XmlReader.Create(new StringReader(Sheet(body)));
        var settings = new XsltSettings { StreamedExecution = forward, StreamingBufferCapacity = 1 };
        using var input = XmlReader.Create(new StringReader(inputXml));
        using var output = new StringWriter();
        if (modern) new XsltCompiler { Settings = settings }.Compile(style).CreateTransformer().ApplyTemplates(input, output);
        else
        {
            if (saved)
            {
#if NET10_0_OR_GREATER
                using var assembly = new MemoryStream();
                transform.CompileToAssembly(style, settings, null, "ValueReplay", "Saved.ValueReplay", assembly);
                transform.Load(System.Reflection.Assembly.Load(assembly.ToArray()).GetType("Saved.ValueReplay", true)!, settings);
#endif
            }
            else transform.Load(style, settings, null);
            transform.Transform(input, null, output);
        }
        Assert.NotEqual(ReadState.Closed, input.ReadState);
        return output.ToString();
    }

    [Theory]
    [InlineData("sum(descendant::n)", "133")]
    [InlineData("sum(.//n)", "133")]
    [InlineData("sum(./descendant::n)", "133")]
    [InlineData("string-join(.//n, '|')", "123|1|2|3|4")]
    [InlineData("string-join((0, .//n, 9), '|')", "0|123|1|2|3|4|9")]
    [InlineData("string-join(descendant::n, '|')", "123|1|2|3|4")]
    [InlineData("string-join(descendant::*, '|')", "1234|123|1|2|3|4")]
    [InlineData("string-join(data(descendant::n), '|')", "123|1|2|3|4")]
    public void AtomizationRetainsNestedMatchesInDocumentOrder(string expression, string expected)
    {
        string body = "<out><xsl:value-of select=\"" + expression + "\"/></out>";
        Assert.Equal("<out>" + expected + "</out>", Run(body, false));
        Assert.Equal("<out>" + expected + "</out>", Run(body, true));
        Assert.Equal("<out>" + expected + "</out>", Run(body, true, modern: true));
        Assert.Equal("<out>" + expected + "</out>", Run(body, true, debug: true));
    }

    [Theory]
    [InlineData(".//section/page-count")]
    [InlineData("descendant::section/child::page-count")]
    [InlineData("doc//section/page-count")]
    [InlineData("/doc//section/page-count")]
    [InlineData(".//section/self::section/page-count")]
    [InlineData(".//section//page-count")]
    public void CompoundDescendantValuesMatchHistoricalAncestorHeaders(string path)
    {
        const string input = "<doc><section><page-count><section><page-count>1</page-count><page-count>2</page-count><page-count>3</page-count></section></page-count></section><wrong><page-count>8</page-count></wrong><section><page-count>4</page-count></section></doc>";
        // The last path also selects descendant page-count nodes; this input gives
        // the same population while the unrelated page-count must remain excluded.
        string body = "<out><xsl:value-of select=\"string-join(" + path + ", '|')\"/></out>";
        const string expected = "<out>123|1|2|3|4</out>";
        Assert.Equal(expected, Run(body, false, inputXml: input));
        Assert.Equal(expected, Run(body, true, inputXml: input));
        Assert.Equal(expected, Run(body, true, modern: true, inputXml: input));
        Assert.Equal(expected, Run(body, true, debug: true, inputXml: input));
#if NET10_0_OR_GREATER
        Assert.Equal(expected, Run(body, true, saved: true, inputXml: input));
        Assert.Equal(expected, Run(body, true, debug: true, saved: true, inputXml: input));
#endif
    }

    [Fact]
    public void CompoundValuesRetainNamespacesAndMixedSequenceBoundaries()
    {
        const string input = "<doc xmlns:p='urn:p'><p:section><p:n><p:section><p:n>1</p:n><p:n>2</p:n></p:section></p:n></p:section><section><p:n>9</p:n></section><p:section><p:n>3</p:n></p:section></doc>";
        const string body = "<out><xsl:value-of select=\"string-join((0, .//Q{urn:p}section/Q{urn:p}n, 4), '|')\"/></out>";
        const string expected = "<out>0|12|1|2|3|4</out>";
        Assert.Equal(expected, Run(body, false, inputXml: input));
        Assert.Equal(expected, Run(body, true, inputXml: input));
        Assert.Equal(expected, Run(body, true, modern: true, inputXml: input));
    }

    [Theory]
    [InlineData(32)]
    [InlineData(40000)]
    public void ReplayedNamesAreAtomizedForNodesAttributesAndNamespaces(int textLength)
    {
        using var input = XmlReader.Create(new StringReader("<p:n xmlns:p='urn:p' p:key='source'><p:n p:key='nested'>" + new string('v', textLength) + "</p:n></p:n>"));
        using var document = new StreamedDocument(input);
        var iterator = new Runtime.DescendantIterator();
        iterator.Create(document.CreateNavigator(), Runtime.XmlNavNameFilter.Create("n", "urn:p"), false);
        Assert.True(iterator.MoveNext());
        Assert.Equal(textLength, ((StreamedNavigator)iterator.Current).ValueOperand().Value.Length);
        Assert.True(iterator.MoveNext());
        var node = iterator.Current.Clone();
        void Atomized(XPathNavigator value)
        {
            Assert.Same(value.NameTable.Get(value.LocalName), value.LocalName);
            Assert.Same(value.NameTable.Get(value.NamespaceURI), value.NamespaceURI);
            Assert.Same(value.NameTable.Get(value.Prefix), value.Prefix);
        }
        Atomized(node);
        var attribute = node.Clone();
        Assert.True(attribute.MoveToAttribute("key", "urn:p"));
        Atomized(attribute);
        Assert.Equal("nested", attribute.Value);
        var ns = node.Clone();
        Assert.True(ns.MoveToNamespace("p"));
        Assert.Same(ns.NameTable.Get(ns.LocalName), ns.LocalName);
        Assert.Same(ns.NameTable.Get(ns.Value), ns.Value);
        Assert.True(node.MoveToParent());
        Atomized(node);
        Assert.Equal("source", node.GetAttribute("key", "urn:p"));
        if (textLength == 32) Assert.Empty(document.ValueReplayFiles);
        else Assert.Equal(2, document.ValueReplayFiles.Count());
        Assert.False(iterator.MoveNext());
    }

    [Theory]
    [InlineData("<out><xsl:value-of select='//n' separator='|'/></out>", "<out>123|1|2|3|4</out>")]
    [InlineData("<out><xsl:attribute name='v' select='//n' separator='|'/></out>", "<out v=\"123|1|2|3|4\"/>")]
    [InlineData("<out v='{descendant::n}'/>", "<out v=\"123 1 2 3 4\"/>")]
    [InlineData("<xsl:variable name='empty' as='text()'><xsl:value-of select=\"''\"/></xsl:variable><out><xsl:value-of select='$empty, //n, $empty' separator='|'/></out>", "<out>123|1|2|3|4</out>")]
    public void SimpleContentRetainsItsBoundariesAcrossNestedNodes(string body, string expected)
    {
        Assert.Equal(expected, Run(body, false));
        Assert.Equal(expected, Run(body, true));
        Assert.Equal(expected, Run(body, true, modern: true));
    }

#if NET10_0_OR_GREATER
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavedAssembliesRetainOverlappingValuePopulations(bool debug) =>
        Assert.Equal("<out>123|1|2|3|4</out>", Run("<out><xsl:value-of select='//n' separator='|'/></out>", true, debug: debug, saved: true));
#endif

    [Fact]
    public void LargeOverlappingPopulationRetainsOnlyOpenHeadersAndDeletesItsSpool()
    {
        string[] values = Enumerable.Range(1, 2000).Select(i => i.ToString(CultureInfo.InvariantCulture)).ToArray();
        using var input = XmlReader.Create(new StringReader("<doc xml:lang='en'><n id='outer'>" + string.Concat(values.Select(v => "<n>" + v + "</n>")) + "</n></doc>"));
        string[] paths;
        using (var document = new StreamedDocument(input))
        {
            var iterator = new Runtime.DescendantIterator();
            iterator.Create(document.CreateNavigator(), Runtime.XmlNavNameFilter.Create("n", ""), false);
            Assert.True(iterator.MoveNext());
            var outer = (StreamedNavigator)iterator.Current.Clone();
            Assert.Equal(string.Concat(values), outer.ValueOperand().Value);
            Assert.InRange(document.RetainedNodeCount, 1, 4);
            Assert.Equal(0, document.RetainedValueHeaderCount);
            Assert.Equal(0, document.RetainedValueBufferBytes);
            paths = document.ValueReplayFiles.ToArray();
            Assert.Equal(2, paths.Length);
            if (!OperatingSystem.IsWindows())
                foreach (string path in paths)
                    Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
            Assert.Contains(paths, p => new FileInfo(p).Length > 100_000);
            foreach (string expected in values)
            {
                Assert.True(iterator.MoveNext());
                Assert.Equal(expected, iterator.Current.Value);
                Assert.Equal("en", iterator.Current.XmlLang);
                var parent = iterator.Current.Clone();
                Assert.True(parent.MoveToParent());
                Assert.True(parent.IsSamePosition(outer));
                Assert.Equal("outer", parent.GetAttribute("id", ""));
            }
            Assert.False(iterator.MoveNext());
        }
        Assert.All(paths, path => Assert.False(File.Exists(path)));
        Assert.NotEqual(ReadState.Closed, input.ReadState);
    }

    [Fact]
    public void RawForwardNavigationStillRejectsLostDescendants()
    {
        using var input = XmlReader.Create(new StringReader(Input));
        using var document = new StreamedDocument(input);
        var iterator = new Runtime.DescendantIterator();
        iterator.Create(document.CreateNavigator(), Runtime.XmlNavNameFilter.Create("n", ""), false);
        Assert.True(iterator.MoveNext());
        Assert.Equal("123", iterator.Current.Value);
        Assert.Equal("stream-position", Assert.Throws<Xdm.XdmException>(() => iterator.MoveNext()).Code);
        Assert.Empty(document.ValueReplayFiles);
    }

    [Fact]
    public void FlatValuePopulationDoesNotCreateTemporaryFiles()
    {
        using var input = XmlReader.Create(new StringReader("<doc><n>1</n><n>2</n></doc>"));
        using var document = new StreamedDocument(input);
        var iterator = new Runtime.DescendantIterator();
        iterator.Create(document.CreateNavigator(), Runtime.XmlNavNameFilter.Create("n", ""), false);
        for (int i = 1; i <= 2; i++)
        {
            Assert.True(iterator.MoveNext());
            Assert.Equal(i.ToString(CultureInfo.InvariantCulture), ((StreamedNavigator)iterator.Current).ValueOperand().Value);
            Assert.Empty(document.ValueReplayFiles);
        }
        Assert.False(iterator.MoveNext());
    }

    [Fact]
    public void CharacterRangesPreserveSurrogatesMixedContentAndEmptyElements()
    {
        using var input = XmlReader.Create(new StringReader("<doc><n>α<n>𐐷😺</n><!--ignored--><![CDATA[β]]><n/>γ</n></doc>"));
        using var document = new StreamedDocument(input);
        var iterator = new Runtime.DescendantIterator();
        iterator.Create(document.CreateNavigator(), Runtime.XmlNavNameFilter.Create("n", ""), false);
        Assert.True(iterator.MoveNext());
        Assert.Equal("α𐐷😺βγ", ((StreamedNavigator)iterator.Current).ValueOperand().Value);
        Assert.True(iterator.MoveNext()); Assert.Equal("𐐷😺", iterator.Current.Value);
        Assert.True(iterator.MoveNext()); Assert.Equal("", iterator.Current.Value);
        Assert.False(iterator.MoveNext());
    }

    [Fact]
    public void SmallOverlappingValuesDoNotNeedTemporaryFiles()
    {
        using var input = XmlReader.Create(new StringReader(Input));
        using var document = new StreamedDocument(input);
        var iterator = new Runtime.DescendantIterator();
        iterator.Create(document.CreateNavigator(), Runtime.XmlNavNameFilter.Create("n", ""), false);
        foreach (string expected in new[] { "123", "1", "2", "3", "4" })
        {
            Assert.True(iterator.MoveNext());
            Assert.Equal(expected, ((StreamedNavigator)iterator.Current).ValueOperand().Value);
            Assert.Empty(document.ValueReplayFiles);
        }
        Assert.False(iterator.MoveNext());
    }

    [Fact]
    public void SequentialMemoryWindowsResetTheirLengthsAndExpireOldRanges()
    {
        string large = new string('x', StreamedValueReplay.MemoryLimit / 2 - 4096);
        using var input = XmlReader.Create(new StringReader("<doc><g><n><n a='" + large + "'>one</n></n></g><g><n><n>" + large + "</n></n></g></doc>"));
        using var document = new StreamedDocument(input);
        var group = document.CreateNavigator();
        Assert.True(group.MoveToFirstChild()); Assert.True(group.MoveToFirstChild());
        XPathNavigator? old = null;
        foreach (string expected in new[] { "one", large })
        {
            var iterator = new Runtime.DescendantIterator();
            iterator.Create(group, Runtime.XmlNavNameFilter.Create("n", ""), false);
            Assert.True(iterator.MoveNext());
            Assert.Equal(expected, ((StreamedNavigator)iterator.Current).ValueOperand().Value);
            Assert.Empty(document.ValueReplayFiles);
            Assert.InRange(document.RetainedValueBufferBytes, 1, 2 * StreamedValueReplay.MemoryLimit);
            if (old != null) Assert.Throws<Xdm.XdmException>(() => old.Value);
            Assert.True(iterator.MoveNext()); Assert.Equal(expected, iterator.Current.Value);
            old = iterator.Current.Clone();
            Assert.False(iterator.MoveNext());
            group.MoveToNext();
        }
    }

    [Fact]
    public void InvalidInputDeletesPartiallyWrittenReplayFiles()
    {
        using var input = XmlReader.Create(new StringReader("<doc><n><n>" + new string('x', StreamedValueReplay.MemoryLimit) + "</n><n>2"));
        string[] paths;
        using (var document = new StreamedDocument(input))
        {
            var iterator = new Runtime.DescendantIterator();
            iterator.Create(document.CreateNavigator(), Runtime.XmlNavNameFilter.Create("n", ""), false);
            Assert.True(iterator.MoveNext());
            Assert.Throws<XmlException>(() => ((StreamedNavigator)iterator.Current).ValueOperand().Value);
            paths = document.ValueReplayFiles.ToArray();
            Assert.Equal(2, paths.Length);
        }
        Assert.All(paths, path => Assert.False(File.Exists(path)));
    }

    [Fact]
    public void SequentialWindowsReuseTheirFilesAndExpireOldValueRanges()
    {
        using var input = XmlReader.Create(new StringReader("<doc><g><n><n>" + new string('x', StreamedValueReplay.MemoryLimit) + "one</n></n></g><g><n><n>" + new string('x', StreamedValueReplay.MemoryLimit) + "two</n></n></g></doc>"));
        using var document = new StreamedDocument(input);
        var group = document.CreateNavigator();
        Assert.True(group.MoveToFirstChild()); Assert.True(group.MoveToFirstChild());
        XPathNavigator? old = null;
        string[]? files = null;
        foreach (string expected in new[] { new string('x', StreamedValueReplay.MemoryLimit) + "one", new string('x', StreamedValueReplay.MemoryLimit) + "two" })
        {
            var iterator = new Runtime.DescendantIterator();
            iterator.Create(group, Runtime.XmlNavNameFilter.Create("n", ""), false);
            Assert.True(iterator.MoveNext());
            Assert.Equal(expected, ((StreamedNavigator)iterator.Current).ValueOperand().Value);
            files ??= document.ValueReplayFiles.ToArray();
            Assert.Equal(2, files.Length);
            Assert.Equal(files, document.ValueReplayFiles.ToArray());
            if (old != null) Assert.Throws<Xdm.XdmException>(() => old.Value);
            Assert.True(iterator.MoveNext());
            Assert.Equal(expected, iterator.Current.Value);
            old = iterator.Current.Clone();
            Assert.False(iterator.MoveNext());
            group.MoveToNext();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancellationDuringValueReplayReleasesItsFiles(bool spill)
    {
        using var cancellation = new CancellationTokenSource();
        string expected = spill ? new string('x', StreamedValueReplay.MemoryLimit) : "one";
        using var input = XmlReader.Create(new StringReader("<doc><n><n>" + expected + "</n></n></doc>"));
        string[] files;
        using (var document = new StreamedDocument(input, cancellation: cancellation.Token))
        {
            var iterator = new Runtime.DescendantIterator();
            iterator.Create(document.CreateNavigator(), Runtime.XmlNavNameFilter.Create("n", ""), false);
            Assert.True(iterator.MoveNext());
            Assert.Equal(expected, ((StreamedNavigator)iterator.Current).ValueOperand().Value);
            Assert.True(iterator.MoveNext());
            files = document.ValueReplayFiles.ToArray();
            Assert.Equal(spill ? 2 : 0, files.Length);
            cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(() => iterator.Current.Value);
            Assert.Throws<OperationCanceledException>(() => iterator.MoveNext());
        }
        Assert.All(files, path => Assert.False(File.Exists(path)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TypedValuesRetainListsUnionMembersAndNilAnnotations(bool spill)
    {
        var schemas = new XmlSchemaSet();
        schemas.Add("", XmlReader.Create(new StringReader("<xs:schema xmlns:xs='http://www.w3.org/2001/XMLSchema'><xs:element name='doc' type='xs:anyType'/><xs:element name='n' type='xs:anyType' nillable='true'/></xs:schema>")));
        schemas.Add("urn:test:types", XmlReader.Create(new StringReader("<xs:schema xmlns:xs='http://www.w3.org/2001/XMLSchema' targetNamespace='urn:test:types'><xs:simpleType name='ints'><xs:list itemType='xs:integer'/></xs:simpleType><xs:simpleType name='choice'><xs:union memberTypes='xs:integer xs:date'/></xs:simpleType></xs:schema>")));
        string xml = "<doc xmlns:xs='http://www.w3.org/2001/XMLSchema' xmlns:xsi='http://www.w3.org/2001/XMLSchema-instance' xmlns:t='urn:test:types'><n><n xsi:type='xs:decimal'>1.20</n><n xsi:type='t:ints'>2 3</n><n xsi:type='t:choice'>4</n><n xsi:type='xs:string' xsi:nil='true'/><n xsi:type='xs:QName'>t:ints</n></n></doc>";
        if (spill) xml = xml.Replace("<n><n xsi:type=", "<n><n xsi:type=\"xs:string\">" + new string('x', StreamedValueReplay.MemoryLimit) + "𐐷😺</n><n xsi:type=");
        XmlReader Reader() => XmlReader.Create(new StringReader(xml), new XmlReaderSettings { Schemas = schemas, ValidationType = ValidationType.Schema });
        static string[] Atoms(XPathNavigator node) => Xdm.Sequences.Atomize(new XPathItem[] { node })
            .Select(item => ((Xdm.AtomicValue)item).Type.Name.EQName + ":" + item.Value).ToArray();
        var grounded = new XmlDocument();
        using (var reader = Reader()) grounded.Load(reader);
        XPathNodeIterator expected = grounded.CreateNavigator()!.SelectDescendants("n", "", false);
        using var forward = Reader();
        using var document = new StreamedDocument(forward);
        var actual = new Runtime.DescendantIterator();
        actual.Create(document.CreateNavigator(), Runtime.XmlNavNameFilter.Create("n", ""), false);
        while (expected.MoveNext())
        {
            Assert.True(actual.MoveNext());
            Assert.Equal(Atoms(expected.Current!), Atoms(((StreamedNavigator)actual.Current).ValueOperand()));
            Assert.Equal(spill ? 2 : 0, document.ValueReplayFiles.Count());
        }
        Assert.False(actual.MoveNext());
    }
}
