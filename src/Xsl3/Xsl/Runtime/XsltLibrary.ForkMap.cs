// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Xml.XPath;
using Xsl3.Xdm;

namespace Xsl3.Runtime;

public sealed partial class XsltLibrary
{
    public XPathItem X31ForkMap(IList<XPathItem> prongs, XPathItem focus, double capacity, bool xslt)
    {
        var values = new PreparedForkBindings[prongs.Count];
        RunForkCalls(prongs, focus, capacity, (index, runtime, function) => {
            // Freeze any retained headers or closure captures before the private view is
            // disposed. This uses the same graph projection as explicit fork results.
            var library = runtime.XsltFunctions;
            values[index] = library.PrepareForkResult(library.CallFunction(function));
        }, serialGrounded: true);
        var builder = new Builder(xslt ? "XTDE3365" : "XQDY0137");
        foreach (PreparedForkBindings value in values)
        {
            XmlQueryRuntime.CheckCancellation();
            IList<XPathItem> entry = ProjectForkBindings(value);
            if (entry.Count != 1 || entry[0] is not MapItem map)
                throw new System.InvalidOperationException("A compiled map-entry prong must return one map");
            foreach (var (key, items) in map.Entries)
                X31MapEntry(builder, new XPathItem[] { key }, items);
        }
        return X31MapDone(builder);
    }
}
