// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Security;
using System.Xml;
using Xunit;

namespace Xsl3.Tests;

public class StreamedNodePredicateTests
{
    private const string Input = "<doc><group><row id='a' keep=''><value>A</value></row><row id='b'><value>B</value></row></group>"
        + "<group><row id='c' keep='0'><value>C</value></row><row id='d' keep='1'><value>D</value></row></group></doc>";

    private static string Sheet(string expression) =>
        SheetBody("<xsl:value-of select='" + SecurityElement.Escape(expression) + "' separator='|'/>");

    private static string SheetBody(string body) =>
        "<xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform'><xsl:mode streamable='yes'/><xsl:output method='text'/>"
        + "<xsl:template match='/'>" + body + "</xsl:template></xsl:stylesheet>";

    [Theory]
    [InlineData("count(//row[@keep])", "3")]
    [InlineData("//row[@keep]/@id", "a|c|d")]
    [InlineData("exists(//row[@missing])", "false")]
    [InlineData("count(//row[@keep][@id])", "3")]
    [InlineData("(//row)[@keep]/@id", "a|c|d")]
    [InlineData("//row[@keep[. eq '0']]/@id", "c")]
    [InlineData("copy-of(doc)//row[@keep][1]/@id", "a|c")]
    [InlineData("(copy-of(doc)//row[@keep])[1]/@id", "a")]
    [InlineData("copy-of(doc)//row[if (position() = 1) then @keep else ()]/@id", "a|c")]
    [InlineData("count(copy-of(doc)//row[(@keep, 1)])", "3")]
    public void NodePredicatesUseExistenceWithoutChangingNumericOrExplicitPositionRules(string expression, string expected)
    {
        string sheet = Sheet(expression);
        foreach (bool forward in new[] { false, true })
        {
            var transform = new XsltCompiler { Settings = new() { StreamedExecution = forward } }.Compile(new StringReader(sheet)).CreateTransformer();
            using var input = XmlReader.Create(new StringReader(Input)); using var output = new StringWriter();
            transform.ApplyTemplates(input, output); Assert.Equal(expected, output.ToString());
            Assert.NotEqual(ReadState.Closed, input.ReadState);
        }
    }
    [Fact]
    public void PredicatedRowsEmitBeforeTheCompleteSourceIsRead()
    {
        var ids = Enumerable.Range(0, 10000).ToArray();
        byte[] bytes = Encoding.UTF8.GetBytes("<doc>" + string.Concat(ids.Select(id =>
            "<row id='" + id + "'" + (id % 2 == 0 ? " keep=''" : "") + "><value>source</value></row>")) + "</doc>");
        var executable = new XsltCompiler { Settings = new() { StreamedExecution = true } }
            .Compile(new StringReader(SheetBody("<xsl:for-each select='//row[@keep]'><xsl:value-of select='@id'/><xsl:text>|</xsl:text></xsl:for-each>")));
        using var input = new MemoryStream(bytes); using var reader = XmlReader.Create(input); using var output = new FirstOutput(input);
        executable.CreateTransformer().ApplyTemplates(reader, output);
        Assert.NotNull(output.InputAtFirstWrite);
        Assert.InRange(output.InputAtFirstWrite.Value, 1, bytes.Length / 2);
        Assert.Equal(string.Join("|", ids.Where(id => id % 2 == 0)) + "|", Encoding.UTF8.GetString(output.ToArray()));
        Assert.NotEqual(ReadState.Closed, reader.ReadState);
    }

    private sealed class FirstOutput(MemoryStream input) : MemoryStream
    {
        internal long? InputAtFirstWrite { get; private set; }
        public override void Write(byte[] buffer, int offset, int count)
        { if (count != 0) InputAtFirstWrite ??= input.Position; base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer)
        { if (!buffer.IsEmpty) InputAtFirstWrite ??= input.Position; base.Write(buffer); }
    }

#if NET10_0_OR_GREATER
    [Theory]
    [InlineData("//row[@keep]/@id", "a|c|d")]
    [InlineData("count(//row[@missing])", "0")]
    [InlineData("copy-of(doc)//row[if (position() = 1) then @keep else ()]/@id", "a|c")]
    public void SavedAssembliesKeepNodeExistenceAndExplicitPositionRules(string expression, string expected)
    {
        var settings = new XsltSettings { StreamedExecution = true };
        using var assembly = new MemoryStream(); using var stylesheet = XmlReader.Create(new StringReader(Sheet(expression)));
        new XslCompiledTransform().CompileToAssembly(stylesheet, settings, null, "Xsl3.Tests.NodePredicates", "Saved.NodePredicates", assembly);
        var transform = new XslCompiledTransform();
        transform.Load(System.Reflection.Assembly.Load(assembly.ToArray()).GetType("Saved.NodePredicates", true)!, settings);
        using var input = XmlReader.Create(new StringReader(Input)); using var output = new StringWriter();
        transform.Transform(input, null, output);
        Assert.Equal(expected, output.ToString()); Assert.NotEqual(ReadState.Closed, input.ReadState);
    }
#endif

}
