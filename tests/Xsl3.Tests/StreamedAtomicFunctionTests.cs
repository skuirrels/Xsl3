// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.IO;
using System.Xml;
using System.Xml.Schema;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

public class StreamedAtomicFunctionTests
{
    private const string Input = "<doc><value>1.25</value><value>2.75</value></doc>";

    private static string Sheet(string select, string declaration = "") => $$"""
        <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
            xmlns:xs="http://www.w3.org/2001/XMLSchema" xmlns:f="urn:test:functions">
          <xsl:mode streamable="yes"/><xsl:output method="text"/>
          <xsl:function name="f:total" as="xs:decimal">
            <xsl:param name="values" as="xs:decimal*"/>
            <xsl:sequence select="sum($values)"/>
          </xsl:function>
          {{declaration}}
          <xsl:template match="/"><xsl:value-of select="{{select}}"/></xsl:template>
        </xsl:stylesheet>
        """;

    [Theory]
    [InlineData("f:total(doc/value)")]
    [InlineData("doc/value => f:total()")]
    [InlineData("f:total#1(doc/value)")]
    [InlineData("(function($x as xs:decimal*) as xs:decimal { sum($x) })(doc/value)")]
    [InlineData("let $f := f:total#1 return $f(doc/value)")]
    [InlineData("(function-lookup(QName('urn:test:functions', 'f:total'), 1) treat as function(xs:decimal*) as xs:decimal)(doc/value)")]
    public void AtomicParametersConsumeNodesBeforeAdvancing(string select)
    {
        foreach (bool forward in new[] { false, true })
        {
            var transform = new XsltCompiler { Settings = new() { StreamedExecution = forward } }
                .Compile(new StringReader(Sheet(select))).CreateTransformer();
            using var reader = XmlReader.Create(new StringReader(Input));
            Assert.Equal("4", string.Concat(transform.ApplyTemplates(reader)));
        }
    }

    [Theory]
    [InlineData("f:total(doc/value)")]
    [InlineData("f:total#1(doc/value)")]
    public void ParameterTypesUseTheirOwnNamespaceDeclarations(string select)
    {
        string sheet = Sheet(select).Replace("<xsl:param name=\"values\" as=\"xs:decimal*\"/>",
            "<xsl:param xmlns:t='http://www.w3.org/2001/XMLSchema' name='values' as='t:decimal*'/>")
            .Replace("<xsl:template match=\"/\">", "<xsl:template xmlns:t='urn:caller' match='/'>");
        foreach (bool forward in new[] { false, true })
        {
            var transform = new XsltCompiler { Settings = new() { StreamedExecution = forward } }.Compile(new StringReader(sheet)).CreateTransformer();
            using var reader = XmlReader.Create(new StringReader(Input));
            Assert.Equal("4", string.Concat(transform.ApplyTemplates(reader)));
        }
    }

    [Theory]
    [InlineData("f:total(doc/value)")]
    [InlineData("f:total#1(doc/value)")]
    public void FunctionReturnTypesUseTheFunctionDeclarationNamespace(string select)
    {
        string sheet = Sheet(select).Replace("<xsl:function name=\"f:total\" as=\"xs:decimal\">",
            "<xsl:function xmlns:t='http://www.w3.org/2001/XMLSchema' name='f:total' as='t:decimal'>")
            .Replace("<xsl:template match=\"/\">", "<xsl:template xmlns:t='urn:caller' match='/'>");
        foreach (bool forward in new[] { false, true })
        {
            var transform = new XsltCompiler { Settings = new() { StreamedExecution = forward } }.Compile(new StringReader(sheet)).CreateTransformer();
            using var reader = XmlReader.Create(new StringReader(Input));
            Assert.Equal("4", string.Concat(transform.ApplyTemplates(reader)));
        }
    }

    [Fact]
    public void AnExpandedAbsorbingFunctionPreservesDynamicCallState()
    {
        string sheet = Sheet("f:via(doc/value)", """
            <xsl:function name="f:via" streamability="absorbing" as="xs:decimal">
              <xsl:param name="input" as="node()*"/>
              <xsl:sequence select="f:total#1($input)"/>
            </xsl:function>
            """);
        var transform = new XsltCompiler { Settings = new() { StreamedExecution = true } }.Compile(new StringReader(sheet)).CreateTransformer();
        using var reader = XmlReader.Create(new StringReader(Input));
        Assert.Equal("4", string.Concat(transform.ApplyTemplates(reader)));
    }

    [Theory]
    [InlineData("(function($x as xs:integer) {$x})(doc/value)", "<doc><value>1</value><value>2</value></doc>", "XPTY0004")]
    [InlineData("(function($x as xs:decimal*) {$x})(map{})", "<doc/>", "FOTY0013")]
    public void DynamicArgumentsKeepCanonicalConversionErrors(string select, string input, string code)
    {
        foreach (bool forward in new[] { false, true })
        {
            var transform = new XsltCompiler { Settings = new() { StreamedExecution = forward } }.Compile(new StringReader(Sheet(select))).CreateTransformer();
            using var reader = XmlReader.Create(new StringReader(input));
            Assert.Contains(code, Assert.Throws<XsltDynamicException>(() => transform.ApplyTemplates(reader)).Message);
        }
    }

    [Theory]
    [InlineData("$f(doc/value, ())")]
    [InlineData("$f((), doc/value)")]
    public void EmptyArgumentsDoNotRetainThePreviousArgumentBuffer(string call)
    {
        string select = "let $f := function($x as xs:decimal*, $y as xs:decimal*) as xs:decimal { sum($x) + sum($y) } return " + call;
        var transform = new XsltCompiler { Settings = new() { StreamedExecution = true } }.Compile(new StringReader(Sheet(select))).CreateTransformer();
        using var reader = XmlReader.Create(new StringReader(Input));
        Assert.Equal("4", string.Concat(transform.ApplyTemplates(reader)));
    }

    [Fact]
    public void DynamicCallStateIsFreshForEachLoopUnderTheOrdinaryOptimizer()
    {
        string sheet = Sheet("f:total#1($values)", "<xsl:variable name='values' select='/doc/value'/>")
            .Replace("<xsl:template match=\"/\">", "<xsl:template name='t'><xsl:for-each select='1 to 3'>")
            .Replace("</xsl:template>", "</xsl:for-each></xsl:template>");
        var transform = new XsltCompiler { Settings = new() { StreamedExecution = true } }.Compile(new StringReader(sheet)).CreateTransformer();
        transform.GlobalContextItem = XdmNode.Load(new StringReader(Input));
        Assert.Equal("444", string.Concat(transform.CallTemplate(new QName("t"))));
    }

    [Theory]
    [InlineData("map{'key': 7}(doc/value)", "<doc><value>key</value></doc>", "7")]
    [InlineData("[7, 8](doc/value)", "<doc><value>2</value></doc>", "8")]
    public void MapsAndArraysKeepTheirFunctionConversionRules(string select, string input, string expected)
    {
        var transform = new XsltCompiler { Settings = new() { StreamedExecution = true } }.Compile(new StringReader(Sheet(select))).CreateTransformer();
        using var reader = XmlReader.Create(new StringReader(input));
        Assert.Equal(expected, string.Concat(transform.ApplyTemplates(reader)));
    }

    [Theory]
    [InlineData("f:total(doc/@values)")]
    [InlineData("f:total#1(doc/@values)")]
    public void TypedListArgumentsExpandBeforeLeavingTheirNode(string select)
    {
        const string schema = """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
              <xs:simpleType name="integers"><xs:list itemType="xs:integer"/></xs:simpleType>
              <xs:element name="doc"><xs:complexType><xs:attribute name="values" type="integers"/></xs:complexType></xs:element>
            </xs:schema>
            """;
        var schemas = new XmlSchemaSet();
        schemas.Add(null, XmlReader.Create(new StringReader(schema)));
        var transform = new XsltCompiler { Settings = new() { SchemaAware = true, StreamedExecution = true } }
            .Compile(new StringReader(Sheet(select))).CreateTransformer();
        using var reader = XmlReader.Create(new StringReader("<doc values='9007199254740993 1'/>"), new XmlReaderSettings { ValidationType = ValidationType.Schema, Schemas = schemas });
        Assert.Equal("9007199254740994", string.Concat(transform.ApplyTemplates(reader)));
    }

#if NET10_0_OR_GREATER
    [Fact]
    public void SavedDynamicCallsRetainIncrementalArgumentCollection()
    {
        var settings = new XsltSettings { StreamedExecution = true };
        using var assembly = new MemoryStream();
        new XslCompiledTransform().CompileToAssembly(XmlReader.Create(new StringReader(Sheet("f:total#1(doc/value)"))), settings, null,
            "Xsl3.Tests.StreamedAtomicFunction", "Saved.AtomicFunction", assembly);
        var transform = new XslCompiledTransform();
        transform.Load(System.Reflection.Assembly.Load(assembly.ToArray()).GetType("Saved.AtomicFunction", true)!, settings);
        using var reader = XmlReader.Create(new StringReader(Input));
        using var output = new StringWriter();
        transform.Transform(reader, null, output);
        Assert.Equal("4", output.ToString());
    }
#endif
}
