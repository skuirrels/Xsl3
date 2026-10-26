// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using Xunit;

namespace Xsl3.Tests;

public class StreamedConditionalContentTests
{
    private const string Input = "<doc><row id='a'><value>one</value></row><row id='b'><value>two</value></row></doc>";
    private const string Rows = "<row id=\"a\"><value>one</value></row><row id=\"b\"><value>two</value></row>";

    private static string Sheet(string body) => "<xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform' xmlns:xs='http://www.w3.org/2001/XMLSchema' exclude-result-prefixes='xs'>"
        + "<xsl:mode streamable='yes'/><xsl:output omit-xml-declaration='yes'/><xsl:template match='/'>" + body + "</xsl:template></xsl:stylesheet>";

    [Theory]
    [InlineData("<xsl:sequence select='doc/row'/><xsl:on-empty><empty/></xsl:on-empty>", "<out>" + Rows + "</out>")]
    [InlineData("<xsl:on-non-empty><before/></xsl:on-non-empty><xsl:sequence select='doc/row'/><xsl:on-non-empty><after/></xsl:on-non-empty>", "<out><before/>" + Rows + "<after/></out>")]
    [InlineData("<xsl:variable name='label' select='&quot;captured&quot;'/><xsl:on-non-empty><before label='{$label}'/></xsl:on-non-empty><xsl:sequence select='doc/row'/><xsl:on-empty select='error()'/>", "<out><before label=\"captured\"/>" + Rows + "</out>")]
    [InlineData("<xsl:on-non-empty select='error()'/><xsl:sequence select='doc/missing'/><xsl:on-empty><empty/></xsl:on-empty>", "<out><empty/></out>")]
    [InlineData("<xsl:sequence select='()'/><xsl:on-empty select='doc/row'/>", "<out>" + Rows + "</out>")]
    [InlineData("<marker/><xsl:on-non-empty select='doc/row'/>", "<out><marker/>" + Rows + "</out>")]
    [InlineData("<xsl:sequence><xsl:sequence select='doc/row'/><xsl:on-empty><inner/></xsl:on-empty></xsl:sequence><xsl:on-non-empty><outer/></xsl:on-non-empty>", "<out>" + Rows + "<outer/></out>")]
    public void ConditionalSequencesConsumeNodesWhileTheyAreReadable(string content, string expected)
    {
        foreach (bool forward in new[] { false, true })
            Assert.Equal(expected, Run(Sheet("<out>" + content + "</out>"), Input, forward));
    }

    [Theory]
    [InlineData("<xsl:sequence select='&quot;&quot;'/><xsl:on-non-empty select='error()'/>", "1|true")]
    [InlineData("<xsl:on-non-empty select='error()'/><xsl:sequence select='&quot;&quot;, &quot;&quot;'/>", "2|true")]
    [InlineData("<xsl:sequence select='&quot;&quot;'/><xsl:on-empty select='&quot;fallback&quot;'/>", "1|true")]
    public void VacuousAtomicItemsAreDiscardedOnlyWhenOnEmptyReplacesThem(string content, string expected)
    {
        string body = "<xsl:variable name='items' as='item()*'>" + content + "</xsl:variable><xsl:value-of select='count($items), $items instance of xs:string*' separator='|'/>";
        foreach (bool forward in new[] { false, true })
            Assert.Equal(expected, Run(Sheet(body), "<doc/>", forward));
    }

    [Fact]
    public void VacuousPrefixAndConditionalItemsKeepTheirOriginalPositionsAndTypes()
    {
        string body = "<xsl:variable name='items' as='item()*'><xsl:sequence select='&quot;&quot;'/><xsl:on-non-empty select='&quot;before&quot;'/><xsl:sequence select='1'/><xsl:on-non-empty select='&quot;after&quot;'/></xsl:variable>"
            + "<xsl:value-of select='count($items), $items[3] instance of xs:integer, string-join($items ! string(.), &quot;:&quot;)' separator='|'/>";
        foreach (bool forward in new[] { false, true })
            Assert.Equal("4|true|:before:1:after", Run(Sheet(body), "<doc/>", forward));
    }

    [Fact]
    public void WherePopulatedTestsEachSelectedNodeBeforeAdvancing()
    {
        string sheet = Sheet("<out><xsl:where-populated><xsl:sequence select='doc/row'/></xsl:where-populated></out>");
        foreach (bool forward in new[] { false, true })
            Assert.Equal("<out><row><value>one</value></row><row> </row></out>", Run(sheet, "<doc><row/><row><value>one</value></row><row> </row></doc>", forward));
    }

    [Theory]
    [InlineData("<xsl:sequence select='doc/row'/><xsl:on-empty><empty/></xsl:on-empty>")]
    [InlineData("<xsl:where-populated><xsl:sequence select='doc/row'/></xsl:where-populated>")]
    public void NonVacuousRowsEmitBeforeTheCompleteSourceIsRead(string content)
    {
        byte[] bytes = Encoding.UTF8.GetBytes("<doc>" + string.Concat(Enumerable.Repeat("<row><value>one</value></row>", 10000)) + "</doc>");
        var executable = new XsltCompiler { Settings = new() { StreamedExecution = true } }.Compile(new StringReader(Sheet("<out>" + content + "</out>")));
        using var input = new MemoryStream(bytes); using var reader = XmlReader.Create(input); using var output = new FirstOutput(input);
        executable.CreateTransformer().ApplyTemplates(reader, output);
        Assert.NotNull(output.InputAtFirstWrite);
        Assert.InRange(output.InputAtFirstWrite.Value, 1, bytes.Length / 2);
        using var result = XmlReader.Create(new MemoryStream(output.ToArray()));
        var document = new System.Xml.XPath.XPathDocument(result).CreateNavigator();
        Assert.Equal(10000d, document.Evaluate("count(/out/row/value)"));
    }

#if NET10_0_OR_GREATER
    [Theory]
    [InlineData("<out><xsl:on-non-empty><before/></xsl:on-non-empty><xsl:sequence select='doc/row'/><xsl:on-empty><empty/></xsl:on-empty></out>", "<out><before/>" + Rows + "</out>")]
    [InlineData("<out><xsl:sequence select='()'/><xsl:on-empty select='doc/row'/></out>", "<out>" + Rows + "</out>")]
    [InlineData("<out><xsl:where-populated><xsl:sequence select='doc/row'/></xsl:where-populated></out>", "<out>" + Rows + "</out>")]
    [InlineData("<xsl:sequence select='data(doc/row/@id)'/>", "a b")]
    public void SavedAssembliesRetainConditionalAndRootContentContinuations(string body, string expected)
    {
        var settings = new XsltSettings { StreamedExecution = true };
        using var assembly = new MemoryStream(); using var stylesheet = XmlReader.Create(new StringReader(Sheet(body)));
        new XslCompiledTransform().CompileToAssembly(stylesheet, settings, null, "Xsl3.Tests.Conditional", "Saved.Conditional", assembly);
        var transform = new XslCompiledTransform();
        transform.Load(System.Reflection.Assembly.Load(assembly.ToArray()).GetType("Saved.Conditional", true)!, settings);
        using var input = XmlReader.Create(new StringReader(Input)); using var output = new StringWriter();
        transform.Transform(input, null, output);
        Assert.Equal(expected, output.ToString()); Assert.NotEqual(ReadState.Closed, input.ReadState);
    }
#endif

    private static string Run(string sheet, string xml, bool forward)
    {
        var executable = new XsltCompiler { Settings = new() { StreamedExecution = forward } }.Compile(new StringReader(sheet));
        using var input = XmlReader.Create(new StringReader(xml)); using var output = new StringWriter();
        executable.CreateTransformer().ApplyTemplates(input, output);
        Assert.NotEqual(ReadState.Closed, input.ReadState);
        return output.ToString();
    }

    private sealed class FirstOutput(MemoryStream input) : MemoryStream
    {
        internal long? InputAtFirstWrite { get; private set; }
        public override void Write(byte[] buffer, int offset, int count)
        { if (count != 0) InputAtFirstWrite ??= input.Position; base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer)
        { if (!buffer.IsEmpty) InputAtFirstWrite ??= input.Position; base.Write(buffer); }
    }
}
