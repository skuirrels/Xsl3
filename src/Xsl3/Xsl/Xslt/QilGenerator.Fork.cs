// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using Xsl3.Qil;
using Xsl3.Runtime;
using Xsl3.XPath;
using Xsl3.XPath31;
using System.Collections.Generic;
using System.Linq;
using T = Xsl3.XmlQueryTypeFactory;

namespace Xsl3.Xslt;

internal sealed partial class QilGenerator
{
    QilNode IXPath31Environment.ParallelMap(IReadOnlyList<QilNode> entries, bool xslt)
    {
        QilList functions = _f.BaseFactory.Sequence();
        foreach (QilNode entry in entries)
            functions.Add(Lift31(_ => entry, 0, debugName: "<map entry>"));
        return ForkMap31(functions, xslt);
    }

    private QilNode CompileMap31(XslNode node)
    {
        if (!StreamedCompilation31 || node.Content.Count < 2 || node.Content.Any(child => child.NodeType != XslNodeType.MapEntry))
            return X31(nameof(XsltLibrary.X31MapMerge), T.Item, Box(CompileInstructions(node.Content)));
        QilList functions = _f.BaseFactory.Sequence();
        foreach (XslNode entry in node.Content)
            functions.Add(Lift31(_ => {
                _forkDepth31++;
                try { return CompileInstructions(new[] { entry }); }
                finally { _forkDepth31--; }
            }, 0, debugName: "<xsl:map-entry>"));
        return ForkMap31(functions, xslt: true);
    }

    private QilNode ForkMap31(QilList functions, bool xslt)
    {
        // The XPath expression may have an inner focus that differs from the host's
        // instruction focus. Prepared captures identify its actual source boundary.
        return Stateful31(nameof(XsltLibrary.X31ForkMap), T.Item, functions,
            X31(nameof(XsltLibrary.X31AbsentFocus), T.Item),
            _f.Double(_compiler.Settings.StreamingBufferCapacity), _f.Boolean(xslt));
    }

    private int _forkDepth31;
    private QilNode CompileFork31(XslNode node)
    {
        QilList functions = _f.BaseFactory.Sequence(), results = _f.BaseFactory.Sequence();
        foreach (XslNode prong in node.Content)
        {
            functions.Add(Lift31(_ => {
                QilNode body;
                _forkDepth31++;
                try { body = CompileInstructions(new[] { prong }); }
                finally { _forkDepth31--; }
                results.Add(body); // used only to compute the concatenated result type
                return Stateful31(nameof(XsltLibrary.X31ForkPush), T.Empty, body);
            }, 0, debugName: "<xsl:fork prong>", outputWorker: true));
        }
        return Stateful31(nameof(XsltLibrary.X31Fork), results.XmlType!, functions,
            FocusAbsent ? X31(nameof(XsltLibrary.X31AbsentFocus), T.Item) : ((IXPathEnvironment)this).GetCurrent()!,
            _f.Double(_compiler.Settings.StreamingBufferCapacity));
    }
}
