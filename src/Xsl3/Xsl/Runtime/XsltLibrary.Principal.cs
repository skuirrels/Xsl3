// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Xml;
using System.Xml.XPath;
using Xsl3.Xdm;

namespace Xsl3.Runtime;

public sealed partial class XsltLibrary
{
    public XPathItem X31BufferResultItem(XPathItem item) =>
        ProjectForkBindings(PrepareForkOutput(new[] { item }))[0];

    // Ordinary evaluation and fork callbacks share principal ownership, serialization
    // configuration and conflict checks. Only the invoking thread touches this target.
    internal IList<XPathItem> WritePrincipalResult(PrincipalResult? target, IList<XPathItem> items,
        XmlWriterSettings settings, OutputExtras? extras, bool raw)
    {
        if (target == null) return raw ? items : X31ContentItems(items);
        if (raw && extras?.RawMethod != null && target.WriteItems(items, settings, extras)) return Sequences.Empty;
        target.BeginExplicit(settings, extras);
        if (raw)
        {
            bool previousAtomic = false;
            for (int i = 0; i < items.Count; i++)
            {
                XmlQueryRuntime.CheckCancellation();
                bool atomic = items[i] is not XPathNavigator;
                if (i > 0 && (extras?.ItemSeparator != null || atomic && previousAtomic))
                    target.WriteString(extras?.ItemSeparator ?? " ");
                if (items[i] is XPathNavigator { NodeType: XPathNodeType.Root } root) WriteChildren(target, root);
                else if (items[i] is XPathNavigator node) target.WriteNode(node, defattr: true);
                else target.WriteString(items[i].Value);
                previousAtomic = atomic;
            }
        }
        else WriteChildren(target, (XPathNavigator)items[0]);
        target.EndExplicit();
        return Sequences.Empty;
    }
}
