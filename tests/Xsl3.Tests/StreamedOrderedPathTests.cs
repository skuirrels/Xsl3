// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.IO;
using System.Reflection;
using System.Security;
using System.Xml;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

public class StreamedOrderedPathTests
{
    private const string Input = "<doc><row><part><value>1</value></part><part><value>2</value></part></row><row><part><value>3</value></part></row></doc>";
    private const string Extra = "<xsl:variable name='extra' as='element(row)'><row><part><value>x</value></part></row></xsl:variable>";

    [Theory]
    [InlineData("insert-before($extra, 1, doc/row)/part")]
    [InlineData("insert-before($extra, 2, doc/row)/part/value")]
    [InlineData("insert-before(doc/row, 2, $extra)/part/value")]
    [InlineData("insert-before(doc/row, 99999999999999999999999, $extra)/part/value")]
    [InlineData("insert-before($extra, -5, doc/row)/part/value")]
    [InlineData("insert-before(one-or-more(doc/row), 2, $extra)/part/value")]
    [InlineData("insert-before(insert-before(doc/row, 2, $extra), 1, $extra)/part/value")]
    [InlineData("insert-before(insert-before($extra, 2, $extra), 2, doc/row)/part/value")]
    [InlineData("($extra, doc/row)/part")]
    [InlineData("($extra, doc/row)/part/value")]
    [InlineData("(doc/row, $extra)/part/value")]
    [InlineData("($extra, (), (doc/row, $extra))/part/value")]
    [InlineData("insert-before(($extra, doc/row), 2, $extra)/part/value")]
    [InlineData("($extra, insert-before(doc/row, 2, $extra))/part/value")]
    public void MixedNodePathsKeepDocumentOrderAndLiveContent(string expression)
    {
        // Relative order of different documents is implementation-defined. Each document
        // must still form one block, and its own nodes must be in canonical order.
        AssertCanonicalDocuments(Run(expression, false));
        AssertCanonicalDocuments(Run(expression, true));
    }

    [Theory]
    [InlineData("insert-before((), 1, doc/row)/part/value", "1|2|3")]
    [InlineData("insert-before(doc/row, 2, ())/part/value", "1|2|3")]
    [InlineData("count(insert-before($extra, 2, doc/row)/part/value)", "4")]
    [InlineData("count(insert-before((), 2, ())/part/value)", "0")]
    [InlineData("((), doc/row)/part/value", "1|2|3")]
    [InlineData("(doc/row, ())/part/value", "1|2|3")]
    [InlineData("count(($extra, doc/row)/part/value)", "4")]
    [InlineData("count(((), ())/part/value)", "0")]
    public void EmptyPopulationsAndInspectionKeepTheirContracts(string expression, string expected)
    {
        Assert.Equal(expected, Run(expression, false));
        Assert.Equal(expected, Run(expression, true));
    }

    [Theory]
    [InlineData("insert-before(doc/row, 1, $extra)/part/value/text()")]
    [InlineData("($extra, doc/row)/part/value/text()")]
    public void AdjacentTextNodesRetainSimpleContentJoining(string expression)
    {
        foreach (bool forward in new[] { false, true })
            Assert.Contains(Run(expression, forward), new[] { "x123", "123x" });
    }

    [Theory]
    [InlineData("insert-before($extra, 2, doc//row)/part/value")]
    [InlineData("($extra, doc//row)/part/value")]
    public void CrawlingMixedPathsRetainTheirStaticRejection(string expression)
    {
        foreach (bool forward in new[] { false, true })
            Assert.Contains("XTSE3430", Assert.Throws<XslLoadException>(() => Run(expression, forward)).Message);
    }

    [Theory]
    [InlineData("insert-before(doc/row, 1, 'x')/part/value", "XPTY0019")]
    [InlineData("insert-before('x', 1, doc/row)/part/value", "XPTY0019")]
    [InlineData("insert-before((), (), ())/part/value", "XPTY0004")]
    [InlineData("insert-before(doc/row, 1.5, $extra)/part/value", "XPTY0004")]
    [InlineData("insert-before(insert-before(doc/row, 1.5, $extra), 2, $extra)/part/value", "XPTY0004")]
    [InlineData("('x', doc/row)/part/value", "XPTY0019")]
    [InlineData("(doc/row, 'x')/part/value", "XPTY0019")]
    [InlineData("($extra, insert-before(doc/row, 1.5, $extra))/part/value", "XPTY0004")]
    public void RewrittenPathsKeepNodeAndPositionErrors(string expression, string code)
    {
        Assert.Equal(code, Assert.Throws<XdmException>(() => Run(expression, false)).Code);
        Assert.Equal(code, Assert.Throws<XdmException>(() => Run(expression, true)).Code);
    }

    [Fact]
    public void ReversedAndRepeatedGroundedAnchorsAreCanonicalized()
    {
        const string declarations = "<xsl:variable name='g'><root><row><part><value>a</value></part></row><row><part><value>b</value></part></row></root></xsl:variable><xsl:variable name='s' as='element()*' select='($g/root/row[2], $g/root/row[1], $g/root/row[1])'/>";
        const string expression = "insert-before($s, 2, doc/row)/part/value";
        foreach (bool forward in new[] { false, true })
            Assert.Contains(Run(expression, forward, declarations: declarations), new[] { "a|b|1|2|3", "1|2|3|a|b" });
    }

    [Fact]
    public void CommaPathsMergeUnorderedAndRepeatedGroundedNodesWithOneLivePopulation()
    {
        const string declarations = "<xsl:variable name='g'><root><row><part><value>a</value></part></row><row><part><value>b</value></part></row></root></xsl:variable>";
        const string expression = "($g/root/row[2], doc/row, $g/root/row[1], $g/root/row[2])/part/value";
        foreach (bool forward in new[] { false, true })
            Assert.Contains(Run(expression, forward, declarations: declarations), new[] { "a|b|1|2|3", "1|2|3|a|b" });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void CopiedCommaPathContentRetainsDocumentBlocks(bool debug, bool executable)
    {
        const string expression = "($extra, doc/row)/part/value";
        Assert.Contains(Run(expression, false, debug: debug, executable: executable, copy: true), new[] { "x123", "123x" });
        Assert.Contains(Run(expression, true, debug: debug, executable: executable, copy: true), new[] { "x123", "123x" });
#if NET9_0_OR_GREATER
        if (!executable)
            Assert.Contains(Run(expression, true, debug: debug, saved: true, copy: true), new[] { "x123", "123x" });
#endif
    }

    [Theory]
    [InlineData("insert-before(($g/row[2], $g/row[1]), 2, ($g/row[1], $g/row[2]))/part/value")]
    [InlineData("($g/row[2], $g/row[1], $g/row[1], $g/row[2])/part/value")]
    public void AliasedGroundedPopulationsAreDeduplicated(string expression)
    {
        const string declarations = "<xsl:variable name='g' select='copy-of(doc)'/>";
        Assert.Equal("1|2|3", Run(expression, false, declarations: declarations));
        Assert.Equal("1|2|3", Run(expression, true, declarations: declarations));
    }

    [Theory]
    [InlineData("insert-before(copy-of($extra), 2, copy-of($extra))/part/value")]
    [InlineData("(copy-of($extra), copy-of($extra))/part/value")]
    public void IndependentConstructorsWithEqualValuesKeepTheirIdentities(string expression)
    {
        Assert.Equal("x|x", Run(expression, false));
        Assert.Equal("x|x", Run(expression, true));
    }

    [Theory]
    [InlineData("exactly-one")]
    [InlineData("zero-or-one")]
    public void CardinalityBoundedCrawlingInputsRemainSafeChildAnchors(string function)
    {
        string expression = $"insert-before($extra, 2, {function}((doc//row)[1]))/part/value";
        foreach (bool forward in new[] { false, true })
            Assert.Contains(Run(expression, forward), new[] { "x|1|2", "1|2|x" });
    }

    [Theory]
    [InlineData("(doc//row)[1]/part/value", "1|2")]
    [InlineData("(doc//row)[2]/part/value", "3")]
    [InlineData("(doc//row)[999999999999999999999999999999999]/part/value", "")]
    [InlineData("doc/row/part[1]/value", "1|3")]
    public void ExactIntegerPredicatesStopBeforeAdvancingPastTheirSelectedNode(string expression, string expected)
    {
        Assert.Equal(expected, Run(expression, false));
        Assert.Equal(expected, Run(expression, true));
    }

    [Fact]
    public void AtMostOneProofOverridesAnUnprovenCallerOrder()
    {
        const string functions = """
            <xsl:function name="f:children" streamability="absorbing" as="xs:string">
              <xsl:param name="nodes" as="node()*"/><xsl:sequence select="string-join($nodes/part/value, '|')"/>
            </xsl:function>
            <xsl:function name="f:first" streamability="absorbing" as="xs:string">
              <xsl:param name="nodes" as="node()*"/><xsl:sequence select="f:children(exactly-one($nodes[1]))"/>
            </xsl:function>
            """;
        Assert.Equal("1|2", Run("f:first(doc/row ! .)", false, globals: functions));
        Assert.Equal("1|2", Run("f:first(doc/row ! .)", true, globals: functions));
    }

    [Fact]
    public void PredicatedGroundedStepsRetainTheirExistingSemantics()
    {
        const string expression = "let $g := copy-of(doc) return insert-before($g/row, 1, $g/row)/part[1]/value";
        Assert.Equal("1|3", Run(expression, false));
        Assert.Equal("1|3", Run(expression, true));
    }

    [Theory]
    [InlineData(false, "insert-before($extra, 2, doc/row)/part/value")]
    [InlineData(true, "insert-before($extra, 2, doc/row)/part/value")]
    [InlineData(false, "($extra, doc/row)/part/value")]
    [InlineData(true, "($extra, doc/row)/part/value")]
    public void BothPublicApisConsumeTheOrderedPopulation(bool executable, string expression)
    {
        AssertCanonicalDocuments(Run(expression, true, executable: executable));
    }

#if NET9_0_OR_GREATER
    [Theory]
    [InlineData(false, "insert-before($extra, 2, doc/row)/part/value")]
    [InlineData(true, "insert-before($extra, 2, doc/row)/part/value")]
    [InlineData(false, "($extra, doc/row)/part/value")]
    [InlineData(true, "($extra, doc/row)/part/value")]
    public void OrderedPathsSurviveSavedCompilation(bool debug, string expression) =>
        AssertCanonicalDocuments(Run(expression, true, saved: true, debug: debug));
#endif

    [Theory]
    [InlineData("insert-before($extra, 2, doc/row)/part/value")]
    [InlineData("($extra, doc/row)/part/value")]
    public void TheFirstStreamedValueIsDeliveredBeforeTheEndOfInput(string expression)
    {
        string body = $"<xsl:for-each select='{expression}'><xsl:message select='string(.)'/></xsl:for-each>";
        var transform = new XslCompiledTransform();
        transform.Load(XmlReader.Create(new StringReader(Sheet(body, Extra))), new() { StreamedExecution = true, StreamingBufferCapacity = 1 }, null);
        using var input = XmlReader.Create(new StringReader(Input));
        bool observed = false;
        var arguments = new XsltArgumentList();
        arguments.XsltMessageEncountered += (_, message) => {
            if (message.Message != "1") return;
            Assert.False(input.EOF);
            observed = true;
        };
        using var output = new StringWriter();
        transform.Transform(input, arguments, output);
        Assert.True(observed);
        Assert.NotEqual(ReadState.Closed, input.ReadState);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AFollowingAttributePathDoesNotSortAndExhaustTheLivePopulation(bool debug)
    {
        const string declarations = "<xsl:variable name='extra' as='element(row)'><row><part id='x'/></row></xsl:variable>";
        const string body = "<xsl:for-each select='($extra, doc/row)/part/@id'><xsl:message select='string(.)'/></xsl:for-each>";
        var transform = new XslCompiledTransform(enableDebug: debug);
        transform.Load(XmlReader.Create(new StringReader(Sheet(body, declarations))), new() { StreamedExecution = true, StreamingBufferCapacity = 1 }, null);
        using var input = XmlReader.Create(new StringReader("<doc><row><part id='a'/><part id='b'/></row><row><part id='c'/></row></doc>"));
        var values = new System.Collections.Generic.List<string>();
        var arguments = new XsltArgumentList();
        arguments.XsltMessageEncountered += (_, message) => {
            if (message.Message == "a") Assert.False(input.EOF);
            values.Add(message.Message);
        };
        using var output = new StringWriter();
        transform.Transform(input, arguments, output);
        Assert.Contains(string.Join("|", values), new[] { "x|a|b|c", "a|b|c|x" });
        Assert.NotEqual(ReadState.Closed, input.ReadState);
    }

    [Fact]
    public void ALongCommaPathEmitsIncrementallyWithBoundedOpenSourceHeaders()
    {
        const int records = 5000;
        var xml = new System.Text.StringBuilder("<doc>");
        for (int i = 0; i < records; i++)
            xml.Append("<row><part><value>").Append(i).Append("</value></part></row>");
        xml.Append("</doc>");
        using var input = XmlReader.Create(new StringReader(xml.ToString()));
        using var document = new Streaming.StreamedDocument(input);
        const string body = "<xsl:for-each select='($extra, doc/row)/part/value'><xsl:message select='string(.)'/></xsl:for-each>";
        var transform = new XslCompiledTransform();
        transform.Load(XmlReader.Create(new StringReader(Sheet(body, Extra))), new() { StreamedExecution = true, StreamingBufferCapacity = 1 }, null);
        int received = 0, maximumHeaders = 0;
        var arguments = new XsltArgumentList();
        arguments.XsltMessageEncountered += (_, message) => {
            if (message.Message == "x") return;
            Assert.Equal(received.ToString(System.Globalization.CultureInfo.InvariantCulture), message.Message);
            if (received == 0) Assert.False(input.EOF);
            maximumHeaders = System.Math.Max(maximumHeaders, document.RetainedNodeCount);
            received++;
        };
        using var output = new StringWriter();
        transform.Transform(document, arguments, output);
        Assert.Equal(records, received);
        // This fixture has six open node levels including its text leaf; allow one
        // lookahead chain, independently of the number of records.
        Assert.InRange(maximumHeaders, 1, 12);
        Assert.NotEqual(ReadState.Closed, input.ReadState);
    }

    [Fact]
    public void NestedPositionsAreEvaluatedOncePerInsertion()
    {
        const string function = "<xsl:function name='f:position' as='xs:integer'><xsl:message>position</xsl:message><xsl:sequence select='2'/></xsl:function>";
        const string body = "<xsl:value-of select='insert-before(insert-before($extra, f:position(), $extra), f:position(), doc/row)/part/value' separator='|'/>";
        var transform = new XslCompiledTransform();
        transform.Load(XmlReader.Create(new StringReader(Sheet(body, Extra, function))),
            new() { StreamedExecution = true, StreamingBufferCapacity = 1 }, null);
        int evaluations = 0;
        var arguments = new XsltArgumentList();
        arguments.XsltMessageEncountered += (_, message) => { Assert.Equal("position", message.Message); evaluations++; };
        using var input = XmlReader.Create(new StringReader(Input));
        using var output = new StringWriter();
        transform.Transform(input, arguments, output);
        Assert.Equal(2, evaluations);
        AssertCanonicalDocuments(output.ToString());
        Assert.NotEqual(ReadState.Closed, input.ReadState);
    }

    private static void AssertCanonicalDocuments(string result) =>
        Assert.Contains(result, new[] { "x|1|2|3", "1|2|3|x" });

    private static string Sheet(string body, string declarations, string globals = "") => $$"""
        <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:f="urn:test:functions" xmlns:xs="http://www.w3.org/2001/XMLSchema">
          {{globals}}<xsl:mode streamable="yes"/><xsl:output method="text"/>
          <xsl:template match="/">{{declarations}}{{body}}</xsl:template>
        </xsl:stylesheet>
        """;

    private static string Run(string expression, bool forward, bool executable = false, bool saved = false, string declarations = Extra, string globals = "", bool debug = false, bool copy = false)
    {
        string body = copy ? $"<xsl:copy-of select=\"{SecurityElement.Escape(expression)}\"/>"
            : $"<xsl:value-of select=\"{SecurityElement.Escape(expression)}\" separator=\"|\"/>";
        string sheet = Sheet(body, declarations, globals);
        var settings = new XsltSettings { StreamedExecution = forward, StreamingBufferCapacity = 1 };
        using var input = XmlReader.Create(new StringReader(Input));
        if (executable)
        {
            var transformer = new XsltCompiler { Settings = settings }.Compile(new StringReader(sheet)).CreateTransformer();
            using var executableOutput = new StringWriter();
            transformer.ApplyTemplates(input, executableOutput);
            Assert.NotEqual(ReadState.Closed, input.ReadState);
            return executableOutput.ToString();
        }
        var transform = new XslCompiledTransform(enableDebug: debug);
        using var stylesheet = XmlReader.Create(new StringReader(sheet));
        if (saved)
        {
#if NET9_0_OR_GREATER
            using var assembly = new MemoryStream();
            transform.CompileToAssembly(stylesheet, settings, null, "OrderedPath", "Saved.OrderedPath", assembly);
            transform.Load(Assembly.Load(assembly.ToArray()).GetType("Saved.OrderedPath", true)!);
#endif
        }
        else transform.Load(stylesheet, settings, null);
        using var output = new StringWriter();
        transform.Transform(input, null, output);
        Assert.NotEqual(ReadState.Closed, input.ReadState);
        return output.ToString();
    }
}
