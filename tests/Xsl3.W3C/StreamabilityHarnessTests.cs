// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xsl3.W3C.Qt3;
using Xsl3.W3C.Xslt30;
using Xunit;
using Catalog = Xsl3.W3C.Xslt30.Catalog;
using Profile = Xsl3.W3C.Xslt30.Profile;
using Runner = Xsl3.W3C.Xslt30.Runner;
using TestCase = Xsl3.W3C.Xslt30.TestCase;

namespace Xsl3.W3C;

public class StreamabilityHarnessTests
{
    [Fact]
    public void ChecksEveryExpressionWithoutAPrincipalStylesheet()
    {
        WithCase("""
            <posture-and-sweep context-posture="striding"><xpath part="1">a</xpath><xpath part="2">@code</xpath></posture-and-sweep>
            """, "Striding", "consuming", test =>
        {
            Outcome outcome = new Runner().Run(test);
            Assert.Equal(OutcomeKind.Fail, outcome.Kind);
            Assert.Contains("part 2", outcome.Detail);
            Assert.Contains("Motionless", outcome.Detail);
        });
    }

    [Fact]
    public void AuditIncludesEveryExpressionBeforeTheFirstAssertionMismatch()
    {
        WithCase("<posture-and-sweep context-posture='striding'><xpath part='1'>a</xpath><xpath part='2'>@code</xpath></posture-and-sweep>",
            "grounded", "motionless", test =>
        {
            string[]? expressions = null;
            var runner = new Runner { AnalysisCompleted = (_, values) => expressions = values.Select(v => v.Expression.Value).ToArray() };
            Assert.Equal(OutcomeKind.Fail, runner.Run(test).Kind);
            Assert.Equal(new[] { "a", "@code" }, expressions);
        });
    }

    [Fact]
    public void StaticAuditDoesNotMatchAMessageMentionOrADynamicError()
    {
        Assert.False(Xslt30RunTests.MatchesStaticCode(Xslt30RunTests.PrimaryDiagnostic(
            new InvalidOperationException("Expected XTSE3430 but the harness failed")), "XTSE3430"));
        Assert.False(Xslt30RunTests.MatchesStaticCode(Xslt30RunTests.PrimaryDiagnostic(
            new XsltDynamicException(new Xdm.QName("http://www.w3.org/2005/xqt-errors", "XTSE3430"), "wrong phase")), "XTSE3430"));
    }

    [Fact]
    public void StaticAuditUsesThePrimaryErrorAndRetainsItsLocation()
    {
        var warning = new XsltStaticError(new Xsl3.Xslt.CompilerError("test.xsl", 1, 2, "", "XTSE3430: warning") { IsWarning = true });
        var first = new XsltStaticError(new Xsl3.Xslt.CompilerError("test.xsl", 3, 4, "", "XTSE0730: before XTSE3430"));
        var second = new XsltStaticError(new Xsl3.Xslt.CompilerError("test.xsl", 5, 6, "", "XTSE3430: later error"));
        var exception = new XsltStaticException(new[] { warning, first, second }, null);
        var actual = Xslt30RunTests.PrimaryDiagnostic(new Exception("wrapper mentions XTSE3430", exception));
        Assert.False(Xslt30RunTests.MatchesStaticCode(actual, "XTSE3430"));
        Assert.True(Xslt30RunTests.MatchesStaticCode(actual, "XTSE0730"));
        Assert.Equal("test.xsl", actual.ModuleUri);
        Assert.Equal(3, actual.Line);
        Assert.Equal(4, actual.Column);
        Assert.Equal(first.Message, actual.Reason);
    }

    [Theory]
    [InlineData("item()", "child::node()", "striding", "consuming")]
    [InlineData("node()", "child::node()", "striding", "consuming")]
    [InlineData("document-node()", "parent::node()", "grounded", "motionless")]
    [InlineData("element()", "@*", "striding", "motionless")]
    [InlineData("atomic", "child::*", "grounded", "motionless")]
    [InlineData("childless-node-kind", "child::*", "grounded", "motionless")]
    public void UsesTheCatalogContextType(string contextType, string expression, string posture, string sweep)
    {
        WithCase($"<posture-and-sweep context-posture='striding' context-type='{contextType}'><xpath>{expression}</xpath></posture-and-sweep>",
            posture, sweep, test => Assert.Equal(OutcomeKind.Pass, new Runner().Run(test).Kind));
    }

    [Theory]
    [InlineData("<posture-and-sweep context-posture='striding'/>", "no expressions")]
    [InlineData("<posture-and-sweep context-posture='unknown'><xpath>a</xpath></posture-and-sweep>", "Unknown streamability posture")]
    [InlineData("<posture-and-sweep context-posture='striding'><xpath>1 +</xpath></posture-and-sweep>", "XPST0003")]
    public void MalformedAnalysisDoesNotPass(string input, string detail)
    {
        WithCase(input, "striding", "consuming", test =>
        {
            Outcome outcome = new Runner().Run(test);
            Assert.Equal(OutcomeKind.Fail, outcome.Kind);
            Assert.Contains(detail, outcome.Detail);
        });
    }

    [Fact]
    public void UnsupportedExpressionIsReportedAsNotRun()
    {
        WithCase("<posture-and-sweep context-posture='striding'><xpath>Q{urn:unknown}f(*)</xpath></posture-and-sweep>", "grounded", "consuming", test =>
        {
            Outcome outcome = new Runner().Run(test);
            Assert.Equal(OutcomeKind.NotRun, outcome.Kind);
            Assert.Contains("not available", outcome.Detail);
        });
    }

    [Fact]
    public void NegatingAnUnsupportedAnalysisDoesNotTurnItIntoAPass()
    {
        WithCase("<posture-and-sweep context-posture='striding'><xpath>Q{urn:unknown}f(*)</xpath></posture-and-sweep>", "grounded", "consuming",
            test => Assert.Equal(OutcomeKind.NotRun, new Runner().Run(test).Kind),
            result: "<not><assert-posture-and-sweep result-posture='grounded' result-sweep='consuming'/></not>");
    }

    [Fact]
    public void UnlistedAnalysisSetsAreOptInAndListedSetsAreNotDuplicated()
    {
        WithCase("<posture-and-sweep context-posture='striding'><xpath>a</xpath></posture-and-sweep>", "striding", "consuming", test =>
        {
            string root = test.Set.Catalog.Root;
            string dir = Path.Combine(root, "tests", "sandp", "nested");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "_hidden-test-set.xml");
            XElement set = XDocument.Load(Path.Combine(root, "set.xml")).Root!;
            set.SetAttributeValue("name", "hidden");
            set.Save(file);
            Assert.Single(Catalog.Load(root).TestSets);
            Assert.Equal(2, Catalog.Load(root, includePostureAndSweep: true).TestSets.Count);
            XElement catalog = XDocument.Load(Path.Combine(root, "catalog.xml")).Root!;
            catalog.Add(new XElement(Catalog.Ns + "test-set", new XAttribute("name", "hidden"),
                new XAttribute("file", "tests/sandp/nested/_hidden-test-set.xml")));
            catalog.Save(Path.Combine(root, "catalog.xml"));
            Assert.Equal(2, Catalog.Load(root, includePostureAndSweep: true).TestSets.Count);
        });
    }

    [Theory]
    [InlineData("<posture-and-sweep context-posture='striding'/>")]
    [InlineData("<posture-and-sweep context-posture='striding'><xpath>1 +</xpath></posture-and-sweep>")]
    public void NegatingMalformedAnalysisDoesNotTurnItIntoAPass(string input)
    {
        WithCase(input, "grounded", "motionless", test => Assert.Equal(OutcomeKind.Fail, new Runner().Run(test).Kind),
            result: "<not><assert-posture-and-sweep result-posture='grounded' result-sweep='motionless'/></not>");
    }

    [Fact]
    public void NestedAssertionsPreserveUnsupportedAnalysis()
    {
        WithCase("<posture-and-sweep context-posture='striding'><xpath>Q{urn:unknown}f(*)</xpath></posture-and-sweep>", "grounded", "consuming",
            test => Assert.Equal(OutcomeKind.NotRun, new Runner().Run(test).Kind),
            result: "<not><any-of><assert-posture-and-sweep result-posture='grounded' result-sweep='consuming'/></any-of></not>");
    }

    [Fact]
    public void ResolvesNamespacesFromTheCatalogExpression()
    {
        WithCase("<posture-and-sweep context-posture='striding'><xpath xmlns:p='http://www.w3.org/2005/xpath-functions'>p:count(*)</xpath></posture-and-sweep>",
            "grounded", "consuming", test => Assert.Equal(OutcomeKind.Pass, new Runner().Run(test).Kind));
    }

    [Theory]
    [InlineData("feature", "streaming", true, true)]
    [InlineData("feature", "streaming", false, false)]
    [InlineData("feature", "streaming-fallback", true, false)]
    [InlineData("sweep_and_posture", "supports-sweep-and-posture-assessments", true, true)]
    public void DevelopmentProfileDoesNotChangeNormalClaims(string type, string value, bool satisfied, bool expected)
    {
        WithCase("<posture-and-sweep context-posture='striding'><xpath>a</xpath></posture-and-sweep>", "striding", "consuming", test =>
        {
            Assert.Equal(expected, Profile.Streaming.Applies(test));
            Assert.Equal(!satisfied, Profile.Xslt30.Applies(test));
            Assert.Equal(type == "feature" && value == "streaming" && satisfied, Xslt30RunTests.Requires(test, "feature", "streaming"));
        }, $"<{type} value='{value}' satisfied='{satisfied.ToString().ToLowerInvariant()}'/>");
    }

    private static void WithCase(string input, string posture, string sweep, Action<TestCase> check, string dependencies = "", string? result = null)
    {
        string dir = Path.Combine(Path.GetTempPath(), "xsl3-streamability-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            XNamespace ns = Catalog.Ns;
            File.WriteAllText(Path.Combine(dir, "catalog.xml"), new XElement(ns + "catalog",
                new XElement(ns + "test-set", new XAttribute("name", "analysis"), new XAttribute("file", "set.xml"))).ToString());
            File.WriteAllText(Path.Combine(dir, "set.xml"), $"""
                <test-set xmlns="{ns}" name="analysis">
                  <dependencies>{dependencies}</dependencies>
                  <test-case name="expression"><test>{input}</test><result>
                    {result ?? $"<assert-posture-and-sweep result-posture='{posture.ToLowerInvariant()}' result-sweep='{sweep}'/>"}
                  </result></test-case>
                </test-set>
                """);
            check(Catalog.Load(dir).TestCases.Single());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
