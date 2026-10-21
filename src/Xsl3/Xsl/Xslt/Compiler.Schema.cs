// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Xml;
using System.Xml.Schema;

namespace Xsl3.Xslt
{
    internal sealed partial class Compiler
    {
        // Xsl3 (P6, docs/plan/p6-schema.md): the schemas xsl:import-schema imports, compiled together when first used.
        // Null when the stylesheet imports none, so nothing schema-related runs for it.
        private XmlSchemaSet? _schemas;

        /// <summary>
        /// The default-validation attribute of the principal stylesheet module, "preserve" or "strip" (XSLT 3.0 §25.4);
        /// null when absent (strip). (Per module in the specification; one value for the stylesheet here.)
        /// </summary>
        public string? DefaultValidation;
        private bool _schemasCompiled;

        /// <summary>
        /// The imported schemas, compiled (XSLT 3.0 §3.14.2); null when the stylesheet imports none. An error in them is
        /// XTSE0220.
        /// </summary>
        public XmlSchemaSet? Schemas
        {
            get
            {
                if (_schemas != null && !_schemasCompiled)
                {
                    _schemasCompiled = true;
                    try
                    {
                        _schemas.Compile();
                    }
                    catch (XmlSchemaException e)
                    {
                        ReportError(SourceLineInfo.NoSource, SR.Xml_UserException, "XTSE0220: the imported schemas are not valid: " + e.Message);
                    }
                }
                return _schemas;
            }
        }

        /// <summary>
        /// xsl:import-schema: the schema at <paramref name="location"/>, or the one given inline, or else the one the
        /// processor knows for <paramref name="namespaceUri"/> (<see cref="XsltSettings.Schemas"/>). Errors are XTSE0220.
        /// </summary>
        public void ImportSchema(string? namespaceUri, Uri? location, XmlSchema? inline, XmlResolver? resolver, ISourceLineInfo lineInfo)
        {
            if (namespaceUri == XmlSchema.Namespace && location == null && inline == null)
            {
                return; // (the built-in types are always known)
            }
            _schemas ??= Xsl3.Schema.SchemaDocuments.CreateSet(resolver);
            _schemasCompiled = false;
            try
            {
                if (inline != null)
                {
                    if (namespaceUri != null && (inline.TargetNamespace ?? "") != namespaceUri)
                    {
                        ReportError(lineInfo, SR.Xml_UserException, $"XTSE0220: the inline schema's target namespace is not '{namespaceUri}'");
                        return;
                    }
                    _schemas.Add(inline);
                }
                else if (location != null)
                {
                    // (Already imported, from this or another module: once is enough.)
                    foreach (XmlSchema schema in _schemas.Schemas())
                    {
                        if (schema.SourceUri == location.AbsoluteUri)
                        {
                            return;
                        }
                    }
                    XmlSchema? added = _schemas.Add(namespaceUri, location.AbsoluteUri);
                    if (added == null && namespaceUri != null)
                    {
                        ReportError(lineInfo, SR.Xml_UserException, $"XTSE0220: the schema at {location} is not for namespace '{namespaceUri}'");
                    }
                }
                else
                {
                    string ns = namespaceUri ?? "";
                    if (_schemas.Contains(ns))
                    {
                        return;
                    }
                    bool found = false;
                    foreach (XmlSchema schema in Xsl3.Schema.SchemaTypes.BuiltInSchemas(ns))
                    {
                        _schemas.Add(schema);
                        found = true;
                    }
                    if (!found && Settings.Schemas is { } known)
                    {
                        foreach (XmlSchema schema in known.Schemas(ns))
                        {
                            _schemas.Add(schema);
                            found = true;
                        }
                    }
                    if (!found)
                    {
                        ReportError(lineInfo, SR.Xml_UserException, $"XTSE0220: no schema is known for namespace '{ns}', and xsl:import-schema gives no schema-location");
                    }
                }
            }
            catch (Exception e) when (e is XmlSchemaException or XmlException or System.IO.IOException or UriFormatException or ArgumentException)
            {
                ReportError(lineInfo, SR.Xml_UserException, "XTSE0220: the schema can't be imported: " + e.Message);
            }
        }
    }
}
