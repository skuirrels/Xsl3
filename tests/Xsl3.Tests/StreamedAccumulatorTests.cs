// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.IO;
using System.Net;
using System.Text;
using System;
using System.Collections.Generic;
using System.Xml;
using Xunit;

namespace Xsl3.Tests;

public class StreamedAccumulatorTests
{
    private static string Sheet(string body, string declarations, string applicable = "#all") => $$"""
        <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:xs="http://www.w3.org/2001/XMLSchema" xmlns:err="http://www.w3.org/2005/xqt-errors" exclude-result-prefixes="xs err">
          <xsl:mode streamable="yes" use-accumulators="{{applicable}}"/><xsl:output omit-xml-declaration="yes"/>
          {{declarations}}
          <xsl:template match="doc">{{body}}</xsl:template>
        </xsl:stylesheet>
        """;

    private static string Run(string sheet, string xml, bool forward, bool legacy = false, XmlResolver? resolver = null, bool schemaAware = false)
    {
        using var input = XmlReader.Create(new StringReader(xml));
        using var output = new StringWriter();
        if (legacy)
        {
            var transform = new XslCompiledTransform();
            transform.Load(XmlReader.Create(new StringReader(sheet)), new() { StreamedExecution = forward, EnableDocumentFunction = resolver != null, SchemaAware = schemaAware }, null);
            transform.Transform(input, null, output, resolver);
        }
        else
        {
            var transform = new XsltCompiler { Settings = new() { StreamedExecution = forward, EnableDocumentFunction = resolver != null, SchemaAware = schemaAware } }.Compile(new StringReader(sheet)).CreateTransformer();
            if (resolver != null) transform.DocumentResolver = resolver;
            transform.ApplyTemplates(input, output);
        }
        Assert.NotEqual(ReadState.Closed, input.ReadState);
        return output.ToString();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BeforeAndAfterValuesFollowStartAndEndRules(bool legacy)
    {
        string sheet = Sheet("""
            <out><xsl:for-each select="row"><r before="{accumulator-before('n')}" after="{accumulator-after('n')}"/></xsl:for-each></out>
            """, """
            <xsl:accumulator name="n" as="xs:integer" initial-value="0" streamable="yes">
              <xsl:accumulator-rule match="row" select="$value + 1"/>
              <xsl:accumulator-rule match="row" phase="end" select="$value + 10"/>
            </xsl:accumulator>
            """);
        foreach (bool forward in new[] { false, true })
            Assert.Equal("<out><r before=\"1\" after=\"11\"/><r before=\"12\" after=\"22\"/></out>",
                Run(sheet, "<doc><row/><row/></doc>", forward, legacy));
    }

    [Fact]
    public void AfterAnAncestorCanFinishItsRemainingChildren()
    {
        string sheet = Sheet("""
            <out><xsl:for-each select="row"><r n="{accumulator-before('n')}"/></xsl:for-each><total n="{accumulator-after('n')}"/></out>
            """, """
            <xsl:accumulator name="n" as="xs:integer" initial-value="0" streamable="yes"><xsl:accumulator-rule match="row" select="$value + 1"/></xsl:accumulator>
            """);
        foreach (bool forward in new[] { false, true })
            Assert.Equal("<out><r n=\"1\"/><r n=\"2\"/><total n=\"2\"/></out>", Run(sheet, "<doc><row/><row/></doc>", forward));
    }

    [Fact]
    public void SkippedSubtreesStillContributeToTheAccumulator()
    {
        string sheet = Sheet("<total n=\"{accumulator-after('n')}\"/>", """
            <xsl:accumulator name="n" as="xs:integer" initial-value="0" streamable="yes"><xsl:accumulator-rule match="row" select="$value + 1"/></xsl:accumulator>
            """);
        foreach (bool forward in new[] { false, true })
            Assert.Equal("<total n=\"3\"/>", Run(sheet, "<doc><skip><row/><row/></skip><row/></doc>", forward));
    }

    [Theory]
    [InlineData("error()", "FOER0000")]
    [InlineData("error(xs:QName('err:XTDE3400'))", "XTDE3400")]
    public void ASubsequentRuleErrorDoesNotInvalidateEarlierBeforeValues(string failure, string code)
    {
        string sheet = Sheet("""
            <out><xsl:for-each select="row"><xsl:try><r n="{accumulator-before('n')}"/>
              <xsl:catch><bad code="{local-name-from-QName($err:code)}"/></xsl:catch>
            </xsl:try></xsl:for-each><before n="{accumulator-before('n')}"/></out>
            """, $$"""
            <xsl:accumulator name="n" as="xs:integer" initial-value="0" streamable="yes">
              <xsl:accumulator-rule match="row" select="if (@fail) then {{failure}} else $value + 1"/>
            </xsl:accumulator>
            """);
        foreach (bool forward in new[] { false, true })
            Assert.Equal($"<out><r n=\"1\"/><bad code=\"{code}\"/><bad code=\"{code}\"/><before n=\"0\"/></out>",
                Run(sheet, "<doc><row/><row fail='yes'/><row/></doc>", forward));
    }

    [Fact]
    public void UnusedAccumulatorErrorsAreNotReported()
    {
        string sheet = Sheet("<total n=\"{count(row)}\"/>", """
            <xsl:accumulator name="n" initial-value="error()" streamable="yes"><xsl:accumulator-rule match="row" select="$value"/></xsl:accumulator>
            """);
        foreach (bool forward in new[] { false, true })
            Assert.Equal("<total n=\"2\"/>", Run(sheet, "<doc><row/><row/></doc>", forward));
    }

    [Fact]
    public void RuleDependenciesAtTheSameNodeAreComputedOnce()
    {
        string sheet = Sheet("<out><xsl:for-each select='row'><r a=\"{accumulator-before('a')}\" b=\"{accumulator-before('b')}\"/></xsl:for-each></out>", """
            <xsl:accumulator name="a" as="xs:integer" initial-value="0" streamable="yes"><xsl:accumulator-rule match="row" select="accumulator-before('b') * 10"/></xsl:accumulator>
            <xsl:accumulator name="b" as="xs:integer" initial-value="0" streamable="yes"><xsl:accumulator-rule match="row" select="$value + 1"/></xsl:accumulator>
            """);
        foreach (bool forward in new[] { false, true })
            Assert.Equal("<out><r a=\"10\" b=\"1\"/><r a=\"20\" b=\"2\"/></out>", Run(sheet, "<doc><row/><row/></doc>", forward));
    }

    [Fact]
    public void CyclicRuleDependenciesReportTheCanonicalError()
    {
        string sheet = Sheet("<r n=\"{accumulator-before('a')}\"/>", """
            <xsl:accumulator name="a" initial-value="0" streamable="yes"><xsl:accumulator-rule match="doc" select="accumulator-before('b')"/></xsl:accumulator>
            <xsl:accumulator name="b" initial-value="0" streamable="yes"><xsl:accumulator-rule match="doc" select="accumulator-before('a')"/></xsl:accumulator>
            """);
        foreach (bool forward in new[] { false, true })
            Assert.Contains("XTDE3400", Assert.Throws<XsltDynamicException>(() => Run(sheet, "<doc/>", forward)).Message);
    }

    [Fact]
    public void ADetectedCycleIsNotHiddenByAnEarlierRootValue()
    {
        string sheet = Sheet("<out><xsl:copy-of select='.'/><n v=\"{accumulator-before('n')}\"/></out>", """
            <xsl:accumulator name="n" initial-value="0" streamable="yes"><xsl:accumulator-rule match="row" select="$value + accumulator-before('n')"/></xsl:accumulator>
            """);
        foreach (bool forward in new[] { false, true })
            Assert.Contains("XTDE3400", Assert.Throws<XsltDynamicException>(() => Run(sheet, "<doc><row/></doc>", forward)).Message);
    }

    [Fact]
    public void AnEndRuleCanReadItsOwnBeforeValue()
    {
        string sheet = Sheet("<out><xsl:for-each select='row'><r n=\"{accumulator-after('n')}\"/></xsl:for-each></out>", """
            <xsl:accumulator name="n" as="xs:integer" initial-value="0" streamable="yes">
              <xsl:accumulator-rule match="row" select="$value + 1"/>
              <xsl:accumulator-rule match="row" phase="end" select="$value + accumulator-before('n')"/>
            </xsl:accumulator>
            """);
        foreach (bool forward in new[] { false, true })
            Assert.Equal("<out><r n=\"2\"/><r n=\"6\"/></out>", Run(sheet, "<doc><row/><row/></doc>", forward));
    }

    [Fact]
    public void APatternErrorIsANonMatch()
    {
        string sheet = Sheet("<total n=\"{accumulator-after('n')}\"/>", """
            <xsl:accumulator name="n" as="xs:integer" initial-value="0" streamable="yes">
              <xsl:accumulator-rule match="row" select="$value + 1"/>
              <xsl:accumulator-rule match="row[boolean(error())]" select="999"/>
            </xsl:accumulator>
            """);
        foreach (bool forward in new[] { false, true })
            Assert.Equal("<total n=\"2\"/>", Run(sheet, "<doc><row/><row/></doc>", forward));
    }

    [Fact]
    public void PositionalAccumulatorPatternsAreRejected()
    {
        string sheet = Sheet("<total n=\"{accumulator-after('n')}\"/>", """
            <xsl:accumulator name="n" as="xs:integer" initial-value="0" streamable="yes">
              <xsl:accumulator-rule match="row[2]" select="$value + 1"/>
            </xsl:accumulator>
            """);
        foreach (bool forward in new[] { false, true })
            Assert.Contains("XTSE3430", Assert.Throws<XsltStaticException>(() =>
                new XsltCompiler { Settings = new() { StreamedExecution = forward } }.Compile(new StringReader(sheet))).Message);
    }

    [Theory]
    [InlineData("#all")]
    [InlineData("n")]
    public void OnlyApplicableStreamableAccumulatorsRun(string applicable)
    {
        string sheet = Sheet("<r n=\"{accumulator-after('n')}\"/>", """
            <xsl:accumulator name="n" as="xs:integer" initial-value="0" streamable="yes"><xsl:accumulator-rule match="row" select="$value + 1"/></xsl:accumulator>
            <xsl:accumulator name="other" initial-value="error()"><xsl:accumulator-rule match="doc" select="$value"/></xsl:accumulator>
            """, applicable);
        Assert.Equal("<r n=\"1\"/>", Run(sheet, "<doc><row/></doc>", true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GlobalContextAndInitialSelectionHaveIndependentAccumulatorValues(bool forward)
    {
        string sheet = Sheet("<out global=\"{$global}\" source=\"{accumulator-after('n')}\"/>", """
            <xsl:global-context-item use="required" as="document-node()"/>
            <xsl:variable name="global" select="accumulator-after('n')"/>
            <xsl:accumulator name="n" as="xs:integer" initial-value="0" streamable="yes"><xsl:accumulator-rule match="row" select="$value + 1"/></xsl:accumulator>
            """);
        var transform = new XsltCompiler { Settings = new() { StreamedExecution = forward } }.Compile(new StringReader(sheet)).CreateTransformer();
        transform.GlobalContextItem = Xsl3.Xdm.XdmNode.Load(new StringReader("<doc><row/><row/><row/></doc>"));
        using var input = XmlReader.Create(new StringReader("<doc><row/></doc>"));
        using var output = new StringWriter();
        transform.ApplyTemplates(input, output);
        Assert.Equal("<out global=\"3\" source=\"1\"/>", output.ToString());
    }

    [Theory]
    [InlineData("missing", "", "XTSE3300")]
    [InlineData("n", "<xsl:accumulator name='n' initial-value='0'><xsl:accumulator-rule match='doc' select='$value'/></xsl:accumulator>", "XTSE3300")]
    [InlineData("unknown:n", "<xsl:accumulator name='n' initial-value='0' streamable='yes'><xsl:accumulator-rule match='doc' select='$value'/></xsl:accumulator>", "XTSE0280")]
    [InlineData("n n", "<xsl:accumulator name='n' initial-value='0' streamable='yes'><xsl:accumulator-rule match='doc' select='$value'/></xsl:accumulator>", "XTSE3300")]
    [InlineData("#all n", "<xsl:accumulator name='n' initial-value='0' streamable='yes'><xsl:accumulator-rule match='doc' select='$value'/></xsl:accumulator>", "XTSE3300")]
    public void InvalidNamedApplicabilityIsAStaticError(string applicable, string declarations, string code)
    {
        string sheet = Sheet("<r/>", declarations, applicable);
        Assert.Contains(code, Assert.Throws<XsltStaticException>(() =>
            new XsltCompiler { Settings = new() { StreamedExecution = true } }.Compile(new StringReader(sheet))).Message);
    }

    [Theory]
    [InlineData("copy-of(doc/row)", false)]
    [InlineData("copy-of(doc/row)", true)]
    [InlineData("snapshot(doc/row)", false)]
    [InlineData("snapshot(doc/row)", true)]
    public void CopiesKeepDescendantValuesAfterTheirInputCloses(string selection, bool legacy)
    {
        string sheet = Sheet($$"""
            <xsl:variable name="copies" as="element()*"><xsl:source-document href="urn:copied-input" streamable="yes" use-accumulators="n">
              <xsl:sequence select="{{selection}}"/>
            </xsl:source-document></xsl:variable>
            <out><xsl:for-each select="$copies"><r before="{accumulator-before('n')}" after="{accumulator-after('n')}">
              <xsl:for-each select="child"><child before="{accumulator-before('n')}" after="{accumulator-after('n')}"/></xsl:for-each>
            </r></xsl:for-each></out>
            """, """
            <xsl:accumulator name="n" as="xs:integer" initial-value="0" streamable="yes"><xsl:accumulator-rule match="*" select="$value + 1"/></xsl:accumulator>
            """);
        foreach (bool forward in new[] { false, true })
            Assert.Equal("<out><r before=\"2\" after=\"3\"><child before=\"3\" after=\"3\"/></r><r before=\"4\" after=\"5\"><child before=\"5\" after=\"5\"/></r></out>",
                Run(sheet, "<doc/>", forward, legacy, new SourceResolver("<doc><row><child/></row><row><child/></row></doc>")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ValidatedCopiesKeepValuesAfterTheirStreamCloses(bool legacy)
    {
        string sheet = Sheet("""
            <xsl:variable name="copies" as="element()*"><xsl:source-document href="urn:copied-input" streamable="yes" use-accumulators="n">
              <xsl:copy-of select="doc/row" copy-accumulators="yes" validation="strict"/>
            </xsl:source-document></xsl:variable>
            <out><xsl:for-each select="$copies"><r before="{accumulator-before('n')}" after="{accumulator-after('n')}"/></xsl:for-each></out>
            """, """
            <xsl:import-schema><xs:schema><xs:element name="row" type="xs:string"/></xs:schema></xsl:import-schema>
            <xsl:accumulator name="n" as="xs:integer" initial-value="0" streamable="yes"><xsl:accumulator-rule match="*" select="$value + 1"/></xsl:accumulator>
            """);
        foreach (bool forward in new[] { false, true })
            Assert.Equal("<out><r before=\"2\" after=\"2\"/><r before=\"3\" after=\"3\"/></out>",
                Run(sheet, "<doc/>", forward, legacy, new SourceResolver("<doc><row>one</row><row>two</row></doc>"), schemaAware: true));
    }

    [Theory]
    [InlineData("#all", false)]
    [InlineData("a:n", false)]
    [InlineData("#all", true)]
    [InlineData("a:n", true)]
    public void ImportedSourcesSelectAccumulatorsInTheirOwnPackage(string applicable, bool initialMode)
    {
        string packageFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(packageFile, $$"""
                <xsl:package version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:a="urn:counter" name="urn:stream-accumulator" package-version="1.0" exclude-result-prefixes="a">
                  <xsl:accumulator name="a:n" initial-value="0" streamable="yes"><xsl:accumulator-rule match="row" select="$value + 1"/></xsl:accumulator>
                  <xsl:mode name="m" visibility="public" streamable="yes" use-accumulators="{{applicable}}"/>
                  <xsl:template match="doc" mode="m"><out n="{accumulator-after('a:n')}"/></xsl:template>
                  <xsl:template name="entry" visibility="public"><xsl:source-document href="urn:copied-input" streamable="yes" use-accumulators="{{applicable}}">
                    <out n="{accumulator-after('a:n')}"/>
                  </xsl:source-document></xsl:template>
                </xsl:package>
                """);
            string sheet = """
                <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:a="urn:counter">
                  <xsl:use-package name="urn:stream-accumulator" package-version="1.0"/>
                  <xsl:output omit-xml-declaration="yes"/>
                  <xsl:accumulator name="a:n" initial-value="error()" streamable="yes"><xsl:accumulator-rule match="row" select="$value"/></xsl:accumulator>
                </xsl:stylesheet>
                """;
            foreach (bool forward in new[] { false, true })
            {
                var compiler = new XsltCompiler { Settings = new() { StreamedExecution = forward, EnableDocumentFunction = true } };
                compiler.Packages.Add("urn:stream-accumulator", "1.0", new Uri(packageFile));
                var transform = compiler.Compile(new StringReader(sheet)).CreateTransformer();
                transform.DocumentResolver = new SourceResolver("<doc><row/><row/></doc>");
                using var output = new StringWriter();
                if (initialMode)
                {
                    using var input = XmlReader.Create(new StringReader("<doc><row/><row/></doc>"));
                    transform.ApplyTemplates(input, output, new Xsl3.Xdm.QName("m"));
                }
                else transform.CallTemplate(new Xsl3.Xdm.QName("entry"), output);
                Assert.Equal("<out n=\"2\"/>", output.ToString());
            }
        }
        finally { File.Delete(packageFile); }
    }

    private sealed class SourceResolver(string xml) : XmlResolver
    {
        public override ICredentials? Credentials { set { } }
        public override object GetEntity(Uri absoluteUri, string? role, Type? type) => new MemoryStream(Encoding.UTF8.GetBytes(xml));
    }

    [Fact]
    public void VersionOneAccumulatorConstantsRemainReadable()
    {
        var table = new Xsl3.XPath31.ConstantTable();
        table.Add(new Dictionary<string, Runtime.AccumulatorInfo> {
            ["Q{}n"] = new(7, new[] { false, true }, null, OnPrincipal: true, Streamable: true)
        });
        using var bytes = new MemoryStream();
        Xsl3.XPath31.ConstantSerializer.Write(new Runtime.XmlQueryDataWriter(bytes), table);
        byte[] old = bytes.ToArray();
        // Version 1 has the same accumulator record without the final Streamable flag.
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(old, 1);
        Array.Resize(ref old, old.Length - 1);
        var loaded = Xsl3.XPath31.ConstantSerializer.Read(new Runtime.XmlQueryDataReader(new MemoryStream(old)));
        var info = ((Dictionary<string, Runtime.AccumulatorInfo>)loaded.Get(0))["Q{}n"];
        Assert.Equal(7, info.Offset);
        Assert.Equal(new[] { false, true }, info.EndPhases);
        Assert.True(info.OnPrincipal);
        Assert.False(info.Streamable);
    }

#if NET10_0_OR_GREATER
    [Fact]
    public void SavedAssembliesKeepStreamedAccumulatorBindings()
    {
        string sheet = Sheet("<out><xsl:for-each select='row'><r n=\"{accumulator-before('n')}\"/></xsl:for-each><total n=\"{accumulator-after('n')}\"/></out>", """
            <xsl:accumulator name="n" as="xs:integer" initial-value="0" streamable="yes"><xsl:accumulator-rule match="row" select="$value + 1"/></xsl:accumulator>
            """);
        var settings = new XsltSettings { StreamedExecution = true };
        using var assembly = new MemoryStream();
        new XslCompiledTransform().CompileToAssembly(XmlReader.Create(new StringReader(sheet)), settings, null,
            "Xsl3.Tests.StreamedAccumulator", "Saved.Accumulator", assembly);
        var transform = new XslCompiledTransform();
        transform.Load(System.Reflection.Assembly.Load(assembly.ToArray()).GetType("Saved.Accumulator", true)!, settings);
        using var input = XmlReader.Create(new StringReader("<doc><row/><row/></doc>"));
        using var output = new StringWriter();
        transform.Transform(input, null, output);
        Assert.Equal("<out><r n=\"1\"/><r n=\"2\"/><total n=\"2\"/></out>", output.ToString());
    }
#endif
}
