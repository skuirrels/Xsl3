// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Xml;
using Xunit;

namespace Xsl3.Tests;

public class StreamedAccumulatorStorageTests
{
    private const string Input = "<doc><row id='a'><x/></row><row id='b'><y/></row><row id='c'><x/><y/></row></doc>";
    // Different expanded names, rule phases and initial values make a lost or aliased slot observable.
    private const string Counters = """
        <xsl:accumulator name="a:n" initial-value="0" streamable="yes">
          <xsl:accumulator-rule match="row" select="$value + 1"/>
        </xsl:accumulator>
        <xsl:accumulator name="b:n" initial-value="10" streamable="yes">
          <xsl:accumulator-rule match="row" phase="end" select="$value + 2"/>
        </xsl:accumulator>
        <xsl:accumulator name="n" initial-value="100" streamable="yes">
          <xsl:accumulator-rule match="row" select="$value + 3"/>
          <xsl:accumulator-rule match="row" phase="end" select="$value + 5"/>
        </xsl:accumulator>
        <xsl:accumulator name="N" initial-value="1000" streamable="yes">
          <xsl:accumulator-rule match="missing" select="$value + 1"/>
        </xsl:accumulator>
        """;
    private const string Values = """
        <xsl:value-of select="concat(@id, ':', accumulator-before('a:n'), '/')"/>
        <xsl:value-of select="accumulator-after('a:n')"/><xsl:text>:</xsl:text>
        <xsl:value-of select="concat(accumulator-before('b:n'), '/')"/>
        <xsl:value-of select="accumulator-after('b:n')"/><xsl:text>:</xsl:text>
        <xsl:value-of select="concat(accumulator-before('n'), '/')"/>
        <xsl:value-of select="accumulator-after('n')"/><xsl:text>:</xsl:text>
        <xsl:value-of select="concat(accumulator-before('N'), '/')"/>
        <xsl:value-of select="accumulator-after('N')"/><xsl:text>;</xsl:text>
        """;
    private const string First = "a:1/1:10/12:103/108:1000/1000;";
    private const string All = First + "b:2/2:12/14:111/116:1000/1000;c:3/3:14/16:119/124:1000/1000;";

    private static string Sheet(string body, string declarations) => $$"""
        <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
            xmlns:a="urn:counter:a" xmlns:b="urn:counter:b" xmlns:err="http://www.w3.org/2005/xqt-errors">
          <xsl:mode streamable="yes" use-accumulators="#all"/><xsl:output method="text"/>
          {{declarations}}
          <xsl:template match="/">{{body}}</xsl:template>
        </xsl:stylesheet>
        """;

    private static int Deliveries =>
#if NET10_0_OR_GREATER
        3;
#else
        2;
#endif

    private static void Execute(string sheet, bool debug, bool forward, int delivery, XmlReader input,
        TextWriter output, CancellationToken cancellation = default, XmlResolver? resolver = null)
    {
        var settings = new XsltSettings {
            StreamedExecution = forward, IncludeDebugInformation = debug,
            StreamingBufferCapacity = 1, EnableDocumentFunction = resolver != null,
        };
        if (delivery == 1)
        {
            var transformer = new XsltCompiler { Settings = settings }.Compile(new StringReader(sheet)).CreateTransformer();
            transformer.CancellationToken = cancellation;
            if (resolver != null) transformer.DocumentResolver = resolver;
            transformer.ApplyTemplates(input, output);
        }
        else
        {
            var transform = new XslCompiledTransform(debug);
            using var stylesheet = XmlReader.Create(new StringReader(sheet));
#if NET10_0_OR_GREATER
            if (delivery == 2)
            {
                using var image = new MemoryStream();
                transform.CompileToAssembly(stylesheet, settings, null,
                    "AccumulatorStorage" + Guid.NewGuid().ToString("N"), "Saved.AccumulatorStorage", image);
                transform.Load(Assembly.Load(image.ToArray()).GetType("Saved.AccumulatorStorage", true)!, settings);
            }
            else
#endif
                transform.Load(stylesheet, settings, null);
            transform.Transform(input, new XsltArgumentList { CancellationToken = cancellation }, output, resolver);
        }
    }

    private static void Verify(string sheet, string expected, bool debug, XmlResolver? resolver = null)
    {
        foreach (bool forward in new[] { false, true })
        for (int delivery = 0; delivery < Deliveries; delivery++)
        {
            using var input = XmlReader.Create(new StringReader(Input));
            using var output = new StringWriter();
            Execute(sheet, debug, forward, delivery, input, output, resolver: resolver);
            Assert.Equal(expected, output.ToString());
            Assert.NotEqual(ReadState.Closed, input.ReadState);
        }
    }

    [Theory]
    [InlineData("start", "a:1/1;b:2/2;c:3/3;")]
    [InlineData("end", "a:0/1;b:1/2;c:2/3;")]
    public void SingleCounterKeepsValuesWhenTheOtherPhaseHasNoRules(string phase, string expected)
    {
        string sheet = Sheet("""
            <xsl:for-each select="doc/row"><xsl:value-of select="concat(@id, ':', accumulator-before('n'), '/', accumulator-after('n'), ';')"/></xsl:for-each>
            """, $$"""
            <xsl:accumulator name="n" initial-value="0" streamable="yes">
              <xsl:accumulator-rule match="row" phase="{{phase}}" select="$value + 1"/>
            </xsl:accumulator>
            """);
        foreach (bool debug in new[] { false, true }) Verify(sheet, expected, debug);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MultipleCounterSlotsKeepNamespaceCaseAndPhaseValues(bool debug) =>
        Verify(Sheet("<xsl:for-each select='doc/row'>" + Values + "</xsl:for-each>", Counters), All, debug);

    private sealed class SourceResolver : XmlResolver
    {
        public override ICredentials? Credentials { set { } }
        public override object GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn) =>
            absoluteUri.OriginalString == "urn:counter-input" ? new MemoryStream(System.Text.Encoding.UTF8.GetBytes(Input)) : throw new XmlException("Unexpected test resource");
    }

    [Theory]
    [InlineData("copy-of(doc/row)")]
    [InlineData("snapshot(doc/row)")]
    public void MultipleCounterCopiesSurviveClosingTheirSource(string selection)
    {
        string body = "<xsl:variable name='copies' as='element()*'><xsl:source-document href='urn:counter-input' streamable='yes' use-accumulators='#all'>"
            + "<xsl:sequence select='" + selection + "'/></xsl:source-document></xsl:variable>"
            + "<xsl:for-each select='$copies'>" + Values + "</xsl:for-each>";
        foreach (bool debug in new[] { false, true }) Verify(Sheet(body, Counters), All, debug, new SourceResolver());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeferredHeadersSerializeEveryCounterSlot(bool debug) =>
        Verify(Sheet("<xsl:for-each select='doc/row/x/ancestor::row except doc/row/y/ancestor::row'>" + Values + "</xsl:for-each>", Counters), First, debug);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ForkConsumersShareEveryCounterWithoutChangingItsValues(bool debug)
    {
        string prong = "<xsl:sequence><xsl:for-each select='doc/row'>" + Values + "</xsl:for-each></xsl:sequence>";
        Verify(Sheet("<xsl:fork>" + prong + prong + "</xsl:fork>", Counters), All + All, debug);
    }

    [Theory]
    [InlineData("start", "error()", "FOER0000")]
    [InlineData("end", "error()", "FOER0000")]
    [InlineData("start", "accumulator-before('bad')", "XTDE3400")]
    [InlineData("end", "accumulator-after('bad')", "XTDE3400")]
    public void FaultedAdditionalCounterDoesNotCorruptPublishedOtherValues(string phase, string rule, string code)
    {
        string declarations = Counters + $$"""
            <xsl:accumulator name="bad" initial-value="0" streamable="yes">
              <xsl:accumulator-rule match="row" phase="{{phase}}" select="{{rule}}"/>
            </xsl:accumulator>
            """;
        string body = "<xsl:for-each select='doc/row'>" + Values + "<xsl:try><xsl:value-of select=\"accumulator-after('bad')\"/>"
            + "<xsl:catch><xsl:value-of select=\"concat(local-name-from-QName($err:code), ';')\"/></xsl:catch></xsl:try></xsl:for-each>";
        string expected = All.Replace(";", ";" + code + ";", StringComparison.Ordinal);
        foreach (bool debug in new[] { false, true }) Verify(Sheet(body, declarations), expected, debug);
    }

    private sealed class CancellingWriter(CancellationTokenSource cancellation) : StringWriter
    {
        private void CancelAfterOutput() { if (GetStringBuilder().Length != 0) cancellation.Cancel(); }
        public override void Write(char[] buffer, int index, int count) { base.Write(buffer, index, count); CancelAfterOutput(); }
        public override void Write(ReadOnlySpan<char> buffer) { base.Write(buffer); CancelAfterOutput(); }
        public override void Write(string? value) { base.Write(value); CancelAfterOutput(); }
    }

    [Theory]
    [InlineData("start")]
    [InlineData("end")]
    public void SinglePhaseRulesStillObserveCancellationAfterActualOutput(string phase)
    {
        string sheet = Sheet("<xsl:for-each select='doc/row'><xsl:value-of select=\"concat(accumulator-after('n'), ';')\"/></xsl:for-each>", $$"""
            <xsl:accumulator name="n" initial-value="0" streamable="yes">
              <xsl:accumulator-rule match="row" phase="{{phase}}" select="$value + 1"/>
            </xsl:accumulator>
            """);
        // Output exceeds the serializer buffer so cancellation happens during execution, not its final flush.
        string xml = "<doc>" + string.Concat(System.Linq.Enumerable.Repeat("<row/>", 10000)) + "</doc>";
        foreach (bool debug in new[] { false, true })
        for (int delivery = 0; delivery < Deliveries; delivery++)
        {
            using var input = XmlReader.Create(new StringReader(xml));
            using var cancellation = new CancellationTokenSource();
            using var output = new CancellingWriter(cancellation);
            var error = Record.Exception(() => Execute(sheet, debug, true, delivery, input, output, cancellation.Token));
            Assert.NotNull(error);
            while (error is not OperationCanceledException && error.InnerException != null) error = error.InnerException;
            Assert.Equal(cancellation.Token, Assert.IsType<OperationCanceledException>(error).CancellationToken);
            Assert.NotEmpty(output.ToString());
            Assert.False(input.EOF);
            Assert.NotEqual(ReadState.Closed, input.ReadState);
        }
    }
}
