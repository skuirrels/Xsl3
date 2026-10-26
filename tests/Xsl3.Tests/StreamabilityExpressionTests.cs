// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Schema;
using Xsl3.Streaming;
using Xsl3.XPath31;
using Xsl3.XPath31.Functions;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

public class StreamabilityExpressionTests
{
    // Examples and consequences of XSLT 3.0 §19.8.1 and §19.8.8-9, deliberately combining
    // rules so an incorrect usage, focus, or variable scope changes the final verdict.
    [Theory]
    [InlineData("count(*) + 1", "Grounded", "Consuming")]
    [InlineData("price + tax", "Roaming", "FreeRanging")]
    [InlineData("string(.)", "Grounded", "Consuming")]
    [InlineData("name(.)", "Grounded", "Motionless")]
    [InlineData("string(@id) || name(.)", "Grounded", "Motionless")]
    [InlineData("exists(.)", "Grounded", "Motionless")]
    [InlineData("count(@*) + count(*)", "Grounded", "Consuming")]
    [InlineData("(@a, @b)", "Striding", "Motionless")]
    [InlineData("(@a, ancestor::*)", "Roaming", "FreeRanging")]
    [InlineData("(a, b)", "Roaming", "FreeRanging")]
    [InlineData("(1, a, 2)", "Striding", "Consuming")]
    [InlineData("if (@x) then a else b", "Striding", "Consuming")]
    [InlineData("if (@x) then @a else b", "Striding", "Consuming")]
    [InlineData("if (@x) then count(a) else count(b)", "Grounded", "Consuming")]
    [InlineData("if (@x) then ancestor::* else b", "Roaming", "FreeRanging")]
    [InlineData("if (a) then b else c", "Roaming", "FreeRanging")]
    [InlineData("a | b", "Crawling", "Consuming")]
    [InlineData("a intersect b", "Crawling", "Consuming")]
    [InlineData("a except b", "Crawling", "Consuming")]
    [InlineData("a | parent::*", "Roaming", "FreeRanging")]
    [InlineData("() | a", "Striding", "Consuming")]
    [InlineData(". is ..", "Grounded", "Motionless")]
    [InlineData(". << child::*", "Grounded", "Consuming")]
    [InlineData("let $n := count(*) return $n + $n", "Grounded", "Consuming")]
    [InlineData("let $x := . return name($x)", "Roaming", "FreeRanging")]
    [InlineData("for $i in 1 to 3 return name(@code)", "Grounded", "Motionless")]
    [InlineData("for $i in 1 to 3 return salary", "Roaming", "FreeRanging")]
    [InlineData("for $x in copy-of(*) return $x/a", "Grounded", "Consuming")]
    [InlineData("for $x in * return $x/a", "Roaming", "FreeRanging")]
    [InlineData("some $x in data(*) satisfies $x > 0", "Grounded", "Consuming")]
    [InlineData("every $x in * satisfies true()", "Roaming", "FreeRanging")]
    [InlineData(". cast as xs:integer", "Grounded", "Consuming")]
    [InlineData(". castable as xs:integer", "Grounded", "Consuming")]
    [InlineData("xs:integer(.)", "Grounded", "Consuming")]
    [InlineData(". instance of element()", "Grounded", "Motionless")]
    [InlineData(". instance of document-node(element())", "Grounded", "Consuming")]
    [InlineData(". treat as document-node(element())", "Roaming", "FreeRanging")]
    [InlineData(". treat as element()", "Striding", "Motionless")]
    [InlineData("a[@id]", "Striding", "Consuming")]
    [InlineData("a[b]", "Roaming", "FreeRanging")]
    [InlineData("a[last()]", "Roaming", "FreeRanging")]
    [InlineData("ancestor::*[last()]", "Climbing", "Motionless")]
    [InlineData("descendant::a[1]", "Striding", "Consuming")]
    [InlineData("(descendant::a)[1]", "Striding", "Consuming")]
    [InlineData("descendant::a[position()]", "Crawling", "Consuming")]
    [InlineData("descendant::a/head[@id]", "Crawling", "Consuming")]
    [InlineData("descendant::a/head[1]", "Roaming", "FreeRanging")]
    [InlineData("head(descendant::a)", "Striding", "Consuming")]
    [InlineData("outermost(descendant::a)", "Striding", "Consuming")]
    [InlineData("innermost(descendant::a)", "Roaming", "FreeRanging")]
    [InlineData("reverse(ancestor::*)", "Roaming", "FreeRanging")]
    [InlineData("reverse(ancestor::*/name())", "Grounded", "Motionless")]
    [InlineData("copy-of(.)/a/following-sibling::*", "Grounded", "Consuming")]
    [InlineData("snapshot(.)/a", "Grounded", "Consuming")]
    [InlineData("map {'a': copy-of(a), 'b': copy-of(b)}", "Grounded", "Consuming")]
    [InlineData("map {'a': a}", "Roaming", "FreeRanging")]
    [InlineData("map {string(a): copy-of(b)}", "Roaming", "FreeRanging")]
    [InlineData("[copy-of(a)]", "Grounded", "Consuming")]
    [InlineData("[copy-of(a), copy-of(b)]", "Roaming", "FreeRanging")]
    [InlineData("[a]", "Roaming", "FreeRanging")]
    [InlineData("array {data(a)}", "Grounded", "Consuming")]
    [InlineData("map {'k': 1}?(a)", "Grounded", "Consuming")]
    [InlineData("head(map {'k': 1})(a)", "Grounded", "Consuming")]
    [InlineData("?*", "Grounded", "Motionless")]
    [InlineData("function($x as xs:integer) as xs:integer {$x + 1}(a)", "Grounded", "Consuming")]
    [InlineData("function($x) {$x}(a)", "Roaming", "FreeRanging")]
    [InlineData("for-each(a, function($x as xs:integer) {$x + 1})", "Grounded", "Consuming")]
    [InlineData("for-each(a, function($x) {$x})", "Roaming", "FreeRanging")]
    [InlineData("for-each-pair(a, b, function($x as xs:integer, $y as xs:integer) {$x + $y})", "Roaming", "FreeRanging")]
    [InlineData("fold-left(a, 0, function($x, $y as xs:integer) {$x + $y})", "Grounded", "Consuming")]
    [InlineData("fold-right(a, 0, function($x as xs:integer, $y) {$x + $y})", "Roaming", "FreeRanging")]
    [InlineData("filter(a, function($x as xs:integer) {$x > 0})", "Roaming", "FreeRanging")]
    [InlineData("string#1", "Grounded", "Motionless")]
    [InlineData("name#0", "Roaming", "FreeRanging")]
    [InlineData("current-group#0", "Grounded", "Motionless")]
    [InlineData("current-grouping-key#0", "Grounded", "Motionless")]
    [InlineData("substring(?, 2)", "Grounded", "Motionless")]
    [InlineData("concat(a, ?)", "Grounded", "Consuming")]
    [InlineData("string#1(a)", "Roaming", "FreeRanging")]
    [InlineData("a => string()", "Grounded", "Consuming")]
    [InlineData("a => (upper-case#1)()", "Grounded", "Consuming")]
    [InlineData("accumulator-before('sum')", "Grounded", "Motionless")]
    [InlineData("accumulator-after('sum')", "Grounded", "FreeRanging")]
    [InlineData("current-group()", "Roaming", "FreeRanging")]
    [InlineData("current-grouping-key()", "Grounded", "Motionless")]
    [InlineData("current-merge-group()", "Grounded", "Motionless")]
    public void CombinesExpressionRules(string expression, string posture, string sweep)
    {
        Streamability result = Analyze(expression);
        Assert.Equal(Enum.Parse<Posture>(posture), result.Posture);
        Assert.Equal(Enum.Parse<Sweep>(sweep), result.Sweep);
    }

    [Fact]
    public void TracksCurrentFromTheOuterExpressionAcrossFocusChanges()
    {
        var analysis = new ExpressionAnalysis();
        Expr expression = Parser.ParseXPath("a[@ref = current()/@id]");
        Assert.Equal(Posture.Striding, analysis.Analyze(expression, new(UType.Document, Posture.Striding)).Posture);
        Streamability current = analysis.Results.Single(e => e.Key is FunctionCall { Name.LocalName: "current" }).Value;
        Assert.Equal(UType.Document, current.Type);
        Assert.Equal(Posture.Climbing, current.Posture);
        Assert.Equal(Posture.Grounded, analysis.Analyze(expression, new(UType.Element, Posture.Grounded)).Posture);
        Assert.Equal(Posture.Climbing, Analyze("current()", new() { InPattern = true }).Posture);
    }

    [Fact]
    public void BindingsShadowAndRestoreTheOuterVariableAndPreserveCallableSignatures()
    {
        var options = new ExpressionAnalysisOptions { Variables = new Dictionary<QName, AnalysisVariable>
        {
            [new QName("v")] = new(Type("xs:string")),
            [new QName("m")] = new(Type("map(xs:string, xs:integer)")),
            [new QName("f")] = new(Type("function(xs:integer) as xs:string")),
        } };
        var analysis = new ExpressionAnalysis(options);
        Streamability value = analysis.Analyze(Parser.ParseXPath("(let $v := 3 return $v, $v)"), new(UType.Element, Posture.Striding));
        Assert.Equal(UType.Decimal | UType.String, value.Type);
        Assert.Equal(UType.String, analysis.Analyze(Parser.ParseXPath("$f(a)"), new(UType.Element, Posture.Striding)).Type);
        Assert.Equal(UType.Decimal, Analyze("$m?key", options).Type);
        Assert.Equal(Sweep.Consuming, Analyze("$m(a)", options).Sweep);
        Assert.Equal(Posture.Grounded, Analyze("let $f := upper-case#1 return $f(a)").Posture);
        Assert.Equal(Posture.Roaming, Analyze("let $x := 1 return (let $x := a return $x)").Posture);
        Assert.Equal("XPST0008", Assert.Throws<XPathStaticException>(() => Analyze("$missing")).Code);
    }

    [Fact]
    public void InlineFunctionsCannotCaptureAStreamingParameterButCanShadowIt()
    {
        var options = new ExpressionAnalysisOptions { Variables = new Dictionary<QName, AnalysisVariable>
        {
            [new QName("node")] = new(Type("element()")) { StreamingParameter = new(UType.Element, Posture.Striding, Sweep.Motionless, "19.8.5") },
        } };
        Assert.Equal(Posture.Roaming, Analyze("function() {$node}", options).Posture);
        Assert.Equal(Posture.Grounded, Analyze("function($node) {$node}", options).Posture);
        Assert.Equal(Posture.Grounded, Analyze("function() {let $node := 1 return $node}", options).Posture);
        Assert.Equal(Posture.Roaming, Analyze("function() {let $node := $node return 1}", options).Posture);
    }

    [Theory]
    [InlineData(false, false, "None", "FreeRanging")]
    [InlineData(true, false, "None", "Consuming")]
    [InlineData(true, true, "None", "Motionless")]
    [InlineData(true, false, "Start", "FreeRanging")]
    [InlineData(true, false, "End", "Motionless")]
    public void AccumulatorAfterUsesExplicitHostContext(bool inSequence, bool precedingConsuming, string phase, string expectedSweep)
    {
        var options = new ExpressionAnalysisOptions { InSequenceConstructor = inSequence,
            PrecedingConsumingInstruction = precedingConsuming, AccumulatorPhase = Enum.Parse<AccumulatorPhase>(phase) };
        Assert.Equal(Enum.Parse<Sweep>(expectedSweep), Analyze("accumulator-after('a')", options).Sweep);
        Assert.Equal(Sweep.Motionless, new ExpressionAnalysis(options).Analyze(
            Parser.ParseXPath("accumulator-after('a')"), new(UType.Text, Posture.Striding)).Sweep);
    }

    [Fact]
    public void RootAndLastRespectTheContextTypeAndPosture()
    {
        var analysis = new ExpressionAnalysis();
        Assert.Equal(Posture.Striding, analysis.Analyze(Parser.ParseXPath("/a"), new(UType.Document, Posture.Striding)).Posture);
        Assert.Equal(Posture.Roaming, analysis.Analyze(Parser.ParseXPath("/a"), new(UType.Element, Posture.Striding)).Posture);
        Assert.Equal(Posture.Crawling, analysis.Analyze(Parser.ParseXPath("//a/b"), new(UType.Document, Posture.Striding)).Posture);
        Assert.Equal(Posture.Roaming, analysis.Analyze(Parser.ParseXPath("//a/b"), new(UType.Element, Posture.Striding)).Posture);
        Assert.Equal(Sweep.Motionless, analysis.Analyze(Parser.ParseXPath("last()"), new(UType.Element, Posture.Grounded)).Sweep);
        Assert.Equal(Sweep.FreeRanging, Analyze("last()").Sweep);
    }

    [Theory]
    [InlineData("empty-sequence()", (int)UType.Empty)]
    [InlineData("xs:error", (int)UType.Empty)]
    [InlineData("xs:int*", (int)UType.Decimal)]
    [InlineData("xs:numeric", (int)UType.Numeric)]
    [InlineData("function(xs:string) as xs:integer", (int)UType.Function)]
    [InlineData("map(xs:string, item()*)", (int)UType.Function)]
    [InlineData("array(xs:string)", (int)UType.Function)]
    [InlineData("document-node(element())", (int)UType.Document)]
    public void MapsSequenceTypesToFundamentalTypes(string type, int expected) => Assert.Equal((UType)expected, Analyze($"() treat as {type}").Type);

    [Fact]
    public void SchemaUnionsAndRestrictionsUseTheCanonicalResolver()
    {
        var schemas = new XmlSchemaSet();
        using var reader = XmlReader.Create(new StringReader("""
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" targetNamespace="urn:types">
              <xs:simpleType name="small"><xs:restriction base="xs:int"/></xs:simpleType>
              <xs:simpleType name="mixed"><xs:union memberTypes="xs:int xs:date xs:duration"/></xs:simpleType>
            </xs:schema>
            """));
        schemas.Add(null, reader); schemas.Compile();
        var options = new ExpressionAnalysisOptions { Types = new StaticTypeResolver(ExpressionAnalysisOptions.StandardNamespace, schemas: schemas) };
        Assert.Equal(UType.Decimal, Analyze("() treat as Q{urn:types}small", options).Type);
        Assert.Equal(UType.Decimal | UType.Date | UType.Duration, Analyze("() treat as Q{urn:types}mixed", options).Type);
    }

    [Fact]
    public void EveryRegisteredBuiltInHasAnalysisMetadata()
    {
        var gaps = new List<string>();
        foreach (FunctionDefinition definition in FunctionLibrary.Definitions)
        {
            var expression = new FunctionCall(new LexicalQName(null, definition.Name.LocalName, definition.Name.NamespaceUri),
                Enumerable.Repeat<Expr>(new EmptySequenceExpr(), definition.Arity).ToArray());
            try { new ExpressionAnalysis().Analyze(expression, new(UType.Any, Posture.Grounded)); }
            catch (NotSupportedException e) { gaps.Add(e.Message); }
        }
        Assert.True(gaps.Count == 0, string.Join(Environment.NewLine, gaps));
    }

    [Fact]
    public void HostFunctionPolicyUsesExpandedNamesAndChecksStreamingPartials()
    {
        var descriptor = new AnalysisFunction((FunctionItemTest)Type("function(element(), xs:integer) as xs:string").Item!,
            new[] { OperandUsage.Inspection, OperandUsage.Absorption }, DeclaredStreamable: true);
        var options = new ExpressionAnalysisOptions
        {
            Types = new StaticTypeResolver(prefix => prefix == "f" ? "urn:functions" : ExpressionAnalysisOptions.StandardNamespace(prefix)),
            ResolveFunction = (name, arity) => name.NamespaceUri == "urn:functions" && name.LocalName == "inspect" && arity == 2 ? descriptor : null,
        };
        Assert.Equal(Posture.Roaming, Analyze("f:inspect(., ?)", options).Posture);
        Assert.Equal(Posture.Grounded, Analyze("f:inspect(?, 1)", options).Posture);
        Assert.Equal(Sweep.Motionless, Analyze("f:inspect(., 1)", options).Sweep);
        Assert.Equal(UType.String, Analyze("Q{urn:functions}inspect(., 1)", options).Type);
        Assert.Equal("XPST0081", Assert.Throws<XPathStaticException>(() => Analyze("missing:f()", options)).Code);
    }

    [Fact]
    public void CurrentGroupRequiresTheHostsFocusAndNoHigherOrderBoundary()
    {
        var options = new ExpressionAnalysisOptions { CurrentGroupSelection = new(UType.Element, Posture.Striding, Sweep.Consuming, "group select") };
        Assert.Equal(Sweep.Consuming, Analyze("current-group()", options).Sweep);
        Assert.Equal(Sweep.FreeRanging, Analyze("for $i in 1 to 3 return current-group()", options).Sweep);
        Assert.Equal(Sweep.FreeRanging, Analyze("a/current-group()", options).Sweep);
    }

    [Fact]
    public void VariadicNamedReferencesDoNotAllocateAnAritySizedArray()
    {
        Streamability result = Analyze("concat#2147483647");
        Assert.Equal(Posture.Grounded, result.Posture);
        var signature = Assert.IsType<FunctionItemTest>(result.ItemType);
        Assert.Equal(int.MaxValue, signature.Parameters!.Count);
        Assert.IsType<AtomicTypeTest>(signature.Parameters[int.MaxValue - 1].Item);
    }

    [Fact]
    public void ArrowFunctionLookupMakesAScanningPredicatePositional()
    {
        Streamability result = Analyze("descendant::a/b[(QName('http://www.w3.org/2005/xpath-functions', 'position') => function-lookup(0))() gt 0]");
        Assert.Equal(Posture.Roaming, result.Posture);
        Assert.Equal(Sweep.FreeRanging, result.Sweep);
    }

    [Theory]
    [InlineData("exists((1,2) ! last())")]
    [InlineData("exists((1,2) ! position())")]
    [InlineData("exists((1,2) ! ([last()]?1))")]
    [InlineData("exists((1,2)[last()])")]
    public void LocallyBoundNumericFocusDoesNotMakeAScanningPredicatePositional(string predicate)
    {
        Streamability result = Analyze("descendant::a/b[" + predicate + "]");
        Assert.Equal(Posture.Crawling, result.Posture);
        Assert.Equal(Sweep.Consuming, result.Sweep);
    }

    [Theory]
    [InlineData("Grounded")]
    [InlineData("Roaming")]
    public void AxisPredicatesAreAssessedEvenWhenTheAxisVerdictIsAlreadyKnown(string posture)
    {
        var analysis = new ExpressionAnalysis();
        var expression = Assert.IsType<AxisStep>(Parser.ParseXPath("a[count(*)]"));
        analysis.Analyze(expression, new(UType.Element, Enum.Parse<Posture>(posture)));
        Assert.True(analysis.Results.ContainsKey(expression.Predicates[0]));
        Assert.Throws<XPathStaticException>(() => analysis.Analyze(Parser.ParseXPath("a[$missing]"), new(UType.Element, Enum.Parse<Posture>(posture))));
    }

    [Fact]
    public void NestedRootOperandsAreVisitedOnce()
    {
        int expansions = 0;
        var options = new ExpressionAnalysisOptions { Types = new StaticTypeResolver(prefix =>
        {
            if (prefix == "fn") expansions++;
            return ExpressionAnalysisOptions.StandardNamespace(prefix);
        }) };
        string expression = ".";
        for (int i = 0; i < 8; i++) expression = $"fn:root({expression})";
        Analyze(expression, options);
        Assert.InRange(expansions, 8, 16);
    }

    [Theory]
    [InlineData("map(xs:string, empty-sequence())", "$v?key")]
    [InlineData("array(empty-sequence())", "$v?1")]
    public void LookupPreservesKnownEmptyMemberTypes(string type, string expression)
    {
        var options = new ExpressionAnalysisOptions { Variables = new Dictionary<QName, AnalysisVariable> { [new QName("v")] = new(Type(type)) } };
        Assert.Equal(UType.Empty, Analyze(expression, options).Type);
    }

    [Fact]
    public void HigherOrderCallsPreserveKnownEmptyReturnTypes() => Assert.Equal(UType.Empty,
        Analyze("for-each(a, function($x as xs:integer) as empty-sequence() {()})").Type);

    [Fact]
    public void CastableResolvesTheTargetType() => Assert.Equal("XPST0081",
        Assert.Throws<XPathStaticException>(() => Analyze("1 castable as missing:type")).Code);

    [Fact]
    public void HostPolicyIsResolvedOncePerNameAndArityInEachAnalysis()
    {
        int resolutions = 0;
        var descriptor = new AnalysisFunction((FunctionItemTest)Type("function(xs:integer) as xs:integer").Item!, new[] { OperandUsage.Absorption });
        var options = new ExpressionAnalysisOptions { ResolveFunction = (name, arity) => { resolutions++; return descriptor; } };
        var analysis = new ExpressionAnalysis(options);
        Expr expression = Parser.ParseXPath("(Q{urn:host}f(1), Q{urn:host}f(2), Q{urn:host}f#1)");
        analysis.Analyze(expression, new(UType.Element, Posture.Striding));
        Assert.Equal(1, resolutions);
        analysis.Analyze(expression, new(UType.Element, Posture.Striding));
        Assert.Equal(2, resolutions);
    }

    [Fact]
    public void InvalidHostPoliciesAreRejectedEvenForNamedReferences()
    {
        var descriptor = new AnalysisFunction((FunctionItemTest)Type("function(xs:integer) as xs:integer").Item!, Array.Empty<OperandUsage>());
        var options = new ExpressionAnalysisOptions { ResolveFunction = (_, _) => descriptor };
        Assert.Throws<InvalidOperationException>(() => Analyze("Q{urn:host}f#1", options));
    }

    [Fact]
    public void FocusDependentArrowsDoNotNarrowNumericDescendantPredicates()
    {
        var descriptor = new AnalysisFunction((FunctionItemTest)Type("function(xs:integer) as xs:integer").Item!,
            new[] { OperandUsage.Absorption }, FocusDependent: true);
        var options = new ExpressionAnalysisOptions { ResolveFunction = (_, _) => descriptor };
        Assert.Equal(Posture.Crawling, Analyze("descendant::a[0 => Q{urn:host}f()]", options).Posture);
    }

    [Fact]
    public void SpecialFunctionArgumentsStillGetAssessed()
    {
        Assert.Equal("XPST0008", Assert.Throws<XPathStaticException>(() => Analyze("current-merge-group($missing)")).Code);
    }

    [Fact]
    public void NonStreamablePartialsStillAssessAllSuppliedArguments()
    {
        var descriptor = new AnalysisFunction((FunctionItemTest)Type("function(element(), xs:integer) as xs:string").Item!,
            new[] { OperandUsage.Inspection, OperandUsage.Absorption }, FocusDependent: true, DeclaredStreamable: true);
        var options = new ExpressionAnalysisOptions { ResolveFunction = (_, _) => descriptor };
        Assert.Equal("XPST0008", Assert.Throws<XPathStaticException>(() => Analyze("Q{urn:host}f(?, $missing)", options)).Code);
    }

    [Theory]
    [InlineData("Climbing")]
    [InlineData("Roaming")]
    public void ScanningCannotEraseAnIncompatibleContext(string posture)
    {
        Streamability result = new ExpressionAnalysis().Analyze(Parser.ParseXPath("a/b"), new(UType.Element, Enum.Parse<Posture>(posture)));
        Assert.Equal(Sweep.FreeRanging, result.Sweep);
    }

    private static SequenceTypeTest Type(string type) => new StaticTypeResolver(ExpressionAnalysisOptions.StandardNamespace).Resolve(Parser.ParseSequenceType(type));
    private static Streamability Analyze(string expression, ExpressionAnalysisOptions? options = null) =>
        new ExpressionAnalysis(options).Analyze(Parser.ParseXPath(expression), new(UType.Element, Posture.Striding));
}
