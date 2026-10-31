// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml;
using Xsl3.W3C.Qt3;
using Xunit;
using Catalog = Xsl3.W3C.Xslt30.Catalog;

namespace Xsl3.W3C;

public sealed class RuntimeCaseReportTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "xsl3-runtime-report-" + Guid.NewGuid().ToString("N"));

    public RuntimeCaseReportTests()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "catalog.xml"), $"""
            <catalog xmlns="{Catalog.Ns}"><test-set name="si-copy" file="set.xml"/></catalog>
            """);
        File.WriteAllText(Path.Combine(_root, "set.xml"), $"""
            <test-set xmlns="{Catalog.Ns}" name="si-copy"><test-case name="example">
              <test/><result><assert>true()</assert></result>
            </test-case></test-set>
            """);
    }

    [Fact]
    public void DisposingAnInterruptedRunDoesNotClaimCompletion()
    {
        string path = Path.Combine(_root, "interrupted.jsonl");
        using (var report = new RuntimeCaseReport(path, _root, "strm", true))
            report.Record(Catalog.Load(_root).TestCases.Single(), Outcome.Fail("actual diagnostic"));
        string[] lines = File.ReadAllLines(path);
        Assert.Equal(2, lines.Length);
        using JsonDocument row = JsonDocument.Parse(lines[1]);
        Assert.Equal("case", row.RootElement.GetProperty("Kind").GetString());
        Assert.Equal("actual diagnostic", row.RootElement.GetProperty("Detail").GetString());
        Assert.Equal("Fail", row.RootElement.GetProperty("Outcome").GetString());
    }

    [Fact]
    public void AnExistingRunIsPreserved()
    {
        string path = Path.Combine(_root, "previous.jsonl");
        File.WriteAllText(path, "retained evidence");
        Assert.Throws<IOException>(() => new RuntimeCaseReport(path, _root, "strm", true));
        Assert.Equal("retained evidence", File.ReadAllText(path));
    }

    [Fact]
    public void ACompletedRunRecordsIdentityFingerprintsAndTotals()
    {
        string path = Path.Combine(_root, "complete.jsonl");
        using (var report = new RuntimeCaseReport(path, _root, "strm", true))
        {
            report.Record(Catalog.Load(_root).TestCases.Single(), Outcome.Pass());
            report.Complete(1, 0, 0, 0, new Dictionary<string, int>());
        }
        string[] lines = File.ReadAllLines(path);
        Assert.Equal(3, lines.Length);
        using JsonDocument row = JsonDocument.Parse(lines[1]);
        Assert.Equal("si-copy/example", row.RootElement.GetProperty("Case").GetString());
        Assert.Equal("set.xml", row.RootElement.GetProperty("SetFile").GetString());
        Assert.Equal(64, row.RootElement.GetProperty("DefinitionSha256").GetString()!.Length);
        using JsonDocument complete = JsonDocument.Parse(lines[2]);
        Assert.Equal("complete", complete.RootElement.GetProperty("Kind").GetString());
        Assert.Equal(1, complete.RootElement.GetProperty("Selected").GetInt32());
        Assert.Equal(0, complete.RootElement.GetProperty("UnmatchedExpectedErrorCodes").GetInt32());
    }

    [Fact]
    public void ForwardingEnabledReportsRequireAnActualCaseCapture()
    {
        using var report = new RuntimeCaseReport(Path.Combine(_root, "missing-capture.jsonl"), _root, "strm", true, captureSources: true);
        Assert.Throws<InvalidOperationException>(() => report.Record(Catalog.Load(_root).TestCases.Single(), Outcome.Pass()));
    }

    [Fact]
    public void EvaluationEnabledReportsRequireTheirOwnCaseCapture()
    {
        using var report = new RuntimeCaseReport(Path.Combine(_root, "missing-evaluation.jsonl"), _root, "strm", true, captureEvaluation: true);
        Assert.Throws<InvalidOperationException>(() => report.Record(Catalog.Load(_root).TestCases.Single(), Outcome.Pass()));
    }

    [Fact]
    public void EvaluationCapturesDoNotShareLateResultsOrMutableCompilerDiagnostics()
    {
        var previous = new RuntimeCaseReport.EvaluationCapture();
        var next = new RuntimeCaseReport.EvaluationCapture();
        next.Observe(null, null);
        var diagnostic = new Xsl3.Xslt.CompilerError("urn:original", 3, 7, "", "XTSE0010: original error");
        var errors = new Xsl3.Xslt.CompilerErrorCollection { diagnostic };
        previous.Observe(new InvalidOperationException("outer message mentions XTSE3430"), errors);
        diagnostic.ErrorText = "XTSE3430: changed error";
        Assert.Equal("Returned", next.Snapshot().Status);
        var actual = previous.Snapshot();
        Assert.Equal("Error", actual.Status);
        Assert.Equal("Q{http://www.w3.org/2005/xqt-errors}XTSE0010", actual.Diagnostic!.Code);
        Assert.Equal("urn:original", actual.Diagnostic.ModuleUri);
        Assert.Equal(3, actual.Diagnostic.Line);
        Assert.Equal(7, actual.Diagnostic.Column);
        Assert.Throws<InvalidOperationException>(() => previous.Observe(null, null));
    }

    [Theory]
    [InlineData("static", "Error", "XTSE0545")]
    [InlineData("dynamic", "Error", "FOAR0001")]
    [InlineData("result", "Returned", null)]
    [InlineData("skip", "NotObserved", null)]
    public void ActualEvaluationIsRecordedBeforeCheckingTheExpectedAssertion(string kind, string status, string? code)
    {
        File.WriteAllText(Path.Combine(_root, "source.xml"), "<doc/>");
        string declarations = kind == "static" ? "<xsl:mode name='m' streamable='yes'/><xsl:mode name='m' streamable='no'/>" : "";
        string expression = kind == "dynamic" ? "1 div 0" : "42";
        File.WriteAllText(Path.Combine(_root, "style.xsl"), $$"""
            <xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform'>
              {{declarations}}<xsl:template match='/'><xsl:sequence select='{{expression}}'/></xsl:template>
            </xsl:stylesheet>
            """);
        string unsupported = kind == "skip" ? "<collection uri='urn:unavailable'/>" : "";
        string assertion = code == null ? "<assert>true()</assert>" : "<error code='" + code + "'/>";
        File.WriteAllText(Path.Combine(_root, "set.xml"), $$"""
            <test-set xmlns="{{Catalog.Ns}}" name="si-copy"><test-case name="evaluation">
              <environment><source role="." file="source.xml" streaming="yes"/>{{unsupported}}</environment>
              <test><stylesheet file="style.xsl"/></test><result>{{assertion}}</result>
            </test-case></test-set>
            """);
        var test = Catalog.Load(_root).TestCases.Single();
        var runner = new Xsl3.W3C.Xslt30.Runner();
        Outcome control = runner.Run(test);
        string path = Path.Combine(_root, "evaluation.jsonl");
        using (var report = new RuntimeCaseReport(path, _root, "strm", true, captureEvaluation: true))
        {
            var capture = report.CaptureEvaluation()!;
            Outcome observed = runner.Run(test, capture.Observe);
            Assert.Equal(control, observed);
            report.Record(test, observed, evaluation: capture);
        }
        using var row = JsonDocument.Parse(File.ReadAllLines(path)[1]);
        var evidence = row.RootElement.GetProperty("Evaluation");
        Assert.Equal(status, evidence.GetProperty("Status").GetString());
        if (code != null)
        {
            var diagnostic = evidence.GetProperty("Diagnostic");
            Assert.Equal("Q{http://www.w3.org/2005/xqt-errors}" + code, diagnostic.GetProperty("Code").GetString());
            Assert.Equal(kind == "static", diagnostic.GetProperty("IsStatic").GetBoolean());
        }
        else Assert.Equal(JsonValueKind.Null, evidence.GetProperty("Diagnostic").ValueKind);
        Assert.True(row.RootElement.GetProperty("DeclaredInitialSources")[0].GetProperty("Streaming").GetBoolean());
    }

    [Fact]
    public void ForwardingReportRetainsTheRuntimeRepresentation()
    {
        string path = Path.Combine(_root, "forwarding.jsonl");
        using (var report = new RuntimeCaseReport(path, _root, "strm", true, captureSources: true))
        using (var capture = report.CaptureCase())
        {
            var unit = new XslCompiledTransform();
            using var stylesheet = XmlReader.Create(new StringReader("""
                <xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform'>
                  <xsl:mode streamable='yes'/><xsl:template match='/'><out><xsl:value-of select='count(doc/n)'/></out></xsl:template>
                </xsl:stylesheet>
                """));
            unit.Load(stylesheet, new XsltSettings { StreamedExecution = true }, null);
            using var input = XmlReader.Create(new StringReader("<doc><n/></doc>"));
            using var output = new StringWriter();
            using (Xsl3.Streaming.StreamingExecutionCapture.ObserveStage(Xsl3.Streaming.StreamingExecutionCapture.Stage.Stylesheet))
                unit.Transform(input, null, output);
            report.Record(Catalog.Load(_root).TestCases.Single(), Outcome.Pass(), capture);
        }
        string[] lines = File.ReadAllLines(path);
        using JsonDocument header = JsonDocument.Parse(lines[0]), row = JsonDocument.Parse(lines[1]);
        Assert.True(header.RootElement.GetProperty("ForwardingCaptured").GetBoolean());
        var evidence = row.RootElement.GetProperty("Forwarding");
        Assert.Equal(2, evidence.GetProperty("FormatVersion").GetInt32());
        Assert.Equal(1, evidence.GetProperty("Started").GetInt32());
        var execution = evidence.GetProperty("Executions")[0];
        Assert.Equal("Stylesheet", execution.GetProperty("Stage").GetString());
        Assert.True(execution.GetProperty("BodyCompleted").GetBoolean());
        var source = execution.GetProperty("Sources")[0];
        Assert.True(source.GetProperty("ActualForward").GetBoolean());
        Assert.Equal("Xsl3.Streaming.StreamedNavigator", source.GetProperty("NavigatorType").GetString());
    }

    [Fact]
    public void RunnerSeparatesStylesheetCommandsFromCompilationAndAssertions()
    {
        File.WriteAllText(Path.Combine(_root, "source.xml"), "<doc><n/></doc>");
        File.WriteAllText(Path.Combine(_root, "style.xsl"), """
            <xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform'>
              <xsl:param name='flag' static='yes' select='true()'/>
              <xsl:template match='/'><out use-when='$flag'><xsl:value-of select='count(doc/n)'/></out></xsl:template>
            </xsl:stylesheet>
            """);
        File.WriteAllText(Path.Combine(_root, "set.xml"), $"""
            <test-set xmlns="{Catalog.Ns}" name="si-copy"><test-case name="stages">
              <environment><source role="." file="source.xml"/></environment>
              <test><stylesheet file="style.xsl"/></test>
              <result><assert>/out = '1'</assert></result>
            </test-case></test-set>
            """);
        using var capture = new Xsl3.Streaming.StreamingExecutionCapture();
        Outcome outcome = new Xsl3.W3C.Xslt30.Runner().Run(Catalog.Load(_root).TestCases.Single());
        Assert.Equal(OutcomeKind.Pass, outcome.Kind);
        var executions = capture.Snapshot().Executions;
        Assert.Contains(executions, e => e.Stage == "Compilation");
        Assert.Contains(executions, e => e.Stage == "Stylesheet");
        Assert.Contains(executions, e => e.Stage == "Assertion");
        Assert.DoesNotContain(executions, e => e.Stage == "Unclassified");
    }

    [Fact]
    public void DeferredResultReplayKeepsItsStylesheetStageWhenAnAssertionInvokesIt()
    {
        var transform = new XslCompiledTransform();
        using (var stylesheet = XmlReader.Create(new StringReader("""
            <xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform'>
              <xsl:mode streamable='yes'/><xsl:template match='/'><out><xsl:value-of select='count(doc/n)'/></out></xsl:template>
            </xsl:stylesheet>
            """))) transform.Load(stylesheet, new XsltSettings { StreamedExecution = true }, null);
        using var capture = new Xsl3.Streaming.StreamingExecutionCapture();
        var replay = Xsl3.W3C.Xslt30.Runner.ReplayResultTree(writer => {
            using var input = XmlReader.Create(new StringReader("<doc><n/></doc>"));
            transform.Transform(input, null, writer);
        }, new List<string>(), new Dictionary<string, StringWriter>());
        Assert.Equal(0, capture.Snapshot().Started);
        using (Xsl3.Streaming.StreamingExecutionCapture.ObserveStage(Xsl3.Streaming.StreamingExecutionCapture.Stage.Assertion))
        {
            var first = replay();
            Assert.Equal("1", first.Value);
            Assert.True(first.IsSamePosition(replay()));
        }
        var execution = Assert.Single(capture.Snapshot().Executions);
        Assert.Equal("Stylesheet", execution.Stage);
        Assert.True(Assert.Single(execution.Sources).ActualForward);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
