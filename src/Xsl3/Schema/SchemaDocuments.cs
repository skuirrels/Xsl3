// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;

namespace Xsl3.Schema
{
    /// <summary>
    /// Xsl3 (P6): reading schema documents. Elements a schema marks for a later XSD version, with the versioning
    /// namespace's <c>vc:minVersion</c> and <c>vc:maxVersion</c> (XSD 1.1 §4.2.1, conditional inclusion), are left out, as
    /// an XSD 1.0 processor should: an XSD 1.1 assertion then doesn't stop the rest of the schema being used.
    /// </summary>
    internal static class SchemaDocuments
    {
        private const string Versioning = "http://www.w3.org/2007/XMLSchema-versioning";

        /// <summary>A schema set whose documents (and those they include and import) are read this way.</summary>
        public static XmlSchemaSet CreateSet(XmlResolver? resolver) => new() { XmlResolver = new Resolver(resolver ?? new XmlUrlResolver()) };

        /// <summary>The document as an XSD 1.0 processor sees it; the stream itself when it has no conditional inclusion.</summary>
        private static Stream Filter(Stream stream, Uri uri)
        {
            var copy = new MemoryStream();
            stream.CopyTo(copy);
            stream.Dispose();
            copy.Position = 0;
            if (copy.Length == 0 || !System.Text.Encoding.UTF8.GetString(copy.GetBuffer(), 0, (int)copy.Length).Contains(Versioning, StringComparison.Ordinal))
            {
                return copy;
            }
            XDocument doc;
            using (XmlReader reader = XmlReader.Create(copy, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore }, uri.AbsoluteUri))
            {
                doc = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
            }
            XName min = XName.Get("minVersion", Versioning), max = XName.Get("maxVersion", Versioning);
            foreach (XElement e in doc.Descendants().Where(e => !Applies((string?)e.Attribute(min), (string?)e.Attribute(max))).ToList())
            {
                e.Remove();
            }
            var filtered = new MemoryStream();
            doc.Save(filtered);
            filtered.Position = 0;
            return filtered;
        }

        // Whether an element is for XSD 1.0: minVersion <= 1.0 < maxVersion.
        private static bool Applies(string? minVersion, string? maxVersion) =>
            (minVersion == null || !decimal.TryParse(minVersion.Trim(), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out decimal lo) || lo <= 1.0m) &&
            (maxVersion == null || !decimal.TryParse(maxVersion.Trim(), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out decimal hi) || hi > 1.0m);

        private sealed class Resolver(XmlResolver inner) : XmlResolver
        {
            public override ICredentials Credentials
            {
                set => inner.Credentials = value;
            }

            public override object? GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn)
            {
                object? entity = inner.GetEntity(absoluteUri, role, ofObjectToReturn);
                return entity is Stream stream ? Filter(stream, absoluteUri) : entity;
            }

            public override Uri ResolveUri(Uri? baseUri, string? relativeUri) => inner.ResolveUri(baseUri, relativeUri);
        }
    }
}
