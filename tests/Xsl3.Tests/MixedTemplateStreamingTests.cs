// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Xml;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

public class MixedTemplateStreamingTests
{
    private const string Sheet = """
        <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
          xmlns:xs="http://www.w3.org/2001/XMLSchema" xmlns:err="http://www.w3.org/2005/xqt-errors">
          <xsl:mode streamable="yes"/><xsl:output method="text"/>
          <xsl:template match="/">
            <xsl:for-each select="doc/row">
              <xsl:try><xsl:sequence select="trace(xs:integer(@id), 'row') idiv xs:integer(@divisor)"/>
                <xsl:catch errors="err:FOAR0001"><xsl:value-of select="@id"/></xsl:catch>
              </xsl:try>
            </xsl:for-each>
          </xsl:template>
        </xsl:stylesheet>
        """;

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void MixedTemplateResultsReachTheWriterBeforeThePopulationEnds(bool modern, bool debug)
    {
        const int rows = 20;
        var xml = new StringBuilder("<doc>");
        var expected = new StringBuilder();
        for (int i = 1; i <= rows; i++)
        {
            xml.Append("<row id='").Append(i).Append("' divisor='0'/>");
            expected.Append(i);
        }
        xml.Append("</doc>");
        using var reader = new CountingReader(XmlReader.Create(new StringReader(xml.ToString())));
        using var text = new StringWriter();
        using var inner = XmlWriter.Create(text, new() { ConformanceLevel = ConformanceLevel.Fragment, OmitXmlDeclaration = true });
        using var output = new ObservingWriter(inner, () => reader.RowsRead);
        int firstTrace = -1;
        Action<string, string> trace = (_, _) => { if (firstTrace < 0) firstTrace = reader.RowsRead; };
        if (modern)
        {
            var executable = new XsltCompiler { Settings = new() { StreamedExecution = true } }.Compile(new StringReader(Sheet));
            var transformer = executable.CreateTransformer();
            transformer.TraceHandler = trace;
            transformer.ApplyTemplates(reader, output);
        }
        else
        {
            var transform = new XslCompiledTransform(debug);
            using var stylesheet = XmlReader.Create(new StringReader(Sheet));
            transform.Load(stylesheet, new() { StreamedExecution = true }, null);
            transform.Transform(reader, new XsltArgumentList { TraceHandler = trace }, output);
        }
        output.Flush();
        Assert.Equal(expected.ToString(), text.ToString());
        Assert.NotEqual(ReadState.Closed, reader.ReadState);
        Assert.InRange(firstTrace, 1, rows - 1);
        Assert.InRange(output.RowsAtFirstText, 1, rows - 1);
    }

    [Theory]
    [InlineData("<xsl:sequence select='[1, (), [2, 3]], 4'/>", "1 2 3 4")]
    [InlineData("<xsl:variable name='empty' as='document-node()'><xsl:document/></xsl:variable><xsl:sequence select='1, $empty, 2'/>", "12")]
    [InlineData("<xsl:sequence select='&quot;&quot;, 1, &quot;&quot;'/>", " 1 ")]
    public void PullCompanionsRetainPrincipalNormalization(string body, string expected)
    {
        foreach (bool forward in new[] { false, true })
            foreach (bool debug in new[] { false, true })
                Assert.Equal(expected, Render(Program(body), "<doc/>", forward, debug));
    }

    [Fact]
    public void PerRecordTryDiscardsItsPartialValueBeforeTheCatch()
    {
        const string body = "<xsl:for-each select='doc/row'><xsl:try><xsl:sequence select='xs:integer(@id), xs:integer(@id) idiv xs:integer(@divisor)'/>"
            + "<xsl:catch errors='err:FOAR0001'><xsl:value-of select='@id'/></xsl:catch></xsl:try></xsl:for-each>";
        const string xml = "<doc><row id='1' divisor='0'/><row id='2' divisor='0'/><row id='3' divisor='0'/></doc>";
        foreach (bool forward in new[] { false, true })
            foreach (bool debug in new[] { false, true }) Assert.Equal("123", Render(Program(body), xml, forward, debug));
    }

    [Fact]
    public void AtomicValuesAndCatchTextKeepTheirCrossRecordBoundaries()
    {
        const string xml = "<doc><row id='1' divisor='1'/><row id='2' divisor='1'/><row id='3' divisor='0'/><row id='4' divisor='1'/><row id='5' divisor='1'/></doc>";
        foreach (bool forward in new[] { false, true })
            foreach (bool debug in new[] { false, true }) Assert.Equal("1 234 5", Render(Sheet, xml, forward, debug));
    }

    [Fact]
    public void TypedDefaultTemplateParametersRetainTheirValueBoundaries()
    {
        const string extra = "<xsl:template name='emit'><xsl:context-item use='absent'/><xsl:param name='n' as='xs:integer' select='0'/><xsl:sequence select='$n'/></xsl:template>";
        const string body = "<xsl:for-each select='doc/row'><xsl:call-template name='emit'><xsl:with-param name='n' select='xs:integer(@id)'/></xsl:call-template></xsl:for-each>";
        const string xml = "<doc><row id='1'/><row id='2'/><row id='3'/></doc>";
        foreach (bool forward in new[] { false, true })
            foreach (bool debug in new[] { false, true }) Assert.Equal("1 2 3", Render(Program(body, extra), xml, forward, debug));
    }

    [Fact]
    public void RawExecutableResultsKeepAtomicKindsWithoutANormalizationWrapper()
    {
        string sheet = Program("<xsl:for-each select='doc/row'><xsl:sequence select='xs:integer(@id)'/></xsl:for-each>");
        var executable = new XsltCompiler { Settings = new() { StreamedExecution = true } }.Compile(new StringReader(sheet));
        using var input = XmlReader.Create(new StringReader("<doc><row id='1'/><row id='2'/><row id='3'/></doc>"));
        XdmValue result = executable.CreateTransformer().ApplyTemplates(input);
        Assert.Equal(3, result.Count);
        for (int i = 0; i < result.Count; i++) Assert.Equal((i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), Assert.IsType<XdmAtomicValue>(result[i]).ToString());
        Assert.NotEqual(ReadState.Closed, input.ReadState);
    }

#if NET9_0_OR_GREATER
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MixedTemplateResultsSurviveSavedCompilation(bool debug)
    {
        var transform = new XslCompiledTransform(debug);
        using var stylesheet = XmlReader.Create(new StringReader(Sheet)); using var assembly = new MemoryStream();
        transform.CompileToAssembly(stylesheet, new() { StreamedExecution = true }, null, "MixedTemplate" + Guid.NewGuid().ToString("N"), "Saved.Mixed", assembly);
        transform.Load(Assembly.Load(assembly.ToArray()).GetType("Saved.Mixed", true)!);
        using var input = XmlReader.Create(new StringReader("<doc><row id='1' divisor='1'/><row id='2' divisor='0'/><row id='3' divisor='1'/></doc>"));
        using var output = new StringWriter(); transform.Transform(input, null, output);
        Assert.Equal("123", output.ToString()); Assert.NotEqual(ReadState.Closed, input.ReadState);
    }
#endif

    [Fact]
    public void OmittedAndTunnelParametersKeepDefaultsAndSingleEvaluation()
    {
        const string extra = "<xsl:template name='emit'><xsl:context-item use='absent'/>"
            + "<xsl:param name='n' as='xs:integer' select='trace(7, &quot;default&quot;)'/>"
            + "<xsl:param name='t' as='xs:integer' tunnel='yes' select='trace(3, &quot;tunnel&quot;)'/>"
            + "<xsl:sequence select='$n, $n, $t'/></xsl:template>";
        const string body = "<xsl:call-template name='emit'/><xsl:call-template name='emit'>"
            + "<xsl:with-param name='n' select='trace(2, &quot;explicit&quot;)'/>"
            + "<xsl:with-param name='t' select='trace(9, &quot;supplied-tunnel&quot;)' tunnel='yes'/></xsl:call-template>";
        foreach (bool forward in new[] { false, true })
            foreach (bool debug in new[] { false, true })
            {
                var labels = new System.Collections.Generic.List<string>();
                var transform = new XslCompiledTransform(debug);
                using var stylesheet = XmlReader.Create(new StringReader(Program(body, extra)));
                transform.Load(stylesheet, new() { StreamedExecution = forward }, null);
                using var input = XmlReader.Create(new StringReader("<doc/>"));
                using var output = new StringWriter();
                transform.Transform(input, new XsltArgumentList { TraceHandler = (label, _) => labels.Add(label) }, output);
                Assert.Equal("7 7 3 2 2 9", output.ToString());
                labels.Sort(StringComparer.Ordinal);
                Assert.Equal(new[] { "default", "explicit", "supplied-tunnel", "tunnel" }, labels);
            }
    }

    [Fact]
    public void RecursiveNamedTemplatesKeepTheirCallBoundaries()
    {
        const string extra = "<xsl:template name='left'><xsl:context-item use='absent'/><xsl:param name='n' as='xs:integer'/>"
            + "<xsl:if test='$n gt 0'><xsl:sequence select='$n'/><xsl:call-template name='right'><xsl:with-param name='n' select='$n - 1'/></xsl:call-template></xsl:if></xsl:template>"
            + "<xsl:template name='right'><xsl:context-item use='absent'/><xsl:param name='n' as='xs:integer'/>"
            + "<xsl:if test='$n gt 0'><xsl:sequence select='$n'/><xsl:call-template name='left'><xsl:with-param name='n' select='$n - 1'/></xsl:call-template></xsl:if></xsl:template>";
        const string body = "<xsl:call-template name='left'><xsl:with-param name='n' select='6'/></xsl:call-template>";
        foreach (bool forward in new[] { false, true })
            foreach (bool debug in new[] { false, true })
                Assert.Equal("6 5 4 3 2 1", Render(Program(body, extra), "<doc/>", forward, debug));
    }

    [Fact]
    public void RawResultsRetainConstructedNodeIdentityAcrossSuspension()
    {
        string sheet = Program("<xsl:variable name='node' as='element()'><made/></xsl:variable><xsl:sequence select='$node, 1, $node'/>");
        var executable = new XsltCompiler { Settings = new() { StreamedExecution = true } }.Compile(new StringReader(sheet));
        using var input = XmlReader.Create(new StringReader("<doc/>"));
        XdmValue result = executable.CreateTransformer().ApplyTemplates(input);
        Assert.Equal(3, result.Count);
        Assert.True(Assert.IsType<XdmNode>(result[0]).Navigator.IsSamePosition(Assert.IsType<XdmNode>(result[2]).Navigator));
        Assert.IsType<XdmAtomicValue>(result[1]);
    }

    [Fact]
    public void ManyTemplateBranchesSelectExactlyTheMatchingRule()
    {
        var rules = new StringBuilder();
        var input = new StringBuilder("<doc>");
        var expected = new StringBuilder();
        const int count = 128;
        for (int i = 0; i < count; i++)
        {
            rules.Append("<xsl:template match='r").Append(i).Append("'><xsl:sequence select='").Append(i)
                .Append("'/><xsl:text>|</xsl:text></xsl:template>");
            input.Append("<r").Append(i).Append("/>");
            expected.Append(i).Append('|');
        }
        input.Append("<fallback/></doc>");
        rules.Append("<xsl:template match='fallback'><xsl:text>end</xsl:text></xsl:template>");
        expected.Append("end");
        string sheet = Program("<xsl:apply-templates select='doc/*'/>", rules.ToString());
        foreach (bool forward in new[] { false, true })
            foreach (bool debug in new[] { false, true })
                Assert.Equal(expected.ToString(), Render(sheet, input.ToString(), forward, debug));
    }

    [Fact]
    public void ContextFreeRawNamedTemplateFinishesAfterItsFork()
    {
        const string sheet = "<xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform'>"
            + "<xsl:mode streamable='yes'/><xsl:template name='go'><xsl:fork>"
            + "<xsl:sequence select='1'/><xsl:sequence select='2'/></xsl:fork></xsl:template></xsl:stylesheet>";
        var transformer = new XsltCompiler { Settings = new() { StreamedExecution = true } }
            .Compile(new StringReader(sheet)).CreateTransformer();
        using var cancellation = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(2));
        transformer.CancellationToken = cancellation.Token;
        Assert.Equal("1 2", transformer.CallTemplate(new QName("", "go")).ToString());
    }

    [Fact]
    public void RawFunctionItemTemplateResultsAreProducedExactlyOnce()
    {
        string sheet = Program("<xsl:sequence select='function() { 1 }'/>");
        var transformer = new XsltCompiler { Settings = new() { StreamedExecution = true } }
            .Compile(new StringReader(sheet)).CreateTransformer();
        using var cancellation = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(2));
        transformer.CancellationToken = cancellation.Token;
        using var input = XmlReader.Create(new StringReader("<doc/>"));
        XdmValue result = transformer.ApplyTemplates(input);
        Assert.Single(result);
        Assert.IsType<XdmFunctionItem>(result[0]);
    }

    private static string Program(string body, string extra = "") => "<xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform' xmlns:xs='http://www.w3.org/2001/XMLSchema' xmlns:err='http://www.w3.org/2005/xqt-errors'>"
        + "<xsl:mode streamable='yes'/><xsl:output method='text'/>" + extra + "<xsl:template match='/'>" + body + "</xsl:template></xsl:stylesheet>";

    private static string Render(string sheet, string xml, bool forward, bool debug)
    {
        var transform = new XslCompiledTransform(debug);
        using var stylesheet = XmlReader.Create(new StringReader(sheet)); transform.Load(stylesheet, new() { StreamedExecution = forward }, null);
        using var input = XmlReader.Create(new StringReader(xml)); using var output = new StringWriter(); transform.Transform(input, null, output);
        Assert.NotEqual(ReadState.Closed, input.ReadState); return output.ToString();
    }

    private sealed class CountingReader(XmlReader reader) : XmlReader
    {
        internal int RowsRead { get; private set; }
        public override bool Read() { bool result = reader.Read(); if (result && reader.NodeType == XmlNodeType.Element && reader.LocalName == "row") RowsRead++; return result; }
        public override int AttributeCount => reader.AttributeCount;
        public override string BaseURI => reader.BaseURI;
        public override int Depth => reader.Depth;
        public override bool EOF => reader.EOF;
        public override bool HasValue => reader.HasValue;
        public override bool IsEmptyElement => reader.IsEmptyElement;
        public override string LocalName => reader.LocalName;
        public override string NamespaceURI => reader.NamespaceURI;
        public override XmlNameTable NameTable => reader.NameTable;
        public override XmlNodeType NodeType => reader.NodeType;
        public override string Prefix => reader.Prefix;
        public override ReadState ReadState => reader.ReadState;
        public override string Value => reader.Value;
        public override string GetAttribute(int i) => reader.GetAttribute(i);
        public override string? GetAttribute(string name) => reader.GetAttribute(name);
        public override string? GetAttribute(string localName, string? namespaceURI) => reader.GetAttribute(localName, namespaceURI);
        public override string? LookupNamespace(string prefix) => reader.LookupNamespace(prefix);
        public override bool MoveToAttribute(string name) => reader.MoveToAttribute(name);
        public override bool MoveToAttribute(string localName, string? namespaceURI) => reader.MoveToAttribute(localName, namespaceURI);
        public override bool MoveToElement() => reader.MoveToElement();
        public override bool MoveToFirstAttribute() => reader.MoveToFirstAttribute();
        public override bool MoveToNextAttribute() => reader.MoveToNextAttribute();
        public override bool ReadAttributeValue() => reader.ReadAttributeValue();
        public override void ResolveEntity() => reader.ResolveEntity();
        public override void Close() => reader.Close();
    }

    private sealed class ObservingWriter(XmlWriter writer, Func<int> rowsRead) : XmlWriter
    {
        internal int RowsAtFirstText { get; private set; } = -1;
        private void Text(string? value) { if (value?.Length > 0 && RowsAtFirstText < 0) RowsAtFirstText = rowsRead(); }
        public override void WriteString(string? value) { Text(value); writer.WriteString(value); }
        public override void WriteRaw(string value) { Text(value); writer.WriteRaw(value); }
        public override void WriteRaw(char[] buffer, int index, int count) { Text(new string(buffer, index, count)); writer.WriteRaw(buffer, index, count); }
        public override void WriteChars(char[] buffer, int index, int count) { Text(new string(buffer, index, count)); writer.WriteChars(buffer, index, count); }
        public override void WriteWhitespace(string? value) { Text(value); writer.WriteWhitespace(value); }
        public override WriteState WriteState => writer.WriteState;
        public override XmlWriterSettings? Settings => writer.Settings;
        public override string? LookupPrefix(string ns) => writer.LookupPrefix(ns);
        public override void Flush() => writer.Flush();
        public override void Close() => writer.Close();
        public override void WriteStartDocument() => writer.WriteStartDocument();
        public override void WriteStartDocument(bool standalone) => writer.WriteStartDocument(standalone);
        public override void WriteEndDocument() => writer.WriteEndDocument();
        public override void WriteDocType(string name, string? pubid, string? sysid, string? subset) => writer.WriteDocType(name, pubid, sysid, subset);
        public override void WriteStartElement(string? prefix, string localName, string? ns) => writer.WriteStartElement(prefix, localName, ns);
        public override void WriteEndElement() => writer.WriteEndElement();
        public override void WriteFullEndElement() => writer.WriteFullEndElement();
        public override void WriteStartAttribute(string? prefix, string localName, string? ns) => writer.WriteStartAttribute(prefix, localName, ns);
        public override void WriteEndAttribute() => writer.WriteEndAttribute();
        public override void WriteCData(string? text) => writer.WriteCData(text);
        public override void WriteComment(string? text) => writer.WriteComment(text);
        public override void WriteProcessingInstruction(string name, string? text) => writer.WriteProcessingInstruction(name, text);
        public override void WriteEntityRef(string name) => writer.WriteEntityRef(name);
        public override void WriteCharEntity(char ch) => writer.WriteCharEntity(ch);
        public override void WriteSurrogateCharEntity(char lowChar, char highChar) => writer.WriteSurrogateCharEntity(lowChar, highChar);
        public override void WriteBase64(byte[] buffer, int index, int count) => writer.WriteBase64(buffer, index, count);
    }
}
