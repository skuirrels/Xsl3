// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Xml.XPath;

namespace MS.Internal.Xml.XPath
{
    internal abstract class ResettableIterator : XPathNodeIterator
    {
        // Xsl3: upstream copies and resets XPathNodeIterator's internal "count" cache here. That field is
        // internal to System.Private.Xml; it already starts at -1, so leaving it alone only means a clone
        // recomputes Count on first use. Nothing in Xsl3 calls ResetCount.
        public ResettableIterator()
        {
        }
        protected ResettableIterator(ResettableIterator other)
        {
        }

        public abstract void Reset();

        // Construct extension: CurrentPosition should return 0 if MoveNext() wasn't called after Reset()
        // (behavior is not defined for XPathNodeIterator)
        public abstract override int CurrentPosition { get; }
    }
}
