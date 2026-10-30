// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Xml;
using Xsl3.Streaming;
using Xunit;

namespace Xsl3.Tests;

public class StreamingExecutionCaptureTests
{
    private const string Stylesheet = """
        <xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform'>
          <xsl:mode streamable='yes'/><xsl:output method='text'/>
          <xsl:template match='/'><xsl:value-of select='count(doc/n)'/></xsl:template>
        </xsl:stylesheet>
        """;

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public void BothApisReportTheActualInputRepresentation(bool api, bool forward, bool dedicated)
    {
        var settings = new XsltSettings { StreamedExecution = forward, UseDedicatedThread = dedicated };
        using var capture = new StreamingExecutionCapture();
        using var input = XmlReader.Create(new StringReader("<doc><n/><n/></doc>"));
        string text;
        if (api)
        {
            var executable = new XsltCompiler { Settings = settings }.Compile(new StringReader(Stylesheet));
            using var output = new MemoryStream();
            executable.CreateTransformer().ApplyTemplates(input, output);
            text = Encoding.UTF8.GetString(output.ToArray()).TrimStart('\uFEFF');
        }
        else
        {
            var unit = new XslCompiledTransform();
            using var stylesheet = XmlReader.Create(new StringReader(Stylesheet));
            unit.Load(stylesheet, settings, null);
            using var output = new StringWriter();
            unit.Transform(input, null, output);
            text = output.ToString();
        }
        Assert.Equal("2", text);
        var report = capture.Snapshot();
        Assert.Equal(1, report.Started);
        var execution = Assert.Single(report.Executions);
        Assert.True(execution.BodyCompleted);
        Assert.Equal(forward, execution.StreamedInitialMode);
        Assert.True(execution.ForwardInput);
        Assert.Equal("reader", execution.InputKind);
        var source = Assert.Single(execution.Sources);
        Assert.Equal(api || forward ? "Initial" : "Default", source.Kind);
        Assert.Equal(forward, source.RequestedForward);
        Assert.Equal(forward, source.ActualForward);
        Assert.Equal(forward, source.NavigatorType == typeof(StreamedNavigator).FullName);
    }

    [Fact]
    public void CapturingRepresentationDoesNotReadTheSource()
    {
        using var input = new CountingReader(XmlReader.Create(new StringReader("<unclosed>")));
        using var document = new StreamedDocument(input);
        using var capture = new StreamingExecutionCapture();
        using (var execution = StreamingExecutionCapture.BeginExecution(true, input))
        {
            StreamingExecutionCapture.RecordSource(StreamingExecutionCapture.SourceKind.Initial, true, document.CreateNavigator());
            execution!.CompleteBody();
        }
        Assert.Equal(0, input.Reads);
        Assert.True(Assert.Single(Assert.Single(capture.Snapshot().Executions).Sources).ActualForward);
    }

    [Theory]
    [InlineData("api-uri")]
    [InlineData("api-stream")]
    [InlineData("legacy-uri")]
    public void OtherPublicInputPathsKeepTheirActualForwardRepresentation(string path)
    {
        var settings = new XsltSettings { StreamedExecution = true };
        using var capture = new StreamingExecutionCapture();
        using var output = new StringWriter();
        if (path.StartsWith("api-", StringComparison.Ordinal))
        {
            var transformer = new XsltCompiler { Settings = settings }.Compile(new StringReader(Stylesheet)).CreateTransformer();
            transformer.DocumentResolver = new SourceResolver();
            if (path == "api-uri") transformer.ApplyTemplates(new Uri("urn:capture:source"), output);
            else
            {
                using var input = new MemoryStream(Encoding.UTF8.GetBytes("<doc><n/><n/></doc>"));
                transformer.ApplyTemplates(input, null, output);
                Assert.True(input.CanRead);
            }
        }
        else
        {
            string file = Path.GetTempFileName();
            try
            {
                File.WriteAllText(file, "<doc><n/><n/></doc>");
                var unit = new XslCompiledTransform();
                using var stylesheet = XmlReader.Create(new StringReader(Stylesheet));
                unit.Load(stylesheet, settings, null);
                unit.Transform(file, null, output);
            }
            finally { File.Delete(file); }
        }
        Assert.Equal("2", output.ToString());
        var execution = Assert.Single(capture.Snapshot().Executions);
        Assert.True(execution.BodyCompleted);
        Assert.Equal(path == "api-uri" ? "uri" : path == "api-stream" ? "stream" : "reader", execution.InputKind);
        var source = Assert.Single(execution.Sources);
        Assert.True(source.RequestedForward);
        Assert.True(source.ActualForward);
    }

    [Fact]
    public void AnExecutionThatFailsBeforeOpeningInputIsNotReportedAsForwarded()
    {
        var unit = new XslCompiledTransform();
        using var stylesheet = XmlReader.Create(new StringReader(Stylesheet.Replace("<xsl:mode", "<xsl:global-context-item use='required'/><xsl:mode", StringComparison.Ordinal)));
        unit.Load(stylesheet, new XsltSettings { StreamedExecution = true }, null);
        using var input = new CountingReader(XmlReader.Create(new StringReader("<unclosed>")));
        using var capture = new StreamingExecutionCapture();
        using var output = new StringWriter();
        var error = Assert.ThrowsAny<Exception>(() => unit.Transform(input, null, output));
        Assert.Contains("XTDE3086", error.ToString(), StringComparison.Ordinal);
        var execution = Assert.Single(capture.Snapshot().Executions);
        Assert.True(execution.StreamedInitialMode);
        Assert.False(execution.BodyCompleted);
        Assert.Empty(execution.Sources);
        Assert.Equal(0, input.Reads);
    }

    [Fact]
    public void SourceDocumentsOpenedByForkWorkersBelongToTheCallingCapture()
    {
        var unit = new XslCompiledTransform();
        using var stylesheet = XmlReader.Create(new StringReader("""
            <xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform'>
              <xsl:mode streamable='yes'/><xsl:output method='text'/>
              <xsl:template match='/'><xsl:fork>
                <xsl:sequence><xsl:source-document href='urn:capture:source' streamable='yes'><xsl:value-of select='count(doc/n)'/></xsl:source-document></xsl:sequence>
                <xsl:sequence><xsl:source-document href='urn:capture:source' streamable='yes'><xsl:value-of select='count(doc/n)'/></xsl:source-document></xsl:sequence>
              </xsl:fork></xsl:template>
            </xsl:stylesheet>
            """));
        unit.Load(stylesheet, new XsltSettings { StreamedExecution = true, EnableDocumentFunction = true }, null);
        using var input = XmlReader.Create(new StringReader("<doc/>"));
        using var capture = new StreamingExecutionCapture();
        using var output = new StringWriter();
        unit.Transform(input, null, output, new SourceResolver());
        Assert.Equal("22", output.ToString());
        var execution = Assert.Single(capture.Snapshot().Executions);
        Assert.True(execution.BodyCompleted);
        var sources = execution.Sources.Where(s => s.Kind == "SourceDocument").ToArray();
        Assert.Equal(2, sources.Length);
        Assert.All(sources, s => { Assert.True(s.RequestedForward); Assert.True(s.ActualForward); });
    }

    [Fact]
    public void AFailedInitialUriDoesNotClaimAForwardNavigator()
    {
        var transformer = new XsltCompiler { Settings = new XsltSettings { StreamedExecution = true } }
            .Compile(new StringReader(Stylesheet)).CreateTransformer();
        transformer.DocumentResolver = new SourceResolver(fail: true);
        using var capture = new StreamingExecutionCapture();
        using var output = new StringWriter();
        var error = Assert.ThrowsAny<Exception>(() => transformer.ApplyTemplates(new Uri("urn:capture:source"), output));
        Assert.Contains("FODC0002", error.ToString(), StringComparison.Ordinal);
        var execution = Assert.Single(capture.Snapshot().Executions);
        Assert.False(execution.BodyCompleted);
        var source = Assert.Single(execution.Sources);
        Assert.Equal("Initial", source.Kind);
        Assert.Equal("Failed", source.Status);
        Assert.True(source.RequestedForward);
        Assert.Null(source.ActualForward);
        Assert.Null(source.NavigatorType);
    }

    [Fact]
    public void ACaughtSourceDocumentFailureRemainsVisibleInASuccessfulExecution()
    {
        var unit = new XslCompiledTransform();
        using var stylesheet = XmlReader.Create(new StringReader("""
            <xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform'>
              <xsl:mode streamable='yes'/><xsl:output method='text'/>
              <xsl:template match='/'><xsl:try>
                <xsl:source-document href='urn:capture:source' streamable='yes'><xsl:value-of select='count(doc/n)'/></xsl:source-document>
                <xsl:catch><xsl:text>caught</xsl:text></xsl:catch>
              </xsl:try></xsl:template>
            </xsl:stylesheet>
            """));
        unit.Load(stylesheet, new XsltSettings { StreamedExecution = true, EnableDocumentFunction = true }, null);
        using var input = XmlReader.Create(new StringReader("<doc/>"));
        using var capture = new StreamingExecutionCapture();
        using var output = new StringWriter();
        unit.Transform(input, null, output, new SourceResolver(fail: true));
        Assert.Equal("caught", output.ToString());
        var execution = Assert.Single(capture.Snapshot().Executions);
        Assert.True(execution.BodyCompleted);
        var source = Assert.Single(execution.Sources, s => s.Kind == "SourceDocument");
        Assert.Equal("Failed", source.Status);
        Assert.Null(source.ActualForward);
        Assert.Null(source.NavigatorType);
    }

    [Fact]
    public void NestedCapturesRestoreTheirParentAndKeepIncompleteExecutionsVisible()
    {
        using var input = XmlReader.Create(new StringReader("<doc/>"));
        using var outer = new StreamingExecutionCapture();
        using (var execution = StreamingExecutionCapture.BeginExecution(true, input))
        {
            using (var inner = new StreamingExecutionCapture())
            {
                using (StreamingExecutionCapture.BeginExecution(false, input)) { }
                Assert.False(Assert.Single(inner.Snapshot().Executions).BodyCompleted);
            }
            using (var nested = StreamingExecutionCapture.BeginExecution(false, input)) nested!.CompleteBody();
            Assert.Equal(2, outer.Snapshot().Started);
            Assert.Single(outer.Snapshot().Executions); // the outer execution is still active
            execution!.CompleteBody();
        }
        var entries = outer.Snapshot().Executions.OrderBy(e => e.Id).ToArray();
        Assert.Equal(2, entries.Length);
        Assert.Null(entries[0].Parent);
        Assert.Equal(entries[0].Id, entries[1].Parent);
        Assert.All(entries, e => Assert.True(e.BodyCompleted));
    }

    private sealed class CountingReader(XmlReader reader) : XmlWrappingReader(reader)
    {
        internal int Reads;
        public override bool Read() { Reads++; return base.Read(); }
    }

    private sealed class SourceResolver(bool fail = false) : XmlResolver
    {
        public override ICredentials? Credentials { set { } }
        public override object GetEntity(Uri uri, string? role, Type? type)
        {
            if (uri.OriginalString != "urn:capture:source") throw new XmlException("Unexpected fixture source: " + uri);
            if (fail) throw new IOException("Fixture source retrieval failed");
            return new MemoryStream(Encoding.UTF8.GetBytes("<doc><n/><n/></doc>"));
        }
    }
}
