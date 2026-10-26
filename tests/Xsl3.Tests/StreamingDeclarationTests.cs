// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.IO;
using System.Linq;
using System;
using System.Xml;
using Xsl3.Xslt;
using Xunit;

namespace Xsl3.Tests;

public class StreamingDeclarationTests
{
    private const string Xsl = "http://www.w3.org/1999/XSL/Transform";

    [Theory]
    [InlineData("inspection", "absorbing", false)]
    [InlineData("inspection", null, false)]
    [InlineData(null, "inspection", false)]
    [InlineData("inspection", "inspection", true)]
    [InlineData("x:custom", "y:custom", true)]
    [InlineData("x:custom", "Q{urn:category}custom", true)]
    [InlineData("x:custom", "y:other", false)]
    [InlineData("Q{urn:other}custom", "y:custom", false)]
    public void FunctionOverridesPreserveStreamabilityCategory(string? original, string? replacement, bool compatible)
    {
        string Function(string? category) => "<xsl:function name='f:test' visibility='public' "
            + (category == null ? "" : $"streamability='{category}'")
            + "><xsl:param name='n' as='node()'/><xsl:sequence select='name($n)'/></xsl:function>";
        CheckOverride(Function(original), Function(replacement), compatible);
    }

    [Theory]
    [InlineData("yes", "no", false)]
    [InlineData("yes", null, false)]
    [InlineData("yes", "yes", true)]
    [InlineData("no", "yes", true)]
    public void AttributeSetOverridesPreserveTheStreamingContract(string original, string? replacement, bool compatible)
    {
        string Set(string? streamable) => "<xsl:attribute-set name='attrs' visibility='public' "
            + (streamable == null ? "" : $"streamable='{streamable}'") + "/>";
        CheckOverride(Set(original), Set(replacement), compatible);
    }

    private static void CheckOverride(string original, string replacement, bool compatible)
    {
        string file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, $"<xsl:package xmlns:xsl='{Xsl}' xmlns:f='urn:functions' xmlns:x='urn:category' name='urn:stream-override' package-version='1.0' version='3.0'>{original}</xsl:package>");
            var compiler = new XsltCompiler { Settings = new() { Streamability = XsltStreamabilityMode.Fallback } };
            compiler.Packages.Add("urn:stream-override", "1.0", new Uri(file));
            string sheet = Stylesheet($"<xsl:use-package xmlns:y='urn:category' name='urn:stream-override' package-version='1.0'><xsl:override>{replacement}</xsl:override></xsl:use-package>");
            if (compatible) Assert.NotNull(compiler.Compile(new StringReader(sheet)));
            else Assert.Contains("XTSE3070", Assert.Throws<XsltStaticException>(() => compiler.Compile(new StringReader(sheet))).Message);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void StrictIsTheDefaultAndUndefinedPoliciesAreRejected()
    {
        Assert.Equal(XsltStreamabilityMode.Strict, new XsltSettings().Streamability);
        Assert.Throws<ArgumentOutOfRangeException>(() => new XsltSettings { Streamability = (XsltStreamabilityMode)42 });
    }

    [Theory]
    [InlineData("<xsl:source-document href='input.xml' streamable='yes'><xsl:sequence select='*'/></xsl:source-document>")]
    [InlineData("<xsl:source-document href='input.xml' streamable='yes'><xsl:value-of select='a'/><xsl:value-of select='b'/></xsl:source-document>")]
    [InlineData("<xsl:source-document href='input.xml' streamable='yes'><xsl:sequence select='/*/following::*'/></xsl:source-document>")]
    public void StrictChecksNestedSourceDocumentsBeforeExecution(string body)
    {
        string sheet = Stylesheet($"<xsl:template name='xsl:initial-template'>{body}</xsl:template>");
        Assert.Contains("XTSE3430", Assert.Throws<XsltStaticException>(() => new XsltCompiler().Compile(new StringReader(sheet))).Message);
        Assert.NotNull(new XsltCompiler { Settings = new() { Streamability = XsltStreamabilityMode.Fallback } }.Compile(new StringReader(sheet)));
    }

    [Theory]
    [InlineData("<xsl:value-of version='1.0' select='42'/>")]
    [InlineData("<xsl:if version='1.0' test='true()'><xsl:value-of select='42'/></xsl:if>")]
    [InlineData("<out xsl:version='1.0'><xsl:value-of version='3.0' select='42'/></out>")]
    public void StrictRejectsBackwardsCompatibleInstructionsEvenWhenMotionless(string body)
    {
        string sheet = Stylesheet($"<xsl:mode streamable='yes'/><xsl:template match='*'>{body}</xsl:template>");
        XsltStaticException error = Assert.Throws<XsltStaticException>(() => new XsltCompiler().Compile(new StringReader(sheet)));
        Assert.Contains(error.Errors, e => e.Code?.LocalName == "XTSE3430" && e.LineNumber > 0);
        Assert.NotNull(new XsltCompiler { Settings = new() { Streamability = XsltStreamabilityMode.Fallback } }.Compile(new StringReader(sheet)));
        Assert.NotNull(new XsltCompiler().Compile(new StringReader(sheet.Replace("'1.0'", "'2.0'", StringComparison.Ordinal))));
    }

    [Theory]
    [InlineData("<xsl:mode streamable='yes'/><xsl:template match='*'><xsl:value-of select='a'/><xsl:value-of select='b'/></xsl:template>")]
    [InlineData("<xsl:mode streamable='yes'/><xsl:template match='*[a]'><out/></xsl:template>")]
    [InlineData("<xsl:mode streamable='yes'/><xsl:template match='*'><xsl:param name='p' select='a'/><out/></xsl:template>")]
    [InlineData("<xsl:mode name='s' streamable='yes'/><xsl:template match='*' mode='#all'><xsl:sequence select='.'/></xsl:template>")]
    [InlineData("<xsl:attribute-set name='a' streamable='yes'><xsl:attribute name='n' select='a'/></xsl:attribute-set>")]
    [InlineData("<xsl:attribute-set name='a' streamable='yes'/><xsl:attribute-set name='a'/>")]
    [InlineData("<xsl:accumulator name='a' streamable='yes' initial-value='0'><xsl:accumulator-rule match='*' select='count(*)'/></xsl:accumulator>")]
    [InlineData("<xsl:function name='f:f' streamability='inspection'><xsl:param name='p' as='node()'/><xsl:sequence select='count($p/*)'/></xsl:function>")]
    [InlineData("<xsl:template name='xsl:initial-template'><xsl:merge><xsl:merge-source streamable='yes' for-each-source='&quot;input.xml&quot;' select='//*'><xsl:merge-key select='@id'/></xsl:merge-source><xsl:merge-action/></xsl:merge></xsl:template>")]
    public void StrictChecksEachKindOfStreamableDeclaration(string content)
    {
        string sheet = Stylesheet(content);
        Assert.Contains("XTSE3430", Assert.Throws<XsltStaticException>(() => new XsltCompiler().Compile(new StringReader(sheet))).Message);
        Assert.NotNull(new XsltCompiler { Settings = new() { Streamability = XsltStreamabilityMode.Fallback } }.Compile(new StringReader(sheet)));
    }

    [Fact]
    public void AttributeSetStreamabilityErrorsIdentifyARealDeclaration()
    {
        const string sheet = """
            <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:attribute-set name="a" streamable="yes">
                <xsl:attribute name="n" select="child"/>
              </xsl:attribute-set>
            </xsl:stylesheet>
            """;
        var error = Assert.Throws<XsltStaticException>(() => new XsltCompiler().Compile(new StringReader(sheet)));
        Assert.Contains(error.Errors, e => e.Code?.LocalName == "XTSE3430" && e.LineNumber == 2 && e.ColumnNumber > 0);
    }

    [Theory]
    [InlineData("<xsl:mode streamable='yes'/><xsl:template match='*'><xsl:value-of select='count(*)'/></xsl:template>")]
    [InlineData("<xsl:mode streamable='yes'/><xsl:template match='text()'><xsl:sequence select='string(.)'/></xsl:template>")]
    [InlineData("<xsl:attribute-set name='a' streamable='yes'><xsl:attribute name='n' select='@id'/></xsl:attribute-set>")]
    [InlineData("<xsl:accumulator name='a' streamable='yes' initial-value='0'><xsl:accumulator-rule match='text()' select='$value + string-length(.)'/></xsl:accumulator>")]
    [InlineData("<xsl:function name='f:f' streamability='inspection'><xsl:param name='p' as='node()'/><xsl:sequence select='name($p)'/></xsl:function>")]
    [InlineData("<xsl:function name='f:f' streamability='deep-descent' as='element()*'><xsl:param name='p' as='node()'/><xsl:sequence select='$p//section'/></xsl:function>")]
    [InlineData("<xsl:template name='xsl:initial-template'><xsl:source-document href='input.xml' streamable='yes'><out><xsl:value-of select='boolean(.//ITEM)'/></out></xsl:source-document></xsl:template>")]
    [InlineData("<xsl:mode streamable='yes'/><xsl:template match='*'><xsl:message>Processing</xsl:message><xsl:value-of select='count(*)'/></xsl:template>")]
    [InlineData("<xsl:mode streamable='yes'/><xsl:template match='*'><out><xsl:value-of select='count(*)'/><after value='{accumulator-after(&quot;a&quot;)}'/></out></xsl:template><xsl:accumulator name='a' initial-value='0'><xsl:accumulator-rule match='*' select='$value+1'/></xsl:accumulator>")]
    [InlineData("<xsl:mode streamable='yes'/><xsl:template match='*'><xsl:for-each-group select='copy-of(*)' group-adjacent='@id'><out a='{count(current-group())}' b='{count(current-group())}'/></xsl:for-each-group></xsl:template>")]
    [InlineData("<xsl:mode streamable='yes'/><xsl:template match='*'><xsl:value-of select='some $n in child::* satisfies $n/@id = 1'/></xsl:template>")]
    [InlineData("<xsl:mode streamable='yes'/><xsl:template match='*'><xsl:value-of select='avg(.//section/page-count)'/></xsl:template>")]
    [InlineData("<xsl:mode streamable='yes'/><xsl:template match='*'><out><xsl:fork><xsl:sequence><xsl:attribute name='id' select='@id'/></xsl:sequence><xsl:sequence select='title'/></xsl:fork></out></xsl:template>")]
    [InlineData("<xsl:template name='xsl:initial-template'><xsl:variable name='n' select='1'/><xsl:source-document href='input.xml' streamable='yes'><xsl:value-of select='$n + count(*)'/></xsl:source-document></xsl:template>")]
    public void GuaranteedDeclarationsCompile(string content) => Assert.NotNull(new XsltCompiler().Compile(new StringReader(Stylesheet(content))));

    [Fact]
    public void AttributeSetReferenceRuleAppliesEvenWithFallback()
    {
        string sheet = Stylesheet("<xsl:attribute-set name='a' streamable='yes' use-attribute-sets='b'/><xsl:attribute-set name='b'/>");
        Assert.Contains("XTSE0730", Assert.Throws<XsltStaticException>(() => new XsltCompiler
            { Settings = new() { Streamability = XsltStreamabilityMode.Fallback } }.Compile(new StringReader(sheet))).Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplainIncludesTheSourceDocumentVerdict(bool optimized)
    {
        string sheet = Stylesheet("<xsl:function name='f:inspect---node' streamability='inspection'><xsl:param name='n' as='node()'/><xsl:sequence select='name($n)'/></xsl:function>"
            + "<xsl:template name='xsl:initial-template'><xsl:source-document href='input.xml' streamable='yes'><out><xsl:value-of select='count(*)'/></out></xsl:source-document></xsl:template>");
        var executable = new XsltCompiler().Compile(new StringReader(sheet));
        var output = new StringWriter();
        executable.Explain(output, optimized: optimized);
        Assert.Contains("streamability: guaranteed", output.ToString());
        Assert.Contains("Consuming", output.ToString());
        using XmlReader xml = XmlReader.Create(new StringReader(output.ToString()));
        while (xml.Read()) { } // Declaration names and URIs must remain valid XML comment content.
    }

    [Fact]
    public void NamedEntryPointIsCheckedWhenItsCompilationUnitIsRequested()
    {
        string sheet = Stylesheet("<xsl:template name='bad'><xsl:source-document href='input.xml' streamable='yes'><xsl:sequence select='*'/></xsl:source-document></xsl:template>");
        XsltExecutable executable = new XsltCompiler().Compile(new StringReader(sheet));
        Assert.Contains("XTSE3430", Assert.Throws<XsltStaticException>(() => executable.CreateTransformer().CallTemplate(new Xsl3.Xdm.QName("", "bad"), new StringWriter())).Message);
    }

    [Fact]
    public void ImportedNamedEntryPointUsesTheResolvedPackageComponent()
    {
        string packageFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(packageFile, $"<xsl:package xmlns:xsl='{Xsl}' version='3.0' name='urn:stream-test' package-version='1.0'>"
                + "<xsl:template name='entry' visibility='public'><xsl:source-document href='input.xml' streamable='yes'><xsl:sequence select='*'/></xsl:source-document></xsl:template></xsl:package>");
            var compiler = new XsltCompiler();
            compiler.Packages.Add("urn:stream-test", "1.0", new Uri(packageFile));
            XsltExecutable executable = compiler.Compile(new StringReader(Stylesheet("<xsl:use-package name='urn:stream-test' package-version='1.0'/>")));
            Assert.Contains("XTSE3430", Assert.Throws<XsltStaticException>(() => executable.CreateTransformer()
                .CallTemplate(new Xsl3.Xdm.QName("", "entry"), new StringWriter())).Message);
        }
        finally { File.Delete(packageFile); }
    }

    [Fact]
    public void FallbackRunsNormallyAndKeepsTheCompiledSettings()
    {
        var settings = new XsltSettings { Streamability = XsltStreamabilityMode.Fallback };
        string sheet = Stylesheet("<xsl:mode streamable='yes'/><xsl:template match='doc'><xsl:value-of select='a'/><xsl:value-of select='b'/></xsl:template>");
        XsltExecutable executable = new XsltCompiler { Settings = settings }.Compile(new StringReader(sheet));
        settings.Streamability = XsltStreamabilityMode.Strict;
        var output = new StringWriter();
        executable.CreateTransformer().ApplyTemplates(Xsl3.Xdm.XdmNode.Load(new StringReader("<doc><a>1</a><b>2</b></doc>")), output);
        Assert.Equal("12", output.ToString());
    }

    [Theory]
    [InlineData("<out/>")]
    [InlineData("<xsl:sequence select='1'/><xsl:for-each-group select='()' group-by='.'/>")]
    public void InvalidForkChildrenHaveAStaticDiagnostic(string children)
    {
        string sheet = Stylesheet($"<xsl:template name='xsl:initial-template'><xsl:fork>{children}</xsl:fork></xsl:template>");
        Assert.Contains("XTSE0010", Assert.Throws<XsltStaticException>(() => new XsltCompiler().Compile(new StringReader(sheet))).Message);
        using var reader = System.Xml.XmlReader.Create(new StringReader(sheet));
        Assert.Contains("XTSE0010", Assert.Throws<XslLoadException>(() => new XslCompiledTransform(enableDebug: true).Load(reader)).Message);
    }

    [Theory]
    [InlineData("<xsl:mode streamable='yes'/><xsl:template match='/'><out a='{accumulator-after(&quot;a&quot;)}' b='{accumulator-after(&quot;a&quot;)}'/></xsl:template><xsl:accumulator name='a' initial-value='0'><xsl:accumulator-rule match='*' select='$value+1'/></xsl:accumulator>")]
    [InlineData("<xsl:function name='f:f' streamability='shallow-descent'><xsl:param name='n' as='node()'/><xsl:sequence select='(1 to 5) ! $n'/></xsl:function>")]
    public void MultipleConsumptionCannotHideInAttributesOrRepeatedParameters(string content)
    {
        Assert.Contains("XTSE3430", Assert.Throws<XsltStaticException>(() => new XsltCompiler().Compile(new StringReader(Stylesheet(content)))).Message);
    }

    [Theory]
    [InlineData("absorbing")]
    [InlineData("inspection")]
    [InlineData("filter")]
    [InlineData("shallow-descent")]
    [InlineData("deep-descent")]
    [InlineData("ascent")]
    [InlineData("Q{urn:extension}streamability")]
    public void ZeroArityFunctionCannotDeclareStreamability(string category)
    {
        string sheet = Stylesheet($"<xsl:function name='f:zero' streamability='{category}'><xsl:sequence select='1'/></xsl:function>");
        XsltStaticException error = Assert.Throws<XsltStaticException>(() => new XsltCompiler().Compile(new StringReader(sheet)));
        Assert.Contains("XTSE3155", error.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("streamability='unclassified'")]
    public void UnclassifiedZeroArityFunctionsStillCompile(string attribute)
    {
        XsltExecutable executable = new XsltCompiler().Compile(new StringReader(Stylesheet(
            $"<xsl:function name='f:zero' {attribute}><xsl:sequence select='1'/></xsl:function>" +
            "<xsl:template name='xsl:initial-template'><xsl:value-of select='f:zero()'/></xsl:template>")));
        var output = new StringWriter();
        executable.CreateTransformer().CallTemplate(null, output);
        Assert.Equal("1", output.ToString());
    }

    [Fact]
    public void RetainsTheSourceConstructsBeforeUnstreamedLowering()
    {
        Compiler compiler = Load("""
            <xsl:mode name="stream" streamable="yes"/>
            <xsl:accumulator name="total" initial-value="0" streamable="yes"><xsl:accumulator-rule match="*" select="$value + 1"/></xsl:accumulator>
            <xsl:attribute-set name="attrs" streamable="yes"><xsl:attribute name="id" select="@id"/></xsl:attribute-set>
            <xsl:function name="f:inspect" streamability="inspection"><xsl:param name="node"/><xsl:sequence select="name($node)"/></xsl:function>
            <xsl:template name="xsl:initial-template">
              <xsl:source-document streamable="yes" href="{base-uri()}"><xsl:fork><xsl:sequence select="count(*)"/></xsl:fork></xsl:source-document>
              <xsl:merge><xsl:merge-source streamable="yes" for-each-source="'input.xml'" select="/*"><xsl:merge-key select="@id"/></xsl:merge-source><xsl:merge-action/></xsl:merge>
            </xsl:template>
            """);
        Assert.Empty(compiler.CompilerErrorColl);
        Assert.Equal("yes", Assert.Single(compiler.ModeDecls).Value.Attributes["streamable"]);
        Assert.Equal(TriState.True, Assert.Single(compiler.Accumulators).Value.Declaration.V2!.Streamable);
        Assert.Equal(TriState.True, Assert.Single(Assert.Single(compiler.AttributeSets).Value.Content).V2!.Streamable);
        Assert.Equal("inspection", Assert.Single(compiler.Functions).Value.Function.V2!.StreamabilityCategory);
        Template template = compiler.AllTemplates.OfType<Template>().Single();
        XslNode source = template.Content[0];
        Assert.True(source.V2!.IsSourceDocument);
        Assert.Equal(TriState.True, source.V2.Streamable);
        Assert.Equal("{base-uri()}", source.V2.SourceHref);
        Assert.True(Assert.Single(source.Content).V2!.IsFork);
        XslNode mergeSource = template.Content[1].Content[0];
        Assert.Equal(TriState.True, mergeSource.V2!.Streamable);
        Assert.Equal("'input.xml'", mergeSource.V2.SourceForEachSource);
        Assert.Contains("doc(", mergeSource.V2.ForEachSource);
    }

    [Fact]
    public void AttributeSetDeclarationsRetainTheirOwnStreamingFlags()
    {
        Compiler compiler = Load("<xsl:attribute-set name='a' streamable='yes'/><xsl:attribute-set name='a'/>");
        AttributeSet set = Assert.Single(compiler.AttributeSets).Value;
        Assert.Equal(2, set.Content.Count);
        Assert.Contains(set.Content, declaration => declaration.V2!.Streamable == TriState.True);
        Assert.Contains(set.Content, declaration => declaration.V2!.Streamable == TriState.Unknown);
    }

    private static Compiler Load(string content)
    {
        var compiler = new Compiler(new XsltSettings(), debug: false, scriptAssemblyPath: null);
        using XmlReader reader = XmlReader.Create(new StringReader(Stylesheet(content)));
        new XsltLoader().Load(compiler, reader, xmlResolver: null, origResolver: null);
        return compiler;
    }

    private static string Stylesheet(string content) =>
        $"<xsl:stylesheet version='3.0' xmlns:xsl='{Xsl}' xmlns:f='urn:functions'><xsl:output method='text'/>{content}</xsl:stylesheet>";
}
