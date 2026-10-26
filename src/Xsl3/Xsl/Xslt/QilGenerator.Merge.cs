// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Linq;
using Xsl3.Qil;
using Xsl3.Runtime;
using Xsl3.XPath31;
using T = Xsl3.XmlQueryTypeFactory;

namespace Xsl3.Xslt;

internal sealed partial class QilGenerator
{
    private (List<string?> Names, MergeSpec Spec) MergeDeclaration31(List<XslNode> sources)
    {
        var names = new List<string?>();
        var spec = new MergeSpec();
        foreach (XslNode source in sources)
        {
            EnterScope(source);
            try
            {
                string? name = source.V2?.NameAvt;
                if (name != null && names.Contains(name)) ReportXPath31Error("XTSE3190", $"Two merge sources are named {name}");
                names.Add(name);
                if (source.Content.Count != sources[0].Content.Count)
                    ReportXPath31Error("XTSE2200", "The merge sources have different numbers of merge keys");
                spec.SortBeforeMerge.Add(source.V2!.SortBeforeMerge);
            }
            finally { ExitScope(); }
        }
        return (names, spec);
    }

    private QilNode CompileMergePlan31(List<XslNode> sources, MergeSpec spec)
    {
        QilList settings = _f.BaseFactory.Sequence();
        foreach (XslNode source in sources)
        {
            EnterScope(source);
            try
            {
                foreach (XslNode key in source.Content)
                {
                    EnterScope(key);
                    try
                    {
                        XslNodeV2 data = key.V2!;
                        settings.Add(X31(nameof(XsltLibrary.X31MergeKeyOptions), T.Item,
                            CompileStringAvt(data.Order ?? "ascending")!, CompileStringAvt(data.Lang ?? "")!,
                            CompileStringAvt(data.Collation ?? "")!, CompileStringAvt(data.CaseOrder ?? "")!,
                            CompileStringAvt(data.DataType ?? "")!, _f.Double((int)data.MergeKeyPresence), _f.String(StaticBaseUri31() ?? "")));
                    }
                    finally { ExitScope(); }
                }
            }
            finally { ExitScope(); }
        }
        return X31(nameof(XsltLibrary.X31MergePlan), T.Item, Box(settings), _f.Double(sources[0].Content.Count), _f.Double(ConstantPool.Add(spec)));
    }

    private QilNode CompileMergeKeys31(XslNode source) => Lift31(own => WithFixedFocus31(_f.TypeAssert(own[0], T.Item), () =>
    {
        QilList keys = _f.BaseFactory.Sequence();
        foreach (XslNode key in source.Content)
        {
            EnterScope(key);
            try
            {
                QilNode value = key.Select != null ? SelectOrContent31(key, key.Select) : TemporaryOutput31(() => SelectOrContent31(key, null));
                keys.Add(X31(nameof(XsltLibrary.X31Wrap), T.Item, Box(value)));
            }
            finally { ExitScope(); }
        }
        return Box(keys);
    }), 1);

    private QilNode MergeSelection31(XslNode source)
    {
        QilNode selection = CompileXPath31(source.Select!);
        if (source.V2?.Streamable != TriState.True) return selection;
        // §15.4 applies snapshots even when the processor elects to use a tree input.
        QilIterator item = _f.For(selection);
        return _f.Loop(item, X31(nameof(XsltLibrary.X31Snapshot), T.ItemS, Box(item), _f.Boolean(_compiler.Settings.SchemaAware)));
    }

    private QilNode CompileMaterializedMerge31(List<XslNode> sources, XslNode action, List<string?> names, QilIterator plan)
    {
        QilList inputs = _f.BaseFactory.Sequence(), keyFunctions = _f.BaseFactory.Sequence();
        foreach (XslNode source in sources)
        {
            EnterScope(source);
            try
            {
                string? forEach = source.V2!.ForEachItem ?? source.V2.ForEachSource;
                QilNode sourceInputs;
                if (forEach == null) sourceInputs = X31(nameof(XsltLibrary.X31Wrap), T.Item, Box(MergeSelection31(source)));
                else
                {
                    QilIterator each = _f.For(Box(CompileXPath31(forEach)));
                    LoopFocus saved = _curLoop;
                    _curLoop.SetFocus(each);
                    try
                    {
                        QilNode position = _curLoop.GetPosition(), size = _curLoop.GetLast();
                        sourceInputs = _curLoop.ConstructLoop(WithMergeAnchorFocus31(each, position, size,
                            () => X31(nameof(XsltLibrary.X31Wrap), T.Item, Box(MergeSelection31(source)))));
                    }
                    finally { _curLoop = saved; }
                }
                inputs.Add(X31(nameof(XsltLibrary.X31Wrap), T.Item, Box(sourceInputs)));
                keyFunctions.Add(CompileMergeKeys31(source));
            }
            finally { ExitScope(); }
        }
        bool streamed = sources.Any(source => source.V2?.Streamable == TriState.True);
        QilIterator inputsLet = _f.Let(Box(inputs)), keysLet = _f.Let(Box(keyFunctions));
        QilIterator cursor = _f.Let(Stateful31(nameof(XsltLibrary.X31MaterializedMergeNew), T.Item, inputsLet, keysLet, plan));
        return _f.Loop(inputsLet, _f.Loop(keysLet,
            CompileMergeAction31(cursor, nameof(XsltLibrary.X31MaterializedMergeNextGroup), names, action, streamed)));
    }

    private QilNode AbsentMergeSize31() => X31(nameof(XsltLibrary.X31RaiseDouble), T.DoubleX,
        _f.String("XPDY0002"), _f.String("The context size is absent in a merge action with a streamable source"));
}
