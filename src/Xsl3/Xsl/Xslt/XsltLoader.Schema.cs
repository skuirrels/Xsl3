// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Schema;
using ContextInfo = Xsl3.Xslt.XsltInput.ContextInfo;

namespace Xsl3.Xslt
{
    internal sealed partial class XsltLoader
    {
        // XSLT 3.0 §3.14.2
        private readonly XsltInput.XsltAttribute[] _importSchemaAttributes = {
            new XsltInput.XsltAttribute("namespace",       V2Opt),
            new XsltInput.XsltAttribute("schema-location", V2Opt),
        };

        /// <summary>
        /// Xsl3 (P6): xsl:import-schema, in a schema-aware processor. The schema comes from <c>schema-location</c>, from
        /// an <c>xs:schema</c> child, or else from the schemas the processor knows for <c>namespace</c>.
        /// </summary>
        private void LoadImportSchema()
        {
            ContextInfo ctxInfo = _input.GetAttributes(_importSchemaAttributes);
            string? namespaceUri = ParseStringAttribute(0, "namespace");
            string? location = ParseStringAttribute(1, "schema-location");
            ISourceLineInfo lineInfo = ctxInfo.lineInfo ?? SourceLineInfo.NoSource;
            XmlSchema? inline = null;
            if (_input.MoveToFirstChild())
            {
                do
                {
                    if (_input.NodeType != XmlNodeType.Element)
                    {
                        continue;
                    }
                    if (_input.NamespaceUri == XmlSchema.Namespace && _input.LocalName == "schema" && inline == null)
                    {
                        inline = ReadInlineSchema(lineInfo);
                    }
                    else
                    {
                        ReportError(SR.Xml_UserException, $"XTSE0010: xsl:import-schema can contain only one xs:schema, not {_input.QualifiedName}");
                        _input.SkipNode();
                    }
                }
                while (_input.MoveToNextSibling());
            }
            if (inline != null && location != null)
            {
                ReportError(SR.Xml_UserException, "XTSE0215: xsl:import-schema has both a schema-location attribute and an xs:schema child");
                return;
            }
            Uri? uri = null;
            if (location != null && inline == null)
            {
                try
                {
                    uri = ResolveUri(location, _input.BaseUri ?? "");
                }
                catch (Exception e) when (e is UriFormatException or ArgumentException)
                {
                    ReportError(SR.Xml_UserException, $"XTSE0220: schema-location '{location}' is not a valid URI");
                    return;
                }
            }
            _compiler.ImportSchema(namespaceUri?.Trim(), uri, inline, _xmlResolver, lineInfo);
        }

        // An xs:schema child of xsl:import-schema: its subtree, written out with the namespaces in scope, read as a schema.
        private XmlSchema? ReadInlineSchema(ISourceLineInfo lineInfo)
        {
            var text = new StringBuilder();
            using (XmlWriter writer = XmlWriter.Create(text, new XmlWriterSettings { OmitXmlDeclaration = true, ConformanceLevel = ConformanceLevel.Fragment }))
            {
                CopyInput(writer, inScope: true);
            }
            try
            {
                using XmlReader reader = XmlReader.Create(new StringReader(text.ToString()), new XmlReaderSettings(), _input.BaseUri ?? "");
                return XmlSchema.Read(reader, null);
            }
            catch (Exception e) when (e is XmlSchemaException or XmlException)
            {
                _compiler.ReportError(lineInfo, SR.Xml_UserException, "XTSE0220: the inline schema is not valid: " + e.Message);
                return null;
            }
        }

        // Writes the element the input is on, and its subtree (comments and processing instructions aren't kept by the
        // input, nor needed). The outermost element declares every namespace in scope, as the schema's QName-valued
        // attributes may use any of them.
        private void CopyInput(XmlWriter writer, bool inScope)
        {
            switch (_input.NodeType)
            {
                case XmlNodeType.Element:
                {
                    string prefix = _input.Prefix, local = _input.LocalName, ns = _input.NamespaceUri;
                    ContextInfo ctxInfo = _input.GetLiteralAttributes(asStylesheet: false);
                    writer.WriteStartElement(prefix, local, ns);
                    if (inScope)
                    {
                        foreach (var (p, uri) in _input.StaticNamespaces())
                        {
                            if (p != prefix && p != "xml")
                            {
                                writer.WriteAttributeString("xmlns", p, null, uri);
                            }
                        }
                        if (prefix.Length > 0 && _input.LookupXmlNamespace("") is { Length: > 0 } defaultNs)
                        {
                            writer.WriteAttributeString("xmlns", defaultNs);
                        }
                    }
                    else
                    {
                        for (NsDecl? decl = ctxInfo.nsList; decl != null; decl = decl.Prev)
                        {
                            if (decl.Prefix != null && decl.NsUri != null && decl.Prefix != prefix)
                            {
                                if (decl.Prefix.Length == 0)
                                {
                                    writer.WriteAttributeString("xmlns", decl.NsUri);
                                }
                                else
                                {
                                    writer.WriteAttributeString("xmlns", decl.Prefix, null, decl.NsUri);
                                }
                            }
                        }
                    }
                    for (int i = 1; _input.MoveToLiteralAttribute(i); i++)
                    {
                        writer.WriteAttributeString(_input.Prefix, _input.LocalName, _input.NamespaceUri, _input.Value);
                    }
                    _input.MoveToElement();
                    if (_input.MoveToFirstChild())
                    {
                        do
                        {
                            CopyInput(writer, inScope: false);
                        }
                        while (_input.MoveToNextSibling());
                    }
                    writer.WriteEndElement();
                    break;
                }
                case XmlNodeType.Text:
                case XmlNodeType.CDATA:
                case XmlNodeType.Whitespace:
                case XmlNodeType.SignificantWhitespace:
                    writer.WriteString(_input.Value);
                    break;
            }
        }
    }
}
