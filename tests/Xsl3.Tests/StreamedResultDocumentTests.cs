// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.XPath;
using Xsl3.Runtime;
using Xunit;

namespace Xsl3.Tests;

public class StreamedResultDocumentTests
{
    private static string Sheet(string body, string declarations = "") =>
        "<xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform' xmlns:xs='http://www.w3.org/2001/XMLSchema' exclude-result-prefixes='xs'>"
        + "<xsl:mode streamable='yes'/><xsl:output omit-xml-declaration='yes'/>" + declarations
        + "<xsl:template match='/'>" + body + "</xsl:template></xsl:stylesheet>";

    private static (string Principal, Dictionary<string, string> Secondary) Run(string body, string declarations = "", bool forward = true,
        string xml = "<doc><row x='one'/><row x='two'/></doc>")
    {
        var transformer = new XsltCompiler { Settings = new() { StreamedExecution = forward, EnableResultDocument = true } }
            .Compile(new StringReader(Sheet(body, declarations))).CreateTransformer();
        var writers = new Dictionary<string, StringWriter>();
        transformer.ResultDocumentHandler = uri => writers[uri.ToString()] = new StringWriter();
        using var reader = XmlReader.Create(new StringReader(xml));
        using var principal = new StringWriter();
        transformer.ApplyTemplates(reader, principal);
        var results = new Dictionary<string, string>();
        foreach (var item in writers) results.Add(item.Key, item.Value.ToString());
        return (principal.ToString(), results);
    }

    [Theory]
    [InlineData("xml", false)]
    [InlineData("xml", true)]
    [InlineData("text", false)]
    [InlineData("text", true)]
    public void SecondaryOutputBeginsBeforeInputEnds(string method, bool fork)
    {
        string result = "<xsl:result-document href='urn:out' method='" + method + "'><out><xsl:for-each select='doc/row'><r><xsl:value-of select='@id'/></r></xsl:for-each></out></xsl:result-document>";
        string body = fork ? "<xsl:fork><xsl:sequence select='()'/><xsl:sequence>" + result + "</xsl:sequence></xsl:fork>" : result;
        var transformer = new XsltCompiler { Settings = new() { StreamedExecution = true, StreamingBufferCapacity = 1 } }
            .Compile(new StringReader(Sheet(body))).CreateTransformer();
        using var input = new StreamingProbe.GeneratedInput(262144);
        long? firstInput = null;
        var output = new ObservedWriter(() => firstInput ??= input.ReadBytes);
        transformer.ResultDocumentHandler = _ => output;
        using var reader = XmlReader.Create(input);
        using var principal = new StringWriter();
        transformer.ApplyTemplates(reader, principal);
        Assert.True(output.Closed); Assert.True(output.Count > 0);
        Assert.NotNull(firstInput); Assert.True(firstInput < input.Length);
        Assert.Equal(input.Length, input.ReadBytes); Assert.Empty(principal.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DebugSecondaryOutputBeginsBeforeInputEnds(bool fork)
    {
        const string result = "<xsl:result-document href='urn:out'><out><xsl:for-each select='doc/row'><r><xsl:value-of select='@id'/></r></xsl:for-each></out></xsl:result-document>";
        string body = fork ? "<xsl:fork><xsl:sequence select='()'/><xsl:sequence>" + result + "</xsl:sequence></xsl:fork>" : result;
        var transform = new XslCompiledTransform(enableDebug: true);
        using var style = XmlReader.Create(new StringReader(Sheet(body)));
        transform.Load(style, new XsltSettings { StreamedExecution = true, EnableResultDocument = true, StreamingBufferCapacity = 1 }, null);
        using var input = new StreamingProbe.GeneratedInput(262144);
        long? firstInput = null;
        var output = new ObservedWriter(() => firstInput ??= input.ReadBytes);
        using var reader = XmlReader.Create(input);
        using var principal = new StringWriter();
        transform.Transform(reader, new XsltArgumentList { ResultDocumentHandler = _ => output }, principal);
        Assert.True(output.Closed); Assert.True(output.Count > 0);
        Assert.NotNull(firstInput); Assert.True(firstInput < input.Length);
        Assert.Equal(input.Length, input.ReadBytes); Assert.Empty(principal.ToString());
    }

    [Theory]
    [InlineData("xml")]
    [InlineData("html")]
    [InlineData("xhtml")]
    [InlineData("text")]
    public void DynamicNamedOutputAndNestedResultsRetainTheirSerializationAndCurrentUri(string method)
    {
        const string body = "<before/><xsl:result-document href='urn:outer' format=\"{string('named')}\"><out a='{current-output-uri()}'>A<br/>B"
            + "<xsl:result-document href='urn:inner' method='text'><xsl:value-of select='current-output-uri()'/></xsl:result-document>"
            + "<xsl:for-each select='doc/row'><xsl:value-of select='@x'/></xsl:for-each></out></xsl:result-document><after/>";
        string declarations = "<xsl:output name='named' method='" + method + "' omit-xml-declaration='yes'/>";
        var expected = Run(body, declarations, false);
        var actual = Run(body, declarations);
        Assert.Equal("<before/><after/>", actual.Principal);
        Assert.Equal(expected.Principal, actual.Principal);
        Assert.Equal(expected.Secondary["urn:outer"], actual.Secondary["urn:outer"]);
        Assert.Equal("urn:inner", actual.Secondary["urn:inner"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResultDocumentAttributesUseTheCallersOutputUri(bool forward)
    {
        const string body = "<xsl:result-document href='urn:outer'><outer><xsl:result-document href='urn:inner' "
            + "format=\"{if (current-output-uri() = 'urn:outer') then 'xml-output' else 'text-output'}\"><inner/></xsl:result-document></outer></xsl:result-document>";
        const string declarations = "<xsl:output name='xml-output' method='xml' omit-xml-declaration='yes'/><xsl:output name='text-output' method='text'/>";
        var result = Run(body, declarations, forward);
        Assert.Equal("<inner/>", result.Secondary["urn:inner"]);
        Assert.Equal("<outer/>", result.Secondary["urn:outer"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ACaughtMissingFormatDoesNotLeakItsOutputFrameIntoLaterInstructions(bool forward)
    {
        const string body = "<xsl:try><xsl:result-document href='urn:bad' format='missing'><bad/></xsl:result-document><xsl:catch><caught/></xsl:catch></xsl:try>"
            + "<xsl:result-document href='urn:good'><out uri='{current-output-uri()}'/></xsl:result-document><done/>";
        var result = Run(body, forward: forward);
        Assert.Equal("<caught/><done/>", result.Principal);
        Assert.Equal("<out uri=\"urn:good\"/>", result.Secondary["urn:good"]);
        Assert.False(result.Secondary.ContainsKey("urn:bad"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TextSerializationAppliesCharacterMapsAndNormalization(bool forward)
    {
        const string body = "<xsl:result-document href='urn:out' method='text' use-character-maps='cm' normalization-form='NFC'><out>@e&#x301;</out></xsl:result-document>";
        const string declarations = "<xsl:character-map name='cm'><xsl:output-character character='@' string='[at]'/></xsl:character-map>";
        Assert.Equal("[at]é", Run(body, declarations, forward).Secondary["urn:out"]);
    }

    [Theory]
    [InlineData("xml", "NFC", "e", "\u0301", "<out>é</out>")]
    [InlineData("text", "NFC", "e", "\u0301", "é")]
    [InlineData("xml", "NFC", "\u1100", "\u1161", "<out>가</out>")]
    [InlineData("text", "NFKC", "\u3131", "\u314F", "가")]
    public void NormalizationComposesAcrossIncrementalTextWrites(string method, string form, string left, string right, string expected)
    {
        string body = "<xsl:result-document href='urn:out' method='" + method + "' normalization-form='" + form + "'><out>"
            + "<xsl:for-each select='doc/row'><xsl:value-of select='@x'/></xsl:for-each></out></xsl:result-document>";
        string xml = "<doc><row x='" + left + "'/><row x='" + right + "'/></doc>";
        Assert.Equal(expected, Run(body, forward: false, xml: xml).Secondary["urn:out"]);
        Assert.Equal(expected, Run(body, xml: xml).Secondary["urn:out"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TextMethodFlattensMarkupBeforeNormalizationAndIgnoresAttributeCharacterMaps(bool forward)
    {
        const string body = "<xsl:result-document href='urn:out' method='text' normalization-form='NFC' use-character-maps='cm'>"
            + "<out a='@'>e<xsl:comment>ignored</xsl:comment><xsl:processing-instruction name='p'>ignored</xsl:processing-instruction>"
            + "<b>&#x301;</b><xsl:text> </xsl:text>@</out></xsl:result-document>";
        const string declarations = "<xsl:character-map name='cm'><xsl:output-character character='@' string='[at]'/></xsl:character-map>";
        Assert.Equal("é [at]", Run(body, declarations, forward).Secondary["urn:out"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TextMethodIgnoresInheritedXmlRootAndCDataConstraints(bool forward)
    {
        const string declarations = "<xsl:output standalone='yes' cdata-section-elements='out'/>";
        const string body = "<xsl:result-document href='urn:out' method='text' use-character-maps='cm'><out>@</out><out>tail</out></xsl:result-document>";
        const string map = "<xsl:character-map name='cm'><xsl:output-character character='@' string='[at]'/></xsl:character-map>";
        Assert.Equal("[at]tail", Run(body, declarations + map, forward).Secondary["urn:out"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PrincipalTextNormalizationFlushesTheFinalSegmentForOwnedAndBorrowedWriters(bool borrowed)
    {
        const string declarations = "<xsl:output method='text' normalization-form='NFC'/>";
        const string body = "<xsl:for-each select='doc/row'><xsl:value-of select='@x'/></xsl:for-each>";
        var transformer = new XsltCompiler { Settings = new() { StreamedExecution = true } }.Compile(new StringReader(Sheet(body, declarations))).CreateTransformer();
        using var input = XmlReader.Create(new StringReader("<doc><row x='e'/><row x='&#x301;'/><row x='z'/></doc>"));
        using var output = new StringWriter();
        if (borrowed)
        {
            using var writer = XmlWriter.Create(output, new XmlWriterSettings { OmitXmlDeclaration = true, ConformanceLevel = ConformanceLevel.Fragment });
            transformer.ApplyTemplates(input, writer);
            Assert.Equal("éz", output.ToString());
            Assert.NotEqual(WriteState.Closed, writer.WriteState);
        }
        else { transformer.ApplyTemplates(input, output); Assert.Equal("éz", output.ToString()); }
    }

    [Theory]
    [InlineData(NormalizationForm.FormC)]
    [InlineData(NormalizationForm.FormD)]
    [InlineData(NormalizationForm.FormKC)]
    [InlineData(NormalizationForm.FormKD)]
    public void ScalarAtATimeNormalizationMatchesTheWholeString(NormalizationForm form)
    {
        const string text = "e\u0301\u0327|\u1100\u1161\u11A8|\u3131\u314F|@\u0301\u0327|end";
        using var output = new StringWriter();
        using var inner = XmlWriter.Create(output, TextSettings());
        using var writer = new SerializationWriter(inner, new OutputExtras { Normalization = form });
        foreach (Rune scalar in text.EnumerateRunes()) writer.WriteString(scalar.ToString());
        writer.Flush();
        Assert.Equal(text.Normalize(form), output.ToString());
    }

    [Fact]
    public void CapturedResultTreesKeepTheirDocumentUriAndTheOutputStateIsRestored()
    {
        const string body = "<before/><xsl:result-document href='urn:tree'><out><xsl:for-each select='doc/row'><r><xsl:value-of select='@x'/></r></xsl:for-each></out></xsl:result-document><after/>";
        Action<string, XPathNavigator>? saved = XmlQueryRuntime.ResultTreeHandler;
        XPathNavigator? captured = null;
        try
        {
            XmlQueryRuntime.ResultTreeHandler = (_, tree) => captured = tree;
            var actual = Run(body);
            Assert.Equal("<before/><after/>", actual.Principal);
            Assert.Equal("<out><r>one</r><r>two</r></out>", actual.Secondary["urn:tree"]);
            Assert.NotNull(captured); Assert.Equal("urn:tree", captured.BaseURI);
            Assert.False(Assert.IsType<Xsl3.Tree.CompactNavigator>(captured).Document.NoDocumentUri);
        }
        finally { XmlQueryRuntime.ResultTreeHandler = saved; }
    }

    [Fact]
    public void DynamicEmptyHrefKeepsPrincipalResultOwnership()
    {
        var result = Run("<xsl:result-document href=\"{string('')}\"><out><xsl:value-of select='count(doc/row)'/></out></xsl:result-document>");
        Assert.Equal("<out>2</out>", result.Principal); Assert.Empty(result.Secondary);
    }

    [Fact]
    public void ABodyErrorClosesTheSecondaryWriterAndRestoresThePrincipalOutput()
    {
        const string body = "<before/><xsl:try><xsl:result-document href='urn:bad'><out><xsl:sequence select=\"error(QName('urn:err', 'body'))\"/></out></xsl:result-document>"
            + "<xsl:catch><caught/></xsl:catch></xsl:try><after/>";
        var transformer = new XsltCompiler { Settings = new() { StreamedExecution = true } }.Compile(new StringReader(Sheet(body))).CreateTransformer();
        var secondary = new ObservedWriter(() => { });
        transformer.ResultDocumentHandler = _ => secondary;
        using var reader = XmlReader.Create(new StringReader("<doc/>"));
        using var output = new StringWriter();
        transformer.ApplyTemplates(reader, output);
        Assert.Equal("<before/><caught/><after/>", output.ToString()); Assert.True(secondary.Closed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ASecondaryWriterErrorIsCatchableAndDoesNotLeaveItsOutputInstalled(bool fork)
    {
        const string result = "<xsl:result-document href='urn:bad'><out>content</out></xsl:result-document>";
        string operation = fork ? "<xsl:fork><xsl:sequence select='()'/><xsl:sequence>" + result + "</xsl:sequence></xsl:fork>" : result;
        string body = "<before/><xsl:try>" + operation + "<xsl:catch errors='w:failure' xmlns:w='urn:writer'><caught xsl:exclude-result-prefixes='w'/></xsl:catch></xsl:try><after/>";
        var transformer = new XsltCompiler { Settings = new() { StreamedExecution = true } }.Compile(new StringReader(Sheet(body))).CreateTransformer();
        var failure = new Xsl3.Xdm.XdmException("TEST0001", "writer failed") { Name = new Xsl3.Xdm.QName("urn:writer", "failure") };
        var secondary = new ObservedWriter(() => throw failure);
        transformer.ResultDocumentHandler = _ => secondary;
        using var reader = XmlReader.Create(new StringReader("<doc/>"));
        using var output = new StringWriter();
        transformer.ApplyTemplates(reader, output);
        Assert.Equal("<before/><caught/><after/>", output.ToString()); Assert.True(secondary.Closed);
    }

    [Fact]
    public void CancellationFromASecondaryWriterClosesItAndKeepsTheInputBorrowed()
    {
        using var cancellation = new CancellationTokenSource();
        const string body = "<xsl:result-document href='urn:out'><out><xsl:for-each select='doc/row'><r><xsl:value-of select='@id'/></r></xsl:for-each></out></xsl:result-document>";
        var transformer = new XsltCompiler { Settings = new() { StreamedExecution = true } }.Compile(new StringReader(Sheet(body))).CreateTransformer();
        var secondary = new ObservedWriter(cancellation.Cancel);
        transformer.CancellationToken = cancellation.Token; transformer.ResultDocumentHandler = _ => secondary;
        using var source = new StreamingProbe.GeneratedInput(262144);
        using var reader = XmlReader.Create(source);
        using var output = new StringWriter();
        var error = Assert.Throws<XsltDynamicException>(() => transformer.ApplyTemplates(reader, output));
        Assert.Contains("cancelled", error.Message);
        Assert.True(secondary.Closed); Assert.NotEqual(ReadState.Closed, reader.ReadState);
    }

#if NET9_0_OR_GREATER
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PublicLegacyAndSavedPathsSerializeSecondaryResults(bool saved)
    {
        string directory = Path.Combine(Path.GetTempPath(), "xsl3-secondary-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string body = "<xsl:result-document href='out.xml'><out><xsl:for-each select='doc/row'><r x='{@x}'/></xsl:for-each></out></xsl:result-document><done/>";
            using var sheet = XmlReader.Create(new StringReader(Sheet(body)), new XmlReaderSettings(), new Uri(Path.Combine(directory, "style.xsl")).AbsoluteUri);
            var settings = new XsltSettings { StreamedExecution = true, EnableResultDocument = true };
            var transform = new XslCompiledTransform();
            if (saved)
            {
                using var assembly = new MemoryStream();
                transform.CompileToAssembly(sheet, settings, null, "SecondaryResult", "Saved.SecondaryResult", assembly);
                transform.Load(Assembly.Load(assembly.ToArray()).GetType("Saved.SecondaryResult", true)!, settings);
            }
            else transform.Load(sheet, settings, null);
            transform.BaseOutputUri = new Uri(Path.Combine(directory, "principal.xml"));
            using var reader = XmlReader.Create(new StringReader("<doc><row x='one'/><row x='two'/></doc>"));
            using var output = new StringWriter();
            transform.Transform(reader, null, output);
            Assert.Equal("<done/>", output.ToString());
            Assert.Equal("<out><r x=\"one\"/><r x=\"two\"/></out>", File.ReadAllText(Path.Combine(directory, "out.xml")));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
#endif

    private static XmlWriterSettings TextSettings()
    {
        var settings = new XmlWriterSettings { ConformanceLevel = ConformanceLevel.Fragment }; settings.SetOutputMethod(XmlOutputMethod.Text); return settings;
    }

    private sealed class ObservedWriter(Action write) : TextWriter
    {
        internal bool Closed;
        internal long Count;
        public override Encoding Encoding => Encoding.UTF8;
        public override void Write(char value) { Count++; write(); }
        public override void Write(char[] value, int offset, int count) { Count += count; if (count > 0) write(); }
        public override void Write(string? value) { Count += value?.Length ?? 0; if (!string.IsNullOrEmpty(value)) write(); }
        protected override void Dispose(bool disposing) { Closed = true; base.Dispose(disposing); }
    }
}
