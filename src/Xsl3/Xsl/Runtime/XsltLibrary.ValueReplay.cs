// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Xml.XPath;
using Xsl3.Streaming;

namespace Xsl3.Runtime;

public sealed partial class XsltLibrary
{
    // Only compiler-proven, unfiltered value populations call this helper. A plain
    // source navigator still rejects attempts to revisit consumed descendants.
    public XPathItem X31ValueOperand(XPathItem item) => item is StreamedNavigator node ? node.ValueOperand() : item;
}
