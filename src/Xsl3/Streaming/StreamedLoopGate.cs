// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using Xsl3.IlGen;
using Xsl3.Qil;

namespace Xsl3.Streaming;

internal static class StreamedLoopGate
{
    // The new iterator cannot occur in the already-compiled condition. This invariant lets IL
    // evaluate the condition before the input iterator has fetched its next item.
    internal static QilLoop Create(QilFactory factory, QilNode source, QilNode condition)
    {
        QilLoop gate = factory.Filter(factory.For(source), condition);
        OptimizerPatterns.Write(gate).AddPattern(OptimizerPatternName.LoopGate);
        return gate;
    }
}
