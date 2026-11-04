// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using Xsl3.IlGen;
using Xsl3.Qil;
using Xsl3.Streaming;
using Xunit;

namespace Xsl3.Tests;

// S3-18: graphs with forward sources use the conservative optimizer policy. Each graph here is one that the ordinary
// policy rewrites so that a read moves (hoisted out of its loop, or inlined past a later read). The test runs the real
// optimizer under both policies, in any build configuration, and checks the result with the Debug verifier's contract:
// the ordinary policy breaks it, so the graph is a real rewrite target; the streamed policy keeps every read in place.
public class StreamedOptimizerPolicyTests
{
    [Theory]
    [InlineData("hoist")]
    [InlineData("inline")]
    public void TheStreamedPolicyKeepsReadsThatTheOrdinaryPolicyMoves(string rewrite)
    {
        QilExpression ordinary = Graph(rewrite, streamed: false);
        var ordinaryReads = StreamedQilVerifier.Capture(ordinary);
        QilExpression rewritten = new XmlILOptimizerVisitor(ordinary, optimize: true).Optimize();
        Assert.Throws<InvalidOperationException>(() => ordinaryReads.Verify(rewritten));

        QilExpression streamed = Graph(rewrite, streamed: true);
        var streamedReads = StreamedQilVerifier.Capture(streamed);
        QilNode root = streamed.Root;
        QilExpression kept = new XmlILOptimizerVisitor(streamed, optimize: true).Optimize();
        streamedReads.Verify(kept);
        Assert.Equal(root.NodeType, kept.Root.NodeType);
    }

    // hoist: [NormalizeLoopInvariant] turns a filter whose condition ignores the item into a conditional, so the
    // condition's read runs once before the loop's read instead of inside it.
    // inline: [EliminateIteratorUsedAtMostOnce] substitutes a single-use let binding at its use, so the binding's read
    // runs after the read that preceded the use.
    private static QilExpression Graph(string rewrite, bool streamed)
    {
        var f = new QilFactory();
        QilNode root = rewrite switch
        {
            "hoist" => f.Filter(f.For(f.Content(f.XmlContext())), f.Not(f.IsEmpty(f.Descendant(f.XmlContext())))),
            "inline" => Inline(f),
            _ => throw new ArgumentOutOfRangeException(nameof(rewrite)),
        };
        QilExpression expression = f.QilExpression(root, f);
        expression.HasStreamedInputs = streamed;
        return expression;
    }

    private static QilNode Inline(QilFactory f)
    {
        QilIterator binding = f.Let(f.Content(f.XmlContext()));
        return f.Loop(binding, f.Sequence(new QilNode[] { f.Descendant(f.XmlContext()), binding }));
    }
}
