// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using Xunit;

namespace Xsl3.Tests;

public class StreamedCopyContentTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" validation='strip'")]
    [InlineData(" validation='preserve'")]
    [InlineData(" copy-namespaces='no'")]
    public void NodeOnlyCopyContentEmitsBeforeTheContainingRowIsRead(string options)
    {
        string sheet = "<xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform'>"
            + "<xsl:mode streamable='yes' on-no-match='shallow-copy'/><xsl:output omit-xml-declaration='yes'/>"
            + "<xsl:template match='row'><out><xsl:copy-of select='@*'" + options + "/>"
            + "<xsl:apply-templates/></out></xsl:template></xsl:stylesheet>";
        var executable = new XsltCompiler { Settings = new() { StreamedExecution = true } }.Compile(new StringReader(sheet));
        byte[] input = Encoding.UTF8.GetBytes("<doc><row id='row'>" + string.Concat(Enumerable.Repeat("<part>value</part>", 10000)) + "</row></doc>");
        using var source = new MemoryStream(input);
        using var reader = XmlReader.Create(source);
        using var output = new FirstOutput(source);
        executable.CreateTransformer().ApplyTemplates(reader, output);
        Assert.NotNull(output.InputAtFirstWrite);
        Assert.InRange(output.InputAtFirstWrite.Value, 1, input.Length / 2);
        using var result = XmlReader.Create(new MemoryStream(output.ToArray()));
        var document = new System.Xml.XPath.XPathDocument(result).CreateNavigator();
        Assert.Equal(10000d, document.Evaluate("count(/doc/out/part)"));
        Assert.Equal("row", document.SelectSingleNode("/doc/out/@id")!.Value);
    }

    [Theory]
    [InlineData("strip")]
    [InlineData("preserve")]
    public void AttributeCopiesRemainParentlessDistinctNodesInVariables(string validation)
    {
        string sheet = "<xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform'>"
            + "<xsl:mode streamable='yes'/><xsl:output method='text'/><xsl:template match='/'>"
            + "<xsl:for-each select='doc/row'><xsl:variable name='copies' as='attribute()*'><xsl:copy-of select='@*' validation='" + validation + "'/></xsl:variable>"
            + "<xsl:value-of select='count($copies), count($copies/..), $copies[1] is @id, string($copies[1])' separator='|'/></xsl:for-each>"
            + "</xsl:template></xsl:stylesheet>";
        var executable = new XsltCompiler { Settings = new() { StreamedExecution = true } }.Compile(new StringReader(sheet));
        using var input = XmlReader.Create(new StringReader("<doc><row id='value'/></doc>"));
        using var output = new StringWriter(); executable.CreateTransformer().ApplyTemplates(input, output);
        Assert.Equal("1|0|false|value", output.ToString());
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
