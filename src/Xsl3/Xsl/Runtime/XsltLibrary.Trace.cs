// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Xml.XPath;
using Xsl3.Streaming;
using Xsl3.Xdm;
using Xsl3.XPath31.Functions;

namespace Xsl3.Runtime;

public sealed partial class XsltLibrary
{
    public bool X31HasTraceHandler()
    {
        XmlQueryRuntime.CheckCancellation();
        return _runtime.ExternalContext.HasTraceHandler;
    }

    public string X31TraceLabel(IList<XPathItem> label)
    {
        XmlQueryRuntime.CheckCancellation();
        FunctionDefinition definition = FunctionLibrary.Find(QName.Ns.Fn, "trace", 2)!;
        return Coercion.Coerce(label, definition.ParameterType(1), definition, 1)[0].Value;
    }

    public XPathItem X31TraceItem(XPathItem item)
    {
        XmlQueryRuntime.CheckCancellation();
        // Retain identity and annotations. Diagnostic serialization and the subsequent
        // consumer must both be able to read this explicitly buffered subtree.
        return item is StreamedNavigator streamed ? streamed.BufferSubtree()
            : item is XPathNavigator node ? node.Clone() : item;
    }
}
