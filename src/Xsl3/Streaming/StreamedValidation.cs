// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System;
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

    private XmlSchemaType? RootType(XmlReader input, out bool laxRoot)
    {
        laxRoot = false;
        if (type != null) return type;
        if (schemas.GlobalElements[new XmlQualifiedName(input.LocalName, input.NamespaceURI)] == null)
        {
            if (strict) throw Error("XTTE1512", $"No element declaration for {input.Name} is in the imported schemas (validation=\"strict\")");
            laxRoot = true;
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
        public override object ReadContentAsObject() => _initialized ? reader.ReadContentAsObject() : base.ReadContentAsObject();
        public override object ReadContentAs(Type returnType, IXmlNamespaceResolver? namespaceResolver) => _initialized
            ? reader.ReadContentAs(returnType, namespaceResolver) : base.ReadContentAs(returnType, namespaceResolver);

        internal bool Initialize()
        {
            XmlSchemaType? rootType = validation.RootType(reader, out bool laxRoot);
            List<RootAttribute>? original = null;
            if (laxRoot && (reader.GetAttribute("type", XmlSchema.InstanceNamespace) != null || reader.GetAttribute("nil", XmlSchema.InstanceNamespace) != null))
            {
                // No declaration means that the root itself is not assessed (§25.4.1.1). Its instance
                // attributes remain visible to consumers, but must not stipulate an element type or nil value.
                original = RootAttributes.Capture(reader);
                reader = new RootAttributes(reader, original.FindAll(attribute => !attribute.InstanceAssessment));
            }
            try { reader = SchemaReaderBridge.Create(reader, settings, rootType); }
            catch (System.NotSupportedException error) { throw validation.Gap(error.Message); }
            _initialized = true;
            // The partial reader starts in Initial state even over a reader already on the root header.
            bool result = reader.Read();
            if (result && original != null) reader = RootAttributes.Restore(reader, original);
            return result;
        }

        public override bool Read()
        {
            bool result = base.Read();
            if (result && !_initialized && NodeType == XmlNodeType.Element) return Initialize();
            return result;
        }

    }

    private sealed record RootAttribute(string LocalName, string Namespace, string Prefix, string Value, bool Default, IXmlSchemaInfo? Schema, int LineNumber, int LinePosition)
    {
        internal string Name => Prefix.Length == 0 ? LocalName : Prefix + ":" + LocalName;
        internal bool InstanceAssessment => Namespace == XmlSchema.InstanceNamespace && LocalName is "type" or "nil";
    }

    /// <summary>A bounded view of just the current root header. The underlying reader stays on its element.</summary>
    private sealed class RootAttributes(XmlReader input, List<RootAttribute> attributes) : XmlReader, IXmlNamespaceResolver, IXmlLineInfo
    {
        private readonly XmlReader reader = input;
        private bool _header = true, _text;
        private int _attribute = -1;
        private RootAttribute? Attribute => _header && _attribute >= 0 ? attributes[_attribute] : null;

        internal static List<RootAttribute> Capture(XmlReader reader)
        {
            var attributes = new List<RootAttribute>();
            if (reader.MoveToFirstAttribute())
            {
                do
                {
                    IXmlSchemaInfo? info = reader.SchemaInfo;
                    var line = reader as IXmlLineInfo;
                    attributes.Add(new(reader.LocalName, reader.NamespaceURI, reader.Prefix, reader.Value, reader.IsDefault,
                        info == null ? null : new XmlSchemaInfo { SchemaType = info.SchemaType, SchemaAttribute = info.SchemaAttribute,
                            SchemaElement = info.SchemaElement, MemberType = info.MemberType, Validity = info.Validity, IsNil = info.IsNil, IsDefault = info.IsDefault },
                        line?.LineNumber ?? 0, line?.LinePosition ?? 0));
                } while (reader.MoveToNextAttribute());
                reader.MoveToElement();
            }
            return attributes;
        }

        internal static XmlReader Restore(XmlReader reader, List<RootAttribute> original)
        {
            List<RootAttribute> validated = Capture(reader);
            var remaining = new Dictionary<(string, string), RootAttribute>();
            foreach (RootAttribute attribute in validated) remaining.Add((attribute.LocalName, attribute.Namespace), attribute);
            var exposed = new List<RootAttribute>(original.Count);
            foreach (RootAttribute attribute in original)
                exposed.Add(remaining.Remove((attribute.LocalName, attribute.Namespace), out var assessed) ? assessed
                    : attribute with { Schema = null });
            exposed.AddRange(remaining.Values);
            return new RootAttributes(reader, exposed);
        }

        public override int AttributeCount => _header ? attributes.Count : reader.AttributeCount;
        public override XmlNodeType NodeType => Attribute != null ? _text ? XmlNodeType.Text : XmlNodeType.Attribute : reader.NodeType;
        public override string LocalName => Attribute is { } attribute ? _text ? "" : attribute.LocalName : reader.LocalName;
        public override string NamespaceURI => Attribute is { } attribute ? _text ? "" : attribute.Namespace : reader.NamespaceURI;
        public override string Prefix => Attribute is { } attribute ? _text ? "" : attribute.Prefix : reader.Prefix;
        public override string Name => Attribute is { } attribute ? _text ? "" : attribute.Name : reader.Name;
        public override string Value => Attribute?.Value ?? reader.Value;
        public override int Depth => reader.Depth + (Attribute == null ? 0 : _text ? 2 : 1);
        public override bool HasValue => Attribute != null || reader.HasValue;
        public override bool IsEmptyElement => Attribute == null && reader.IsEmptyElement;
        public override bool IsDefault => Attribute?.Default ?? reader.IsDefault;
        public override IXmlSchemaInfo? SchemaInfo => Attribute is { } attribute ? attribute.Schema : reader.SchemaInfo;
        public override Type ValueType => Attribute is { } attribute
            ? _text ? typeof(string) : attribute.Schema?.SchemaType?.Datatype?.ValueType ?? typeof(string) : reader.ValueType;
        public override string BaseURI => reader.BaseURI;
        public override XmlReaderSettings? Settings => reader.Settings;
        public override XmlNameTable NameTable => reader.NameTable;
        public override bool EOF => reader.EOF;
        public override ReadState ReadState => reader.ReadState;
        public override XmlSpace XmlSpace => reader.XmlSpace;
        public override string XmlLang => reader.XmlLang;
        public override bool CanResolveEntity => reader.CanResolveEntity;
        public override void ResolveEntity() => reader.ResolveEntity();
        public override string? LookupNamespace(string prefix) => reader.LookupNamespace(prefix);
        public IDictionary<string, string> GetNamespacesInScope(XmlNamespaceScope scope) =>
            reader is IXmlNamespaceResolver resolver ? resolver.GetNamespacesInScope(scope) : new Dictionary<string, string>();
        string? IXmlNamespaceResolver.LookupNamespace(string prefix) => LookupNamespace(prefix);
        public string? LookupPrefix(string namespaceName) => (reader as IXmlNamespaceResolver)?.LookupPrefix(namespaceName);
        public bool HasLineInfo() => (reader as IXmlLineInfo)?.HasLineInfo() == true;
        public int LineNumber => Attribute?.LineNumber ?? (reader as IXmlLineInfo)?.LineNumber ?? 0;
        public int LinePosition => Attribute?.LinePosition ?? (reader as IXmlLineInfo)?.LinePosition ?? 0;
        public override object ReadContentAsObject()
        {
            if (!_header) return reader.ReadContentAsObject();
            if (Attribute is not { } attribute) return base.ReadContentAsObject();
            return attribute.Schema?.SchemaType?.Datatype?.ParseValue(attribute.Value, NameTable, this) ?? attribute.Value;
        }
        public override object ReadContentAs(Type returnType, IXmlNamespaceResolver? namespaceResolver)
        {
            if (!_header) return reader.ReadContentAs(returnType, namespaceResolver);
            if (Attribute?.Schema?.SchemaType?.Datatype is not { } datatype) return base.ReadContentAs(returnType, namespaceResolver);
            return datatype.ChangeType(ReadContentAsObject(), returnType, namespaceResolver ?? this);
        }

        public override string? GetAttribute(string name) => _header ? attributes.Find(attribute => attribute.Name == name)?.Value : reader.GetAttribute(name);
        public override string? GetAttribute(string localName, string? namespaceURI) => _header
            ? attributes.Find(attribute => attribute.LocalName == localName && attribute.Namespace == (namespaceURI ?? ""))?.Value
            : reader.GetAttribute(localName, namespaceURI);
        public override string GetAttribute(int index) => _header ? attributes[index].Value : reader.GetAttribute(index);
        public override bool MoveToAttribute(string name) => !_header ? reader.MoveToAttribute(name) : Select(attributes.FindIndex(attribute => attribute.Name == name));
        public override bool MoveToAttribute(string localName, string? namespaceURI) => !_header ? reader.MoveToAttribute(localName, namespaceURI)
            : Select(attributes.FindIndex(attribute => attribute.LocalName == localName && attribute.Namespace == (namespaceURI ?? "")));
        public override void MoveToAttribute(int index)
        {
            if (!_header) reader.MoveToAttribute(index);
            else
            {
                if ((uint)index >= (uint)attributes.Count) throw new ArgumentOutOfRangeException(nameof(index));
                Select(index);
            }
        }
        private bool Select(int index)
        {
            if (index < 0 || index >= attributes.Count) return false;
            _attribute = index;
            _text = false;
            return true;
        }
        public override bool MoveToFirstAttribute() => _header ? Select(0) : reader.MoveToFirstAttribute();
        public override bool MoveToNextAttribute() => _header ? Select(_attribute + 1) : reader.MoveToNextAttribute();
        public override bool MoveToElement()
        {
            if (!_header) return reader.MoveToElement();
            bool moved = _attribute >= 0;
            _attribute = -1;
            _text = false;
            return moved;
        }
        public override bool ReadAttributeValue()
        {
            if (!_header) return reader.ReadAttributeValue();
            if (_attribute < 0 || _text) return false;
            _text = true;
            return true;
        }
        public override bool Read()
        {
            _header = false;
            return reader.Read();
        }
        public override void Close()
        {
            _header = false;
            reader.Close();
        }
    }
}
