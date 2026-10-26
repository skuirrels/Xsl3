// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Linq;
using Xsl3.W3C.Qt3;
using Runner = Xsl3.W3C.Xslt30.Runner;
using Xunit;
using Catalog = Xsl3.W3C.Xslt30.Catalog;

namespace Xsl3.W3C;

[CollectionDefinition("Streaming harness settings", DisableParallelization = true)]
public sealed class StreamingHarnessSettingsCollection;

[Collection("Streaming harness settings")]
public class StreamedInitialSourceHarnessTests
{
    [Fact]
    public void StrictValidatedResolverSourcesRemainReadersThroughXmlAssertions()
    {
        string directory = Path.Combine(Path.GetTempPath(), "xsl3-validated-source-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string? strict = Environment.GetEnvironmentVariable("XSL3_STREAM_STRICT");
        try
        {
            Environment.SetEnvironmentVariable("XSL3_STREAM_STRICT", "1");
            File.WriteAllText(Path.Combine(directory, "catalog.xml"), $"""
                <catalog xmlns="{Catalog.Ns}"><test-set name="typed-entry" file="set.xml"/></catalog>
                """);
            File.WriteAllText(Path.Combine(directory, "set.xml"), $"""
                <test-set xmlns="{Catalog.Ns}" name="typed-entry">
                  <test-case name="typed-reader">
                    <environment>
                      <source file="source.xml" uri="source.xml" validation="strict"/>
                      <schema file="source.xsd" role="source-reference"/>
                    </environment>
                    <test><stylesheet file="input.xsl"/></test>
                    <result><assert>/out = 'true'</assert></result>
                  </test-case>
                </test-set>
                """);
            File.WriteAllText(Path.Combine(directory, "source.xml"), "<doc><item>1</item></doc>");
            File.WriteAllText(Path.Combine(directory, "source.xsd"), """
                <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
                  <xs:element name="doc"><xs:complexType><xs:sequence><xs:element name="item" type="xs:integer"/></xs:sequence></xs:complexType></xs:element>
                </xs:schema>
                """);
            File.WriteAllText(Path.Combine(directory, "input.xsl"), """
                <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:xs="http://www.w3.org/2001/XMLSchema" exclude-result-prefixes="xs">
                  <xsl:output method="text"/>
                  <xsl:template name="xsl:initial-template">
                    <xsl:source-document streamable="yes" href="source.xml" validation="preserve">
                      <xsl:text>&lt;</xsl:text>
                      <out><xsl:value-of select="doc/item instance of element(*, xs:integer)"/></out>
                    </xsl:source-document>
                  </xsl:template>
                </xsl:stylesheet>
                """);
            Outcome outcome = new Runner().Run(Catalog.Load(directory).TestCases.Single());
            Assert.True(outcome.Kind == OutcomeKind.Pass, outcome.Detail);
        }
        finally
        {
            Environment.SetEnvironmentVariable("XSL3_STREAM_STRICT", strict);
            // This also catches open fixture streams on Windows.
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void StrictInitialSourceIsNotParsedIntoTheGlobalContext()
    {
        string directory = Path.Combine(Path.GetTempPath(), "xsl3-streamed-entry-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string? strict = Environment.GetEnvironmentVariable("XSL3_STREAM_STRICT");
        try
        {
            Environment.SetEnvironmentVariable("XSL3_STREAM_STRICT", "1");
            File.WriteAllText(Path.Combine(directory, "catalog.xml"), $"""
                <catalog xmlns="{Catalog.Ns}"><test-set name="entry" file="set.xml"/></catalog>
                """);
            File.WriteAllText(Path.Combine(directory, "set.xml"), $"""
                <test-set xmlns="{Catalog.Ns}" name="entry">
                  <test-case name="separate-context">
                    <environment><source role="." streaming="true"><content><![CDATA[<unclosed>]]></content></source></environment>
                    <test><stylesheet file="input.xsl"/></test>
                    <result><error code="XTDE3086"/></result>
                  </test-case>
                </test-set>
                """);
            File.WriteAllText(Path.Combine(directory, "input.xsl"), """
                <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
                  <xsl:mode streamable="yes"/>
                  <xsl:global-context-item use="required"/>
                  <xsl:template match="/"><xsl:sequence select="1"/></xsl:template>
                </xsl:stylesheet>
                """);
            Outcome outcome = new Runner().Run(Catalog.Load(directory).TestCases.Single());
            // Eager parsing would fail on the unclosed source before the required-global-context check.
            Assert.True(outcome.Kind == OutcomeKind.Pass, outcome.Detail);
        }
        finally
        {
            Environment.SetEnvironmentVariable("XSL3_STREAM_STRICT", strict);
            Directory.Delete(directory, recursive: true);
        }
    }
}
