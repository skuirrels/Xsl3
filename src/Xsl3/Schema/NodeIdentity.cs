// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Xml;

namespace Xsl3.Schema;

/// <summary>Provider-known identity facts for one element, independently of schema annotations.</summary>
internal sealed record NodeIdentity(
    IReadOnlySet<(string Local, string Namespace)>? IdAttributes,
    IReadOnlySet<(string Local, string Namespace)>? IdrefAttributes,
    IReadOnlyCollection<string>? LookupIds)
{
    internal static INodeIdentityReader? Reader(XmlReader reader)
    {
        while (true)
        {
            if (reader is INodeIdentityReader identity) return identity;
            if (reader is not XmlWrappingReader wrapping) return null;
            reader = wrapping.InnerReader;
        }
    }
}

internal interface INodeIdentityReader
{
    NodeIdentity? CurrentIdentity { get; }
}
