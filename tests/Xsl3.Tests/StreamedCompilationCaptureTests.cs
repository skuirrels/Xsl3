// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Xml;
using Xsl3.Qil;
using Xsl3.Streaming;
using Xunit;

namespace Xsl3.Tests;

public class StreamedCompilationCaptureTests
{
    [Theory]
    [InlineData("legacy", false)]
    [InlineData("legacy", true)]
    [InlineData("modern", false)]
    [InlineData("modern", true)]
#if NET9_0_OR_GREATER
    [InlineData("saved", false)]
    [InlineData("saved", true)]
#endif
    public void PublicCompilationPathsRecordActualVerifierCompletion(string api, bool forward)
    {
        const string stylesheet = """
            <xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform'>
              <xsl:mode streamable='yes'/>
              <xsl:template match='/'><xsl:value-of select='count(doc/n)'/></xsl:template>
            </xsl:stylesheet>
            """;
        using var capture = new StreamingExecutionCapture();
        using (StreamingExecutionCapture.ObserveStage(StreamingExecutionCapture.Stage.Compilation))
        {
            var settings = new XsltSettings { StreamedExecution = forward };
            if (api == "modern")
                new XsltCompiler { Settings = settings }.Compile(new StringReader(stylesheet));
            else
            {
                using var reader = XmlReader.Create(new StringReader(stylesheet));
#if NET9_0_OR_GREATER
                if (api == "saved")
                {
                    using var assembly = new MemoryStream();
                    new XslCompiledTransform().CompileToAssembly(reader, settings, null, "Verifier", "Saved.Verifier", assembly);
                }
                else
#endif
                new XslCompiledTransform().Load(reader, settings, null);
            }
        }
        var entry = Assert.Single(capture.SnapshotCompilations().Compilations);
        Assert.Equal("Compilation", entry.Stage);
        Assert.Equal(forward, entry.HasStreamedInputs);
        Assert.True(entry.OptimizationCompleted);
#if DEBUG
        int expected = forward ? 1 : 0;
#else
        int expected = 0;
#endif
        Assert.Equal(expected, entry.VerifierStarted);
        Assert.Equal(expected, entry.VerifierCompleted);
    }

    [Fact]
    public void ARejectedRewriteRecordsAnAttemptWithoutClaimingVerification()
    {
        using var capture = new StreamingExecutionCapture();
        var compilation = StreamingExecutionCapture.BeginCompilation(true);
        var factory = new QilFactory();
        var read = factory.Content(factory.XmlContext());
        var expression = factory.QilExpression(read, factory);
        var verifier = StreamedQilVerifier.Capture(expression);
        expression.Root = factory.Sequence(new[] { read, read });
        Assert.Throws<InvalidOperationException>(() => verifier.Verify(expression, compilation));
        var entry = Assert.Single(capture.SnapshotCompilations().Compilations);
        Assert.Equal(1, entry.VerifierStarted);
        Assert.Equal(0, entry.VerifierCompleted);
        Assert.False(entry.OptimizationCompleted);
    }

    [Fact]
    public void NestedCapturesDoNotLoseOrShareCompilationEvidence()
    {
        using var outer = new StreamingExecutionCapture();
        var pending = StreamingExecutionCapture.BeginCompilation(true)!;
        using (var inner = new StreamingExecutionCapture())
        {
            StreamingExecutionCapture.BeginCompilation(false)!.CompleteOptimization();
            Assert.False(Assert.Single(inner.SnapshotCompilations().Compilations).HasStreamedInputs);
            Assert.False(Assert.Single(outer.SnapshotCompilations().Compilations).OptimizationCompleted);
        }
        pending.CompleteOptimization(); // Missing verification remains visible even when optimization returned.
        var snapshot = outer.SnapshotCompilations();
        pending.BeginVerification();
        pending.CompleteVerification();
        Assert.Equal(0, Assert.Single(snapshot.Compilations).VerifierCompleted);
        Assert.Equal(1, Assert.Single(outer.SnapshotCompilations().Compilations).VerifierCompleted);
    }
}
