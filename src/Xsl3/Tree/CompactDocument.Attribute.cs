// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Xml;

namespace Xsl3.Tree;

public sealed partial class CompactDocument
{
    // A single untyped, parentless attribute needs neither a child-node buffer nor
    // a mutable builder stack. Annotation/identity-sensitive copies use the builder.
    internal static CompactDocument UntypedAttribute(XmlNameTable names, string prefix, string local, string ns, string value)
    {
        NamePool pool = NamePool.For(names);
        int name = pool.Name(prefix, local, ns);
        var document = new CompactDocument
        {
            _nameTable = names,
            _pool = pool,
            _xmlPrefix = pool.XmlPrefix,
            _xmlUri = pool.XmlUri,
            _local = pool.Local,
            _uri = pool.Uri,
            _prefix = pool.Prefix,
            _expanded = pool.Expanded,
            _kind = Array.Empty<byte>(),
            _parent = Array.Empty<int>(),
            _end = Array.Empty<int>(),
            _name = Array.Empty<int>(),
            _data = Array.Empty<int>(),
            _attrOwner = [None],
            _attrName = [name],
            _attrValue = [0],
            _attrCount = 1,
            _values = [value],
            _nsOwner = Array.Empty<int>(),
            _nsPrefix = Array.Empty<string>(),
            _nsUri = Array.Empty<string>(),
        };
        document.BuildScopes();
        return document;
    }
}
