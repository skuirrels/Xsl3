// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using Xsl3.Qil;
using Xsl3.Runtime;
using T = Xsl3.XmlQueryTypeFactory;

namespace Xsl3.Xslt;

internal sealed partial class QilGenerator
{
    System.IDisposable? XPath31.IXPath31Environment.FunctionBody()
    {
        if (_currentMerge31 == null) return null;
        var scope = new MergeFunctionScope(this, _currentMerge31);
        _currentMerge31 = null;
        return scope;
    }

    // XSLT 3.0 15.6: implicit merge context is absent in function values. Explicit
    // variables are still captured by the XPath compiler through its normal closure ABI.
    private sealed class MergeFunctionScope(QilGenerator owner,
        (QilNode Group, QilNode Key, QilNode Sources, List<string?> Names)? saved) : System.IDisposable
    {
        public void Dispose() => owner._currentMerge31 = saved;
    }

    private QilNode CompilePullMerge31(List<XslNode> sources, XslNode action, List<string?> names, QilIterator plan)
    {
        QilList inputs = _f.BaseFactory.Sequence();
        for (int index = 0; index < sources.Count; index++)
        {
            XslNode source = sources[index];
            EnterScope(source);
            try
            {
                XslNodeV2 data = source.V2!;
                QilIterator keyFunction = _f.Let(CompileMergeKeys31(source));
                bool forward = StreamedCompilation31 && data.Streamable == TriState.True && data.SourceForEachSource != null && !data.SortBeforeMerge
                    && _compiler.StreamabilityResults?.TryGetValue(source, out Streaming.StreamabilityVerdict? verdict) == true && verdict.Guaranteed;
                QilNode Input(QilNode? document, QilNode anchor)
                {
                    QilNode producer = Lift31(_ =>
                    {
                        QilIterator item = _f.For(CompileXPath31(source.Select!));
                        return _f.Loop(item, Stateful31(nameof(XsltLibrary.X31MergeRowNew), T.Item, item, keyFunction, plan,
                            _f.Double(index), _f.Boolean(data.Streamable == TriState.True)));
                    }, 1, pull: true);
                    return Stateful31(nameof(XsltLibrary.X31MergeInputNew), T.Item,
                        Stateful31(nameof(XsltLibrary.X31PullNew), T.Item, producer), _f.Double(index), anchor,
                        document == null ? _f.TypeAssert(_f.Sequence(), T.ItemS) : Box(document));
                }
                string? forEach = forward ? data.SourceForEachSource : data.ForEachItem ?? data.ForEachSource;
                QilNode sourceInputs;
                if (forEach == null) sourceInputs = Input(null, _f.Double(1));
                else
                {
                    QilNode anchors = CompileXPath31(forEach);
                    if (forward) anchors = Coerce31(anchors, $"Q{{{XmlReservedNs.NsXs}}}string*", "XPTY0004");
                    QilIterator each = _f.For(Box(anchors));
                    LoopFocus saved = _curLoop;
                    _curLoop.SetFocus(each);
                    try
                    {
                        QilNode position = _curLoop.GetPosition(), size = _curLoop.GetLast();
                        QilNode Make(QilNode context, QilNode? document) => WithMergeAnchorFocus31(context, position, size, () => Input(document, position));
                        QilNode input;
                        if (forward)
                        {
                            QilIterator document = _f.Let(OpenStreamedSource31(String31(each), data));
                            input = _f.Loop(document, Make(document, document));
                        }
                        else input = Make(each, null);
                        sourceInputs = _curLoop.ConstructLoop(input);
                    }
                    finally { _curLoop = saved; }
                }
                inputs.Add(_f.Loop(keyFunction, sourceInputs));
            }
            finally { ExitScope(); }
        }
        QilIterator cursor = _f.Let(Stateful31(nameof(XsltLibrary.X31ForwardMergeNew), T.Item, Box(inputs), plan));
        return CompileMergeAction31(cursor, nameof(XsltLibrary.X31MergeNextGroup), names, action, streamed: true);
    }

    private QilNode CompileMergeAction31(QilIterator cursor, string nextGroup, List<string?> names, XslNode action, bool streamed)
    {
        QilIterator group = _f.For(PullSequence31(nextGroup, T.ItemS, cursor));
        LoopFocus oldLoop = _curLoop;
        var oldMerge = _currentMerge31;
        _curLoop = new LoopFocus(_f);
        _curLoop.SetFocus(group);
        try
        {
            QilNode position = _curLoop.GetPosition();
            QilNode body = WithMergeAnchorFocus31(X31(nameof(XsltLibrary.X31MergeGroupFirst), T.Item, group), position,
                streamed ? AbsentMergeSize31() : X31(nameof(XsltLibrary.X31MaterializedMergeSize), T.DoubleX, cursor), () =>
            {
                QilIterator items = _f.Let(X31(nameof(XsltLibrary.X31MergeGroupItems), T.ItemS, group)),
                    key = _f.Let(X31(nameof(XsltLibrary.X31MergeGroupKey), T.ItemS, group)),
                    bySource = _f.Let(X31(nameof(XsltLibrary.X31MergeGroupSources), T.ItemS, group));
                _currentMerge31 = (items, key, bySource, names);
                EnterScope(action);
                try { return _f.Loop(items, _f.Loop(key, _f.Loop(bySource, CompileInstructions(action.Content)))); }
                finally { ExitScope(); }
            });
            return _f.Loop(cursor, _curLoop.ConstructLoop(body));
        }
        finally { _curLoop = oldLoop; _currentMerge31 = oldMerge; }
    }

    private QilNode WithMergeAnchorFocus31(QilNode item, QilNode position, QilNode size, System.Func<QilNode> compile) =>
        WithFixedFocus31(item, () =>
        {
            var saved = _fixedFocus31;
            _fixedFocus31 = (saved!.Value.Current, position, size);
            try { return compile(); }
            finally { _fixedFocus31 = saved; }
        });
}
