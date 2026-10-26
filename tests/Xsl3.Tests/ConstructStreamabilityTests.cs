// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Linq;
using System.Xml;
using Xsl3.Streaming;
using Xsl3.Xdm;
using Xsl3.XPath31;
using Xsl3.Xslt;
using Xunit;

namespace Xsl3.Tests;

public class ConstructStreamabilityTests
{
    [Theory]
    [InlineData("<out><xsl:value-of select='count(*)'/></out>", "Grounded", "Consuming")]
    [InlineData("<out><xsl:value-of select='a'/><xsl:value-of select='b'/></out>", "Roaming", "FreeRanging")]
    [InlineData("<out id='{@id}'><xsl:copy-of select='*'/></out>", "Grounded", "Consuming")]
    [InlineData("<out id='{a}'><xsl:copy-of select='b'/></out>", "Roaming", "FreeRanging")]
    [InlineData("<xsl:if test='@id'><xsl:copy-of select='a'/></xsl:if>", "Grounded", "Consuming")]
    [InlineData("<xsl:if test='a'><xsl:copy-of select='b'/></xsl:if>", "Roaming", "FreeRanging")]
    [InlineData("<xsl:choose><xsl:when test='@id'><xsl:copy-of select='a'/></xsl:when><xsl:otherwise><xsl:copy-of select='b'/></xsl:otherwise></xsl:choose>", "Grounded", "Consuming")]
    [InlineData("<xsl:sequence select='*'/>", "Striding", "Consuming")]
    [InlineData("<xsl:attribute name='id' select='@id'/>", "Grounded", "Motionless")]
    [InlineData("<xsl:attribute name='{a}' select='b'/>", "Roaming", "FreeRanging")]
    [InlineData("<xsl:comment select='a'/>", "Grounded", "Consuming")]
    [InlineData("<xsl:namespace name='ns' select='@ns'/>", "Grounded", "Motionless")]
    [InlineData("<xsl:for-each select='a'><xsl:value-of select='b'/></xsl:for-each>", "Grounded", "Consuming")]
    [InlineData("<xsl:for-each select='descendant::a'><xsl:value-of select='b'/></xsl:for-each>", "Roaming", "FreeRanging")]
    [InlineData("<xsl:for-each select='copy-of(a)'><xsl:sequence select='following::*'/></xsl:for-each>", "Grounded", "Consuming")]
    [InlineData("<xsl:copy><xsl:value-of select='a'/></xsl:copy>", "Grounded", "Consuming")]
    [InlineData("<xsl:copy select='.'><xsl:value-of select='a'/></xsl:copy>", "Grounded", "Consuming")]
    [InlineData("<xsl:fork><xsl:sequence select='copy-of(a)'/><xsl:sequence select='copy-of(b)'/></xsl:fork>", "Grounded", "Consuming")]
    [InlineData("<xsl:fork><xsl:sequence select='a'/><xsl:sequence select='b'/></xsl:fork>", "Roaming", "FreeRanging")]
    [InlineData("<xsl:map><xsl:map-entry key='1' select='copy-of(a)'/><xsl:map-entry key='2' select='copy-of(b)'/></xsl:map>", "Grounded", "Consuming")]
    [InlineData("<xsl:map><xsl:map-entry key='1' select='a'/></xsl:map>", "Roaming", "FreeRanging")]
    [InlineData("<xsl:variable name='n' as='xs:integer' select='a'/><xsl:value-of select='$n + $n'/>", "Grounded", "Consuming")]
    [InlineData("<xsl:variable name='n' select='a'/><xsl:value-of select='$n'/>", "Roaming", "FreeRanging")]
    [InlineData("<xsl:variable name='n'><xsl:copy-of select='a'/></xsl:variable><xsl:value-of select='$n'/>", "Grounded", "Consuming")]
    [InlineData("<xsl:value-of select='sum(a)' separator='{count(b)}'/>", "Roaming", "FreeRanging")]
    [InlineData("<xsl:result-document href='out.xml'><xsl:copy-of select='a'/></xsl:result-document>", "Grounded", "Consuming")]
    [InlineData("<xsl:source-document href='{string(a)}' streamable='yes'><xsl:value-of select='count(*)'/></xsl:source-document>", "Grounded", "Consuming")]
    public void ClassifiesSourceInstructions(string body, string posture, string sweep)
    {
        var (compiler, template) = Load(body);
        Streamability result = new ConstructAnalysis(compiler).Analyze(template, new(UType.Element, Posture.Striding));
        Assert.Equal(Enum.Parse<Posture>(posture), result.Posture);
        Assert.Equal(Enum.Parse<Sweep>(sweep), result.Sweep);
    }

    [Theory]
    [InlineData("/", true)]
    [InlineData("p/q[@id]", true)]
    [InlineData("p[@id = ('a', 'b')[last()]]", true)]
    [InlineData("document-node(element(root))", true)]
    [InlineData(".[. instance of xs:string]", true)]
    [InlineData("p[b]", false)]
    [InlineData("p[1]", false)]
    [InlineData("p[position() = 1]", false)]
    [InlineData("id('a')", false)]
    public void ClassifiesMotionlessPatterns(string source, bool motionless) =>
        Assert.Equal(motionless, new ExpressionAnalysis(new() { InPattern = true }).IsMotionlessPattern(Parser.ParseXPath(source)));

    [Fact]
    public void RecordsConstructsWithoutLeakingInnerVariables()
    {
        var (compiler, template) = Load("<xsl:if test='true()'><xsl:variable name='n' select='1'/><xsl:value-of select='$n'/></xsl:if><xsl:value-of select='$n'/>");
        Assert.Equal("XPST0008", Assert.Throws<XPathStaticException>(() =>
            new ConstructAnalysis(compiler).Analyze(template, new(UType.Element, Posture.Striding))).Code);
    }

    [Fact]
    public void RetainsParsedExpressionsAndValueTemplatesForCodeGeneration()
    {
        var (compiler, template) = Load("<out id='{@id}'><xsl:value-of select='count(*)'/></out>");
        var analysis = new ConstructAnalysis(compiler);
        analysis.Analyze(template, new(UType.Element, Posture.Striding));
        XslNode element = template.Content.Single();
        XslNode attribute = element.Content.Single(n => n.NodeType == XslNodeType.LiteralAttribute);
        XslNode valueOf = element.Content.Single(n => n.NodeType == XslNodeType.ValueOf);
        Expr expression = Assert.Single(valueOf.V2!.ParsedExpressions!).Value;
        var valueTemplate = Assert.Single(attribute.V2!.ParsedValueTemplates!).Value;
        analysis.Analyze(template, new(UType.Element, Posture.Grounded));
        Assert.Same(expression, Assert.Single(valueOf.V2.ParsedExpressions!).Value);
        Assert.Same(valueTemplate.Expression, Assert.Single(attribute.V2.ParsedValueTemplates!).Value.Expression);
        using var constants = ConstantPool.Use(new ConstantTable());
        Assert.NotNull(QilGenerator.CompileStylesheet(compiler));
        Assert.Empty(compiler.CompilerErrorColl);
    }

    [Theory]
    [InlineData("absorbing", "xs:integer", "count($node/*)", "Grounded", "Consuming")]
    [InlineData("inspection", "xs:string", "name($node)", "Grounded", "Motionless")]
    [InlineData("filter", "attribute()*", "$node/@*", "Striding", "Motionless")]
    [InlineData("shallow-descent", "node()*", "$node/*", "Striding", "Consuming")]
    [InlineData("deep-descent", "node()*", "$node/descendant::*", "Crawling", "Consuming")]
    [InlineData("ascent", "node()*", "$node/parent::*", "Climbing", "Motionless")]
    public void ResolvesActualStylesheetFunctionDeclarations(string category, string returnType, string expression, string posture, string sweep)
    {
        var (compiler, template) = Load("<xsl:sequence select='Q{urn:f}f(.)'/>", $"""
            <xsl:function xmlns:f="urn:f" name="f:f" as="{returnType}" streamability="{category}">
              <xsl:param name="node" as="node()"/><xsl:sequence select="{expression}"/>
            </xsl:function>
            """);
        Streamability result = new ConstructAnalysis(compiler).Analyze(template, new(UType.Element, Posture.Striding));
        Assert.Equal(Enum.Parse<Posture>(posture), result.Posture);
        Assert.Equal(Enum.Parse<Sweep>(sweep), result.Sweep);
        FunctionStreamability.TryParse(category, out FunctionStreamabilityCategory parsed);
        Template function = Assert.Single(compiler.Functions).Value.Function;
        var resolver = new StaticTypeResolver(ExpressionAnalysisOptions.StandardNamespace);
        var signature = (FunctionItemTest)resolver.Resolve(Parser.ParseSequenceType($"function(node()) as {returnType}")).Item!;
        var variables = new System.Collections.Generic.Dictionary<QName, AnalysisVariable>
        {
            [new QName("node")] = new(signature.Parameters![0]) { StreamingParameter = FunctionStreamability.StreamingParameter(signature.Parameters[0], parsed) },
        };
        Streamability body = new ConstructAnalysis(compiler).Analyze(function, new(UType.Empty, Posture.Roaming), variables);
        Assert.True(FunctionStreamability.IsGuaranteed(signature, parsed, body));
    }

    [Theory]
    [InlineData("<xsl:apply-templates/>", "<xsl:mode streamable='yes'/>", "Grounded", "Consuming")]
    [InlineData("<xsl:apply-templates/>", "", "Roaming", "FreeRanging")]
    [InlineData("<xsl:apply-templates select='copy-of(*)'/>", "", "Grounded", "Consuming")]
    [InlineData("<xsl:for-each select='*'><xsl:sort select='@id'/><xsl:value-of select='.'/></xsl:for-each>", "", "Roaming", "FreeRanging")]
    [InlineData("<xsl:for-each select='copy-of(*)'><xsl:sort select='@id'/><xsl:value-of select='.'/></xsl:for-each>", "", "Grounded", "Consuming")]
    [InlineData("<xsl:for-each-group select='*' group-adjacent='@id'><xsl:copy-of select='current-group()'/></xsl:for-each-group>", "", "Grounded", "Consuming")]
    [InlineData("<xsl:for-each-group select='*' group-by='@id'><xsl:copy-of select='current-group()'/></xsl:for-each-group>", "", "Roaming", "FreeRanging")]
    [InlineData("<xsl:for-each-group select='*' group-adjacent='child::*'><xsl:copy-of select='current-group()'/></xsl:for-each-group>", "", "Roaming", "FreeRanging")]
    [InlineData("<xsl:for-each-group select='*' group-adjacent='@id'><xsl:source-document href='input.xml'><xsl:copy-of select='current-group()'/></xsl:source-document></xsl:for-each-group>", "", "Roaming", "FreeRanging")]
    [InlineData("<xsl:fork><xsl:for-each-group select='*' group-by='@id'><xsl:copy-of select='current-group()'/></xsl:for-each-group></xsl:fork>", "", "Grounded", "Consuming")]
    [InlineData("<xsl:fork><xsl:for-each-group select='*' group-by='@id'><xsl:sort select='position()'/><out><xsl:sequence select='current-group()/a'/></out></xsl:for-each-group></xsl:fork>", "", "Grounded", "Consuming")]
    [InlineData("<xsl:fork><xsl:for-each-group select='*' group-adjacent='@id'><xsl:sort select='position()'/><xsl:copy-of select='current-group()'/></xsl:for-each-group></xsl:fork>", "", "Grounded", "Consuming")]
    [InlineData("<xsl:for-each-group select='*' group-adjacent='@id'><xsl:sort select='position()'/><xsl:copy-of select='current-group()'/></xsl:for-each-group>", "", "Roaming", "FreeRanging")]
    [InlineData("<xsl:for-each-group select='copy-of(*)' group-by='@id'><xsl:sort select='position()'/><xsl:copy-of select='current-group()'/></xsl:for-each-group>", "", "Grounded", "Consuming")]
    [InlineData("<xsl:try><xsl:copy-of select='*'/><xsl:catch select='()'/></xsl:try>", "", "Grounded", "Consuming")]
    [InlineData("<xsl:try><xsl:copy-of select='a'/><xsl:catch select='copy-of(b)'/></xsl:try>", "", "Roaming", "FreeRanging")]
    [InlineData("<xsl:iterate select='*'><xsl:param name='n' as='xs:integer' select='0'/><xsl:next-iteration><xsl:with-param name='n' select='count(*)'/></xsl:next-iteration></xsl:iterate>", "", "Grounded", "Consuming")]
    [InlineData("<xsl:iterate select='*'><xsl:param name='n' select='count(*)'/><xsl:sequence select='()'/></xsl:iterate>", "", "Roaming", "FreeRanging")]
    [InlineData("<xsl:iterate select='*'><xsl:on-completion select='name()'/><xsl:sequence select='()'/></xsl:iterate>", "", "Roaming", "FreeRanging")]
    [InlineData("<xsl:analyze-string select='string(*)' regex='a'><xsl:matching-substring><xsl:value-of select='.'/></xsl:matching-substring></xsl:analyze-string>", "", "Grounded", "Consuming")]
    [InlineData("<xsl:number value='count(*)'/>", "", "Grounded", "Consuming")]
    [InlineData("<xsl:number/>", "", "Roaming", "FreeRanging")]
    [InlineData("<xsl:call-template name='format'><xsl:with-param name='n' select='a'/></xsl:call-template>", "<xsl:template name='format'><xsl:context-item use='absent'/><xsl:param name='n' as='xs:string'/></xsl:template>", "Grounded", "Consuming")]
    [InlineData("<xsl:call-template name='format'/>", "<xsl:template name='format' as='xs:string'><xsl:sequence select=\"'text'\"/></xsl:template>", "Roaming", "FreeRanging")]
    public void CombinesControlFlowRules(string body, string declarations, string posture, string sweep)
    {
        var (compiler, template) = Load(body, declarations);
        Streamability result = new ConstructAnalysis(compiler).Analyze(template, new(UType.Element, Posture.Striding));
        Assert.Equal(Enum.Parse<Posture>(posture), result.Posture);
        Assert.Equal(Enum.Parse<Sweep>(sweep), result.Sweep);
    }

    [Theory]
    [InlineData("<xsl:assert test='@id' select='a'/>", "Grounded", "Consuming")]
    [InlineData("<xsl:assert test='a' select='b'/>", "Roaming", "FreeRanging")]
    [InlineData("<xsl:evaluate xpath=\"'1 + 1'\" context-item='copy-of(.)'/>", "Grounded", "Consuming")]
    [InlineData("<xsl:evaluate xpath=\"'1 + 1'\" context-item='.'/>", "Roaming", "FreeRanging")]
    public void UsesSourceOperandRolesInsteadOfGeneratedBindings(string body, string posture, string sweep)
    {
        var (compiler, template) = Load(body, settings: new() { EnableEvaluate = true, EnableAssertions = true });
        Streamability result = new ConstructAnalysis(compiler).Analyze(template, new(UType.Element, Posture.Striding));
        Assert.Equal(Enum.Parse<Posture>(posture), result.Posture);
        Assert.Equal(Enum.Parse<Sweep>(sweep), result.Sweep);
    }

    internal static (Compiler Compiler, Template Template) Load(string body, string declarations = "", XsltSettings? settings = null)
    {
        var compiler = new Compiler(settings ?? new XsltSettings(), debug: false, scriptAssemblyPath: null);
        using XmlReader reader = XmlReader.Create(new StringReader($"""
            <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:xs="http://www.w3.org/2001/XMLSchema">
              {declarations}<xsl:template name="xsl:initial-template">{body}</xsl:template>
            </xsl:stylesheet>
            """));
        new XsltLoader().Load(compiler, reader, xmlResolver: null, origResolver: null);
        Assert.Empty(compiler.CompilerErrorColl);
        return (compiler, compiler.AllTemplates.OfType<Template>().Single(t => t.Name?.LocalName == "initial-template"));
    }
}
