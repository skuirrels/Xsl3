// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Xsl3.Streaming;
using Xsl3.W3C.Qt3;
using Xsl3.XPath31;

namespace Xsl3.W3C.Xslt30;

public sealed partial class Runner
{
    private sealed record AssessedExpression(XElement Expression, Streamability Value);

    private Outcome RunPostureAndSweep(TestCase test, XElement input)
    {
        XElement[] expressions = input.Elements(Catalog.Ns + "xpath").ToArray();
        if (input.Elements(Catalog.Ns + "xslt").Any())
            return expressions.Length == 0 ? Outcome.NotRun("streamability analysis of XSLT instructions (P7 S2)")
                : Outcome.Fail("posture-and-sweep mixes XPath expressions and XSLT instructions");
        if (expressions.Length == 0) return Outcome.Fail("posture-and-sweep has no expressions");
        Posture contextPosture = ParsePosture((string?)input.Attribute("context-posture"));
        // The catalogue schema's context-type defaults to item(), not to element() or node().
        UType contextType = (string?)input.Attribute("context-type") switch
        {
            null or "item()" => UType.Any,
            "node()" => UType.Nodes,
            "document-node()" => UType.Document,
            "element()" => UType.Element,
            "atomic" => UType.Atomic,
            "childless-node-kind" => UType.ChildlessNodes,
            string type => throw new FormatException($"Unknown streamability context type: {type}"),
        };
        // Assess the complete input before evaluating assertions. Invalid or unsupported input
        // is a harness outcome, and must never become a passing assertion through <not>.
        var assessed = new List<AssessedExpression>();
        using var parsed = Parser.CompilationScope();
        foreach (XElement expression in expressions)
        {
            try
            {
                var options = new ExpressionAnalysisOptions
                {
                    Types = new StaticTypeResolver(prefix => expression.GetNamespaceOfPrefix(prefix)?.NamespaceName
                        ?? ExpressionAnalysisOptions.StandardNamespace(prefix)),
                };
                Streamability value = new ExpressionAnalysis(options).Analyze(Parser.ParseXPath(expression.Value), new(contextType, contextPosture));
                assessed.Add(new(expression, value));
            }
            catch (NotSupportedException e)
            {
                return Outcome.NotRun(e.Message);
            }
            catch (XPathStaticException e)
            {
                return Outcome.Fail($"part {(string?)expression.Attribute("part") ?? "?"}: {e.Code}: {e.Message}");
            }
        }
        return Check(test, test.Result, new RunResult(null, null, Array.Empty<string>()) { StreamabilityResults = assessed });
    }

    private static Outcome CheckPostureAndSweep(XElement assertion, IReadOnlyList<AssessedExpression>? assessed)
    {
        if (assessed == null) return Outcome.Fail("assert-posture-and-sweep has no posture-and-sweep input");
        Posture expectedPosture = ParsePosture((string?)assertion.Attribute("result-posture"));
        Sweep expectedSweep = (string?)assertion.Attribute("result-sweep") switch
        {
            "motionless" => Sweep.Motionless,
            "consuming" => Sweep.Consuming,
            "free-ranging" => Sweep.FreeRanging,
            var sweep => throw new FormatException($"Unknown streamability sweep: {sweep}"),
        };
        foreach (var (expression, actual) in assessed)
        {
            if (actual.Posture != expectedPosture || actual.Sweep != expectedSweep)
                return Outcome.Fail($"part {(string?)expression.Attribute("part") ?? "?"}: {expression.Value}: " +
                    $"expected {expectedPosture}/{expectedSweep}, got {actual.Posture}/{actual.Sweep} (rule {actual.Rule})");
        }
        return Outcome.Pass();
    }

    private static Posture ParsePosture(string? value) => value switch
    {
        "grounded" => Posture.Grounded,
        "climbing" => Posture.Climbing,
        "striding" => Posture.Striding,
        "crawling" => Posture.Crawling,
        "roaming" => Posture.Roaming,
        _ => throw new FormatException($"Unknown streamability posture: {value}"),
    };
}
