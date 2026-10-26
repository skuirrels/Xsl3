// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using Xsl3.Streaming;
using Xsl3.XPath31;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

public class FunctionStreamabilityTests
{
    [Theory]
    [InlineData("unclassified", ".", "Grounded", "Roaming", "FreeRanging")]
    [InlineData("absorbing", ".", "Striding", "Grounded", "Consuming")]
    [InlineData("absorbing", "descendant::*", "Crawling", "Roaming", "FreeRanging")]
    [InlineData("inspection", ".", "Grounded", "Grounded", "Motionless")]
    [InlineData("filter", ".", "Striding", "Striding", "Motionless")]
    [InlineData("filter", "*", "Striding", "Striding", "Consuming")]
    [InlineData("shallow-descent", ".", "Striding", "Striding", "Consuming")]
    [InlineData("shallow-descent", "descendant::*", "Striding", "Roaming", "FreeRanging")]
    [InlineData("deep-descent", ".", "Crawling", "Crawling", "Consuming")]
    [InlineData("deep-descent", "copy-of(.)", "Crawling", "Grounded", "Consuming")]
    [InlineData("ascent", ".", "Climbing", "Climbing", "Motionless")]
    [InlineData("ascent", "*", "Climbing", "Roaming", "FreeRanging")]
    public void AppliesTheDeclaredFunctionCategory(string category, string input, string bodyPosture, string posture, string sweep)
    {
        FunctionStreamability.TryParse(category, out FunctionStreamabilityCategory parsed);
        string resultType = category is "absorbing" or "inspection" ? "xs:integer" : "node()*";
        var signature = Signature($"function(node()) as {resultType}");
        AnalysisFunction function = FunctionStreamability.Describe(signature, parsed);
        var options = new ExpressionAnalysisOptions { ResolveFunction = (name, arity) => name.NamespaceUri == "urn:f" && arity == 1 ? function : null };
        Streamability result = new ExpressionAnalysis(options).Analyze(Parser.ParseXPath($"Q{{urn:f}}f({input})"), new(UType.Element, Posture.Striding));
        Assert.Equal(Enum.Parse<Posture>(posture), result.Posture);
        Assert.Equal(Enum.Parse<Sweep>(sweep), result.Sweep);
        Assert.Equal(category != "unclassified", function.DeclaredStreamable);
        if (category != "unclassified")
            Assert.True(FunctionStreamability.IsGuaranteed(signature, parsed,
                new(category is "absorbing" or "inspection" ? UType.Decimal : UType.Nodes,
                    Enum.Parse<Posture>(bodyPosture), Sweep.Motionless, "body")));
    }

    [Theory]
    [InlineData("inspection")]
    [InlineData("filter")]
    [InlineData("shallow-descent")]
    [InlineData("deep-descent")]
    [InlineData("ascent")]
    public void CategoriesExceptAbsorbingRestrictTheStreamingParameterCardinality(string category)
    {
        FunctionStreamability.TryParse(category, out FunctionStreamabilityCategory parsed);
        Assert.False(FunctionStreamability.IsGuaranteed(Signature("function(node()*) as item()*"), parsed,
            new(UType.Any, Posture.Grounded, Sweep.Motionless, "body")));
    }

    [Fact]
    public void AbsorbingSequencesConsumeWhileSingletonsAreMotionless()
    {
        Assert.Equal(Sweep.Consuming, FunctionStreamability.StreamingParameter(Type("node()*"), FunctionStreamabilityCategory.Absorbing).Sweep);
        Assert.Equal(Sweep.Motionless, FunctionStreamability.StreamingParameter(Type("node()?"), FunctionStreamabilityCategory.Absorbing).Sweep);
        Assert.Equal(Posture.Climbing, FunctionStreamability.StreamingParameter(Type("node()"), FunctionStreamabilityCategory.Ascent).Posture);
    }

    [Fact]
    public void ConversionToAnAtomicReturnTypeGroundsAStreamedResult()
    {
        var value = new Streamability(UType.Element, Posture.Striding, Sweep.Motionless, "body");
        Assert.True(FunctionStreamability.IsGuaranteed(Signature("function(node()) as xs:string"), FunctionStreamabilityCategory.Absorbing, value));
        Assert.False(FunctionStreamability.IsGuaranteed(Signature("function(node()) as xs:string"), FunctionStreamabilityCategory.Inspection, value));
    }

    [Fact]
    public void AscentWithAnAtomicReturnTypeDoesNotTransmitStreamedNodes()
    {
        AnalysisFunction function = FunctionStreamability.Describe(Signature("function(node()) as xs:string"), FunctionStreamabilityCategory.Ascent);
        Streamability result = FunctionStreamability.Call(function, new[] { new Streamability(UType.Element, Posture.Striding, Sweep.Motionless, "input") });
        Assert.Equal(Posture.Grounded, result.Posture);
        Assert.Equal(Sweep.Motionless, result.Sweep);
    }

    [Theory]
    [InlineData("Grounded", "Motionless", "Grounded", "Motionless")]
    [InlineData("Striding", "Motionless", "Climbing", "Motionless")]
    [InlineData("Crawling", "Motionless", "Climbing", "Motionless")]
    [InlineData("Climbing", "Motionless", "Climbing", "Motionless")]
    [InlineData("Roaming", "Motionless", "Roaming", "FreeRanging")]
    [InlineData("Striding", "Consuming", "Roaming", "FreeRanging")]
    [InlineData("Climbing", "FreeRanging", "Roaming", "FreeRanging")]
    public void AscentTransmitsTheFirstArgumentUnderProposedErratumE31(string inputPosture, string inputSweep, string posture, string sweep)
    {
        AnalysisFunction function = FunctionStreamability.Describe(Signature("function(node()) as node()*"), FunctionStreamabilityCategory.Ascent);
        Streamability result = FunctionStreamability.Call(function, new[] { new Streamability(UType.Element, Enum.Parse<Posture>(inputPosture), Enum.Parse<Sweep>(inputSweep), "input") });
        Assert.Equal(Enum.Parse<Posture>(posture), result.Posture);
        Assert.Equal(Enum.Parse<Sweep>(sweep), result.Sweep);
    }

    [Theory]
    [InlineData("xs:string", "Motionless", "Grounded", "Motionless")]
    [InlineData("node()*", "Motionless", "Climbing", "Motionless")]
    [InlineData("node()*", "Consuming", "Roaming", "FreeRanging")]
    [InlineData("node()*", "FreeRanging", "Roaming", "FreeRanging")]
    public void AscentChecksRemainingOperandsAndTheReturnType(string returnType, string remainingSweep, string posture, string sweep)
    {
        AnalysisFunction function = FunctionStreamability.Describe(Signature($"function(node(), xs:string) as {returnType}"), FunctionStreamabilityCategory.Ascent);
        Streamability result = FunctionStreamability.Call(function, new[]
        {
            new Streamability(UType.Element, Posture.Striding, Sweep.Motionless, "first"),
            new Streamability(UType.String, Posture.Grounded, Enum.Parse<Sweep>(remainingSweep), "remaining"),
        });
        Assert.Equal(Enum.Parse<Posture>(posture), result.Posture);
        Assert.Equal(Enum.Parse<Sweep>(sweep), result.Sweep);
    }

    [Fact]
    public void FilterCategoryMayReturnAttributesOfItsInput()
    {
        AnalysisFunction function = FunctionStreamability.Describe(Signature("function(element()) as attribute()*"), FunctionStreamabilityCategory.Filter);
        Streamability result = FunctionStreamability.Call(function, new[] { new Streamability(UType.Element, Posture.Striding, Sweep.Motionless, "input") });
        Assert.Equal(UType.Attribute, result.Type);
        Assert.Equal(Posture.Striding, result.Posture);
    }

    [Fact]
    public void BuiltInSignaturesDoNotRequireTheStylesheetToDeclareXs()
    {
        var options = new ExpressionAnalysisOptions { Types = new StaticTypeResolver(_ => null) };
        Streamability result = new ExpressionAnalysis(options).Analyze(
            Parser.ParseXPath("filter((), function($x) { true() })"), new(UType.Element, Posture.Striding));
        Assert.Equal(Posture.Grounded, result.Posture);
        Assert.Equal(Sweep.Motionless, result.Sweep);
    }

    private static SequenceTypeTest Type(string source) => new StaticTypeResolver(ExpressionAnalysisOptions.StandardNamespace).Resolve(Parser.ParseSequenceType(source));
    private static FunctionItemTest Signature(string source) => (FunctionItemTest)Type(source).Item!;
}
