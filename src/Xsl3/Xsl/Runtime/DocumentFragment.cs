// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Threading;
using System.Xml;
using System.Xml.XPath;
using Xsl3.Schema;
using Xsl3.Xdm;
using Xsl3.XPath31.Functions;

namespace Xsl3.Runtime;

/// <summary>The shared document-reference and shorthand-ID policy for retrieval and forward source entry points.</summary>
internal readonly record struct DocumentFragment(Uri DocumentUri, Uri ResourceUri, string? Id)
{
    internal static XmlReader ScopePositionedReader(XmlReader input, bool ownsInput)
    {
        // Xsl3 (P7C): XmlReader.ReadSubtree alone can omit inherited namespace bindings
        // unused in element/attribute names (including prefixes used by QName values).
        // Use the existing selected-source view to retain that context and ownership.
        XmlReader namespaceSource = input;
        while (namespaceSource is XmlWrappingReader wrapping && namespaceSource is not IXmlNamespaceResolver)
            namespaceSource = wrapping.InnerReader;
        var namespaces = (namespaceSource as IXmlNamespaceResolver)?.GetNamespacesInScope(XmlNamespaceScope.All);
        return new SelectedReader(input.ReadSubtree(), input, ownsInput, input.BaseURI, input.BaseURI, input.BaseURI,
            namespaces, IdDeclarations(input) ?? new Dictionary<(string, string), (string, string)>(),
            IdrefDeclarations(input) ?? new HashSet<(string, string, string, string)>());
    }

    internal static IEqualityComparer<Uri> Comparer { get; } = new DocumentComparer();
    private sealed class DocumentComparer : IEqualityComparer<Uri>
    {
        public bool Equals(Uri? x, Uri? y) => ReferenceEquals(x, y) || x != null && y != null && x.Equals(y)
            && x.GetComponents(UriComponents.Fragment, UriFormat.Unescaped) == y.GetComponents(UriComponents.Fragment, UriFormat.Unescaped);
        public int GetHashCode(Uri uri) => HashCode.Combine(uri.GetHashCode(),
            StringComparer.Ordinal.GetHashCode(uri.GetComponents(UriComponents.Fragment, UriFormat.Unescaped)));
    }

    internal static IDictionary<string, string>? RootNamespaces(XmlReader input)
    {
        while (true)
        {
            if (input is SelectedReader selected) return selected.Namespaces;
            if (input is not XmlWrappingReader wrapping) return null;
            input = wrapping.InnerReader;
        }
    }

    internal static IReadOnlyDictionary<(string, string), (string, string)>? IdDeclarations(XmlReader input)
    {
        while (true)
        {
            if (input is SelectedReader selected) return selected.Ids;
            if (input is not XmlWrappingReader wrapping) return null;
            input = wrapping.InnerReader;
        }
    }

    internal static IReadOnlySet<(string, string, string, string)>? IdrefDeclarations(XmlReader input)
    {
        while (true)
        {
            if (input is SelectedReader selected) return selected.Idrefs;
            if (input is not XmlWrappingReader wrapping) return null;
            input = wrapping.InnerReader;
        }
    }
    // Check the supplied URI before System.Uri or a provider can normalize path separators.
    // A literal backslash is not a URI-reference character; its encoded form remains valid.
    internal static void ValidateReference(string reference)
    {
        if (reference.Contains('\\'))
            throw new XdmException("FODC0005", $"Document reference '{reference}' contains a backslash; use URI path separators or percent-encoding");
    }

    internal static DocumentFragment Parse(Uri uri)
    {
        if (!uri.IsAbsoluteUri) throw new XdmException("FODC0005", $"Document URI '{uri}' is not absolute");
        if (!uri.OriginalString.Contains('#')) return new(uri, uri, null);
        Uri resource = new UriBuilder(uri) { Fragment = "" }.Uri;
        string id = Uri.UnescapeDataString(uri.Fragment.Length == 0 ? "" : uri.Fragment[1..]);
        // An empty fragment identifies the complete resource, sharing its ordinary document cache.
        if (id.Length == 0) return new(resource, resource, null);
        if (!Casting.IsNCName(id))
            throw new XdmException("FODC0002", $"Document fragment '{id}' is not a supported shorthand ID");
        return new(uri, resource, id);
    }

    internal XmlReader Select(XmlReader input, bool ownsInput, CancellationToken cancellation)
    {
        if (Id == null) return input;
        var declaredIds = new Dictionary<(string Local, string Prefix), (string Local, string Prefix)>();
        var declaredIdrefs = new HashSet<(string, string, string, string)>();
        var ancestors = new List<(int Depth, string ReaderBase, string EffectiveBase)>();
        void ReadDeclarations()
        {
            foreach (var (elementLocal, elementPrefix, attributeLocal, attributePrefix) in DtdInfoBridge.GetIdAttributes(input, includeUndeclared: true))
                declaredIds[(elementLocal, elementPrefix)] = (attributeLocal, attributePrefix);
            declaredIdrefs.UnionWith(DtdInfoBridge.GetIdrefAttributes(input, includeUndeclared: true));
        }
        bool current = input.ReadState == ReadState.Interactive;
        // A supplied reader may already have passed its DOCTYPE. The parser still owns
        // those declarations; read them before inspecting its current element.
        if (current)
        {
            cancellation.ThrowIfCancellationRequested();
            ReadDeclarations();
        }
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!current && !input.Read()) break;
            current = false;
            if (input.NodeType == XmlNodeType.DocumentType) ReadDeclarations();
            if (input.NodeType != XmlNodeType.Element) continue;
            while (ancestors.Count != 0 && ancestors[^1].Depth >= input.Depth) ancestors.RemoveAt(ancestors.Count - 1);
            string inheritedBase = ancestors.Count != 0 && ancestors[^1].ReaderBase == input.BaseURI
                ? ancestors[^1].EffectiveBase : input.BaseURI;
            string? localBase = input.GetAttribute("base", XmlReservedNs.NsXml);
            string effectiveBase = localBase == null ? inheritedBase : UriReference.IsAbsolute(localBase) || !UriReference.IsAbsolute(inheritedBase)
                ? localBase : UriReference.Resolve(localBase, inheritedBase);
            ancestors.Add((input.Depth, input.BaseURI, effectiveBase));
            declaredIds.TryGetValue((input.LocalName, input.Prefix), out var declared);
            bool found = false;
            if (input.MoveToFirstAttribute())
            {
                do
                {
                    bool isId = input.LocalName == "id" && input.NamespaceURI == XmlReservedNs.NsXml
                        || declared.Local != null && input.LocalName == declared.Local && input.Prefix == declared.Prefix
                        || input.SchemaInfo?.SchemaType is { } type && SchemaTypes.IsId(type);
                    if (isId && input.Value.Trim(' ', '\t', '\r', '\n') == Id) { found = true; break; }
                }
                while (input.MoveToNextAttribute());
                input.MoveToElement();
            }
            if (found) return new SelectedReader(input.ReadSubtree(), input, ownsInput, DocumentUri.AbsoluteUri, input.BaseURI, inheritedBase,
                (input as IXmlNamespaceResolver)?.GetNamespacesInScope(XmlNamespaceScope.All), declaredIds, declaredIdrefs);
        }
        throw new XdmException("FODC0002", $"No element with ID '{Id}' was found in '{ResourceUri}'");
    }

    internal XPathNavigator? Find(XPathNavigator document, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        XPathNavigator node = document.Clone();
        node.MoveToRoot();
        if (!node.MoveToId(Id!))
        {
            bool found = false;
            XPathNodeIterator elements = node.SelectDescendants(XPathNodeType.Element, matchSelf: false);
            while (elements.MoveNext())
            {
                cancellation.ThrowIfCancellationRequested();
                XPathNavigator element = elements.Current!;
                if (element.GetAttribute("id", XmlReservedNs.NsXml).Trim(' ', '\t', '\r', '\n') == Id)
                {
                    node = element.Clone(); found = true; break;
                }
            }
            if (!found) return null;
        }
        return node;
    }

    internal XmlReader Select(XPathNavigator document, CancellationToken cancellation)
    {
        XPathNavigator node = Find(document, cancellation)
            ?? throw new XdmException("FODC0002", $"No element with ID '{Id}' was found in '{ResourceUri}'");
        XPathNavigator parent = node.Clone();
        string inheritedBase = parent.MoveToParent() ? FunctionLibrary.BaseUri(parent) : node.BaseURI;
        var identities = new Dictionary<long, NodeIdentity>();
        XPathNavigator probe = node.Clone();
        XPathNodeIterator descendants = node.SelectDescendants(XPathNodeType.Element, matchSelf: true);
        long ordinal = -1;
        while (descendants.MoveNext())
        {
            cancellation.ThrowIfCancellationRequested();
            ordinal++;
            XPathNavigator element = descendants.Current!, attribute = element.Clone();
            HashSet<(string, string)>? ids = null, idrefs = null;
            HashSet<string>? lookups = element is Tree.CompactNavigator { LookupIds: { } existing }
                ? new(existing, StringComparer.Ordinal) : null;
            HashSet<string>? knownValues = null;
            if (attribute.MoveToFirstAttribute())
            {
                do
                {
                    cancellation.ThrowIfCancellationRequested();
                    var name = (attribute.LocalName, attribute.NamespaceURI);
                    var type = SchemaTypes.TypeOf(attribute);
                    bool typedId = type != null && SchemaTypes.IsId(type);
                    if (attribute.NamespaceURI == XmlReservedNs.NsXml && attribute.LocalName == "id" || typedId)
                        (knownValues ??= new(StringComparer.Ordinal)).Add(Xdm.Lexical.Collapse(attribute.Value));
                    // Known attribute properties stay local to this element. A lookup by value
                    // does not reveal which attribute, if any, supplied that value to the provider.
                    if (attribute is Tree.CompactNavigator { IsIdAttribute: true } && !typedId)
                        (ids ??= new()).Add(name);
                    if (attribute is Tree.CompactNavigator { IsIdrefAttribute: true } && (type == null || !SchemaTypes.IsIdref(type)))
                        (idrefs ??= new()).Add(name);
                    if (element is not Tree.CompactNavigator && probe.MoveToId(attribute.Value) && probe.IsSamePosition(element))
                        (lookups ??= new(StringComparer.Ordinal)).Add(attribute.Value);
                }
                while (attribute.MoveToNextAttribute());
            }
            if (knownValues != null) lookups?.ExceptWith(knownValues);
            if (ids != null || idrefs != null || lookups is { Count: > 0 })
                identities.Add(ordinal, new(ids, idrefs, lookups is { Count: > 0 } ? lookups : null));
        }
        XmlReader subtree = node.ReadSubtree();
        return new SelectedReader(subtree, subtree, ownsSource: false, DocumentUri.AbsoluteUri, node.BaseURI, inheritedBase,
            node.GetNamespacesInScope(XmlNamespaceScope.All), new Dictionary<(string, string), (string, string)>(),
            new HashSet<(string, string, string, string)>(), identities);
    }

    private sealed class SelectedReader(XmlReader subtree, XmlReader source, bool ownsSource, string documentUri, string readerBase, string inheritedBase,
        IDictionary<string, string>? namespaces, IReadOnlyDictionary<(string, string), (string, string)> ids,
        IReadOnlySet<(string, string, string, string)> idrefs, IReadOnlyDictionary<long, NodeIdentity>? identities = null)
        : XmlWrappingReader(subtree), IXmlNamespaceResolver, INodeIdentityReader
    {
        private bool _closed;
        private long _element = -1;
        private readonly Dictionary<string, string>? _namespaces = namespaces == null ? null : new(namespaces, StringComparer.Ordinal);
        internal IDictionary<string, string>? Namespaces => _namespaces;
        internal IReadOnlyDictionary<(string, string), (string, string)> Ids => ids;
        internal IReadOnlySet<(string, string, string, string)> Idrefs => idrefs;
        public NodeIdentity? CurrentIdentity => identities != null && identities.TryGetValue(_element, out var identity) ? identity : null;
        public override bool Read()
        {
            bool result = base.Read();
            if (result && NodeType == XmlNodeType.Element) _element++;
            return result;
        }
        // A distinct document URI identifies the selected view. Element base URIs continue to come
        // from the original reader, including xml:base inherited from outside the selected element.
        public override string BaseURI => ReadState == ReadState.Initial ? documentUri : base.BaseURI == readerBase ? inheritedBase : base.BaseURI;
        public IDictionary<string, string> GetNamespacesInScope(XmlNamespaceScope scope)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (scope != XmlNamespaceScope.Local && _namespaces != null)
                foreach (var ns in _namespaces)
                    if (scope == XmlNamespaceScope.All || ns.Key != "xml") result[ns.Key] = ns.Value;
            if (reader is IXmlNamespaceResolver resolver)
                foreach (var ns in resolver.GetNamespacesInScope(scope)) result[ns.Key] = ns.Value;
            return result;
        }
        public override string? LookupNamespace(string prefix) => base.LookupNamespace(prefix)
            ?? (_namespaces != null && _namespaces.TryGetValue(prefix, out string? uri) ? uri : null);
        public string? LookupPrefix(string namespaceName)
        {
            foreach (var ns in GetNamespacesInScope(XmlNamespaceScope.All))
                if (ns.Value == namespaceName) return ns.Key;
            return null;
        }
        public override void Close()
        {
            if (_closed) return;
            _closed = true;
            try { base.Close(); }
            finally { if (ownsSource) source.Close(); }
        }
    }
}
