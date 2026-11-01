// Licensed under the MIT license. See LICENSE.TXT in the repository root.
using System;
using System.IO;
using System.Linq;
using Xsl3.Streaming;
using Xsl3.W3C.Qt3;
using Xunit;
using Catalog = Xsl3.W3C.Xslt30.Catalog;
using Runner = Xsl3.W3C.Xslt30.Runner;

namespace Xsl3.W3C;

[Collection("Streaming harness settings")]
public class ExecutionPolicyHarnessTests
{
    [Theory]
    [InlineData(null, null, false)]
    [InlineData("strm", null, false)]
    [InlineData("strm", "1", true)]
    [InlineData("s2-reviewed", null, false)]
    [InlineData("sandp-reviewed", null, false)]
    [InlineData(null, "1", true)]
    public void OrdinaryAndAnalysisProfilesRetainTheirExecutionContracts(string? profile, string? strict, bool rejects)
    {
        WithCase(profile, strict, streamableAccumulator: false, rejects, test => {
            Outcome outcome = new Runner().Run(test);
            Assert.True(outcome.Kind == OutcomeKind.Pass, outcome.Detail);
        });
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("strm", null)]
    [InlineData("strm", "1")]
    [InlineData("s2-reviewed", null)]
    [InlineData("sandp-reviewed", null)]
    public void StaticStreamabilityChecksRemainStrictInEveryExecutionProfile(string? profile, string? strict)
    {
        WithCase(profile, strict, streamableAccumulator: true, rejects: false, test => {
            Outcome outcome = new Runner().Run(test);
            Assert.True(outcome.Kind == OutcomeKind.Pass, outcome.Detail);
        }, consumeTwice: true);
    }

    [Theory]
    [InlineData(null, null, false)]
    [InlineData("strm", "1", true)]
    public void StrictSourcesActuallyForwardWhileOrdinarySourcesRemainTrees(string? profile, string? strict, bool forward)
    {
        WithCase(profile, strict, streamableAccumulator: true, rejects: false, test => {
            using var capture = new StreamingExecutionCapture();
            Outcome outcome = new Runner().Run(test);
            Assert.True(outcome.Kind == OutcomeKind.Pass, outcome.Detail);
            var executions = capture.Snapshot().Executions.Where(e => e.Stage == nameof(StreamingExecutionCapture.Stage.Stylesheet)).ToArray();
            Assert.NotEmpty(executions);
            var sources = executions.SelectMany(e => e.Sources).ToArray();
            if (forward) Assert.Contains(sources, source => source.RequestedForward && source.ActualForward == true);
            else Assert.DoesNotContain(sources, source => source.RequestedForward || source.ActualForward == true);
        });
    }

    private static void WithCase(string? profile, string? strict, bool streamableAccumulator, bool rejects, Action<Xsl3.W3C.Xslt30.TestCase> inspect, bool consumeTwice = false)
    {
        string directory = Path.Combine(Path.GetTempPath(), "xsl3-execution-policy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string? previousProfile = Environment.GetEnvironmentVariable("XSL3_XSLT30_PROFILE");
        string? previousStrict = Environment.GetEnvironmentVariable("XSL3_STREAM_STRICT");
        try
        {
            Environment.SetEnvironmentVariable("XSL3_XSLT30_PROFILE", profile);
            Environment.SetEnvironmentVariable("XSL3_STREAM_STRICT", strict);
            File.WriteAllText(Path.Combine(directory, "catalog.xml"), $"""
                <catalog xmlns="{Catalog.Ns}"><test-set name="policy" file="set.xml"/></catalog>
                """);
            string assertion = consumeTwice ? "<error code='XTSE3430'/>" : rejects ? "<error code='XTSE3300'/>" : "<assert-string-value>2</assert-string-value>";
            string body = consumeTwice ? "<xsl:value-of select='doc/row'/><xsl:value-of select='doc/row'/>"
                : "<xsl:value-of select=\"accumulator-after('n')\"/>";
            File.WriteAllText(Path.Combine(directory, "set.xml"), $"""
                <test-set xmlns="{Catalog.Ns}" name="policy"><test-case name="source">
                  <environment><source file="source.xml" uri="source.xml"/></environment>
                  <test><stylesheet file="style.xsl"/></test><result>{assertion}</result>
                </test-case></test-set>
                """);
            File.WriteAllText(Path.Combine(directory, "source.xml"), "<doc><row/><row/></doc>");
            File.WriteAllText(Path.Combine(directory, "style.xsl"), $$"""
                <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:xs="http://www.w3.org/2001/XMLSchema">
                  <xsl:output method="text"/>
                  <xsl:accumulator name="n" as="xs:integer" initial-value="0" streamable="{{(streamableAccumulator ? "yes" : "no")}}">
                    <xsl:accumulator-rule match="row" select="$value + 1"/>
                  </xsl:accumulator>
                  <xsl:template name="xsl:initial-template">
                    <xsl:source-document href="source.xml" streamable="yes" use-accumulators="n">
                      {{body}}
                    </xsl:source-document>
                  </xsl:template>
                </xsl:stylesheet>
                """);
            inspect(Catalog.Load(directory).TestCases.Single());
        }
        finally
        {
            Environment.SetEnvironmentVariable("XSL3_XSLT30_PROFILE", previousProfile);
            Environment.SetEnvironmentVariable("XSL3_STREAM_STRICT", previousStrict);
            Directory.Delete(directory, recursive: true);
        }
    }
}
