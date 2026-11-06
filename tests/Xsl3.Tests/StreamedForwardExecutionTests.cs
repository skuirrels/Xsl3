// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.Schema;
using Xsl3.Streaming;
using Xsl3.Xdm;
using Xunit;
using static Xsl3.Tests.ForwardEvidence;

namespace Xsl3.Tests;

// Forward-execution evidence for P7 phase A obligations whose earlier tests compared forward and tree output
// only (S3-17, S4-04, S4-05, S4-18, S4-29, S4-30, S5-02, S5-14, S6-10). Each case runs the same stylesheet
// over a 20,000-record input forward and as a tree. The forward run must write the first record's result
// before reading half of the input; the tree run, the control, cannot. Outputs are compared in full. S6-10
// uses the captured input representation instead, because fork output is held until the fork completes.
public class StreamedForwardExecutionTests
{
    private const int Count = 20000;
    private static readonly string Input = Records(Count, i => $"<record id='{i}' g='{(i - 1) / 2}'><v>{i}</v></record>");

    private static string Sheet(string templates, string declarations = "", string method = "text") => $$"""
        <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:xs="http://www.w3.org/2001/XMLSchema"
            xmlns:f="urn:test" exclude-result-prefixes="xs f">
          <xsl:mode streamable="yes"/><xsl:output method="{{method}}" omit-xml-declaration="yes"/>
          {{declarations}}{{templates}}
        </xsl:stylesheet>
        """;

    private static string Run(string sheet, bool forward, bool legacy, string xml = "", XmlResolver? resolver = null)
    {
        xml = xml.Length == 0 ? Input : xml;
        using var source = new CountingReader(xml);
        using var reader = XmlReader.Create(source);
        using var output = new EarlyWriter(source);
        var settings = new XsltSettings { StreamedExecution = forward ? null : false };
        if (legacy)
        {
            var transform = new XslCompiledTransform();
            using var stylesheet = XmlReader.Create(new StringReader(sheet));
            transform.Load(stylesheet, settings, resolver);
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            transform.Transform(reader, new XsltArgumentList { CancellationToken = cancellation.Token }, output);
        }
        else
        {
            var transform = new XsltCompiler { Settings = settings }.Compile(new StringReader(sheet)).CreateTransformer();
            if (resolver != null) transform.DocumentResolver = resolver;
            transform.ApplyTemplates(reader, output);
        }
        AssertWrittenEarly(output.FirstWrite, xml.Length, forward);
        return output.ToString();
    }

    private static void AssertBothWays(string sheet, Action<string> check, string xml = "")
    {
        foreach (bool legacy in new[] { false, true })
        {
            string tree = Run(sheet, forward: false, legacy, xml);
            check(tree);
            Assert.Equal(tree, Run(sheet, forward: true, legacy, xml));
        }
    }

    private static void AssertRows(string output, string first, int count, char separator = ';')
    {
        string[] rows = output.Split(separator, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(count, rows.Length);
        Assert.Equal(first, rows[0]);
    }

    // S3-17: operands are atomized and consumed item by item. The first item's value is written while
    // most of the population is still unread, so the sequence is not materialised before it is used.
    [Theory]
    [InlineData("data(doc/record/v)")]
    [InlineData("doc/record/v ! string(.)")]
    [InlineData("doc/record ! f:text(.)")]
    public void OperandsAreAtomizedItemByItemBeforeTheInputAdvances(string select)
    {
        string sheet = Sheet($"<xsl:template match='/'><xsl:sequence select='{select}'/></xsl:template>",
            "<xsl:function name='f:text' streamability='absorbing' as='xs:string'><xsl:param name='n' as='node()'/><xsl:sequence select='string($n)'/></xsl:function>");
        AssertBothWays(sheet, output => AssertRows(output, "1", Count, ' '));
    }

    [Fact]
    public void SourceDocumentOperandsAreAtomizedItemByItemBeforeTheInputAdvances()
    {
        string sheet = Sheet("<xsl:template name='xsl:initial-template'><xsl:source-document href='urn:input' streamable='yes'>"
            + "<xsl:sequence select='data(doc/record/v)'/></xsl:source-document></xsl:template>");
        foreach (bool forward in new[] { false, true })
        {
            var resolver = new CountingResolver(Input);
            var transform = new XsltCompiler { Settings = new() { StreamedExecution = forward ? null : false, EnableDocumentFunction = true } }.Compile(new StringReader(sheet)).CreateTransformer();
            transform.DocumentResolver = resolver;
            using var output = new EarlyWriter(() => resolver.Bytes);
            transform.CallTemplate(null, output);
            AssertRows(output.ToString(), "1", Count, ' ');
            AssertWrittenEarly(output.FirstWrite, Encoding.UTF8.GetByteCount(Input), forward);
        }
    }

    // S4-04: every source and destination overload of ApplyTemplates runs the initial mode forward. The
    // captured initial source is a streamed navigator; where the destination is a writer, the first record's
    // result is also written before half of the input is read.
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
    public void EverySourceAndDestinationOverloadRunsTheInitialModeForward(string source, string destination)
    {
        string sheet = Sheet("<xsl:template match='record'><xsl:value-of select='@id'/>;</xsl:template>");
        foreach (bool forward in new[] { false, true })
        {
            var transform = new XsltCompiler { Settings = new() { StreamedExecution = forward ? null : false } }.Compile(new StringReader(sheet)).CreateTransformer();
            var resolver = new CountingResolver(Input);
            transform.DocumentResolver = resolver;
            using var text = new CountingReader(Input);
            using var reader = XmlReader.Create(text);
            using var stream = new CountingStream(Input);
            long Read() => source switch { "reader" => text.Characters, "stream" => stream.Bytes, _ => resolver.Bytes };
            using var writer = new EarlyWriter(Read);
            using var bytes = new EarlyStream(Read);
            using var xml = XmlWriter.Create(writer, new XmlWriterSettings { ConformanceLevel = ConformanceLevel.Fragment });
            var uri = new Uri("http://forward.test/input.xml");
            string? raw = null;
            using var capture = new StreamingExecutionCapture();
            using (StreamingExecutionCapture.ObserveStage(StreamingExecutionCapture.Stage.Stylesheet))
            {
                switch (source, destination)
                {
                    case ("reader", "raw"): raw = string.Concat(transform.ApplyTemplates(reader)); break;
                    case ("reader", "stream"): transform.ApplyTemplates(reader, bytes); break;
                    case ("reader", "text"): transform.ApplyTemplates(reader, writer); break;
                    case ("reader", "xml"): transform.ApplyTemplates(reader, xml); break;
                    case ("stream", "raw"): raw = string.Concat(transform.ApplyTemplates(stream, uri)); break;
                    case ("stream", "stream"): transform.ApplyTemplates(stream, uri, bytes); break;
                    case ("stream", "text"): transform.ApplyTemplates(stream, uri, writer); break;
                    case ("stream", "xml"): transform.ApplyTemplates(stream, uri, xml); break;
                    case ("uri", "raw"): raw = string.Concat(transform.ApplyTemplates(uri)); break;
                    case ("uri", "stream"): transform.ApplyTemplates(uri, bytes); break;
                    case ("uri", "text"): transform.ApplyTemplates(uri, writer); break;
                    case ("uri", "xml"): transform.ApplyTemplates(uri, xml); break;
                }
            }
            xml.Flush();
            string actual = raw ?? (destination == "stream" ? Encoding.UTF8.GetString(bytes.ToArray()).TrimStart('﻿') : writer.ToString());
            AssertRows(actual, "1", Count);
            var execution = Assert.Single(capture.Snapshot().Executions, e => e.Stage == "Stylesheet");
            var initial = Assert.Single(execution.Sources, s => s.Kind == "Initial");
            Assert.Equal(forward, initial.ActualForward);
            Assert.Equal(forward, initial.NavigatorType == typeof(StreamedNavigator).FullName);
            if (raw == null)
                AssertWrittenEarly(destination == "stream" ? bytes.FirstWrite : writer.FirstWrite, Encoding.UTF8.GetByteCount(Input), forward, $"{source}/{destination}:");
        }
    }

    // S4-05: a named initial mode receives its parameters and tunnel parameters in forward execution, and
    // the template rules it reaches write their results while the input is still being read.
    [Fact]
    public void NamedModeParametersReachForwardTemplateRules()
    {
        string sheet = Sheet("<xsl:template match='/' mode='m'><xsl:param name='p' required='yes'/>"
            + "<xsl:apply-templates select='doc/record' mode='m'><xsl:with-param name='p' select='$p'/></xsl:apply-templates></xsl:template>"
            + "<xsl:template match='record' mode='m'><xsl:param name='p'/><xsl:param name='t' tunnel='yes'/>"
            + "<xsl:value-of select='$p, $t, @id'/>;</xsl:template>").Replace("<xsl:mode streamable=\"yes\"/>", "<xsl:mode name='m' streamable='yes'/>");
        string? tree = null;
        foreach (bool forward in new[] { false, true })
        {
            var transform = new XsltCompiler { Settings = new() { StreamedExecution = forward ? null : false } }.Compile(new StringReader(sheet)).CreateTransformer();
            using var source = new CountingReader(Input);
            using var reader = XmlReader.Create(source);
            using var output = new EarlyWriter(source);
            transform.ApplyTemplates(reader, output, new QName("m"),
                new Dictionary<QName, XdmValue> { [new QName("p")] = new XdmAtomicValue(7L) },
                new Dictionary<QName, XdmValue> { [new QName("t")] = new XdmAtomicValue(9L) });
            AssertRows(output.ToString(), "7 9 1", Count);
            AssertWrittenEarly(output.FirstWrite, Input.Length, forward);
            if (tree == null) tree = output.ToString(); else Assert.Equal(tree, output.ToString());
        }
    }

    // S4-18: a streamable attribute set used by a streamed template rule.
    [Fact]
    public void StreamableAttributeSetsRunInForwardTemplateRules()
    {
        string sheet = Sheet("<xsl:template match='record'><out xsl:use-attribute-sets='a'><xsl:value-of select='v'/></out></xsl:template>",
            "<xsl:attribute-set name='a' streamable='yes'><xsl:attribute name='n' select='name()'/><xsl:attribute name='id' select='@id'/></xsl:attribute-set>", "xml");
        AssertBothWays(sheet, output =>
        {
            Assert.StartsWith("<out n=\"record\" id=\"1\">1</out><out n=\"record\" id=\"2\">2</out>", output);
            Assert.Equal(Count, output.Split("<out ").Length - 1);
        });
    }

    // S4-29: xsl:next-match dispatches on the current streamed node without advancing past it.
    [Fact]
    public void NextMatchRunsForwardWithoutAdvancingPastTheCurrentNode()
    {
        string sheet = Sheet("<xsl:template match='record' priority='2'><xsl:value-of select='name()'/><xsl:next-match/></xsl:template>"
            + "<xsl:template match='record' priority='1'><xsl:value-of select='.'/>;</xsl:template>");
        AssertBothWays(sheet, output => AssertRows(output, "record1", Count));
    }

    // S4-30: the built-in rules of a streamable mode visit children and attributes forward.
    [Theory]
    [InlineData("shallow-copy", "xml", "<doc><record id=\"1\" g=\"0\"><v>1</v></record><record id=\"2\" g=\"0\"><v>2</v></record>")]
    [InlineData("text-only-copy", "text", "1234")]
    [InlineData("shallow-skip", "text", "1;2;3;")]
    public void BuiltinRulesVisitChildrenAndAttributesForward(string rule, string method, string prefix)
    {
        string templates = rule == "shallow-skip" ? "<xsl:template match='v'><xsl:value-of select='.'/>;</xsl:template>" : "";
        string sheet = Sheet(templates, method: method).Replace("<xsl:mode streamable=\"yes\"/>", $"<xsl:mode streamable='yes' on-no-match='{rule}'/>");
        AssertBothWays(sheet, output => Assert.StartsWith(prefix, output));
    }

    // S5-02: a template rule that never processes a record's children still lets that subtree contribute to
    // the accumulator, and each record's value is written while later records are unread.
    [Fact]
    public void SkippedSubtreesContributeToAccumulatorsWhileStreaming()
    {
        string sheet = Sheet("<xsl:template match='record'><xsl:value-of select=\"accumulator-before('n')\"/>;</xsl:template>",
            "<xsl:accumulator name='n' as='xs:integer' initial-value='0' streamable='yes'><xsl:accumulator-rule match='v' select='$value + 1'/></xsl:accumulator>")
            .Replace("<xsl:mode streamable=\"yes\"/>", "<xsl:mode streamable='yes' use-accumulators='n'/>");
        AssertBothWays(sheet, output =>
        {
            AssertRows(output, "0", Count);
            Assert.EndsWith((Count - 1) + ";", output);
        });
    }

    // S5-14: group members are consumed before the population advances, so the first group is written
    // while most groups are still unread.
    [Theory]
    [InlineData("group-adjacent='@g'")]
    [InlineData("group-starting-with='record[@id mod 2 = 1]'")]
    public void GroupMembersAreConsumedBeforeThePopulationAdvances(string grouping)
    {
        string sheet = Sheet($"<xsl:template match='doc'><xsl:for-each-group select='record' {grouping}>"
            + "<xsl:value-of select='position()'/>:<xsl:for-each select='current-group()'><xsl:value-of select='v'/>,</xsl:for-each>;"
            + "</xsl:for-each-group></xsl:template>");
        AssertBothWays(sheet, output => AssertRows(output, "1:1,2,", Count / 2));
    }

    // S6-10: raw items from a fork prong are copied before the source advances past them. Fork holds each
    // prong's result until the fork completes, to keep declaration order (S6-08), so the first write comes late
    // by design. The evidence is that the run read a streamed navigator, which cannot revisit a record, and
    // still serialized every record in full.
    [Fact]
    public void ForkPrincipalRawItemsAreCopiedBeforeTheSourceAdvances()
    {
        string sheet = Sheet("<xsl:template match='doc'><xsl:fork><xsl:sequence select='()'/><xsl:sequence>"
            + "<xsl:result-document method='xml' omit-xml-declaration='yes' item-separator='|'><xsl:sequence select='record'/></xsl:result-document>"
            + "</xsl:sequence></xsl:fork></xsl:template>", method: "xml");
        string? tree = null;
        foreach (bool forward in new[] { false, true })
        {
            var transform = new XslCompiledTransform();
            using (var stylesheet = XmlReader.Create(new StringReader(sheet)))
                transform.Load(stylesheet, new XsltSettings { StreamedExecution = forward ? null : false }, null);
            using var reader = XmlReader.Create(new StringReader(Input));
            using var output = new StringWriter();
            using var capture = new StreamingExecutionCapture();
            using (StreamingExecutionCapture.ObserveStage(StreamingExecutionCapture.Stage.Stylesheet))
                transform.Transform(reader, null, output);
            string text = output.ToString();
            Assert.StartsWith("<record id=\"1\" g=\"0\"><v>1</v></record>|<record id=\"2\"", text);
            Assert.Equal(Count, text.Split('|').Length);
            if (tree == null) tree = text; else Assert.Equal(tree, text);
            var execution = Assert.Single(capture.Snapshot().Executions, e => e.Stage == "Stylesheet");
            var initial = Assert.Single(execution.Sources);
            Assert.Equal(forward, initial.ActualForward);
            Assert.Equal(forward, initial.NavigatorType == typeof(StreamedNavigator).FullName);
        }
    }

    // S4-06: a supplied XdmNode remains a tree even when forward execution is enabled. Check every
    // destination and reuse the selection, so the representation assertion is accompanied by repeatability.
    [Theory]
    [InlineData("raw")]
    [InlineData("stream")]
    [InlineData("text")]
    [InlineData("xml")]
    public void XdmSelectionKeepsItsTreeRepresentationForEveryDestination(string destination)
    {
        var selection = XdmNode.Load(new StringReader(Input));
        string sheet = Sheet("<xsl:template match='/'><xsl:value-of select='count(doc/record)'/></xsl:template>");
        foreach (bool forward in new[] { false, true })
        {
            var transform = new XsltCompiler { Settings = new() { StreamedExecution = forward ? null : false } }
                .Compile(new StringReader(sheet)).CreateTransformer();
            using var text = new StringWriter();
            using var bytes = new MemoryStream();
            using var xml = XmlWriter.Create(text, new XmlWriterSettings { ConformanceLevel = ConformanceLevel.Fragment });
            string? raw = null;
            using var capture = new StreamingExecutionCapture();
            using (StreamingExecutionCapture.ObserveStage(StreamingExecutionCapture.Stage.Stylesheet))
            {
                switch (destination)
                {
                    case "raw": raw = transform.ApplyTemplates(selection).ToString(); break;
                    case "stream": transform.ApplyTemplates(selection, bytes); break;
                    case "text": transform.ApplyTemplates(selection, text); break;
                    case "xml": transform.ApplyTemplates(selection, xml); break;
                }
            }
            xml.Flush();
            string actual = raw ?? (destination == "stream"
                ? Encoding.UTF8.GetString(bytes.ToArray()).TrimStart('\uFEFF') : text.ToString());
            Assert.Equal(Count.ToString(System.Globalization.CultureInfo.InvariantCulture), actual);
            AssertRepresentation(capture, forward: false);
            Assert.False(Assert.Single(capture.Snapshot().Executions).ForwardInput);
        }
    }

    // S4-11: opting in cannot stream a 1.0 stylesheet, and opting out keeps a 3.0 stylesheet on a tree.
    [Theory]
    [InlineData("1.0", false, false)]
    [InlineData("1.0", false, true)]
    [InlineData("1.0", true, false)]
    [InlineData("1.0", true, true)]
    [InlineData("3.0", false, false)]
    [InlineData("3.0", false, true)]
    public void TreeEntryContractsKeepTheirActualRepresentation(string version, bool forward, bool legacy)
    {
        string sheet = Sheet("<xsl:template match='/'><xsl:value-of select='count(doc/record)'/></xsl:template>");
        if (version == "1.0")
            sheet = sheet.Replace("version=\"3.0\"", "version=\"1.0\"").Replace("<xsl:mode streamable=\"yes\"/>", "");
        var settings = new XsltSettings { StreamedExecution = forward ? null : false };
        using var source = new CountingReader(Input);
        using var reader = XmlReader.Create(source);
        using var output = new EarlyWriter(source);
        using var capture = new StreamingExecutionCapture();
        using (StreamingExecutionCapture.ObserveStage(StreamingExecutionCapture.Stage.Stylesheet))
        {
            if (legacy)
            {
                var transform = new XslCompiledTransform();
                using var stylesheet = XmlReader.Create(new StringReader(sheet));
                transform.Load(stylesheet, settings, null);
                transform.Transform(reader, null, output);
            }
            else
                new XsltCompiler { Settings = settings }.Compile(new StringReader(sheet))
                    .CreateTransformer().ApplyTemplates(reader, output);
        }
        Assert.Equal(Count.ToString(System.Globalization.CultureInfo.InvariantCulture), output.ToString());
        AssertWrittenEarly(output.FirstWrite, Input.Length, forward: false);
        AssertRepresentation(capture, forward: false);
        Assert.NotEqual(ReadState.Closed, reader.ReadState);
    }

    // S4-27: validation supplies annotations and a typed default attribute before a record is consumed.
    // Both public reader entry points must preserve those annotations while still producing early output.
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ValidatedInitialReadersKeepAnnotationsAndForwardExecution(bool forward, bool legacy)
    {
        const string Schema = """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
              <xs:element name="doc"><xs:complexType><xs:sequence>
                <xs:element name="record" type="xs:integer" maxOccurs="unbounded"/>
              </xs:sequence><xs:attribute name="version" type="xs:integer" default="7"/></xs:complexType></xs:element>
            </xs:schema>
            """;
        var schemas = new XmlSchemaSet();
        using (var schemaReader = XmlReader.Create(new StringReader(Schema))) schemas.Add(null, schemaReader);
        string input = Records(Count, i => $"<record>{i}</record>");
        string sheet = Sheet("<xsl:template match='record'><xsl:value-of "
            + "select='. instance of element(*, xs:integer), "
            + "../@version instance of attribute(*, xs:integer), ../@version'/>;</xsl:template>",
            "<xsl:import-schema>" + Schema + "</xsl:import-schema>");
        var settings = new XsltSettings { StreamedExecution = forward ? null : false, SchemaAware = true };
        using var source = new CountingReader(input);
        using var reader = XmlReader.Create(source, new XmlReaderSettings { ValidationType = ValidationType.Schema, Schemas = schemas });
        using var output = new EarlyWriter(source);
        using var capture = new StreamingExecutionCapture();
        using (StreamingExecutionCapture.ObserveStage(StreamingExecutionCapture.Stage.Stylesheet))
        {
            if (legacy)
            {
                var transform = new XslCompiledTransform();
                using var stylesheet = XmlReader.Create(new StringReader(sheet));
                transform.Load(stylesheet, settings, null);
                transform.Transform(reader, null, output);
            }
            else
                new XsltCompiler { Settings = settings }.Compile(new StringReader(sheet))
                    .CreateTransformer().ApplyTemplates(reader, output);
        }
        string defaultVersion = schemas.GlobalElements[new XmlQualifiedName("doc")] is XmlSchemaElement element
            && element.ElementSchemaType is XmlSchemaComplexType type
            ? ((XmlSchemaAttribute)type.AttributeUses[new XmlQualifiedName("version")]!).DefaultValue!
            : throw new InvalidOperationException("The validation fixture must supply the default attribute");
        Assert.Equal(string.Concat(Enumerable.Repeat($"true true {defaultVersion};", Count)), output.ToString());
        AssertWrittenEarly(output.FirstWrite, input.Length, forward);
        AssertRepresentation(capture, forward);
        Assert.NotEqual(ReadState.Closed, reader.ReadState);
    }

    private static void AssertRepresentation(StreamingExecutionCapture capture, bool forward)
    {
        var execution = Assert.Single(capture.Snapshot().Executions, e => e.Stage == "Stylesheet");
        Assert.True(execution.BodyCompleted);
        var source = Assert.Single(execution.Sources);
        Assert.Equal("Opened", source.Status);
        Assert.Equal(forward, source.ActualForward);
        Assert.Equal(forward, source.NavigatorType == typeof(StreamedNavigator).FullName);
    }

    private sealed class CountingResolver(string xml) : XmlResolver
    {
        private readonly List<CountingStream> _opened = new();
        internal long Bytes => _opened.Sum(s => s.Bytes);
        public override ICredentials? Credentials { set { } }
        public override object GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn)
        {
            var stream = new CountingStream(xml);
            _opened.Add(stream);
            return stream;
        }
    }
}
