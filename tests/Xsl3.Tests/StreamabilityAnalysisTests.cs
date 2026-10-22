// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using Xsl3.Streaming;
using Xsl3.XPath31;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

public class StreamabilityAnalysisTests
{
    // XSLT 3.0 §19.8.8.9: cover each row of the transition table, including node kinds that
    // change descendant/self results from crawling to striding.
    [Theory]
    [InlineData("following::*", "Grounded", "Grounded", "Motionless")]
    [InlineData("child::document-node()", "Roaming", "Roaming", "FreeRanging")]
    [InlineData("self::node()", "Climbing", "Climbing", "Motionless")]
    [InlineData("parent::node()", "Climbing", "Climbing", "Motionless")]
    [InlineData("ancestor::node()", "Climbing", "Climbing", "Motionless")]
    [InlineData("ancestor-or-self::node()", "Climbing", "Climbing", "Motionless")]
    [InlineData("@*", "Climbing", "Striding", "Motionless")]
    [InlineData("namespace::*", "Climbing", "Striding", "Motionless")]
    [InlineData("*", "Climbing", "Roaming", "FreeRanging")]
    [InlineData("..", "Striding", "Climbing", "Motionless")]
    [InlineData("ancestor::*", "Striding", "Climbing", "Motionless")]
    [InlineData("ancestor-or-self::*", "Striding", "Climbing", "Motionless")]
    [InlineData("self::*", "Striding", "Striding", "Motionless")]
    [InlineData("@*", "Striding", "Striding", "Motionless")]
    [InlineData("namespace::*", "Striding", "Striding", "Motionless")]
    [InlineData("*", "Striding", "Striding", "Consuming")]
    [InlineData("descendant::*", "Striding", "Crawling", "Consuming")]
    [InlineData("descendant-or-self::*", "Striding", "Crawling", "Consuming")]
    [InlineData("descendant::text()", "Striding", "Striding", "Consuming")]
    [InlineData("descendant-or-self::comment()", "Striding", "Striding", "Consuming")]
    [InlineData("..", "Crawling", "Climbing", "Motionless")]
    [InlineData("ancestor::*", "Crawling", "Climbing", "Motionless")]
    [InlineData("ancestor-or-self::*", "Crawling", "Climbing", "Motionless")]
    [InlineData("@*", "Crawling", "Striding", "Motionless")]
    [InlineData("namespace::*", "Crawling", "Striding", "Motionless")]
    [InlineData("self::*", "Crawling", "Crawling", "Motionless")]
    [InlineData("self::text()", "Crawling", "Striding", "Motionless")]
    [InlineData("*", "Crawling", "Roaming", "FreeRanging")]
    [InlineData("following::*", "Striding", "Roaming", "FreeRanging")]
    [InlineData("preceding-sibling::*", "Striding", "Roaming", "FreeRanging")]
    public void AppliesAxisTransitionTable(string expression, string context, string posture, string sweep)
    {
        Streamability actual = new ExpressionAnalysis().Analyze(Parser.ParseXPath(expression), new(UType.Any, Enum.Parse<Posture>(context)));
        Assert.Equal(Enum.Parse<Posture>(posture), actual.Posture);
        Assert.Equal(Enum.Parse<Sweep>(sweep), actual.Sweep);
        Assert.Equal("19.8.8.9", actual.Rule);
    }

    [Theory]
    [InlineData("text()/child::*", "Grounded", "Consuming", (int)UType.Empty)]
    [InlineData("self::document-node()/..", "Grounded", "Motionless", (int)UType.Empty)]
    [InlineData("@*/descendant::*", "Grounded", "Motionless", (int)UType.Empty)]
    [InlineData("child::attribute()/*", "Grounded", "Motionless", (int)UType.Empty)]
    [InlineData("a/b/c", "Striding", "Consuming", (int)UType.Element)]
    [InlineData("a/@code", "Striding", "Consuming", (int)UType.Attribute)]
    [InlineData("../@status", "Striding", "Motionless", (int)UType.Attribute)]
    [InlineData("a/following::b", "Roaming", "FreeRanging", (int)UType.Element)]
    [InlineData("section//head", "Crawling", "Consuming", (int)UType.Element)]
    [InlineData("descendant::section/head", "Crawling", "Consuming", (int)UType.Element)]
    [InlineData("self::node()//section/head", "Crawling", "Consuming", (int)UType.Element)]
    [InlineData(".//section/head", "Roaming", "FreeRanging", (int)UType.Element)]
    [InlineData("descendant::section ! head", "Roaming", "FreeRanging", (int)UType.Element)]
    [InlineData("@* ! .", "Striding", "Motionless", (int)UType.Attribute)]
    [InlineData("42 ! child::*", "Grounded", "Motionless", (int)UType.Empty)]
    [InlineData("a/namespace::Q{urn:example}name", "Grounded", "Consuming", (int)UType.Empty)]
    [InlineData("a/namespace::Q{}name", "Striding", "Consuming", (int)UType.Namespace)]
    [InlineData("a/namespace::*:name", "Striding", "Consuming", (int)UType.Namespace)]
    public void PropagatesTypesAndFocusThroughPaths(string expression, string posture, string sweep, int type)
    {
        Streamability actual = new ExpressionAnalysis().Analyze(Parser.ParseXPath(expression), new(UType.Any, Posture.Striding));
        Assert.Equal((UType)type, actual.Type);
        Assert.Equal(Enum.Parse<Posture>(posture), actual.Posture);
        Assert.Equal(Enum.Parse<Sweep>(sweep), actual.Sweep);
    }

    [Fact]
    public void ReassessesSharedAstAndKeepsOperandResults()
    {
        var analysis = new ExpressionAnalysis();
        var path = Assert.IsType<PathExpr>(Parser.ParseXPath("a/b"));
        Assert.Equal(Posture.Striding, analysis.Analyze(path, new(UType.Element, Posture.Striding)).Posture);
        Assert.Equal(UType.Element, analysis.Results[path.Left].Type);
        Assert.Equal(UType.Element, analysis.Results[path.Right].Type);
        Assert.Equal(Posture.Grounded, analysis.Analyze(path, new(UType.Element, Posture.Grounded)).Posture);
        Assert.Equal(Sweep.Motionless, analysis.Results[path.Right].Sweep);
    }

    [Fact]
    public void UnknownFunctionPolicyIsAnExplicitGap()
    {
        Assert.Throws<NotSupportedException>(() => new ExpressionAnalysis().Analyze(Parser.ParseXPath("Q{urn:unknown}f(1)"), new(UType.Any, Posture.Striding)));
    }

    [Theory]
    [InlineData("int", (int)UType.Decimal)]
    [InlineData("integer", (int)UType.Decimal)]
    [InlineData("dayTimeDuration", (int)UType.Duration)]
    [InlineData("yearMonthDuration", (int)UType.Duration)]
    [InlineData("dateTimeStamp", (int)UType.DateTime)]
    [InlineData("NCName", (int)UType.String)]
    [InlineData("anyAtomicType", (int)UType.Atomic)]
    public void FundamentalTypesUseTheXdmHierarchy(string name, int expected)
    {
        Assert.Equal((UType)expected, UTypes.FromAtomic(AtomicTypes.Find(QName.Ns.Xs, name)!));
    }
}
