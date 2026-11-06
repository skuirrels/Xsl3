// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Linq;
using System.Text;
using System.Xml;
using Xunit;

namespace Xsl3.Tests;

public class StreamedMergeTests
{
    private static string Sheet(string sources, string action, string declarations = "") => $$"""
        <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:xs="http://www.w3.org/2001/XMLSchema" exclude-result-prefixes="xs">
          <xsl:output omit-xml-declaration="yes"/>{{declarations}}<xsl:template match="/"><out><xsl:merge>
            {{sources}}<xsl:merge-action>{{action}}</xsl:merge-action>
          </xsl:merge></out></xsl:template>
        </xsl:stylesheet>
        """;

    private static string Source(string name, string uris, string select = "rows/row", string key = "xs:integer(@k)", string attributes = "", string keyAttributes = "") =>
        $"<xsl:merge-source name='{name}' streamable='yes' for-each-source=\"{uris}\" select=\"{select}\" {attributes}><xsl:merge-key select=\"{key}\" {keyAttributes}/></xsl:merge-source>";

    private static string Run(string sheet, Sources sources, bool forward = true, bool legacy = false, Action<string>? message = null, bool schemaAware = false, bool debug = false, XsltStreamabilityMode streamability = XsltStreamabilityMode.Strict, bool saved = false)
    {
        using var input = XmlReader.Create(new StringReader("<input/>"));
        using var output = new StringWriter();
        var settings = new XsltSettings { StreamedExecution = forward, EnableDocumentFunction = true, SchemaAware = schemaAware, IncludeDebugInformation = debug, Streamability = streamability };
        if (legacy)
        {
            var transform = new XslCompiledTransform(debug);
            using var stylesheet = XmlReader.Create(new StringReader(sheet));
#if NET10_0_OR_GREATER
            if (saved)
            {
                using var assembly = new MemoryStream();
                transform.CompileToAssembly(stylesheet, settings, null, "MergeFunctionContext" + Guid.NewGuid().ToString("N"), "Saved.MergeFunctionContext", assembly);
                transform.Load(System.Reflection.Assembly.Load(assembly.ToArray()).GetType("Saved.MergeFunctionContext", true)!, settings);
            }
            else
#endif
                transform.Load(stylesheet, settings, null);
            transform.Transform(input, null, output, sources);
        }
        else
        {
            var transform = new XsltCompiler { Settings = settings }.Compile(new StringReader(sheet)).CreateTransformer();
            transform.DocumentResolver = sources;
            transform.MessageHandler = message;
            transform.ApplyTemplates(input, output);
        }
        Assert.NotEqual(ReadState.Closed, input.ReadState);
        return output.ToString();
    }

    private sealed class TrackingReader(XmlReader reader) : XmlWrappingReader(reader)
    {
        internal int Rows;
        internal bool Closed;
        public override bool Read()
        {
            bool read = base.Read();
            if (read && NodeType == XmlNodeType.Element && LocalName == "row") Rows++;
            return read;
        }
        public override void Close() { Closed = true; base.Close(); }
    }

    private sealed class Sources(Dictionary<string, string> documents) : XmlResolver
    {
        internal readonly List<TrackingReader> Opened = new();
        internal readonly List<TrackingStream> Streams = new();
        internal bool SupplyStreams;
        public override ICredentials? Credentials { set { } }
        public override object GetEntity(Uri uri, string? role, Type? type)
        {
            if (SupplyStreams)
            {
                var stream = new TrackingStream(Encoding.UTF8.GetBytes(documents[uri.OriginalString]));
                Streams.Add(stream);
                return stream;
            }
            var reader = new TrackingReader(XmlReader.Create(new StringReader(documents[uri.OriginalString]), new(), uri.OriginalString));
            Opened.Add(reader);
            return reader;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SortedInputsAreMergedWithStableGroups(bool legacy)
    {
        string sheet = Sheet(Source("left", "'urn:left'") + Source("right", "'urn:right'"), """
            <g key="{current-merge-key()}" p="{position()}" first="{@id}" left="{count(current-merge-group('left'))}" right="{count(current-merge-group('right'))}"><xsl:value-of select="current-merge-group() ! string(@id)"/></g>
            """);
        var documents = new Dictionary<string, string> {
            ["urn:left"] = "<rows><row k='1' id='a'/><row k='3' id='c1'/><row k='3' id='c2'/></rows>",
            ["urn:right"] = "<rows><row k='2' id='b'/><row k='3' id='d'/><row k='4' id='e'/></rows>"
        };
        const string expected = "<out><g key=\"1\" p=\"1\" first=\"a\" left=\"1\" right=\"0\">a</g><g key=\"2\" p=\"2\" first=\"b\" left=\"0\" right=\"1\">b</g><g key=\"3\" p=\"3\" first=\"c1\" left=\"2\" right=\"1\">c1 c2 d</g><g key=\"4\" p=\"4\" first=\"e\" left=\"0\" right=\"1\">e</g></out>";
        foreach (bool forward in new[] { false, true })
        {
            var resolver = new Sources(documents);
            Assert.Equal(expected, Run(sheet, resolver, forward, legacy));
            // Source-document/merge resolvers may lend an already configured reader; the caller owns it.
            Assert.All(resolver.Opened, reader => Assert.False(reader.Closed));
        }
    }

    [Fact]
    public void SnapshotKeysCanReadChildrenAndOnlyTheirCopiedAncestors()
    {
        string sheet = Sheet(Source("s", "'urn:input'", key: "xs:integer(v) + xs:integer(../@offset)"),
            "<g key='{current-merge-key()}' siblings='{count(../row)}'><xsl:value-of select='current-merge-group()/v'/></g>");
        var docs = new Dictionary<string, string> { ["urn:input"] = "<rows offset='10'><row><v>1</v></row><row><v>2</v></row></rows>" };
        foreach (bool forward in new[] { false, true })
            Assert.Equal("<out><g key=\"11\" siblings=\"1\">1</g><g key=\"12\" siblings=\"1\">2</g></out>", Run(sheet, new Sources(docs), forward));
    }
    private sealed class TrackingStream(byte[] bytes) : MemoryStream(bytes)
    {
        internal bool Closed;
        protected override void Dispose(bool disposing) { Closed = true; base.Dispose(disposing); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MultipleAnchorsHaveIndependentFocusAndStableTies(bool forward)
    {
        string sheet = Sheet(Source("s", "('urn:a', 'urn:b')",
            select: "if (position() + position() = 4) then rows/row else rows/row[not(@skip)]"),
            "<g p='{position() + position()}' key='{current-merge-key()}'><xsl:value-of select='current-merge-group() ! string(@id)'/></g>");
        var docs = new Dictionary<string, string> {
            ["urn:a"] = "<rows><row k='1' id='a'/><row k='2' id='skip' skip='yes'/><row k='3' id='c'/></rows>",
            ["urn:b"] = "<rows><row k='1' id='b'/><row k='2' id='d' skip='yes'/></rows>"
        };
        Assert.Equal("<out><g p=\"2\" key=\"1\">a b</g><g p=\"4\" key=\"2\">d</g><g p=\"6\" key=\"3\">c</g></out>", Run(sheet, new Sources(docs), forward));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void FallbackSelectionsRetainAnchorPositionAndSize(bool forward, bool legacy)
    {
        string sheet = Sheet(Source("s", "('urn:a', 'urn:b')",
            select: "if (position() + position() = last() + last()) then rows/row else rows/row[not(@skip)]"),
            "<g key='{current-merge-key()}'><xsl:value-of select='current-merge-group() ! string(@id)'/></g>");
        var resolver = new Sources(new()
        {
            ["urn:a"] = "<rows><row k='1' id='a'/><row k='2' id='skip' skip='yes'/></rows>",
            ["urn:b"] = "<rows><row k='1' id='b'/><row k='2' id='c' skip='yes'/></rows>"
        });
        Assert.Equal("<out><g key=\"1\">a b</g><g key=\"2\">c</g></out>",
            Run(sheet, resolver, forward, legacy, streamability: XsltStreamabilityMode.Fallback));
        Assert.All(resolver.Opened, reader => Assert.Equal(2, reader.Rows));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NumericComparisonPreservesOriginalStringKey(bool forward)
    {
        string sheet = Sheet(Source("s", "'urn:a'", key: "string(@k)", keyAttributes: "data-type='number'"),
            "<g key='{current-merge-key()}' type='{current-merge-key() instance of xs:string}'/>");
        var docs = new Dictionary<string, string> { ["urn:a"] = "<rows><row k='02'/><row k='10'/></rows>" };
        Assert.Equal("<out><g key=\"02\" type=\"true\"/><g key=\"10\" type=\"true\"/></out>", Run(sheet, new Sources(docs), forward));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void KeyAttributesUseContainingFocusAndCompareEffectiveValues(bool forward)
    {
        string sheet = Sheet(Source("a", "'urn:a'", keyAttributes: "order='{if (position()=1) then &quot;descending&quot; else &quot;ascending&quot;}'")
            + Source("b", "'urn:b'", keyAttributes: "order='descending'"), "<g key='{current-merge-key()}'/>");
        var docs = new Dictionary<string, string> { ["urn:a"] = "<rows><row k='3'/><row k='1'/></rows>", ["urn:b"] = "<rows><row k='2'/></rows>" };
        Assert.Equal("<out><g key=\"3\"/><g key=\"2\"/><g key=\"1\"/></out>", Run(sheet, new Sources(docs), forward));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitAndAbsentKeyAttributesDisagree(bool forward)
    {
        string sheet = Sheet(Source("a", "'urn:a'") + Source("b", "'urn:b'", keyAttributes: "order='ascending'"), "<g/>");
        var docs = new Dictionary<string, string> { ["urn:a"] = "<rows><row k='1'/></rows>", ["urn:b"] = "<rows><row k='1'/></rows>" };
        Assert.Contains("XTDE2210", Assert.Throws<XsltDynamicException>(() => Run(sheet, new Sources(docs), forward)).Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ContextSizeIsAbsentForStreamableSourcesEvenOnTrees(bool forward)
    {
        string sheet = Sheet(Source("s", "'urn:a'"), "<xsl:value-of select='last() + last()'/>");
        var docs = new Dictionary<string, string> { ["urn:a"] = "<rows><row k='1'/></rows>" };
        Assert.Contains("XPDY0002", Assert.Throws<XsltDynamicException>(() => Run(sheet, new Sources(docs), forward)).Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OutOfOrderInputRaisesItsDefinedError(bool forward)
    {
        string sheet = Sheet(Source("s", "'urn:a'"), "<g/>");
        var docs = new Dictionary<string, string> { ["urn:a"] = "<rows><row k='2'/><row k='1'/></rows>" };
        Assert.Contains("XTDE2220", Assert.Throws<XsltDynamicException>(() => Run(sheet, new Sources(docs), forward)).Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InputSelectionRemainsTreeBasedWhenStreamingIsDisabled(bool forward)
    {
        string rows = string.Concat(Enumerable.Range(1, 1000).Select(i => $"<row k='{i}'/>"));
        var docs = new Dictionary<string, string> { ["urn:a"] = "<rows>" + rows + "</rows>", ["urn:b"] = "<rows>" + rows + "</rows>" };
        var resolver = new Sources(docs);
        string sheet = Sheet(Source("a", "'urn:a'") + Source("b", "'urn:b'"), "<xsl:message select='current-merge-key()'/><g/>");
        int messages = 0;
        Run(sheet, resolver, forward, message: _ =>
        {
            if (messages++ != 0) return;
            Assert.Equal(2, resolver.Opened.Count);
            Assert.All(resolver.Opened, reader =>
            {
                if (forward) Assert.InRange(reader.Rows, 1, 4);
                else Assert.Equal(1000, reader.Rows);
            });
        });
        Assert.Equal(1000, messages);
        Assert.All(resolver.Opened, reader => Assert.Equal(1000, reader.Rows));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void OwnedStreamsCloseOnCompletionAndActionFailure(bool fail, bool forward)
    {
        var resolver = new Sources(new() { ["urn:a"] = "<rows><row k='1'/><row k='3'/></rows>", ["urn:b"] = "<rows><row k='2'/><row k='4'/></rows>" }) { SupplyStreams = true };
        string sheet = Sheet(Source("a", "'urn:a'") + Source("b", "'urn:b'"), fail ? "<xsl:sequence select='error()'/>" : "<g/>");
        if (fail) Assert.Throws<XsltDynamicException>(() => Run(sheet, resolver, forward));
        else Assert.Equal("<out><g/><g/><g/><g/></out>", Run(sheet, resolver, forward));
        Assert.Equal(2, resolver.Streams.Count);
        Assert.All(resolver.Streams, stream => Assert.True(stream.Closed));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyInputAndDuplicateUrisStillMakeIndependentSnapshots(bool forward)
    {
        var resolver = new Sources(new() { ["urn:a"] = "<rows><row k='1'/></rows>", ["urn:empty"] = "<rows/>" });
        string sheet = Sheet(Source("s", "('urn:empty','urn:a','urn:a')"), "<g count='{count(current-merge-group()/@k)}'/>");
        Assert.Equal("<out><g count=\"2\"/></out>", Run(sheet, resolver, forward));
    }

    [Fact]
    public void StreamableNoIsAlsoExcludedByForEachItem()
    {
        string sheet = Sheet("<xsl:merge-source for-each-item='1' streamable='no' select='.'><xsl:merge-key/></xsl:merge-source>", "<g/>");
        Assert.Contains("XTSE3195", Assert.Throws<XsltStaticException>(() => Run(sheet, new Sources(new()))).Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void KeysHaveSingletonFocusAndRetainAccumulatorProvenance(bool forward)
    {
        string declarations = """
            <xsl:accumulator name='n' initial-value='0' as='xs:integer' streamable='yes'>
              <xsl:accumulator-rule match='row' select='$value + 1'/>
              <xsl:accumulator-rule match='row' phase='end' select='$value + 10'/>
            </xsl:accumulator>
            """;
        string sheet = Sheet(Source("s", "'urn:a'", key: "xs:integer(@k) + position() + last()", attributes: "use-accumulators='n'"),
            "<g key='{current-merge-key()}' before='{accumulator-before(&quot;n&quot;)}' after='{accumulator-after(&quot;n&quot;)}'/>", declarations);
        var docs = new Dictionary<string, string> { ["urn:a"] = "<rows><row k='1'/><row k='2'/></rows>" };
        Assert.Equal("<out><g key=\"3\" before=\"1\" after=\"11\"/><g key=\"4\" before=\"12\" after=\"22\"/></out>", Run(sheet, new Sources(docs), forward));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RelativeCollationsAreComparedAsAbsoluteUris(bool forward)
    {
        string sheet = Sheet(Source("a", "'urn:a'", key: "@k", keyAttributes: "xml:base='http://www.w3.org/2005/xpath-functions/' collation='collation/codepoint'")
            + Source("b", "'urn:b'", key: "@k", keyAttributes: "collation='http://www.w3.org/2005/xpath-functions/collation/codepoint'"), "<g key='{current-merge-key()}'/>");
        var docs = new Dictionary<string, string> { ["urn:a"] = "<rows><row k='a'/></rows>", ["urn:b"] = "<rows><row k='b'/></rows>" };
        Assert.Equal("<out><g key=\"a\"/><g key=\"b\"/></out>", Run(sheet, new Sources(docs), forward));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GroundedInputsCanRequestStableSortingBesideAForwardInput(bool forward)
    {
        string sorted = Source("b", "'urn:b'", attributes: "sort-before-merge='yes'").Replace("streamable='yes'", "streamable='no'", StringComparison.Ordinal);
        string sheet = Sheet(Source("a", "'urn:a'") + sorted, "<g key='{current-merge-key()}'><xsl:value-of select='current-merge-group() ! string(@id)'/></g>");
        var docs = new Dictionary<string, string> { ["urn:a"] = "<rows><row k='1' id='a'/><row k='3' id='d'/></rows>", ["urn:b"] = "<rows><row k='2' id='b'/><row k='1' id='c'/><row k='2' id='e'/></rows>" };
        Assert.Equal("<out><g key=\"1\">a c</g><g key=\"2\">b e</g><g key=\"3\">d</g></out>", Run(sheet, new Sources(docs), forward));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IncomparableKeysFromDifferentInputsOfOneSourceRaiseTypeError(bool forward)
    {
        string sheet = Sheet(Source("s", "('urn:a','urn:b')", key: "if (@k='1') then 1 else true()"), "<g/>");
        var docs = new Dictionary<string, string> { ["urn:a"] = "<rows><row k='1'/></rows>", ["urn:b"] = "<rows><row k='2'/></rows>" };
        Assert.Contains("XTTE2230", Assert.Throws<XsltDynamicException>(() => Run(sheet, new Sources(docs), forward)).Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MultiValuedKeysRaiseCardinalityError(bool forward)
    {
        string sheet = Sheet(Source("s", "'urn:a'", key: "(1,2)"), "<g/>");
        var docs = new Dictionary<string, string> { ["urn:a"] = "<rows><row/></rows>" };
        Assert.Contains("XTTE1020", Assert.Throws<XsltDynamicException>(() => Run(sheet, new Sources(docs), forward)).Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DynamicSourceNamesAreMatchedExactly(bool forward)
    {
        string sheet = Sheet(Source("s", "'urn:a'"), "<xsl:variable name='name' select='string(@source)'/><xsl:value-of select='current-merge-group($name)'/>");
        var docs = new Dictionary<string, string> { ["urn:a"] = "<rows><row k='1' source=' s '/></rows>" };
        Assert.Contains("XTDE3490", Assert.Throws<XsltDynamicException>(() => Run(sheet, new Sources(docs), forward)).Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ValidationTypesAndDefaultsSurviveTheSnapshot(bool forward)
    {
        const string schema = """
            <xsl:import-schema><xs:schema><xs:element name='rows'><xs:complexType><xs:sequence>
              <xs:element name='row' maxOccurs='unbounded'><xs:complexType><xs:attribute name='k' type='xs:integer' default='7'/></xs:complexType></xs:element>
            </xs:sequence></xs:complexType></xs:element></xs:schema></xsl:import-schema>
            """;
        string sheet = Sheet(Source("s", "'urn:a'", key: "@k", attributes: "validation='strict'"),
            "<g key='{current-merge-key()}' typed='{current-merge-key() instance of xs:integer}'/>", schema);
        var docs = new Dictionary<string, string> { ["urn:a"] = "<rows><row k='1'/><row/></rows>" };
        Assert.Equal("<out><g key=\"1\" typed=\"true\"/><g key=\"7\" typed=\"true\"/></out>", Run(sheet, new Sources(docs), forward, schemaAware: true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyInputsProduceNoActions(bool forward)
    {
        var resolver = new Sources(new() { ["urn:a"] = "<rows/>", ["urn:b"] = "<rows/>" }) { SupplyStreams = true };
        string sheet = Sheet(Source("s", "('urn:a','urn:b')"), "<xsl:sequence select='error()'/>");
        Assert.Equal("<out/>", Run(sheet, resolver, forward));
        Assert.All(resolver.Streams, stream => Assert.True(stream.Closed));
    }

    [Theory]
    [InlineData("function() { current-merge-group() }()", "XTDE3480")]
    [InlineData("(current-merge-group#0)()", "XTDE3480")]
    [InlineData("function() { current-merge-group('s') }()", "XTDE3480")]
    [InlineData("(current-merge-group#1)('s')", "XTDE3480")]
    [InlineData("function() { current-merge-key() }()", "XTDE3510")]
    [InlineData("(current-merge-key#0)()", "XTDE3510")]
    public void FunctionItemsDoNotCaptureTheImplicitMergeContext(string expression, string code)
    {
        string action = $"<xsl:try><xsl:sequence select=\"{expression}\"/><xsl:catch xmlns:err='http://www.w3.org/2005/xqt-errors' exclude-result-prefixes='err' errors='err:{code}'><absent/></xsl:catch></xsl:try>"
            + "<g k='{current-merge-key()}' n='{count(current-merge-group())}'/>";
        string sheet = Sheet(Source("s", "'urn:a'"), action);
        foreach (bool forward in new[] { false, true })
        foreach (bool debug in new[] { false, true })
        foreach (bool legacy in new[] { false, true })
        foreach (bool saved in new[] { false, true })
        {
            if (saved && !legacy) continue;
#if !NET10_0_OR_GREATER
            if (saved) continue;
#endif
            Assert.Equal("<out><absent/><g k=\"1\" n=\"1\"/><absent/><g k=\"2\" n=\"1\"/></out>", Run(sheet,
                new Sources(new() { ["urn:a"] = "<rows><row k='1'/><row k='2'/></rows>" }), forward, legacy, debug: debug, saved: saved));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MultipleKeysUseLexicographicOrderAndExposeRawComponents(bool forward)
    {
        string source = Source("s", "('urn:a','urn:b')", key: "@k")
            .Replace("</xsl:merge-source>", "<xsl:merge-key select='xs:integer(@n)' order='descending'/></xsl:merge-source>", StringComparison.Ordinal);
        var docs = new Dictionary<string, string> { ["urn:a"] = "<rows><row k='a' n='3'/><row k='a' n='1'/></rows>", ["urn:b"] = "<rows><row k='a' n='2'/><row k='b' n='4'/></rows>" };
        Assert.Equal("<out><g>a 3</g><g>a 2</g><g>a 1</g><g>b 4</g></out>", Run(Sheet(source, "<g><xsl:value-of select='current-merge-key()'/></g>"), new Sources(docs), forward));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MergeActionClearsTheCurrentTemplateRule(bool forward)
    {
        var docs = new Dictionary<string, string> { ["urn:a"] = "<rows><row k='1'/></rows>" };
        Assert.Contains("XTDE0560", Assert.Throws<XsltDynamicException>(() => Run(Sheet(Source("s", "'urn:a'"), "<xsl:next-match/>"), new Sources(docs), forward)).Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NaNDoesNotMakeNonNumericKeysComparable(bool forward)
    {
        string sheet = Sheet(Source("s", "('urn:a','urn:b')", key: "if (@k='1') then xs:double('NaN') else 'text'"), "<g/>");
        var docs = new Dictionary<string, string> { ["urn:a"] = "<rows><row k='1'/></rows>", ["urn:b"] = "<rows><row k='2'/></rows>" };
        Assert.Contains("XTTE2230", Assert.Throws<XsltDynamicException>(() => Run(sheet, new Sources(docs), forward)).Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NumericNaNsCompareEqualAndFollowEmptyKeys(bool forward)
    {
        string sheet = Sheet(Source("s", "'urn:a'", key: "if (@empty) then () else xs:double(@k)"), "<g key='{current-merge-key()}' n='{count(current-merge-group())}'/>");
        var docs = new Dictionary<string, string> { ["urn:a"] = "<rows><row empty='yes'/><row k='NaN'/><row k='NaN'/><row k='1'/></rows>" };
        Assert.Equal("<out><g key=\"\" n=\"1\"/><g key=\"NaN\" n=\"2\"/><g key=\"1\" n=\"1\"/></out>", Run(sheet, new Sources(docs), forward));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrdinaryAnchorAndActionSizesRemainAvailable(bool forward)
    {
        string source = "<xsl:merge-source for-each-item='(10,20)' select='position() + last()'><xsl:merge-key select='.'/></xsl:merge-source>";
        string sheet = Sheet(source, "<g key='{current-merge-key()}' p='{position()+position()}' size='{last()+last()}'/>");
        Assert.Equal("<out><g key=\"3\" p=\"2\" size=\"4\"/><g key=\"4\" p=\"4\" size=\"4\"/></out>", Run(sheet, new Sources(new()), forward));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SourceUrisUseStringFunctionConversion(bool forward)
    {
        string sheet = Sheet(Source("s", "1"), "<g/>");
        Assert.Contains("XPTY0004", Assert.Throws<XsltDynamicException>(() => Run(sheet, new Sources(new()), forward)).Message);
    }

    [Theory]
    [InlineData(false, "case-order=''")]
    [InlineData(true, "case-order=''")]
    [InlineData(false, "data-type=''")]
    [InlineData(true, "data-type=''")]
    public void EmptyExplicitKeywordAttributesAreNotDefaults(bool forward, string attributes)
    {
        var docs = new Dictionary<string, string> { ["urn:a"] = "<rows><row k='1'/></rows>" };
        string sheet = Sheet(Source("s", "'urn:a'", keyAttributes: attributes), "<g/>");
        Assert.Contains("XTDE0030", Assert.Throws<XsltDynamicException>(() => Run(sheet, new Sources(docs), forward)).Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyCollationResolvesAgainstItsBaseUri(bool forward)
    {
        var docs = new Dictionary<string, string> { ["urn:a"] = "<rows><row k='1'/></rows>" };
        string sheet = Sheet(Source("s", "'urn:a'", keyAttributes: "collation='' xml:base='https://example.org/unknown-collation'"), "<g/>");
        Assert.Contains("XTDE1035", Assert.Throws<XsltDynamicException>(() => Run(sheet, new Sources(docs), forward)).Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyCollationCanResolveToARecognizedName(bool forward)
    {
        var docs = new Dictionary<string, string> { ["urn:a"] = "<rows><row k='1'/></rows>" };
        string sheet = Sheet(Source("s", "'urn:a'", keyAttributes: "collation='' xml:base='http://www.w3.org/2005/xpath-functions/collation/codepoint'"), "<g/>");
        Assert.Equal("<out><g/></out>", Run(sheet, new Sources(docs), forward));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitCollationsIgnoreLanguageAndCaseHints(bool forward)
    {
        var docs = new Dictionary<string, string> { ["urn:a"] = "<rows><row k='1'/></rows>" };
        string sheet = Sheet(Source("s", "'urn:a'", keyAttributes: "collation='http://www.w3.org/2005/xpath-functions/collation/codepoint' lang='ignored?!' case-order='ignored?!'"), "<g/>");
        Assert.Equal("<out><g/></out>", Run(sheet, new Sources(docs), forward));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void RetainedSnapshotsAndClosuresSurviveLaterGroups(bool forward, bool debug, bool legacy)
    {
        string sheet = """
            <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:xs="http://www.w3.org/2001/XMLSchema" exclude-result-prefixes="xs">
              <xsl:output omit-xml-declaration="yes"/>
              <xsl:template match="/">
                <xsl:variable name="merged" as="item()*">
                  <xsl:merge>
                    <xsl:merge-source name="copies" streamable="yes" for-each-source="('urn:a','urn:a')" select="rows/row">
                      <xsl:merge-key select="xs:integer(@k)"/>
                    </xsl:merge-source>
                    <xsl:merge-source name="originals" select="doc('urn:b')/rows/row">
                      <xsl:merge-key select="xs:integer(@k)"/>
                    </xsl:merge-source>
                    <xsl:merge-action>
                      <xsl:variable name="nodes" select="current-merge-group()"/>
                      <xsl:sequence select="($nodes, function() { $nodes })"/>
                    </xsl:merge-action>
                  </xsl:merge>
                </xsl:variable>
                <out>
                  <xsl:for-each select="reverse($merged[. instance of function(*)])">
                    <xsl:variable name="nodes" select=".()"/>
                    <g ids="{$nodes ! string(@id)}" fresh="{not($nodes[1] is $nodes[2])}" originals="{$nodes[3] is doc('urn:b')/rows/row[@k = $nodes[3]/@k]}"
                       retained="{every $n in $nodes satisfies some $saved in $merged[. instance of node()] satisfies $n is $saved}"
                       ancestors="{$nodes[1]/../@label}" siblings="{count($nodes[1]/../row)}" children="{$nodes[1]/v}"/>
                  </xsl:for-each>
                </out>
              </xsl:template>
            </xsl:stylesheet>
            """;
        var resolver = new Sources(new()
        {
            ["urn:a"] = "<rows label='copied'><row k='1' id='a'><v>first</v></row><row k='2' id='b'><v>second</v></row></rows>",
            ["urn:b"] = "<rows><row k='1' id='c'/><row k='2' id='d'/></rows>"
        });
        Assert.Equal("<out><g ids=\"b b d\" fresh=\"true\" originals=\"true\" retained=\"true\" ancestors=\"copied\" siblings=\"1\" children=\"second\"/><g ids=\"a a c\" fresh=\"true\" originals=\"true\" retained=\"true\" ancestors=\"copied\" siblings=\"1\" children=\"first\"/></out>",
            Run(sheet, resolver, forward, legacy, debug: debug));
        Assert.All(resolver.Opened, reader => Assert.False(reader.Closed));
    }

#if NET10_0_OR_GREATER
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SavedAssembliesResumeMergeInputs(bool forward, bool debug)
    {
        string sheet = Sheet(Source("s", "('urn:a','urn:b')"), "<g key='{current-merge-key()}'><xsl:value-of select='current-merge-group() ! string(@id)'/></g>");
        var settings = new XsltSettings { StreamedExecution = forward, EnableDocumentFunction = true };
        using var assembly = new MemoryStream();
        new XslCompiledTransform(debug).CompileToAssembly(XmlReader.Create(new StringReader(sheet)), settings, null,
            "Xsl3.Tests.StreamedMerge" + Guid.NewGuid().ToString("N"), "Saved.Merge", assembly);
        var transform = new XslCompiledTransform();
        transform.Load(System.Reflection.Assembly.Load(assembly.ToArray()).GetType("Saved.Merge", true)!, settings);
        var resolver = new Sources(new() { ["urn:a"] = "<rows><row k='1' id='a'/><row k='3' id='c'/></rows>", ["urn:b"] = "<rows><row k='2' id='b'/><row k='3' id='d'/></rows>" }) { SupplyStreams = true };
        using var input = XmlReader.Create(new StringReader("<input/>"));
        using var output = new StringWriter();
        transform.Transform(input, null, output, resolver);
        Assert.Equal("<out><g key=\"1\">a</g><g key=\"2\">b</g><g key=\"3\">c d</g></out>", output.ToString());
        Assert.NotEqual(ReadState.Closed, input.ReadState);
        Assert.All(resolver.Streams, stream => Assert.True(stream.Closed));
    }
#endif

}
