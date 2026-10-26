// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using Xsl3.IlGen;
using Xsl3.Qil;
using Xsl3.Runtime;
using System.Collections.Generic;
using T = Xsl3.XmlQueryTypeFactory;

namespace Xsl3.Xslt;

internal sealed partial class QilGenerator
{
    private Stack<bool>? _streamableScopes31;
    private bool InDeclaredStreamable31 => _streamableScopes31 is { Count: > 0 } scopes && scopes.Peek();

    private void EnterStreamableScope31(XslNode node)
    {
        if (!IsV2 || !_compiler.HasStreamableDeclarations) return;
        bool declared = node.V2?.Streamable == TriState.True || node is Template { Match: not null } template
            && Streaming.ConstructAnalysis.IsStreamableTemplate(_compiler, template);
        bool inherited = InDeclaredStreamable31;
        (_streamableScopes31 ??= new()).Push(inherited || declared);
    }

    private void ExitStreamableScope31()
    {
        if (IsV2 && _compiler.HasStreamableDeclarations) _streamableScopes31!.Pop();
    }

    private QilNode PullSequence31(string method, XmlQueryType type, QilNode state)
    {
        QilNode call = Stateful31(method, type, state);
        OptimizerPatterns.Write(call).AddPattern(OptimizerPatternName.PullSequence);
        return call;
    }

    private QilNode StreamedGroupMembers31(QilNode newCursor, bool nodes)
    {
        QilIterator cursor = _f.Let(newCursor);
        QilNode members = _f.Loop(cursor, PullSequence31(nodes ? nameof(XsltLibrary.X31GroupMemberNodeNext) : nameof(XsltLibrary.X31GroupMemberNext),
            nodes ? T.NodeNotRtfS : T.ItemS, cursor));
        return nodes ? XPath31.XPath31Compiler.SameDepth(members) : members;
    }
}
