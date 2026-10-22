// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Xml;
using System.Xml.Schema;
using Xsl3.Xdm;

namespace Xsl3.Streaming;

/// <summary>Read-time errors and cancellation must pass through the same path when skipping content.</summary>
internal sealed class StreamedInputReader(XmlReader input, bool ownsInput, CancellationToken cancellation, bool stripAnnotations = false) : XmlWrappingReader(input), IXmlNamespaceResolver
{
    private HashSet<(string, string)>? _elementOnly;
    private bool[] _inElementOnly = Array.Empty<bool>();
    public override IXmlSchemaInfo? SchemaInfo => stripAnnotations ? null : base.SchemaInfo;

    public override bool Read()
    {
        try
        {
            while (true)
            {
                cancellation.ThrowIfCancellationRequested();
                if (!base.Read()) return false;
                if (NodeType == XmlNodeType.DocumentType)
                    _elementOnly = DtdInfoBridge.GetElementOnlyElements(this);
                else if (_elementOnly is { Count: > 0 })
                {
                    if (NodeType == XmlNodeType.Element)
                    {
                        if (Depth >= _inElementOnly.Length) Array.Resize(ref _inElementOnly, Depth * 2 + 2);
                        _inElementOnly[Depth] = _elementOnly.Contains((LocalName, Prefix));
                    }
                    else if (NodeType is XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace &&
                        Depth > 0 && Depth - 1 < _inElementOnly.Length && _inElementOnly[Depth - 1]) continue;
                }
                return true;
            }
        }
        catch (Exception e) when (e is not (XdmException or OperationCanceledException) && XmlException.IsCatchableException(e))
        { throw new XdmException("FODC0002", e.Message); }
    }

    public override void Skip()
    {
        // Delegating Skip to the underlying reader would bypass whitespace-reader state and cancellation.
        if (ReadState != ReadState.Interactive) return;
        MoveToElement();
        if (NodeType == XmlNodeType.Element && !IsEmptyElement)
        {
            int depth = Depth;
            while (Read() && Depth > depth) { }
            if (NodeType == XmlNodeType.EndElement) Read();
        }
        else Read();
    }

    public override void Close()
    {
        if (ownsInput) base.Close();
    }

    private IXmlNamespaceResolver? NamespaceResolver
    {
        get
        {
            XmlReader inner = reader;
            while (inner is XmlWrappingReader wrapping) inner = wrapping.InnerReader;
            return inner as IXmlNamespaceResolver;
        }
    }

    public IDictionary<string, string> GetNamespacesInScope(XmlNamespaceScope scope) =>
        NamespaceResolver?.GetNamespacesInScope(scope) ?? new Dictionary<string, string>();
    string? IXmlNamespaceResolver.LookupNamespace(string prefix) => LookupNamespace(prefix);
    public string? LookupPrefix(string namespaceName) => NamespaceResolver?.LookupPrefix(namespaceName);
}
