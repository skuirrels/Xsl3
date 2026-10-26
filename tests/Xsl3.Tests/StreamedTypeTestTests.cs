// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.IO;
using System.Security;
using System.Xml;
using Xunit;

namespace Xsl3.Tests;

public class StreamedTypeTestTests
{
    private const string Input = "<doc><row id='a'><value>1</value><value>2</value></row><row id='b'><value>3</value></row></doc>";
    private static string Sheet(string expression) => $$"""
        <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:xs="http://www.w3.org/2001/XMLSchema">
          <xsl:mode streamable="yes"/><xsl:output method="text"/>
          <xsl:template match="/"><xsl:value-of select="{{SecurityElement.Escape(expression)}}" separator="|"/></xsl:template>
        </xsl:stylesheet>
        """;

    private static string FunctionSheet(string expression, string resultType, string body = "$n/row") =>
        Sheet(expression).Replace("xmlns:xs=", "xmlns:f='urn:test' xmlns:xs=")
            .Replace("<xsl:mode", $"<xsl:function name='f:rows' streamability='shallow-descent' as='{resultType}'><xsl:param name='n' as='node()'/><xsl:sequence select='{body}'/></xsl:function><xsl:mode");

    [Theory]
    [InlineData("doc/row treat as element(row)+", "12|3")]
    [InlineData("(doc/row treat as element(row)+)/value", "1|2|3")]
    [InlineData("remove(doc/row, 2) treat as node()?", "12")]
    [InlineData("(doc/row[1], 1) treat as item()+", "12|1")]
    [InlineData("doc/row ! ((. treat as node()) is .)", "true|true")]
    [InlineData("(1, 2, 3) treat as xs:integer+", "1|2|3")]
    [InlineData("((map{'n':1}, map{'n':2}) treat as map(*)*) ? n", "1|2")]
    public void TreatTransmitsOriginalItemsBeforeAdvancingTheirSource(string expression, string expected)
    {
        foreach (bool forward in new[] { false, true })
        {
            var transform = new XsltCompiler { Settings = new() { StreamedExecution = forward } }
                .Compile(new StringReader(Sheet(expression))).CreateTransformer();
            using var input = XmlReader.Create(new StringReader(Input));
            Assert.Equal(expected, string.Concat(transform.ApplyTemplates(input)));
        }
    }

    [Theory]
    [InlineData("doc/row instance of element(row)+", "true")]
    [InlineData("doc/row instance of element(missing)*", "false")]
    [InlineData("doc/row instance of element(row)?", "false")]
    [InlineData("doc/missing instance of node()+", "false")]
    [InlineData("doc/missing instance of empty-sequence()", "true")]
    [InlineData("doc/row instance of empty-sequence()", "false")]
    [InlineData("(1, 2) instance of xs:decimal+", "true")]
    public void InspectionKeepsSequenceTypeAndCardinalitySemantics(string expression, string expected)
    {
        foreach (bool forward in new[] { false, true })
        {
            var transform = new XsltCompiler { Settings = new() { StreamedExecution = forward } }
                .Compile(new StringReader(Sheet(expression))).CreateTransformer();
            using var input = XmlReader.Create(new StringReader(Input));
            Assert.Equal(expected, string.Concat(transform.ApplyTemplates(input)));
        }
    }

    [Theory]
    [InlineData("doc/missing treat as element(row)+")]
    [InlineData("doc/row treat as element(row)?")]
    [InlineData("doc/row treat as element(missing)*")]
    [InlineData("doc/row/@id treat as xs:string*")]
    [InlineData("data(doc/row/@id) treat as xs:integer*")]
    [InlineData("(1, 'wrong') treat as xs:integer+")]
    [InlineData("doc/row treat as empty-sequence()")]
    public void TreatRetainsXPDY0050WithoutFunctionConversion(string expression)
    {
        foreach (bool forward in new[] { false, true })
        {
            var transform = new XsltCompiler { Settings = new() { StreamedExecution = forward } }
                .Compile(new StringReader(Sheet(expression))).CreateTransformer();
            using var input = XmlReader.Create(new StringReader(Input));
            Assert.Equal("XPDY0050", Assert.Throws<XsltDynamicException>(() => transform.ApplyTemplates(input)).Code.LocalName);
        }
    }
    [Theory]
    [InlineData("(1, 'wrong', error(QName('', 'TEST0001'))) instance of xs:integer*")]
    [InlineData("(doc/row, error(QName('', 'TEST0001'))) instance of element(missing)*")]
    public void InspectionDoesNotMaskLateProducerErrors(string expression)
    {
        foreach (bool forward in new[] { false, true })
        {
            var transform = new XsltCompiler { Settings = new() { StreamedExecution = forward } }
                .Compile(new StringReader(Sheet(expression))).CreateTransformer();
            using var input = XmlReader.Create(new StringReader(Input));
            Assert.Equal("TEST0001", Assert.Throws<XsltDynamicException>(() => transform.ApplyTemplates(input)).Code.LocalName);
        }
    }

#if NET10_0_OR_GREATER
    [Theory]
    [InlineData("(doc/row treat as element(row)+)/value", "1|2|3")]
    [InlineData("remove(doc/row, 2) treat as node()?", "12")]
    [InlineData("doc/row instance of element(row)+", "true")]
    public void SavedAssembliesPreserveTypeChecksAndLiveNodeTransmission(string expression, string expected)
    {
        var settings = new XsltSettings { StreamedExecution = true };
        using var stylesheet = XmlReader.Create(new StringReader(Sheet(expression)));
        using var assembly = new MemoryStream();
        new XslCompiledTransform().CompileToAssembly(stylesheet, settings, null, "Xsl3.Tests.TypeTests", "Saved.TypeTests", assembly);
        var transform = new XslCompiledTransform();
        transform.Load(System.Reflection.Assembly.Load(assembly.ToArray()).GetType("Saved.TypeTests", true)!, settings);
        using var input = XmlReader.Create(new StringReader(Input)); using var output = new StringWriter();
        transform.Transform(input, null, output);
        Assert.Equal(expected, output.ToString());
    }
#endif

    [Theory]
    [InlineData("item()*", "(f:rows(doc) treat as element(row)+)/value", "1|2|3")]
    [InlineData("item()+", "(f:rows(doc) treat as element(row)+)/value", "1|2|3")]
    [InlineData("node()+", "(f:rows(doc) treat as element(row)+)/value", "1|2|3")]
    [InlineData("element(row)+", "(f:rows(doc) treat as element(row)+)/value", "1|2|3")]
    [InlineData("item()*", "f:rows(doc) treat as element(row)+", "12|3")]
    [InlineData("item()+", "f:rows(doc) treat as element(row)+", "12|3")]
    [InlineData("node()+", "f:rows(doc) treat as element(row)+", "12|3")]
    public void RequiredNodeTypesRefineOpaqueFunctionPopulations(string resultType, string expression, string expected)
    {
        string sheet = FunctionSheet(expression, resultType);
        foreach (bool forward in new[] { false, true })
        {
            var transform = new XsltCompiler { Settings = new() { StreamedExecution = forward } }
                .Compile(new StringReader(sheet)).CreateTransformer();
            using var input = XmlReader.Create(new StringReader(Input));
            Assert.Equal(expected, string.Concat(transform.ApplyTemplates(input)));
        }
    }

    [Theory]
    [InlineData("item()+", "$n/missing")]
    [InlineData("element(missing)*", "$n/row")]
    [InlineData("node()?", "$n/row")]
    [InlineData("element(row)", "$n/missing")]
    public void FunctionResultChecksKeepTheirOriginalErrorCode(string resultType, string body)
    {
        foreach (bool forward in new[] { false, true })
        {
            var transform = new XsltCompiler { Settings = new() { StreamedExecution = forward } }
                .Compile(new StringReader(FunctionSheet("f:rows(doc) treat as node()*", resultType, body))).CreateTransformer();
            using var input = XmlReader.Create(new StringReader(Input));
            Assert.Equal("XTTE0780", Assert.Throws<XsltDynamicException>(() => transform.ApplyTemplates(input)).Code.LocalName);
        }
    }

    [Theory]
    [InlineData("item()?")]
    [InlineData("node()?")]
    [InlineData("element(row)")]
    public void SingletonFunctionResultsRemainReadable(string resultType)
    {
        foreach (bool forward in new[] { false, true })
        {
            var transform = new XsltCompiler { Settings = new() { StreamedExecution = forward } }
                .Compile(new StringReader(FunctionSheet("(f:rows(doc) treat as element(row)?)/value", resultType, "$n/row[1]"))).CreateTransformer();
            using var input = XmlReader.Create(new StringReader(Input));
            Assert.Equal("1|2", string.Concat(transform.ApplyTemplates(input)));
        }
    }

#if NET10_0_OR_GREATER
    [Theory]
    [InlineData("item()+")]
    [InlineData("node()+")]
    public void SavedOpaqueFunctionResultsRetainIncrementalConversions(string resultType)
    {
        var settings = new XsltSettings { StreamedExecution = true };
        using var stylesheet = XmlReader.Create(new StringReader(FunctionSheet("(f:rows(doc) treat as element(row)+)/value", resultType)));
        using var assembly = new MemoryStream();
        new XslCompiledTransform().CompileToAssembly(stylesheet, settings, null, "Xsl3.Tests.TypeFunction", "Saved.TypeFunction", assembly);
        var transform = new XslCompiledTransform();
        transform.Load(System.Reflection.Assembly.Load(assembly.ToArray()).GetType("Saved.TypeFunction", true)!, settings);
        using var input = XmlReader.Create(new StringReader(Input)); using var output = new StringWriter(); transform.Transform(input, null, output);
        Assert.Equal("1|2|3", output.ToString());
    }
#endif

}
