// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Xml;
using Xsl3.Streaming;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

public class StreamedInitialModeTests
{
    private const string Input = "<doc><record>1</record><record>2</record><record>3</record></doc>";
    private const string Templates = "<xsl:template match='/'><xsl:apply-templates select='doc/record'/></xsl:template>"
        + "<xsl:template match='record'><xsl:value-of select='.'/></xsl:template>";

    private static string Sheet(string templates = Templates, string declarations = "", string mode = "", bool streamable = true) =>
        "<xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform' xmlns:xs='http://www.w3.org/2001/XMLSchema' "
        + (mode.Length == 0 ? "" : $"default-mode='{mode}' ") + ">"
        + $"<xsl:mode {(mode.Length == 0 ? "" : $"name='{mode}' ")}streamable='{(streamable ? "yes" : "no")}'/>"
        + "<xsl:output method='text'/>" + declarations + templates + "</xsl:stylesheet>";

    private static Xslt30Transformer Transformer(string sheet, bool forward = true) =>
        new XsltCompiler { Settings = new() { StreamedExecution = forward } }.Compile(new StringReader(sheet)).CreateTransformer();

    [Theory]
    [InlineData("default", true, true)]
    [InlineData("default", true, false)]
    [InlineData("default", false, true)]
    [InlineData("default", false, false)]
    [InlineData("strict", true, true)]
    [InlineData("strict", true, false)]
    [InlineData("strict", false, true)]
    [InlineData("strict", false, false)]
    [InlineData("fallback", true, true)]
    [InlineData("fallback", true, false)]
    [InlineData("fallback", false, true)]
    [InlineData("fallback", false, false)]
    public void PublicStaticPolicyKeepsForwardExecutionOptIn(string policy, bool streamable, bool legacy)
    {
        var settings = new XsltSettings();
        if (policy != "default") settings.Streamability = policy == "strict"
            ? XsltStreamabilityMode.Strict : XsltStreamabilityMode.Fallback;
        string sheet = Sheet(declarations: "<xsl:strip-space elements='*'/>", streamable: streamable);
        using var input = XmlReader.Create(new StringReader(Input));
        using var text = new StringWriter();
        bool? firstWriteBeforeEof = null;
        using var writer = new ObservingWriter(text, () => firstWriteBeforeEof ??= !input.EOF);
        using var capture = new StreamingExecutionCapture();
        using var stage = StreamingExecutionCapture.ObserveStage(StreamingExecutionCapture.Stage.Stylesheet);
        if (legacy)
        {
            var transform = new XslCompiledTransform();
            using var style = XmlReader.Create(new StringReader(sheet));
            transform.Load(style, settings, null);
            // A loaded unit owns its settings; later caller mutations cannot change its entry path.
            settings.Streamability = policy == "fallback" ? XsltStreamabilityMode.Strict : XsltStreamabilityMode.Fallback;
            transform.Transform(input, null, writer);
        }
        else
        {
            var executable = new XsltCompiler { Settings = settings }.Compile(new StringReader(sheet));
            settings.Streamability = policy == "fallback" ? XsltStreamabilityMode.Strict : XsltStreamabilityMode.Fallback;
            executable.CreateTransformer().ApplyTemplates(input, writer);
        }
        Assert.Equal("123", text.ToString());
        bool forward = streamable && Environment.GetEnvironmentVariable("XSL3_STREAM_STRICT") == "1";
        Assert.Equal(forward, firstWriteBeforeEof);
        AssertCapturedSource(capture, forward);
        Assert.NotEqual(ReadState.Closed, input.ReadState);
    }

    private static void AssertCapturedSource(StreamingExecutionCapture capture, bool forward)
    {
        var execution = Assert.Single(capture.Snapshot().Executions, e => e.Stage == "Stylesheet");
        Assert.True(execution.BodyCompleted);
        var source = Assert.Single(execution.Sources);
        Assert.Equal("Opened", source.Status);
        Assert.Equal(forward, source.ActualForward);
        Assert.Equal(forward, source.NavigatorType == typeof(StreamedNavigator).FullName);
    }

    [Theory]
    [InlineData("position()")]
    [InlineData("last()")]
    public void NumericGlobalFocusCannotUseTheInitialSelection(string select)
    {
        string sheet = Sheet("<xsl:template match='/'><xsl:value-of select='$g'/></xsl:template>", $"<xsl:variable name='g' select='{select}'/>");
        foreach (bool forward in new[] { false, true })
        {
            var transform = Transformer(sheet, forward);
            using var input = XmlReader.Create(new StringReader(Input));
            Assert.Contains("XPDY0002", Assert.Throws<XsltDynamicException>(() => transform.ApplyTemplates(input)).Message);

            transform.GlobalContextItem = XdmNode.Load(new StringReader("<global/>"));
            using var supplied = XmlReader.Create(new StringReader(Input));
            Assert.Equal("1", transform.ApplyTemplates(supplied).ToString());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReaderOutputIsDeliveredDuringTheForwardPass(bool legacy)
    {
        string sheet = Sheet(declarations: "<xsl:strip-space elements='*'/>");
        using var reader = XmlReader.Create(new StringReader(Input));
        using var text = new StringWriter();
        bool observed = false;
        using var writer = new ObservingWriter(text, () => { Assert.False(reader.EOF); observed = true; });
        if (legacy)
        {
            var transform = new XslCompiledTransform();
            transform.Load(XmlReader.Create(new StringReader(sheet)), new() { StreamedExecution = true }, null);
            transform.Transform(reader, null, writer);
        }
        else Transformer(sheet).ApplyTemplates(reader, writer);
        Assert.True(observed);
        Assert.Equal("123", text.ToString());
        Assert.NotEqual(ReadState.Closed, reader.ReadState);
    }

    [Theory]
    [InlineData("reader", "raw")]
    [InlineData("reader", "stream")]
    [InlineData("reader", "text")]
    [InlineData("reader", "xml")]
    [InlineData("stream", "raw")]
    [InlineData("stream", "stream")]
    [InlineData("stream", "text")]
    [InlineData("stream", "xml")]
    [InlineData("uri", "raw")]
    [InlineData("uri", "stream")]
    [InlineData("uri", "text")]
    [InlineData("uri", "xml")]
    public void AllSourceAndDestinationOverloadsShareTheEntryContract(string source, string destination)
    {
        var transform = Transformer(Sheet());
        var resolver = new InputResolver(Input);
        transform.DocumentResolver = resolver;
        using var input = new TrackedStream(Input);
        using var reader = XmlReader.Create(new StringReader(Input));
        using var output = new MemoryStream();
        using var text = new StringWriter();
        using var writer = new XmlTextWriter(text);
        var uri = new Uri("urn:initial-input");
        string? raw = null;
        switch (source, destination)
        {
            case ("reader", "raw"): raw = string.Concat(transform.ApplyTemplates(reader)); break;
            case ("reader", "stream"): transform.ApplyTemplates(reader, output); break;
            case ("reader", "text"): transform.ApplyTemplates(reader, text); break;
            case ("reader", "xml"): transform.ApplyTemplates(reader, writer); break;
            case ("stream", "raw"): raw = string.Concat(transform.ApplyTemplates(input, uri)); break;
            case ("stream", "stream"): transform.ApplyTemplates(input, uri, output); break;
            case ("stream", "text"): transform.ApplyTemplates(input, uri, text); break;
            case ("stream", "xml"): transform.ApplyTemplates(input, uri, writer); break;
            case ("uri", "raw"): raw = string.Concat(transform.ApplyTemplates(uri)); break;
            case ("uri", "stream"): transform.ApplyTemplates(uri, output); break;
            case ("uri", "text"): transform.ApplyTemplates(uri, text); break;
            case ("uri", "xml"): transform.ApplyTemplates(uri, writer); break;
        }
        writer.Flush();
        string actual = raw ?? (destination == "stream" ? Encoding.UTF8.GetString(output.ToArray()).TrimStart('\uFEFF') : text.ToString());
        Assert.Equal("123", actual);
        Assert.False(input.Closed);
        Assert.NotEqual(ReadState.Closed, reader.ReadState);
        if (source == "uri") Assert.True(Assert.Single(resolver.Opened).Closed);
        else Assert.Empty(resolver.Opened);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void GlobalContextIsGroundedAndIndependent(bool streamable, bool forward)
    {
        string sheet = Sheet("<xsl:template match='/'><xsl:value-of select='$g'/><xsl:apply-templates select='doc/record'/></xsl:template>"
            + "<xsl:template match='record'><xsl:value-of select='.'/></xsl:template>",
            "<xsl:global-context-item use='required' as='document-node()'/><xsl:variable name='g' select='name(/*)'/>", streamable: streamable);
        var transform = Transformer(sheet, forward);
        transform.GlobalContextItem = XdmNode.Load(new StringReader("<global/>"));
        using var reader = XmlReader.Create(new StringReader(Input));
        using var text = new StringWriter();
        transform.ApplyTemplates(reader, text);
        Assert.Equal("global123", text.ToString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RequiredGlobalContextFailsBeforeOpeningInitialInput(bool forward)
    {
        var transform = Transformer(Sheet(declarations: "<xsl:global-context-item use='required'/><xsl:strip-space elements='*'/>"), forward);
        var resolver = new InputResolver(Input);
        transform.DocumentResolver = resolver;
        Assert.Contains("XTDE3086", Assert.Throws<XsltDynamicException>(() => transform.ApplyTemplates(new Uri("urn:input"))).Message);
        Assert.Empty(resolver.Opened);
    }

    [Fact]
    public void XdmSelectionKeepsItsTreeAndGlobalContext()
    {
        var transform = Transformer(Sheet("<xsl:template match='/'><xsl:value-of select='$g'/><xsl:value-of select='count(doc/record)'/></xsl:template>",
            "<xsl:variable name='g' select='name(/*)'/>"));
        Assert.Equal("doc 3", transform.ApplyTemplates(XdmNode.Load(new StringReader(Input))).ToString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NamedModeReceivesParametersAndTunnelParameters(bool forward)
    {
        string sheet = Sheet("<xsl:template match='/' mode='m'><xsl:param name='p' required='yes'/><xsl:param name='t' tunnel='yes'/>"
            + "<xsl:value-of select='$p, $t'/></xsl:template>", mode: "m");
        var transform = Transformer(sheet, forward);
        using var reader = XmlReader.Create(new StringReader(Input));
        Assert.Equal("7 9", transform.ApplyTemplates(reader, new QName("m"),
            new Dictionary<QName, XdmValue> { [new QName("p")] = new XdmAtomicValue(7L) },
            new Dictionary<QName, XdmValue> { [new QName("t")] = new XdmAtomicValue(9L) }).ToString());
    }

    [Fact]
    public void OwnedSourceClosesOnDynamicErrorAndCallerStreamStaysOpen()
    {
        var transform = Transformer(Sheet("<xsl:template match='/'><xsl:sequence select='error()'/></xsl:template>"));
        var resolver = new InputResolver(Input);
        transform.DocumentResolver = resolver;
        Assert.Throws<XsltDynamicException>(() => transform.ApplyTemplates(new Uri("urn:input")));
        Assert.True(Assert.Single(resolver.Opened).Closed);
        using var input = new TrackedStream(Input);
        Assert.Throws<XsltDynamicException>(() => transform.ApplyTemplates(input));
        Assert.False(input.Closed);
    }

    [Fact]
    public void CancellationPreventsSourceAcquisition()
    {
        var transform = Transformer(Sheet());
        var resolver = new InputResolver(Input);
        transform.DocumentResolver = resolver;
        transform.CancellationToken = new CancellationToken(canceled: true);
        Assert.Contains("cancelled", Assert.Throws<XsltDynamicException>(() => transform.ApplyTemplates(new Uri("urn:input"))).Message);
        Assert.Empty(resolver.Opened);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OptionalGlobalContextChecksItsTypeWhenPresent(bool forward)
    {
        var transform = Transformer(Sheet(declarations: "<xsl:global-context-item use='optional' as='element(global)'/>"), forward);
        using var absent = XmlReader.Create(new StringReader(Input));
        Assert.Equal("123", string.Concat(transform.ApplyTemplates(absent)));
        transform.GlobalContextItem = XdmNode.Load(new StringReader("<global/>")); // a document, not the required element
        var resolver = new InputResolver(Input);
        transform.DocumentResolver = resolver;
        Assert.Contains("XTTE0590", Assert.Throws<XsltDynamicException>(() => transform.ApplyTemplates(new Uri("urn:input"))).Message);
        Assert.Empty(resolver.Opened);
    }

    [Fact]
    public void GlobalPrerequisitesDoNotBufferThePrincipalResult()
    {
        var transform = Transformer(Sheet(declarations: "<xsl:global-context-item use='required' as='document-node()'/><xsl:param name='p' required='yes'/>"));
        transform.GlobalContextItem = XdmNode.Load(new StringReader("<global/>"));
        transform.StylesheetParameters[new QName("p")] = new XdmAtomicValue(1L);
        using var reader = XmlReader.Create(new StringReader(Input));
        using var text = new StringWriter();
        bool observed = false;
        using var writer = new ObservingWriter(text, () => { Assert.False(reader.EOF); observed = true; });
        transform.ApplyTemplates(reader, writer);
        Assert.True(observed);
        Assert.Equal("123", text.ToString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PositionedReaderIsRestrictedToItsCurrentSubtree(bool forward)
    {
        var transform = Transformer(Sheet(), forward);
        using var reader = XmlReader.Create(new StringReader("<outer>" + Input + "<after/></outer>"));
        Assert.True(reader.ReadToFollowing("doc"));
        Assert.Equal("123", string.Concat(transform.ApplyTemplates(reader)));
        Assert.NotEqual(ReadState.Closed, reader.ReadState);
        Assert.True(reader.Read());
        Assert.Equal("after", reader.LocalName);
    }

    [Fact]
    public void NonstreamableModeUsesAnOrdinaryTree()
    {
        var transform = Transformer(Sheet("<xsl:template match='/'><xsl:value-of select='count(doc/record), count(doc/record)'/></xsl:template>", streamable: false));
        using var reader = XmlReader.Create(new StringReader(Input));
        Assert.Equal("3 3", transform.ApplyTemplates(reader).ToString());
    }

    [Fact]
    public void LegacyReaderKeepsItsGlobalContextWhenForwardExecutionIsOff()
    {
        var transform = new XslCompiledTransform();
        transform.Load(XmlReader.Create(new StringReader(Sheet("<xsl:template match='/'><xsl:value-of select='$g'/></xsl:template>",
            "<xsl:global-context-item use='required'/><xsl:variable name='g' select='name(/*)'/>"))), new() { StreamedExecution = false }, null);
        using var reader = XmlReader.Create(new StringReader(Input));
        using var output = new StringWriter();
        transform.Transform(reader, null, output);
        Assert.Equal("doc", output.ToString());
    }

#if NET9_0_OR_GREATER
    [Theory]
    [InlineData("default", true)]
    [InlineData("default", false)]
    [InlineData("strict", true)]
    [InlineData("strict", false)]
    [InlineData("fallback", true)]
    [InlineData("fallback", false)]
    public void SavedPublicSelectionSurvivesOppositeLoaderSettings(string policy, bool streamable)
    {
        var settings = new XsltSettings();
        if (policy != "default") settings.Streamability = policy == "strict"
            ? XsltStreamabilityMode.Strict : XsltStreamabilityMode.Fallback;
        using var assembly = new MemoryStream();
        using var stylesheet = XmlReader.Create(new StringReader(Sheet(streamable: streamable)));
        new XslCompiledTransform().CompileToAssembly(stylesheet, settings, null,
            "Xsl3.Tests.PublicSelection", "Saved.PublicSelection", assembly);
        bool forward = streamable && Environment.GetEnvironmentVariable("XSL3_STREAM_STRICT") == "1";
        var transform = new XslCompiledTransform();
        transform.Load(System.Reflection.Assembly.Load(assembly.ToArray()).GetType("Saved.PublicSelection", throwOnError: true)!,
            new XsltSettings { Streamability = policy == "fallback" ? XsltStreamabilityMode.Strict : XsltStreamabilityMode.Fallback,
                StreamedExecution = !forward });
        using var input = XmlReader.Create(new StringReader(Input));
        using var text = new StringWriter();
        bool? firstWriteBeforeEof = null;
        using var writer = new ObservingWriter(text, () => firstWriteBeforeEof ??= !input.EOF);
        using var capture = new StreamingExecutionCapture();
        using (StreamingExecutionCapture.ObserveStage(StreamingExecutionCapture.Stage.Stylesheet))
            transform.Transform(input, null, writer);
        Assert.Equal("123", text.ToString());
        Assert.Equal(forward, firstWriteBeforeEof);
        AssertCapturedSource(capture, forward);
        Assert.NotEqual(ReadState.Closed, input.ReadState);
    }

    [Fact]
    public void SavedInitialModePreservesForwardExecutionAndAbsence()
    {
        var settings = new XsltSettings { StreamedExecution = true };
        using var assembly = new MemoryStream();
        new XslCompiledTransform().CompileToAssembly(XmlReader.Create(new StringReader(Sheet())), settings, null,
            "Xsl3.Tests.StreamedInitial", "Saved.Initial", assembly);
        var transform = new XslCompiledTransform();
        transform.Load(System.Reflection.Assembly.Load(assembly.ToArray()).GetType("Saved.Initial", throwOnError: true)!, settings);
        using var reader = XmlReader.Create(new StringReader(Input));
        using var output = new StringWriter();
        bool observed = false;
        using var writer = new ObservingWriter(output, () => { Assert.False(reader.EOF); observed = true; });
        transform.Transform(reader, null, writer);
        Assert.True(observed);
        Assert.Equal("123", output.ToString());
    }

#endif

    [Fact]
    public void NextMatchDispatchesWithoutAdvancingPastTheCurrentNode()
    {
        var transform = Transformer(Sheet("<xsl:template match='/'><xsl:apply-templates select='doc/record'/></xsl:template>"
            + "<xsl:template match='record' priority='2'><xsl:value-of select='name()'/><xsl:next-match/></xsl:template>"
            + "<xsl:template match='record' priority='1'><xsl:value-of select='.'/></xsl:template>"));
        using var reader = XmlReader.Create(new StringReader(Input));
        Assert.Equal("record1record2record3", string.Concat(transform.ApplyTemplates(reader)));
    }

    [Fact]
    public void StreamableFunctionAndAttributeSetRunInAnInitialMode()
    {
        string sheet = Sheet("<xsl:template match='/'><xsl:apply-templates select='doc/record'/></xsl:template>"
            + "<xsl:template match='record'><out xsl:use-attribute-sets='a'><xsl:value-of select='Q{urn:f}text(.)'/></out></xsl:template>",
            "<xsl:attribute-set name='a' streamable='yes'><xsl:attribute name='n' select='name()'/></xsl:attribute-set>"
            + "<xsl:function name='Q{urn:f}text' streamability='absorbing' as='xs:string'><xsl:param name='n' as='node()'/><xsl:sequence select='string($n)'/></xsl:function>")
            .Replace("method='text'", "method='xml' omit-xml-declaration='yes'").Replace("xmlns:xs=", "exclude-result-prefixes='xs' xmlns:xs=");
        var transform = Transformer(sheet);
        using var reader = XmlReader.Create(new StringReader(Input));
        using var output = new StringWriter();
        transform.ApplyTemplates(reader, output);
        Assert.Equal("<out n=\"record\">1</out><out n=\"record\">2</out><out n=\"record\">3</out>", output.ToString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StreamBaseUriAndWhitespaceRulesApplyToTheSelection(bool forward)
    {
        string sheet = Sheet("<xsl:template match='/'><xsl:value-of select='base-uri(.), count(doc/text())'/></xsl:template>",
            "<xsl:strip-space elements='*'/>");
        var transform = Transformer(sheet, forward);
        using var input = new TrackedStream("<doc>  <record>1</record>  </doc>");
        Assert.Equal("urn:initial-base 0", transform.ApplyTemplates(input, new Uri("urn:initial-base")).ToString());
        Assert.False(input.Closed);
    }

    [Fact]
    public void PrimaryUriInputDoesNotRequireDocumentFunctionPermission()
    {
        var compiler = new XsltCompiler { Settings = new() { StreamedExecution = true, EnableDocumentFunction = false } };
        var transform = compiler.Compile(new StringReader(Sheet())).CreateTransformer();
        transform.DocumentResolver = new InputResolver(Input);
        Assert.Equal("123", string.Concat(transform.ApplyTemplates(new Uri("urn:input"))));
    }

    [Theory]
    [InlineData("text-only-copy")]
    [InlineData("shallow-copy")]
    [InlineData("shallow-skip")]
    public void BuiltinRulesVisitForwardChildrenAndAttributes(string rule)
    {
        string sheet = Sheet("<xsl:template match='record'><xsl:value-of select='.'/></xsl:template>")
            .Replace("streamable='yes'", $"streamable='yes' on-no-match='{rule}'");
        using var input = XmlReader.Create(new StringReader(Input));
        using var output = new StringWriter();
        Transformer(sheet).ApplyTemplates(input, output);
        Assert.Equal("123", output.ToString());
    }

    [Theory]
    [InlineData("@*")]
    [InlineData("@*:a")]
    [InlineData("@Q{urn:a}*")]
    [InlineData("attribute()")]
    public void CopyWithRecursiveTemplatesOnlyReadsTheHeaderBeforeItsChildren(string attributes)
    {
        string sheet = Sheet($"<xsl:template match='*'><xsl:copy><xsl:copy-of select='{attributes}'/><xsl:apply-templates/></xsl:copy></xsl:template>");
        using var input = XmlReader.Create(new StringReader(Input.Replace("<doc>", "<doc a='1' xmlns:p='urn:a' p:a='2'>")));
        using var output = new StringWriter();
        Transformer(sheet).ApplyTemplates(input, output);
        Assert.Equal("123", output.ToString());
    }

    [Theory]
    [InlineData("reader")]
    [InlineData("xdm")]
    public void InitialSelectionDoesNotAcquireUnrequestedAccumulators(string source)
    {
        string declarations = "<xsl:accumulator name='n' initial-value='0' as='xs:integer'><xsl:accumulator-rule match='record' select='$value + 1'/></xsl:accumulator>";
        string sheet = Sheet("<xsl:template match='/'><xsl:value-of select=\"accumulator-after('n')\"/></xsl:template>", declarations, streamable: false);
        var transform = Transformer(sheet);
        transform.GlobalContextItem = XdmNode.Load(new StringReader("<global/>"));
        using var input = XmlReader.Create(new StringReader(Input));
        var error = Assert.Throws<XsltDynamicException>(() =>
        {
            if (source == "reader") transform.ApplyTemplates(input);
            else transform.ApplyTemplates(XdmNode.Load(new StringReader(Input)));
        });
        Assert.Contains("XTDE3362", error.Message);
    }

    [Fact]
    public void AllAccumulatorsApplyToAnIndependentGlobalContextDocument()
    {
        string declarations = "<xsl:global-context-item use='required'/><xsl:accumulator name='n' initial-value='0' as='xs:integer'>"
            + "<xsl:accumulator-rule match='record' select='$value + 1'/></xsl:accumulator>"
            + "<xsl:variable name='g' select=\"accumulator-after('n')\"/>";
        var transform = Transformer(Sheet("<xsl:template match='/'><xsl:value-of select='$g'/></xsl:template>", declarations));
        transform.GlobalContextItem = XdmNode.Load(new StringReader(Input));
        using var input = XmlReader.Create(new StringReader("<other/>"));
        Assert.Equal("3", transform.ApplyTemplates(input).ToString());
    }

    [Fact]
    public void RequestedStreamedObserversRunForTheInitialSelection()
    {
        string declarations = "<xsl:accumulator name='n' streamable='yes' initial-value='0' as='xs:integer'>"
            + "<xsl:accumulator-rule match='record' select='$value + 1'/></xsl:accumulator>";
        var transform = Transformer(Sheet(templates: "<xsl:template match='/'><xsl:apply-templates select='doc/record'/></xsl:template>"
            + "<xsl:template match='record'><xsl:value-of select='accumulator-before(&quot;n&quot;)'/></xsl:template>", declarations: declarations)
            .Replace("streamable='yes'/>", "streamable='yes' use-accumulators='n'/>"));
        var resolver = new InputResolver(Input);
        transform.DocumentResolver = resolver;
        using var output = new StringWriter();
        transform.ApplyTemplates(new Uri("urn:input"), output);
        Assert.Equal("123", output.ToString());
        Assert.True(Assert.Single(resolver.Opened).Closed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UriResolverReadersAreOwnedIncludingPositionedSubtrees(bool positioned)
    {
        using var reader = XmlReader.Create(new StringReader(positioned ? "<outer>" + Input + "<after/></outer>" : Input));
        if (positioned) Assert.True(reader.ReadToFollowing("doc"));
        var transform = Transformer(Sheet());
        transform.DocumentResolver = new InputResolver("") { SuppliedReader = reader };
        Assert.Equal("123", string.Concat(transform.ApplyTemplates(new Uri("urn:input"))));
        Assert.Equal(ReadState.Closed, reader.ReadState);
    }

    [Fact]
    public void VersionOneReaderEntryKeepsItsExistingTreeBehavior()
    {
        const string sheet = "<xsl:stylesheet version='1.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform'>"
            + "<xsl:output method='text'/><xsl:template match='/'><xsl:value-of select='count(doc/record)'/></xsl:template></xsl:stylesheet>";
        var transform = Transformer(sheet);
        using var reader = XmlReader.Create(new StringReader(Input));
        using var output = new StringWriter();
        transform.ApplyTemplates(reader, output);
        Assert.Equal("3", output.ToString());
        Assert.NotEqual(ReadState.Closed, reader.ReadState);
    }

    private sealed class ObservingWriter(TextWriter text, Action observe) : XmlTextWriter(text)
    {
        private bool _observed;
        public override void WriteString(string? value)
        {
            if (!_observed && !string.IsNullOrEmpty(value)) { observe(); _observed = true; }
            base.WriteString(value);
        }
    }

    private sealed class TrackedStream(string xml) : MemoryStream(Encoding.UTF8.GetBytes(xml))
    {
        internal bool Closed;
        protected override void Dispose(bool disposing) { Closed = true; base.Dispose(disposing); }
    }

    private sealed class InputResolver(string xml) : XmlResolver
    {
        internal readonly List<TrackedStream> Opened = new();
        internal XmlReader? SuppliedReader;
        public override ICredentials? Credentials { set { } }
        public override object GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn)
        {
            if (SuppliedReader != null) return SuppliedReader;
            var stream = new TrackedStream(xml);
            Opened.Add(stream);
            return stream;
        }
    }
}
