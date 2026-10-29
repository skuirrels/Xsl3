// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
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

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
