// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Xml;
using System.Xml.Schema;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

public class StreamedForkTests
{
    private static string Sheet(string body, string declarations = "") => $$"""
        <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
          <xsl:mode streamable="yes"/><xsl:output omit-xml-declaration="yes"/>
          {{declarations}}
          <xsl:template match="/">{{body}}</xsl:template>
        </xsl:stylesheet>
        """;

    private static string Run(string body, bool forward = true, bool saved = false, string xml = "<doc><row>one</row><row>two</row></doc>", string declarations = "", bool debug = false)
    {
        var transform = new XslCompiledTransform(enableDebug: debug);
        var settings = new XsltSettings { StreamedExecution = forward, StreamingBufferCapacity = 1 };
        if (saved)
        {
#if NET9_0_OR_GREATER
            using var assembly = new MemoryStream();
            transform.CompileToAssembly(XmlReader.Create(new StringReader(Sheet(body, declarations))), settings, null, "Fork", "Saved.Fork", assembly);
            transform.Load(Assembly.Load(assembly.ToArray()).GetType("Saved.Fork", true)!);
#endif
        }
        else transform.Load(XmlReader.Create(new StringReader(Sheet(body, declarations))), settings, null);
        using var input = XmlReader.Create(new StringReader(xml));
        using var output = new StringWriter();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        transform.Transform(input, new XsltArgumentList { CancellationToken = cancellation.Token }, output);
        return output.ToString();
    }

    [Fact]
    public void ProngsConsumeTheSameForwardInputAndConcatenateTheirResults()
    {
        const string body = "<out><xsl:fork><xsl:sequence><a><xsl:value-of select='count(doc/row)'/></a></xsl:sequence><xsl:sequence><b><xsl:value-of select='count(doc/row)'/></b></xsl:sequence></xsl:fork></out>";
        Assert.Equal(Run(body, false), Run(body));
        Assert.Equal("<out><a>2</a><b>2</b></out>", Run(body));
    }

    [Theory]
    [InlineData("<xsl:sequence select='copy-of(doc/row)'/><xsl:sequence select='count(doc/row)'/>", "<out><row>one</row><row>two</row>2</out>")]
    [InlineData("<xsl:for-each-group select='doc/row' group-adjacent='position()'><g><xsl:sequence select='current-group()'/></g></xsl:for-each-group>", "<out><g><row>one</row></g><g><row>two</row></g></out>")]
    public void DebugEndTagDoesNotBecomeAForkProng(string prongs, string expected)
    {
        string body = "<out><xsl:fork>" + prongs + "</xsl:fork></out>";
        Assert.Equal(expected, Run(body, forward: false, debug: true));
        Assert.Equal(expected, Run(body, debug: true));
#if NET9_0_OR_GREATER
        Assert.Equal(expected, Run(body, saved: true, debug: true));
#endif
    }

    [Theory]
    [InlineData(false)]
#if NET9_0_OR_GREATER
    [InlineData(true)]
#endif
    public void ForkGroupByBuffersItsPopulationBeforeSortingAndMemberAccess(bool saved)
    {
        const string xml = "<doc><row k='b'>one</row><row k='a'>two</row><row k='b'>three</row></doc>";
        const string body = "<out><xsl:fork><xsl:for-each-group select='doc/row' group-by='@k'><xsl:sort select='current-grouping-key()'/><g key='{current-grouping-key()}'><xsl:copy-of select='current-group()'/></g></xsl:for-each-group></xsl:fork></out>";
        string expected = Run(body, false, xml: xml);
        Assert.Contains("<g key=\"a\"><row k=\"a\">two</row></g>", expected);
        Assert.Equal(expected, Run(body, saved: saved, xml: xml));
    }

    [Theory]
    [InlineData(false)]
#if NET9_0_OR_GREATER
    [InlineData(true)]
#endif
    public void ForkSortingByGroupPositionAllowsOneDownwardMemberSelection(bool saved)
    {
        // XSLT 3.0 §19.8.4.19 exempts a direct fork child from the sort prohibition.
        const string xml = "<doc><row k='b'><n>one</n></row><row k='a'><n>two</n></row><row k='b'><n>three</n></row></doc>";
        const string body = "<out><xsl:fork><xsl:for-each-group select='doc/row' group-by='@k'><xsl:sort select='position()'/><in><xsl:sequence select='current-group()/n'/></in></xsl:for-each-group></xsl:fork></out>";
        const string expected = "<out><in><n>one</n><n>three</n></in><in><n>two</n></in></out>";
        Assert.Equal(expected, Run(body, false, xml: xml));
        Assert.Equal(expected, Run(body, saved: saved, xml: xml));
    }

    [Theory]
    [InlineData(false)]
#if NET9_0_OR_GREATER
    [InlineData(true)]
#endif
    public void ForkCapturesForwardGroupsBeforeStartingProngs(bool saved)
    {
        const string xml = "<doc><row k='a'><n>one</n></row><row k='a'><n>two</n></row><row k='b'><n>three</n></row></doc>";
        const string body = "<out><xsl:for-each-group select='doc/row' group-adjacent='@k'><xsl:fork><xsl:sequence><a key='{current-grouping-key()}'><xsl:copy-of select='current-group()'/></a></xsl:sequence><xsl:sequence><b key='{current-grouping-key()}'><xsl:value-of select='current-group()/n'/></b></xsl:sequence></xsl:fork></xsl:for-each-group></out>";
        string expected = Run(body, false, xml: xml);
        Assert.Contains("<b key=\"a\">one two</b>", expected);
        Assert.Equal(expected, Run(body, saved: saved, xml: xml));
    }

    [Theory]
    [InlineData(false)]
#if NET9_0_OR_GREATER
    [InlineData(true)]
#endif
    public void ForkInsideAFunctionUsesItsStreamedOperandWithAbsentContext(bool saved)
    {
        const string xml = "<doc><row k='b'>one</row><row k='a'>two</row><row k='b'>three</row></doc>";
        const string declaration = "<xsl:function name='f:group' xmlns:f='urn:test' exclude-result-prefixes='f' streamability='absorbing'><xsl:param name='input' as='element()'/><xsl:fork><xsl:for-each-group select='$input/row' group-by='@k'><g key='{current-grouping-key()}'><xsl:copy-of select='current-group()'/></g></xsl:for-each-group></xsl:fork></xsl:function>";
        const string body = "<out><xsl:sequence select='f:group(doc)' xmlns:f='urn:test'/></out>";
        string expected = Run(body, false, xml: xml, declarations: declaration);
        Assert.Contains("<g key=\"b\"><row k=\"b\">one</row><row k=\"b\">three</row></g>", expected);
        Assert.Equal(expected, Run(body, saved: saved, xml: xml, declarations: declaration));
    }

    [Fact]
    public void MultipleFunctionProngsShareTheCapturedForwardOperand()
    {
        const string declaration = "<xsl:function name='f:fork' xmlns:f='urn:test' exclude-result-prefixes='f' streamability='absorbing'><xsl:param name='input' as='element()'/><xsl:fork><xsl:sequence><a><xsl:value-of select='count($input/row)'/></a></xsl:sequence><xsl:sequence><b><xsl:value-of select='count($input/row)'/></b></xsl:sequence></xsl:fork></xsl:function>";
        const string body = "<out><xsl:sequence select='f:fork(doc)' xmlns:f='urn:test'/></out>";
        Assert.Equal("<out><a>2</a><b>2</b></out>", Run(body, declarations: declaration));
    }

    [Theory]
    [InlineData(false)]
#if NET9_0_OR_GREATER
    [InlineData(true)]
#endif
    public void MixedGroundedAndStreamedGroupMembersUseTheSameDocumentOrder(bool saved)
    {
        const string declaration = "<xsl:variable name='extra' as='element(row)'><row k='a'><n>extra</n></row></xsl:variable>";
        const string xml = "<doc><row k='a'><n>source</n></row></doc>";
        const string body = "<out><xsl:fork><xsl:for-each-group select='($extra, doc/row)' group-by='@k'><g><xsl:value-of select='current-group()/n' separator='|'/></g></xsl:for-each-group></xsl:fork></out>";
        Assert.Equal("<out><g>source|extra</g></out>", Run(body, false, xml: xml, declarations: declaration));
        Assert.Equal("<out><g>source|extra</g></out>", Run(body, saved: saved, xml: xml, declarations: declaration));
    }

    [Fact]
    public void ForkInsideAVariableReturnsItsItems()
    {
        const string body = "<xsl:variable name='v'><xsl:fork><xsl:sequence><a><xsl:value-of select='count(doc/row)'/></a></xsl:sequence><xsl:sequence><b><xsl:value-of select='count(doc/row)'/></b></xsl:sequence></xsl:fork></xsl:variable><out><xsl:sequence select='$v'/></out>";
        Assert.Equal(Run(body, false), Run(body));
    }

    [Fact]
    public void AtomicProngResultsKeepTheirSequenceSpacing()
    {
        const string body = "<out><xsl:fork><xsl:sequence select='count(doc/row)'/><xsl:sequence select='count(doc/row)'/></xsl:fork></out>";
        Assert.Equal(Run(body, false), Run(body));
        Assert.Equal("<out>2 2</out>", Run(body));
    }

    private sealed class ForwardWriter(XmlReader input, TextWriter text) : XmlTextWriter(text)
    {
        private readonly int _thread = Environment.CurrentManagedThreadId;
        internal bool SawFirstProng;
        public override void WriteStartElement(string? prefix, string localName, string? ns)
        {
            Assert.Equal(_thread, Environment.CurrentManagedThreadId);
            if (localName == "a") { Assert.False(input.EOF); SawFirstProng = true; }
            base.WriteStartElement(prefix, localName, ns);
        }
        public override void WriteString(string? text)
        {
            Assert.Equal(_thread, Environment.CurrentManagedThreadId);
            base.WriteString(text);
        }
    }

    [Fact]
    public void FirstProngStartsBeforeTheInputEndsOnTheInvokingThread()
    {
        string sheet = Sheet("<out><xsl:fork><xsl:sequence><a><xsl:value-of select='count(doc/row)'/></a></xsl:sequence><xsl:sequence><b><xsl:value-of select='count(doc/row)'/></b></xsl:sequence></xsl:fork></out>");
        var transformer = new XsltCompiler { Settings = new() { StreamedExecution = true, StreamingBufferCapacity = 1 } }
            .Compile(new StringReader(sheet)).CreateTransformer();
        using var input = XmlReader.Create(new StringReader("<doc><row/><row/></doc>"));
        using var text = new StringWriter();
        using var output = new ForwardWriter(input, text);
        transformer.ApplyTemplates(input, output);
        Assert.True(output.SawFirstProng);
        Assert.Equal("<out><a>2</a><b>2</b></out>", text.ToString());
    }

    [Theory]
    [InlineData(false)]
#if NET9_0_OR_GREATER
    [InlineData(true)]
#endif
    public void NestedForksAndSavedAssembliesUseTheSameRuntime(bool saved)
    {
        const string body = "<out><xsl:fork><xsl:sequence><a><xsl:fork><xsl:sequence><b><xsl:value-of select='count(doc/row)'/></b></xsl:sequence><xsl:sequence><c><xsl:value-of select='count(doc/row)'/></c></xsl:sequence></xsl:fork></a></xsl:sequence><xsl:sequence><d><xsl:value-of select='count(doc/row)'/></d></xsl:sequence></xsl:fork></out>";
        Assert.Equal(Run(body, false), Run(body, saved: saved));
    }

    [Fact]
    public void ProngsKeepTheEnclosingPositionAndHeaderFocus()
    {
        const string body = "<out><xsl:for-each select='doc/row'><xsl:fork><xsl:sequence><a p='{position()}'><xsl:value-of select='name()'/></a></xsl:sequence><xsl:sequence><b p='{position()}'><xsl:value-of select='name()'/></b></xsl:sequence></xsl:fork></xsl:for-each></out>";
        Assert.Equal(Run(body, false), Run(body));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SourceNodesAreConsumedOnTheirProngs(bool first)
    {
        string body = first
            ? "<out><xsl:fork><xsl:sequence select='doc'/><xsl:sequence><last/></xsl:sequence></xsl:fork></out>"
            : "<out><xsl:fork><xsl:sequence><first/></xsl:sequence><xsl:sequence select='doc'/></xsl:fork></out>";
        Assert.Equal(Run(body, false), Run(body));
    }

    [Fact]
    public void AtomicFocusDoesNotRequireAnInputTee()
    {
        const string body = "<out><xsl:for-each select='(1,2,3)'><xsl:fork><xsl:sequence><a><xsl:value-of select='.'/></a></xsl:sequence><xsl:sequence><b><xsl:value-of select='.'/></b></xsl:sequence></xsl:fork></xsl:for-each></out>";
        Assert.Equal(Run(body, false), Run(body));
    }

    [Fact]
    public void TextFocusUsesItsFrozenValueWithoutASecondReader()
    {
        const string body = "<out><xsl:for-each select='doc/row/text()'><xsl:fork><xsl:sequence><a><xsl:value-of select='.'/></a></xsl:sequence><xsl:sequence><b><xsl:value-of select='.'/></b></xsl:sequence></xsl:fork></xsl:for-each></out>";
        Assert.Equal(Run(body, false), Run(body));
    }

    [Fact]
    public void BufferCapacityIsPositiveAndCopiedWithSettings()
    {
        var settings = new XsltSettings();
        Assert.Equal(64, settings.StreamingBufferCapacity);
        Assert.Throws<ArgumentOutOfRangeException>(() => settings.StreamingBufferCapacity = 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => settings.StreamingBufferCapacity = -1);
        settings.StreamingBufferCapacity = 1;
        Assert.Equal(1, settings.Clone().StreamingBufferCapacity);
    }

    [Fact]
    public void ProngErrorsReturnToTheContainingTry()
    {
        const string body = "<out><xsl:try><xsl:fork><xsl:sequence select=\"error(QName('urn:test', 'first'))\"/><xsl:sequence select=\"error(QName('urn:test', 'second'))\"/></xsl:fork><xsl:catch><caught><xsl:value-of select='$err:code' xmlns:err='http://www.w3.org/2005/xqt-errors'/></caught></xsl:catch></xsl:try></out>";
        Assert.Equal(Run(body, false), Run(body));
    }

    [Fact]
    public void ExplicitTreeInputUsesIsolatedGroundedProngs()
    {
        string sheet = Sheet("<out><xsl:fork><xsl:sequence><a><xsl:value-of select='count(doc/row)'/></a></xsl:sequence><xsl:sequence><b><xsl:value-of select='count(doc/row)'/></b></xsl:sequence></xsl:fork></out>");
        var transformer = new XsltCompiler { Settings = new() { StreamedExecution = true, StreamingBufferCapacity = 1 } }
            .Compile(new StringReader(sheet)).CreateTransformer();
        using var output = new StringWriter();
        transformer.ApplyTemplates(XdmNode.Load(new StringReader("<doc><row/><row/></doc>")), output);
        Assert.Equal("<out><a>2</a><b>2</b></out>", output.ToString());
    }

    [Fact]
    public void ConstantProngsDoNotRequireAnInitialContextItem()
    {
        const string sheet = "<xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform'><xsl:mode streamable='yes'/><xsl:template name='go'><xsl:fork><xsl:sequence select='1'/><xsl:sequence select='2'/></xsl:fork></xsl:template></xsl:stylesheet>";
        var transformer = new XsltCompiler { Settings = new() { StreamedExecution = true } }.Compile(new StringReader(sheet)).CreateTransformer();
        Assert.Equal("1 2", transformer.CallTemplate(new QName("", "go")).ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TypedOutputAnnotationsSurviveBothProngSinks(bool forward)
    {
        string sheet = Sheet("<xsl:fork><xsl:sequence><row xsl:type='xs:integer'><xsl:value-of select='count(doc/row)'/></row></xsl:sequence><xsl:sequence><row xsl:type='xs:integer'><xsl:value-of select='count(doc/row)'/></row></xsl:sequence></xsl:fork>");
        sheet = sheet.Replace("version=\"3.0\"", "version=\"3.0\" xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" exclude-result-prefixes=\"xs\" default-validation=\"preserve\"", StringComparison.Ordinal);
        var transformer = new XsltCompiler { Settings = new() { StreamedExecution = forward, SchemaAware = true, StreamingBufferCapacity = 1 } }
            .Compile(new StringReader(sheet)).CreateTransformer();
        using var input = XmlReader.Create(new StringReader("<doc><row/></doc>"));
        XdmValue result = transformer.ApplyTemplates(input);
        Assert.Equal(2, result.Count);
        foreach (XdmItem item in result)
        {
            var node = Assert.IsType<XdmNode>(item).Navigator;
            Assert.Equal("row", node.LocalName);
            Assert.NotNull(node.SchemaInfo?.SchemaType);
            Assert.Equal(XmlTypeCode.Integer, node.SchemaInfo.SchemaType.TypeCode);
            Assert.Equal(1, node.ValueAsInt);
        }
    }

    [Fact]
    public void ProngsWriteIndependentSecondaryResultDocuments()
    {
        string sheet = Sheet("<out><xsl:fork><xsl:sequence><xsl:result-document href='urn:first'><a><xsl:value-of select='count(doc/row)'/></a></xsl:result-document></xsl:sequence><xsl:sequence><xsl:result-document href='urn:second'><b><xsl:value-of select='count(doc/row)'/></b></xsl:result-document></xsl:sequence></xsl:fork></out>");
        var transformer = new XsltCompiler { Settings = new() { StreamedExecution = true, StreamingBufferCapacity = 1 } }
            .Compile(new StringReader(sheet)).CreateTransformer();
        var outputs = new ConcurrentDictionary<string, StringWriter>();
        int callerThread = Environment.CurrentManagedThreadId;
        transformer.ResultDocumentHandler = uri => {
            Assert.Equal(callerThread, Environment.CurrentManagedThreadId);
            var output = new StringWriter();
            Assert.True(outputs.TryAdd(uri.ToString(), output));
            return output;
        };
        using var input = XmlReader.Create(new StringReader("<doc><row/><row/></doc>"));
        using var result = new StringWriter();
        transformer.ApplyTemplates(input, result);
        Assert.Equal("<out/>", result.ToString());
        Assert.Equal("<a>2</a>", outputs["urn:first"].ToString());
        Assert.Equal("<b>2</b>", outputs["urn:second"].ToString());
    }

    [Fact]
    public void PrincipalDocumentAfterImplicitOutputRaisesTheOrdinaryConflict()
    {
        const string body = "<out><xsl:try><xsl:fork><xsl:sequence><xsl:result-document><a/></xsl:result-document></xsl:sequence><xsl:sequence select='()'/></xsl:fork><xsl:catch errors='e:XTDE1490' xmlns:e='http://www.w3.org/2005/xqt-errors'><caught xsl:exclude-result-prefixes='e'/></xsl:catch></xsl:try></out>";
        Assert.Equal("<out><caught/></out>", Run(body));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
#if NET9_0_OR_GREATER
    [InlineData(false, true)]
    [InlineData(true, true)]
#endif
    public void APrincipalResultDocumentInEitherProngConfiguresTheInvokingWriter(bool later, bool saved)
    {
        const string prong = "<xsl:sequence><xsl:result-document omit-xml-declaration='yes' doctype-system='out.dtd'><out n='{count(doc/row)}'/></xsl:result-document></xsl:sequence>";
        const string empty = "<xsl:sequence select='()'/>";
        string body = "<xsl:fork>" + (later ? empty + prong : prong + empty) + "</xsl:fork>";
        Assert.Equal("<!DOCTYPE out SYSTEM \"out.dtd\"><out n=\"2\"/>", Run(body, false));
        Assert.Equal(Run(body, false), Run(body, saved: saved));
    }

    [Theory]
    [InlineData("json", "<xsl:sequence select=\"map{'count':count(doc/row)}\"/>", "{\"count\":2}")]
    [InlineData("xml", "<xsl:sequence select='doc/row'/>", "<row>one</row>|<row>two</row>")]
    public void PrincipalRawItemsAreBufferedBeforeTheSourceAdvances(string method, string content, string expected)
    {
        string separator = method == "xml" ? " item-separator='|'" : "";
        string body = $"<xsl:fork><xsl:sequence select='()'/><xsl:sequence><xsl:result-document method='{method}' omit-xml-declaration='yes'{separator}>{content}</xsl:result-document></xsl:sequence></xsl:fork>";
        Assert.Equal(expected, Run(body, false));
        Assert.Equal(expected, Run(body));
    }

    [Fact]
    public void NestedPrincipalDocumentsRelayToTheOriginalInvokingWriter()
    {
        const string body = "<xsl:fork><xsl:sequence select='()'/><xsl:sequence><xsl:fork><xsl:sequence select='()'/><xsl:sequence><xsl:result-document omit-xml-declaration='yes'><out n='{count(doc/row)}'/></xsl:result-document></xsl:sequence></xsl:fork></xsl:sequence></xsl:fork>";
        Assert.Equal("<out n=\"2\"/>", Run(body));
    }

    [Fact]
    public void DuplicatePrincipalClaimsRaiseTheOrdinaryError()
    {
        const string body = "<xsl:fork><xsl:sequence><xsl:result-document><one/></xsl:result-document></xsl:sequence><xsl:sequence><xsl:result-document><two/></xsl:result-document></xsl:sequence></xsl:fork>";
        Assert.Contains("XTDE1490", Assert.ThrowsAny<Exception>(() => Run(body)).ToString());
    }

    [Theory]
    [InlineData("count#1")]
    [InlineData("function($x){$x}")]
    public void AdaptivePrincipalSerializationDescribesFunctionsWithoutInvokingTheirCaptures(string value)
    {
        string body = $"<xsl:fork><xsl:sequence select='()'/><xsl:sequence><xsl:result-document method='adaptive'><xsl:sequence select='{value}'/></xsl:result-document></xsl:sequence></xsl:fork>";
        Assert.Equal(Run(body, false), Run(body));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RawResultApiReceivesPrincipalDocumentContentsInProngOrder(bool forward)
    {
        string sheet = Sheet("<xsl:fork><xsl:sequence><xsl:result-document><a><xsl:value-of select='count(doc/row)'/></a></xsl:result-document></xsl:sequence><xsl:sequence><xsl:result-document><b><xsl:value-of select='count(doc/row)'/></b></xsl:result-document></xsl:sequence></xsl:fork>");
        var transformer = new XsltCompiler { Settings = new() { StreamedExecution = forward, StreamingBufferCapacity = 1 } }
            .Compile(new StringReader(sheet)).CreateTransformer();
        using var input = XmlReader.Create(new StringReader("<doc><row/><row/></doc>"));
        XdmValue result = transformer.ApplyTemplates(input);
        Assert.Equal(2, result.Count);
        Assert.Equal("a", Assert.IsType<XdmNode>(result[0]).Navigator.LocalName);
        Assert.Equal("b", Assert.IsType<XdmNode>(result[1]).Navigator.LocalName);
        foreach (XdmItem item in result) Assert.Equal("2", item.StringValue);
        Assert.NotEqual(ReadState.Closed, input.ReadState);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TypedPrincipalContentsRetainAnnotationsInTheRawResultApi(bool forward)
    {
        string sheet = Sheet("<xsl:fork><xsl:sequence select='()'/><xsl:sequence><xsl:result-document><row xsl:type='xs:integer'><xsl:value-of select='count(doc/row)'/></row></xsl:result-document></xsl:sequence></xsl:fork>");
        sheet = sheet.Replace("version=\"3.0\"", "version=\"3.0\" xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" exclude-result-prefixes=\"xs\" default-validation=\"preserve\"", StringComparison.Ordinal);
        var transformer = new XsltCompiler { Settings = new() { StreamedExecution = forward, SchemaAware = true, StreamingBufferCapacity = 1 } }
            .Compile(new StringReader(sheet)).CreateTransformer();
        using var input = XmlReader.Create(new StringReader("<doc><row/></doc>"));
        var node = Assert.IsType<XdmNode>(Assert.Single(transformer.ApplyTemplates(input))).Navigator;
        Assert.Equal("row", node.LocalName);
        Assert.Equal(XmlTypeCode.Integer, node.SchemaInfo!.SchemaType!.TypeCode);
        Assert.Equal(1, node.ValueAsInt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ValidatedItemSeparatedPrincipalContentUsesBufferedSourcePositions(bool forward)
    {
        string sheet = Sheet("<xsl:fork><xsl:sequence select='()'/><xsl:sequence><xsl:result-document type='xs:integer' item-separator='|'><xsl:sequence select='head(doc/row)'/></xsl:result-document></xsl:sequence></xsl:fork>");
        sheet = sheet.Replace("version=\"3.0\"", "version=\"3.0\" xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" exclude-result-prefixes=\"xs\"", StringComparison.Ordinal);
        var transformer = new XsltCompiler { Settings = new() { StreamedExecution = forward, SchemaAware = true, StreamingBufferCapacity = 1 } }
            .Compile(new StringReader(sheet)).CreateTransformer();
        using var input = XmlReader.Create(new StringReader("<doc><row>1</row><row>2</row></doc>"));
        var node = Assert.IsType<XdmNode>(Assert.Single(transformer.ApplyTemplates(input))).Navigator;
        Assert.Equal("row", node.LocalName);
        Assert.Equal(XmlTypeCode.Integer, node.SchemaInfo!.SchemaType!.TypeCode);
        Assert.Equal(1, node.ValueAsInt);
    }

#if NET9_0_OR_GREATER
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavedPrincipalValidationRetainsTheEmptyBuiltInSchemaSet(bool forward)
    {
        string sheet = Sheet("<xsl:fork><xsl:sequence select='()'/><xsl:sequence><xsl:result-document type='xs:integer' item-separator='|'><xsl:sequence select='head(doc/row)'/></xsl:result-document></xsl:sequence></xsl:fork>");
        sheet = sheet.Replace("version=\"3.0\"", "version=\"3.0\" xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" exclude-result-prefixes=\"xs\"", StringComparison.Ordinal);
        var transform = new XslCompiledTransform();
        using var assembly = new MemoryStream();
        transform.CompileToAssembly(XmlReader.Create(new StringReader(sheet)),
            new XsltSettings { StreamedExecution = forward, SchemaAware = true, StreamingBufferCapacity = 1 },
            null, "PrincipalValidation", "Saved.PrincipalValidation", assembly);
        transform.Load(Assembly.Load(assembly.ToArray()).GetType("Saved.PrincipalValidation", true)!);
        using var input = XmlReader.Create(new StringReader("<doc><row>1</row><row>2</row></doc>"));
        using var result = new StringWriter();
        transform.Transform(input, null, result);
        Assert.Equal("<row>1</row>", result.ToString());
    }
#endif

    [Fact]
    public void SuppliedPrincipalWriterRunsOnTheOriginalThreadAndRemainsBorrowed()
    {
        string sheet = Sheet("<xsl:fork><xsl:sequence select='()'/><xsl:sequence><xsl:result-document><out n='{count(doc/row)}'/></xsl:result-document></xsl:sequence></xsl:fork>");
        var transformer = new XsltCompiler { Settings = new() { StreamedExecution = true, StreamingBufferCapacity = 1 } }
            .Compile(new StringReader(sheet)).CreateTransformer();
        using var input = XmlReader.Create(new StringReader("<doc><row/><row/></doc>"));
        using var text = new StringWriter();
        using var writer = new ForwardWriter(input, text);
        transformer.ApplyTemplates(input, writer);
        Assert.Contains("n=\"2\"", text.ToString());
        Assert.NotEqual(WriteState.Closed, writer.WriteState);
        Assert.NotEqual(ReadState.Closed, input.ReadState);
    }

    private sealed class FailingPrincipalWriter(TextWriter output) : XmlTextWriter(output)
    {
        private readonly int _thread = Environment.CurrentManagedThreadId;
        public override void WriteStartElement(string? prefix, string localName, string? ns)
        {
            Assert.Equal(_thread, Environment.CurrentManagedThreadId);
            if (localName == "fail") throw new XdmException("TEST0001", "Host principal writer failed") { Name = new QName("urn:test", "TEST0001") };
            base.WriteStartElement(prefix, localName, ns);
        }
    }

    [Fact]
    public void PrincipalWriterFailuresReturnToTheProngsTry()
    {
        string sheet = Sheet("<xsl:fork><xsl:sequence><xsl:try><xsl:result-document><fail/></xsl:result-document><xsl:catch errors='e:TEST0001' xmlns:e='urn:test'><recovered xsl:exclude-result-prefixes='e'/></xsl:catch></xsl:try></xsl:sequence><xsl:sequence select='()'/></xsl:fork>");
        var transformer = new XsltCompiler { Settings = new() { StreamedExecution = true, StreamingBufferCapacity = 1 } }
            .Compile(new StringReader(sheet)).CreateTransformer();
        using var input = XmlReader.Create(new StringReader("<doc/>"));
        using var text = new StringWriter();
        using var writer = new FailingPrincipalWriter(text);
        transformer.ApplyTemplates(input, writer);
        Assert.Contains("recovered", text.ToString());
    }

    [Fact]
    public void EarlyPrincipalRawOutputDoesNotBlockTheOtherProngsRemainingInput()
    {
        const int rows = 200;
        string xml = "<doc>" + string.Concat(System.Linq.Enumerable.Repeat("<row>value</row>", rows)) + "</doc>";
        string body = $"<xsl:fork><xsl:sequence select='if (count(doc/row) = {rows}) then () else error()'/><xsl:sequence><xsl:result-document item-separator='|' omit-xml-declaration='yes'><xsl:sequence select='head(doc/row)'/></xsl:result-document></xsl:sequence></xsl:fork>";
        Assert.Equal("<row>value</row>", Run(body, xml: xml));
    }

    private sealed class CancellingPrincipalWriter(TextWriter output, CancellationTokenSource cancellation) : XmlTextWriter(output)
    {
        internal bool CancelledInCallback;
        public override void WriteStartElement(string? prefix, string localName, string? ns)
        {
            Assert.True(Xsl3.Runtime.XmlQueryRuntime.Cancellation.CanBeCanceled);
            Assert.NotEqual(cancellation.Token, Xsl3.Runtime.XmlQueryRuntime.Cancellation);
            cancellation.Cancel();
            CancelledInCallback = true;
            Assert.True(Xsl3.Runtime.XmlQueryRuntime.Cancellation.IsCancellationRequested);
            base.WriteStartElement(prefix, localName, ns);
        }
    }

    [Fact]
    public void PrincipalCallbacksObserveProngCancellationAndRestoreCallerState()
    {
        string sheet = Sheet("<xsl:fork><xsl:sequence select='()'/><xsl:sequence><xsl:result-document><out n='{count(doc/row)}'/></xsl:result-document></xsl:sequence></xsl:fork>");
        var transform = new XslCompiledTransform();
        transform.Load(XmlReader.Create(new StringReader(sheet)), new XsltSettings { StreamedExecution = true, StreamingBufferCapacity = 1 }, null);
        using var input = XmlReader.Create(new StringReader("<doc><row/></doc>"));
        using var text = new StringWriter();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var writer = new CancellingPrincipalWriter(text, cancellation);
        CancellationToken saved = Xsl3.Runtime.XmlQueryRuntime.Cancellation;
        var error = Assert.Throws<XslTransformException>(() => transform.Transform(input, new XsltArgumentList { CancellationToken = cancellation.Token }, writer));
        Assert.IsAssignableFrom<OperationCanceledException>(error.InnerException);
        Assert.Contains("Q{http://xsl3/err}cancelled", error.Message);
        Assert.True(writer.CancelledInCallback);
        Assert.Equal(saved, Xsl3.Runtime.XmlQueryRuntime.Cancellation);
    }
}
