// Licensed under the MIT license. See LICENSE.TXT in the repository root.

namespace Xsl3.Xdm
{
    /// <summary>
    /// An expanded QName: a namespace URI and a local name, plus the prefix it was written with (SPEC §7.2).
    /// Two QNames are equal when their namespace URIs and local names are equal; the prefix is ignored.
    /// </summary>
    public sealed class QName : IEquatable<QName>
    {
        public QName(string localName) : this(string.Empty, localName) { }

        public QName(string namespaceUri, string localName) : this(namespaceUri, localName, string.Empty) { }

        public QName(string namespaceUri, string localName, string prefix)
        {
            ArgumentNullException.ThrowIfNull(localName);
            NamespaceUri = namespaceUri ?? string.Empty;
            LocalName = localName;
            Prefix = prefix ?? string.Empty;
        }

        public string NamespaceUri { get; }

        public string LocalName { get; }

        /// <summary>The prefix, or the empty string for none.</summary>
        public string Prefix { get; }

        /// <summary><c>Q{uri}local</c>, the EQName form that needs no namespace context.</summary>
        public string EQName => $"Q{{{NamespaceUri}}}{LocalName}";

        /// <summary>The lexical form <c>prefix:local</c>, or <c>local</c>.</summary>
        public override string ToString() => Prefix.Length == 0 ? LocalName : Prefix + ":" + LocalName;

        public bool Equals(QName? other) =>
            other is not null && LocalName == other.LocalName && NamespaceUri == other.NamespaceUri;

        public override bool Equals(object? obj) => Equals(obj as QName);

        public override int GetHashCode() => HashCode.Combine(NamespaceUri, LocalName);

        public static bool operator ==(QName? a, QName? b) => a is null ? b is null : a.Equals(b);

        public static bool operator !=(QName? a, QName? b) => !(a == b);

        internal static class Ns
        {
            public const string Xs = "http://www.w3.org/2001/XMLSchema";
            public const string Fn = "http://www.w3.org/2005/xpath-functions";
            public const string Math = "http://www.w3.org/2005/xpath-functions/math";
            public const string Map = "http://www.w3.org/2005/xpath-functions/map";
            public const string Array = "http://www.w3.org/2005/xpath-functions/array";
            public const string Err = "http://www.w3.org/2005/xqt-errors";
            public const string Xml = "http://www.w3.org/XML/1998/namespace";
        }

        internal static QName Xs(string local) => new(Ns.Xs, local, "xs");
    }
}
