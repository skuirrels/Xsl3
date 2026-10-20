// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Xml;
using System.Xml.XPath;
using Xsl3.Xdm;

namespace Xsl3.Runtime
{
    public sealed partial class XsltLibrary
    {
        // The unparsed entities of each document read so far, by the document's base URI: name -> (system URI, public id).
        private Dictionary<string, Dictionary<string, (string SystemUri, string PublicId)>>? _unparsedEntities;

        /// <summary>
        /// unparsed-entity-uri and unparsed-entity-public-id (XSLT 3.0 §20.2.4, §20.2.5) for the tree containing node:
        /// the absolute system identifier (an xs:anyURI) or the public identifier of the unparsed entity of that name
        /// declared in the document's DTD, or "" when there is none. XPathDocument keeps no DTD, so the declarations
        /// are read again from the document's base URI, once per document, through the transform's document resolver;
        /// a document that has no base URI, or that the resolver refuses, has no unparsed entities.
        /// </summary>
        public XPathItem X31UnparsedEntity(string name, XPathNavigator node, bool publicId)
        {
            XPathNavigator root = node.Clone();
            root.MoveToRoot();
            if (root.NodeType != XPathNodeType.Root)
            {
                throw new XdmException(publicId ? "XTDE1380" : "XTDE1370", "The node is not in a tree rooted at a document node");
            }
            string uri = root.BaseURI;
            (string SystemUri, string PublicId) entity = default;
            if (uri.Length != 0)
            {
                _unparsedEntities ??= new Dictionary<string, Dictionary<string, (string, string)>>(StringComparer.Ordinal);
                if (!_unparsedEntities.TryGetValue(uri, out Dictionary<string, (string, string)>? entities))
                {
                    entities = _unparsedEntities[uri] = ReadUnparsedEntities(uri);
                }
                entities.TryGetValue(name, out entity);
            }
            return publicId ? AtomicValue.OfString(entity.PublicId ?? "") : new StringValue(AtomicTypes.AnyUri, entity.SystemUri ?? "");
        }

        private Dictionary<string, (string, string)> ReadUnparsedEntities(string uri)
        {
            var entities = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
            try
            {
                XmlResolver resolver = _runtime.ExternalContext.DataSources;
                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Parse, XmlResolver = resolver };
                using XmlReader reader = XmlReader.Create(uri, settings);
                var document = new XmlDocument { XmlResolver = resolver };
                document.Load(reader);
                if (document.DocumentType is { } doctype)
                {
                    foreach (XmlEntity e in doctype.Entities)
                    {
                        if (e.NotationName != null)
                        {
                            string declaredIn = string.IsNullOrEmpty(e.BaseURI) ? uri : e.BaseURI;
                            string system = e.SystemId == null ? "" : new Uri(new Uri(declaredIn), e.SystemId).AbsoluteUri;
                            entities[e.Name] = (system, e.PublicId ?? "");
                        }
                    }
                }
            }
            catch (Exception e) when (e is XmlException or System.IO.IOException or UnauthorizedAccessException or UriFormatException
                or InvalidOperationException or NotSupportedException or System.Net.WebException or System.Security.SecurityException)
            {
                // Not readable again: no unparsed entities.
            }
            return entities;
        }
    }
}
