// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Xml.XPath;
using System.Xml.Schema;
using Xsl3.Xdm;

namespace Xsl3.Runtime;

public sealed partial class XsltLibrary
{
    /// <summary>Open a forward source-document input. Compiled stylesheets own its execution scope.</summary>
    public XPathNavigator X31OpenStreamedSource(string href, string baseUri, string accumulators, string validation, string type)
    {
        if (XmlQueryRuntime.DocumentFunctions == false)
            throw new XdmException("FODC0002", $"Document '{href}' can't be retrieved (XsltSettings.EnableDocumentFunction is off)");
        // This entry point has no schema context. Validated sources use the schema-bearing entry point below;
        // requested observers still require S5. Do not silently materialize either as a tree.
        if (accumulators.Length != 0 || validation is "strict" or "lax" || type.Length != 0)
            throw new XdmException("streaming-unimplemented", "This streamed source entry point has no schema context or accumulator observers")
                { Name = new QName("http://xsl3/err", "streaming-unimplemented") };
        return _runtime.ExternalContext.OpenStreamedSource(href, baseUri,
            validation == "strip" || validation.Length == 0 && Tree.CompactDocument.StripInputAnnotations);
    }

    /// <summary>Close a forward source at the end of its body.</summary>
    public IList<XPathItem> X31CloseStreamedSource(XPathNavigator source)
    {
        if (source is Streaming.StreamedNavigator streamed)
        {
            streamed.Document.CompleteValidation();
            _runtime.CloseStreamedSource(streamed.Document);
        }
        return Array.Empty<XPathItem>();
    }

    /// <summary>Open a forward source with its imported schemas and requested validation mode.</summary>
    public XPathNavigator X31OpenValidatedStreamedSource(string href, string baseUri, string accumulators, string validation, string type, double schemasId)
    {
        if (XmlQueryRuntime.DocumentFunctions == false)
            throw new XdmException("FODC0002", $"Document '{href}' can't be retrieved (XsltSettings.EnableDocumentFunction is off)");
        if (accumulators.Length != 0)
            throw new XdmException("streaming-unimplemented", "Streamed accumulator observers are not implemented yet")
                { Name = new QName("http://xsl3/err", "streaming-unimplemented") };
        var schemas = (XmlSchemaSet)Constant((int)schemasId);
        XmlSchemaType? schemaType = null;
        if (type.Length != 0)
        {
            int end = type.IndexOf('}');
            schemaType = FindValidationType(schemas, new XmlQualifiedName(type[(end + 1)..], type[2..end]))
                ?? throw new XdmException("XTTE1540", $"No type definition is available for {type}");
        }
        return _runtime.ExternalContext.OpenStreamedSource(href, baseUri, stripAnnotations: false,
            new Streaming.StreamedValidation(schemas, validation == "strict", schemaType));
    }
}
