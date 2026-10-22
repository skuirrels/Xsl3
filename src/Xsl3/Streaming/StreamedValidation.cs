// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Xml;
using System.Xml.Schema;
using Xsl3.Schema;
using Xsl3.Xdm;

namespace Xsl3.Streaming;

/// <summary>Schema validation in the input reader chain, with errors scoped to the initiating source instruction.</summary>
internal sealed class StreamedValidation(XmlSchemaSet schemas, bool strict, XmlSchemaType? type = null)
{
    private StreamedDocument? _source;

    internal void Attach(StreamedDocument source)
    {
        _source = source;
    }

    internal XmlReader Wrap(XmlReader input, bool ownsInput)
    {
        if (input.ReadState == ReadState.Interactive && input.NodeType == XmlNodeType.Element)
        {
            // Validate only the supplied subtree; closing its reader leaves the caller's reader open.
            input = input.ReadSubtree();
            ownsInput = true;
        }
        if (!ownsInput) input = new BorrowedReader(input);
        var settings = new XmlReaderSettings
        {
            ValidationType = ValidationType.Schema,
            Schemas = schemas,
            ValidationFlags = XmlSchemaValidationFlags.ProcessIdentityConstraints | XmlSchemaValidationFlags.ReportValidationWarnings
                | XmlSchemaValidationFlags.AllowXmlAttributes,
            DtdProcessing = input.Settings?.DtdProcessing ?? DtdProcessing.Parse,
            XmlResolver = null,
            CloseInput = true,
        };
        settings.ValidationEventHandler += (_, error) =>
        {
            if (error.Severity == XmlSeverityType.Error)
                throw Error(ValidationErrors.Code(type != null ? "XTTE1540" : "XTTE1510", strict, error.Message, ValidationErrors.IsDocumentConstraint(error)), error.Message);
        };
        var reader = new Reader(input, this, settings);
        if (input.ReadState == ReadState.Interactive && input.NodeType == XmlNodeType.Element) reader.Initialize();
        return reader;
    }

    private XdmException Error(string code, string message) => new(code, message) { ValidationSource = _source };

    private XmlSchemaType? RootType(XmlReader input)
    {
        if (type != null) return type;
        if (schemas.GlobalElements[new XmlQualifiedName(input.LocalName, input.NamespaceURI)] == null)
        {
            if (strict) throw Error("XTTE1512", $"No element declaration for {input.Name} is in the imported schemas (validation=\"strict\")");
            if (!string.IsNullOrEmpty(input.GetAttribute("type", XmlSchema.InstanceNamespace)) || !string.IsNullOrEmpty(input.GetAttribute("nil", XmlSchema.InstanceNamespace)))
                throw Gap("Lax assessment of an undeclared streamed root with xsi:type or xsi:nil is not implemented yet");
            return SchemaTypes.AnyType;
        }
        return null;
    }

    private XdmException Gap(string message) => new("streaming-unimplemented", message)
        { Name = new QName("http://xsl3/err", "streaming-unimplemented"), ValidationSource = _source };

    private abstract class NamespaceReader(XmlReader input) : XmlWrappingReader(input), IXmlNamespaceResolver
    {
        public IDictionary<string, string> GetNamespacesInScope(XmlNamespaceScope scope) =>
            reader is IXmlNamespaceResolver resolver ? resolver.GetNamespacesInScope(scope) : new Dictionary<string, string>();
        string? IXmlNamespaceResolver.LookupNamespace(string prefix) => LookupNamespace(prefix);
        public string? LookupPrefix(string namespaceName) => (reader as IXmlNamespaceResolver)?.LookupPrefix(namespaceName);
    }

    private sealed class BorrowedReader(XmlReader input) : NamespaceReader(input)
    {
        public override void Close() { }
    }

    private sealed class Reader(XmlReader input, StreamedValidation validation, XmlReaderSettings settings) : NamespaceReader(input)
    {
        private bool _initialized;
        private readonly XmlSchemaInfo _unknown = new() { SchemaType = SchemaTypes.AnyType, Validity = XmlSchemaValidity.NotKnown };
        public override IXmlSchemaInfo? SchemaInfo => _initialized && NodeType is XmlNodeType.Element or XmlNodeType.EndElement && base.SchemaInfo?.SchemaType == null
            ? _unknown : base.SchemaInfo;

        internal bool Initialize()
        {
            XmlSchemaType? rootType = validation.RootType(reader);
            try { reader = SchemaReaderBridge.Create(reader, settings, rootType); }
            catch (System.NotSupportedException error) { throw validation.Gap(error.Message); }
            _initialized = true;
            // The partial reader starts in Initial state even over a reader already on the root header.
            return reader.Read();
        }

        public override bool Read()
        {
            bool result = base.Read();
            if (result && !_initialized && NodeType == XmlNodeType.Element) return Initialize();
            return result;
        }

    }
}
