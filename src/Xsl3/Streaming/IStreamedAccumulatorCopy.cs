// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Xml.XPath;

namespace Xsl3.Streaming;

// A completed copy owns recorded phases, not the source execution that computed them.
internal interface IStreamedAccumulatorCopy
{
    bool Applies(string name);
    bool Contains(XPathNavigator node);
    bool TryValue(XPathNavigator node, string name, bool after, out IList<XPathItem>? value);
}

internal interface IStreamedAccumulatorCopyProvider
{
    IStreamedAccumulatorCopy? CaptureCopy(XPathNavigator copy, StreamedNavigator original);
}
