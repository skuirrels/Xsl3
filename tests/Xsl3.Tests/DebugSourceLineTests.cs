// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using Xsl3.Qil;
using Xsl3.Xdm;
using Xsl3.XPath31;
using Xsl3.Xslt;
using Xunit;

namespace Xsl3.Tests;

public class DebugSourceLineTests
{
    private const string ModuleUri = "urn:p7h:source-lines";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatedReferencesHaveSeparateLocationsAndOneBinding(bool debug)
    {
        const string sheet = """
            <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:template name="xsl:initial-template">
                <xsl:variable name="value" select="'once'"/>
                <xsl:sequence select="$value"/>
                <xsl:sequence select="$value"/>
              </xsl:template>
            </xsl:stylesheet>
            """;
        var compiler = new Compiler(new XsltSettings(), debug, scriptAssemblyPath: null);
        using var reader = XmlReader.Create(new StringReader(sheet), new XmlReaderSettings(), ModuleUri);
        new XsltLoader().Load(compiler, reader, xmlResolver: null, origResolver: null);
        Template template = Assert.Single(compiler.AllTemplates.OfType<Template>());
        XslNode declaration = template.Content.Single(n => n.NodeType == XslNodeType.Variable);
        XslNode[] uses = template.Content.Where(n => n.NodeType == XslNodeType.Sequence).ToArray();
        using var constants = ConstantPool.Use(new ConstantTable());
        QilExpression graph = QilGenerator.CompileStylesheet(compiler);
        Assert.Empty(compiler.CompilerErrorColl);
        QilNode[] nodes = Nodes(graph).ToArray();
        QilIterator binding = Assert.Single(nodes.OfType<QilIterator>(), n => n.DebugName == "value");
        Assert.Equal(declaration.SourceLine!.Start.Line, binding.Binding!.SourceLine!.Start.Line);
        Assert.Equal(ModuleUri, binding.Binding.SourceLine.Uri);
        foreach (XslNode use in uses)
        {
            QilNode point = Assert.Single(nodes, n => n.SourceLine?.Uri == ModuleUri &&
                n.SourceLine.Start.Line == use.SourceLine!.Start.Line);
            Assert.NotSame(binding, point);
            Assert.Contains(binding, Nodes(point));
        }
        // A use must not attach its location to the shared binding itself.
        Assert.Null(binding.SourceLine);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void RepeatedReferencesEvaluateAnExtensionOnlyOnce(bool debug, bool global)
    {
        string declaration = "<xsl:variable name='value' select='ext:next()'/>";
        string sheet = $$"""
            <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:ext="urn:p7h:extension">
              <xsl:output method="text"/>
              {{(global ? declaration : "")}}
              <xsl:template match="/">
                {{(global ? "" : declaration)}}
                <xsl:sequence select="$value"/>
                <xsl:sequence select="$value"/>
              </xsl:template>
            </xsl:stylesheet>
            """;
        int calls = 0;
        var compiler = new XsltCompiler { Settings = new() { IncludeDebugInformation = debug } };
        compiler.RegisterExtensionFunction(new QName("urn:p7h:extension", "next"), 0,
            _ => new XdmAtomicValue(++calls));
        using var input = XmlReader.Create(new StringReader("<doc/>"));
        using var output = new StringWriter();
        compiler.Compile(new StringReader(sheet)).CreateTransformer().ApplyTemplates(input, output);
        Assert.Equal("1 1", output.ToString());
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ErrorReportsTheExecutingInstructionLocation(bool legacy, bool debug)
    {
        const string sheet = """
            <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:xs="http://www.w3.org/2001/XMLSchema" xmlns:err="http://www.w3.org/2005/xqt-errors">
              <xsl:output method="text"/>
              <xsl:template match="/">
                <xsl:try>
                  <xsl:variable name="value" select="'once'"/>
                  <xsl:sequence select="$value"/>
                  <xsl:sequence select="$value"/>
                  <xsl:sequence select="1 idiv xs:integer(/*/@divisor)"/>
                  <xsl:catch><xsl:value-of select="concat(local-name-from-QName($err:code), '|', $err:line-number, '|', $err:module)"/></xsl:catch>
                </xsl:try>
              </xsl:template>
            </xsl:stylesheet>
            """;
        int line;
        using (var source = XmlReader.Create(new StringReader(sheet)))
        {
            while (source.Read() && !(source.NodeType == XmlNodeType.Element &&
                source.GetAttribute("select") == "1 idiv xs:integer(/*/@divisor)")) { }
            line = ((IXmlLineInfo)source).LineNumber;
            Assert.True(line > 0);
        }
        var settings = new XsltSettings { IncludeDebugInformation = debug };
        using var reader = XmlReader.Create(new StringReader(sheet), new XmlReaderSettings(), ModuleUri);
        using var input = XmlReader.Create(new StringReader("<doc divisor='0'/>"));
        using var output = new StringWriter();
        if (legacy)
        {
            var transform = new XslCompiledTransform(debug);
            transform.Load(reader, settings, null);
            transform.Transform(input, null, output);
        }
        else new XsltCompiler { Settings = settings }.Compile(new StringReader(sheet), new Uri(ModuleUri))
            .CreateTransformer().ApplyTemplates(input, output);
        Assert.Equal($"FOAR0001|{line}|{ModuleUri}", output.ToString());
    }

    private static IEnumerable<QilNode> Nodes(QilNode root)
    {
        var visited = new HashSet<QilNode>();
        var pending = new Stack<QilNode>();
        pending.Push(root);
        while (pending.TryPop(out QilNode? node))
        {
            if (!visited.Add(node)) continue;
            yield return node;
            foreach (QilNode child in node)
                if (child != null) pending.Push(child);
        }
    }
}
