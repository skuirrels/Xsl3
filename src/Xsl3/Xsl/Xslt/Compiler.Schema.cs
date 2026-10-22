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
                    if (_schemas.IsCompiled)
                    {
                        // A stylesheet function can't have the name and arity of a constructor function (XTSE0770).
                        foreach (var ((name, arity), (function, _)) in Functions)
                        {
                            if (arity == 1 && _schemas.GlobalTypes[new XmlQualifiedName(name.LocalName, name.NamespaceUri)] is XmlSchemaSimpleType)
                            {
                                ReportError(function.SourceLine ?? SourceLineInfo.NoSource, SR.Xml_UserException,
                                    $"XTSE0770: the function {name.QualifiedName}#1 has the name of the constructor function of a schema type");
                            }
                        }
                    }
                }
                return _schemas;
            }
        }

        // The schemas the processor knows for a namespace (built in, or XsltSettings.Schemas), imported; false if none.
        private bool ImportKnown(string ns)
        {
            if (_schemas!.Contains(ns))
            {
                return true;
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
            return found;
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
                        ReportError(lineInfo, SR.Xml_UserException, $"XTSE0215: the inline schema's target namespace is not '{namespaceUri}'"); // (XSLT 3.0 §3.14.2)
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
                    // The document's target namespace, and the namespaces it imports (read without compiling it).
                    string target = "";
                    var imports = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
                    try
                    {
                        using XmlReader reader = XmlReader.Create(location.AbsoluteUri, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = resolver ?? new XmlUrlResolver() });
                        reader.MoveToContent();
                        target = reader.GetAttribute("targetNamespace") ?? "";
                        while (reader.Read())
                        {
                            if (reader.NodeType == XmlNodeType.Element && reader.Depth == 1 && reader.LocalName == "import" && reader.NamespaceURI == XmlSchema.Namespace)
                            {
                                imports.Add(reader.GetAttribute("namespace") ?? "");
                            }
                        }
                    }
                    catch (Exception e) when (e is System.IO.IOException or System.Net.WebException or UnauthorizedAccessException)
                    {
                        // (schema-location is a hint: a schema that can't be found leaves the namespace to the known schemas.)
                        ImportKnown(namespaceUri ?? "");
                        return;
                    }
                    if (namespaceUri == null && target.Length > 0)
                    {
                        // Without a namespace attribute, the schema is for no namespace (XSLT 3.0 §3.14.2).
                        ReportError(lineInfo, SR.Xml_UserException, $"XTSE0220: the schema at {location} is for namespace '{target}', but xsl:import-schema has no namespace attribute");
                        return;
                    }
                    if (namespaceUri != null && target != namespaceUri && !imports.Contains(namespaceUri))
                    {
                        // A hint to another namespace's schema: the namespace's known schema, if there is one.
                        if (!ImportKnown(namespaceUri))
                        {
                            ReportError(lineInfo, SR.Xml_UserException, $"XTSE0220: the schema at {location} has no components for namespace '{namespaceUri}'");
                        }
                        return;
                    }
                    var earlier = new System.Collections.Generic.List<XmlSchema>();
                    foreach (XmlSchema schema in _schemas.Schemas(target))
                    {
                        earlier.Add(schema);
                    }
                    XmlSchema? added = _schemas.Add(target, location.AbsoluteUri);
                    if (earlier.Count > 0 && added != null)
                    {
                        // Two schemas for one namespace: where their components clash, the one imported first is kept.
                        // Its import precedence is the higher: a module is loaded before the modules it imports, and
                        // those last to first (import-schema-177).
                        try
                        {
                            _schemas.Compile();
                        }
                        catch (XmlSchemaException)
                        {
                            _schemas.Remove(added);
                        }
                    }
                }
                else
                {
                    // (A namespace no schema is known for imports nothing: its names are then unknown, XPST0051 and the like.)
                    ImportKnown(namespaceUri ?? "");
                }
            }
            catch (Exception e) when (e is XmlSchemaException or XmlException or System.IO.IOException or UriFormatException or ArgumentException)
            {
                ReportError(lineInfo, SR.Xml_UserException, "XTSE0220: the schema can't be imported: " + e.Message);
            }
        }
    }
}
