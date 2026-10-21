// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Xml;
using System.Xml.Schema;
using System.Xml.XPath;
using Xsl3.Tree;
using Xsl3.Xdm;

namespace Xsl3.Schema
{
    /// <summary>
    /// Xsl3 (P6): validation of a constructed node (XSLT 3.0 §25.4): the <c>validation</c> and <c>type</c> attributes of
    /// the instructions that construct or copy nodes. The node is read through System.Xml.Schema's
    /// <see cref="XmlSchemaValidator"/> into a new tree whose elements and attributes carry the type annotations
    /// validation assigns, with the default attributes the schema adds; the new node is the instruction's result.
    /// </summary>
    internal sealed class NodeValidator : IXmlNamespaceResolver
    {
        private readonly XmlSchemaSet _schemas;
        private readonly XmlNameTable _names;
        private readonly bool _strict;
        private readonly bool _byType;
        private readonly XmlSchemaValidator _validator;
        private readonly CompactDocument.Builder _builder;
        private XPathNavigator _scope = null!;   // the element whose namespaces resolve QNames being validated
        private int _depth;
        private string? _error;
        private bool _document;

        private NodeValidator(XmlSchemaSet schemas, XPathNavigator node, bool strict, XmlSchemaObject? partial, bool fragment)
        {
            _schemas = schemas;
            _names = schemas.NameTable;
            _strict = strict;
            _byType = partial != null;
            _validator = new XmlSchemaValidator(_names, schemas, this,
                XmlSchemaValidationFlags.ProcessIdentityConstraints | XmlSchemaValidationFlags.ReportValidationWarnings | XmlSchemaValidationFlags.AllowXmlAttributes);
            _validator.ValidationEventHandler += OnValidation;
            if (partial != null)
            {
                _validator.Initialize(partial);
            }
            else
            {
                _validator.Initialize();
            }
            _builder = new CompactDocument.Builder(node.NameTable, node.BaseURI, fragment, small: true);
        }

        /// <summary>
        /// Validates an element, attribute or document node: against the schemas' declaration for it (strictly or laxly),
        /// or against <paramref name="type"/>. Returns the validated copy; an invalid node raises <paramref name="code"/>
        /// (XTTE1510 for validation="strict"/"lax", XTTE1540 for a type).
        /// </summary>
        public static XPathNavigator Validate(XPathNavigator node, XmlSchemaSet schemas, bool strict, XmlSchemaType? type)
        {
            switch (node.NodeType)
            {
                case XPathNodeType.Attribute:
                    return ValidateAttribute(node, schemas, strict, type);
                case XPathNodeType.Element:
                case XPathNodeType.Root:
                    break;
                default:
                    return node; // (text, comments and processing instructions are untouched)
            }
            if (type == null && node.NodeType == XPathNodeType.Element && node.GetAttribute("type", XmlSchema.InstanceNamespace).Length == 0 &&
                schemas.GlobalElements[new XmlQualifiedName(node.LocalName, node.NamespaceURI)] == null)
            {
                // No declaration for it (XSLT 3.0 §25.4.3): strict is XTTE1512; lax leaves it unvalidated.
                return strict ? throw new XdmException("XTTE1512", $"No element declaration for {node.Name} is in the imported schemas (validation=\"strict\")") : node;
            }
            var v = new NodeValidator(schemas, node, strict, type, fragment: node.NodeType != XPathNodeType.Root);
            string code = type != null ? "XTTE1540" : "XTTE1510";
            if (node.NodeType == XPathNodeType.Root)
            {
                v._document = true;
                v.Document(node, ref code);
            }
            else
            {
                v.Element(node.Clone());
            }
            v._validator.EndValidation();
            if (v._error != null)
            {
                throw new XdmException(code, v._error);
            }
            return Constructed(v._builder.Finish(), node);
        }

        // A document node: exactly one element child, and no text (XSLT 3.0 §25.4.3); its ID/IDREF constraints hold.
        private void Document(XPathNavigator document, ref string code)
        {
            XPathNavigator child = document.Clone();
            int elements = 0;
            if (child.MoveToFirstChild())
            {
                do
                {
                    switch (child.NodeType)
                    {
                        case XPathNodeType.Element:
                            elements++;
                            break;
                        case XPathNodeType.Text:
                            throw new XdmException("XTTE1550", "A document node being validated has a text node child");
                    }
                }
                while (child.MoveToNext());
            }
            if (elements != 1)
            {
                throw new XdmException("XTTE1550", $"A document node being validated has {elements} element children, not one");
            }
            child = document.Clone();
            child.MoveToFirstChild();
            do
            {
                switch (child.NodeType)
                {
                    case XPathNodeType.Element:
                        Element(child.Clone());
                        break;
                    case XPathNodeType.Comment:
                        _builder.Comment(child.Value);
                        break;
                    case XPathNodeType.ProcessingInstruction:
                        _builder.ProcessingInstruction(child.LocalName, child.Value);
                        break;
                }
            }
            while (child.MoveToNext());
            if (_error == null)
            {
                code = "XTTE1555"; // (the ID/IDREF checks, at the end)
            }
        }

        private void Element(XPathNavigator element)
        {
            XPathNavigator parentScope = _scope;
            _scope = element;
            _builder.StartElement(element.Prefix, element.LocalName, element.NamespaceURI, element.BaseURI);
            if (element.MoveToFirstNamespace(XPathNamespaceScope.Local))
            {
                do
                {
                    _builder.NamespaceDeclaration(element.LocalName, element.Value);
                }
                while (element.MoveToNextNamespace(XPathNamespaceScope.Local));
                element.MoveToParent();
            }
            string? xsiType = element.GetAttribute("type", XmlSchema.InstanceNamespace) is { Length: > 0 } t ? t : null;
            string? xsiNil = element.GetAttribute("nil", XmlSchema.InstanceNamespace) is { Length: > 0 } n ? n : null;
            var info = new XmlSchemaInfo();
            _validator.ValidateElement(_names.Add(element.LocalName), _names.Add(element.NamespaceURI), info, xsiType, xsiNil, null, null);
            if (_depth == 0 && _strict && !_byType && info.SchemaElement == null && info.SchemaType == null && _error == null)
            {
                _error = $"No element declaration for {element.Name} is in the imported schemas (validation=\"strict\")";
            }
            if (element.MoveToFirstAttribute())
            {
                do
                {
                    var attributeInfo = new XmlSchemaInfo();
                    if (element.NamespaceURI != XmlSchema.InstanceNamespace)
                    {
                        _validator.ValidateAttribute(_names.Add(element.LocalName), _names.Add(element.NamespaceURI), element.Value, attributeInfo);
                    }
                    _builder.Attribute(element.Prefix, element.LocalName, element.NamespaceURI, element.Value);
                    if (attributeInfo.SchemaType != null)
                    {
                        _builder.AnnotateAttribute(attributeInfo.SchemaType, element.Value);
                    }
                }
                while (element.MoveToNextAttribute());
                element.MoveToParent();
            }
            var defaults = new ArrayList();
            _validator.GetUnspecifiedDefaultAttributes(defaults);
            foreach (XmlSchemaAttribute d in defaults)
            {
                // The schema's default attributes are added (XSLT 3.0 §25.4: the PSVI's values).
                string ns = d.QualifiedName.Namespace;
                string prefix = ns.Length == 0 ? "" : element.LookupPrefix(ns) ?? "d" + defaults.IndexOf(d);
                if (prefix.Length > 0 && element.LookupPrefix(ns) == null)
                {
                    _builder.NamespaceDeclaration(prefix, ns);
                }
                string value = d.DefaultValue ?? d.FixedValue ?? "";
                _builder.Attribute(prefix, d.QualifiedName.Name, ns, value);
                if (d.AttributeSchemaType != null)
                {
                    _builder.AnnotateAttribute(d.AttributeSchemaType, value);
                }
            }
            _validator.ValidateEndOfAttributes(info);
            bool empty = true;
            _depth++;
            if (element.MoveToFirstChild())
            {
                do
                {
                    empty = false;
                    switch (element.NodeType)
                    {
                        case XPathNodeType.Element:
                            Element(element.Clone());
                            _scope = element;
                            break;
                        case XPathNodeType.Text:
                            _validator.ValidateText(element.Value);
                            _builder.Text(element.Value, XPathNodeType.Text);
                            break;
                        case XPathNodeType.Whitespace:
                        case XPathNodeType.SignificantWhitespace:
                            _validator.ValidateWhitespace(element.Value);
                            _builder.Text(element.Value, element.NodeType);
                            break;
                        case XPathNodeType.Comment:
                            _builder.Comment(element.Value);
                            break;
                        case XPathNodeType.ProcessingInstruction:
                            _builder.ProcessingInstruction(element.LocalName, element.Value);
                            break;
                    }
                }
                while (element.MoveToNext());
                element.MoveToParent();
            }
            _depth--;
            object? typed = _validator.ValidateEndElement(info);
            _scope = element;
            XmlSchemaType? type = info.SchemaType;
            if (type == null && _depth == 0 && _strict && _error == null)
            {
                _error = $"{element.Name} could not be validated";
            }
            if (empty && info.SchemaElement is { } declaration && (declaration.DefaultValue ?? declaration.FixedValue) is { } defaultValue && !info.IsNil)
            {
                // An empty element with a default value has it as its content.
                _builder.Text(defaultValue, XPathNodeType.Text);
                empty = false;
            }
            _ = typed;
            AnnotateOpenElement(type, info.IsNil);
            _builder.EndElement(emptyTag: empty);
            _scope = parentScope;
        }

        // The element just validated (still open in the builder): its annotation.
        private void AnnotateOpenElement(XmlSchemaType? type, bool nilled) => _builder.AnnotateElement(type, nilled);

        private static XPathNavigator ValidateAttribute(XPathNavigator attribute, XmlSchemaSet schemas, bool strict, XmlSchemaType? type)
        {
            XmlSchemaSimpleType? simple;
            if (type != null)
            {
                simple = type as XmlSchemaSimpleType ?? throw new XdmException("XTTE1540", $"An attribute can't have the complex type {SchemaTypes.NameOf(type)}");
            }
            else
            {
                var declaration = schemas.GlobalAttributes[new XmlQualifiedName(attribute.LocalName, attribute.NamespaceURI)] as XmlSchemaAttribute;
                if (declaration == null)
                {
                    if (strict)
                    {
                        throw new XdmException("XTTE1512", $"No attribute declaration for {attribute.Name} is in the imported schemas (validation=\"strict\")");
                    }
                    return attribute; // (lax: no declaration, no validation)
                }
                simple = declaration.AttributeSchemaType;
            }
            if (simple?.Datatype != null)
            {
                try
                {
                    simple.Datatype.ParseValue(attribute.Value, schemas.NameTable, attribute);
                }
                catch (Exception e) when (e is XmlSchemaException or FormatException or OverflowException)
                {
                    throw new XdmException(type != null ? "XTTE1540" : "XTTE1510", $"The attribute {attribute.Name}=\"{attribute.Value}\" is not valid: {e.Message}");
                }
            }
            var builder = new CompactDocument.Builder(attribute.NameTable, attribute.BaseURI, fragment: true, small: true);
            builder.Attribute(attribute.Prefix, attribute.LocalName, attribute.NamespaceURI, attribute.Value);
            if (simple != null)
            {
                builder.AnnotateAttribute(simple, attribute.Value);
            }
            return Constructed(builder.Finish(), attribute);
        }

        /// <summary>
        /// A copy of a node that keeps its type annotations (validation="preserve", XSLT 3.0 §25.4.2). With
        /// <paramref name="copyNamespaces"/> false, an element or attribute whose typed value holds a QName or NOTATION
        /// (namespace-sensitive content) can't be copied (XTTE0950): its namespaces wouldn't come with it.
        /// </summary>
        public static XPathNavigator Preserve(XPathNavigator node, bool copyNamespaces, bool keepTypes = true)
        {
            switch (node.NodeType)
            {
                case XPathNodeType.Attribute:
                {
                    XmlSchemaType? type = keepTypes ? SchemaTypes.TypeOf(node) : null;
                    if (type != null && SchemaTypes.IsNamespaceSensitive(type))
                    {
                        throw new XdmException("XTTE0950", $"The attribute {node.Name} has namespace-sensitive content, so it can't be copied with validation=\"preserve\" without its element");
                    }
                    var builder = new CompactDocument.Builder(node.NameTable, node.BaseURI, fragment: true, small: true);
                    builder.Attribute(node.Prefix, node.LocalName, node.NamespaceURI, node.Value);
                    if (type != null)
                    {
                        builder.AnnotateAttribute(type, node.Value);
                    }
                    return Constructed(builder.Finish(), node);
                }
                case XPathNodeType.Element:
                case XPathNodeType.Root:
                {
                    var builder = new CompactDocument.Builder(node.NameTable, node.BaseURI, fragment: node.NodeType == XPathNodeType.Element, small: true);
                    XPathNavigator copy = node.Clone();
                    if (node.NodeType == XPathNodeType.Element)
                    {
                        CopyElement(builder, copy, copyNamespaces, top: true, keepTypes);
                    }
                    else if (copy.MoveToFirstChild())
                    {
                        do
                        {
                            CopyChild(builder, copy, copyNamespaces, keepTypes);
                        }
                        while (copy.MoveToNext());
                    }
                    return Constructed(builder.Finish(), node);
                }
                default:
                {
                    // Text, comments, processing instructions and namespace nodes: a parentless copy.
                    var builder = new CompactDocument.Builder(node.NameTable, node.BaseURI, fragment: true, small: true);
                    if (node.NodeType == XPathNodeType.Namespace)
                    {
                        builder.NamespaceDeclaration(node.LocalName, node.Value);
                    }
                    else
                    {
                        CopyChild(builder, node, copyNamespaces, keepTypes);
                    }
                    return Constructed(builder.Finish(), node);
                }
            }
        }

        private static void CopyChild(CompactDocument.Builder builder, XPathNavigator node, bool copyNamespaces, bool keepTypes)
        {
            switch (node.NodeType)
            {
                case XPathNodeType.Element:
                    CopyElement(builder, node.Clone(), copyNamespaces, top: false, keepTypes);
                    break;
                case XPathNodeType.Text:
                case XPathNodeType.Whitespace:
                case XPathNodeType.SignificantWhitespace:
                    builder.Text(node.Value, node.NodeType);
                    break;
                case XPathNodeType.Comment:
                    builder.Comment(node.Value);
                    break;
                case XPathNodeType.ProcessingInstruction:
                    builder.ProcessingInstruction(node.LocalName, node.Value);
                    break;
            }
        }

        private static void CopyElement(CompactDocument.Builder builder, XPathNavigator element, bool copyNamespaces, bool top, bool keepTypes)
        {
            XmlSchemaType? type = keepTypes ? SchemaTypes.TypeOf(element) : null;
            builder.StartElement(element.Prefix, element.LocalName, element.NamespaceURI, element.BaseURI);
            if (copyNamespaces)
            {
                // The top element takes every namespace in scope; the others, those they declare.
                if (element.MoveToFirstNamespace(top ? XPathNamespaceScope.ExcludeXml : XPathNamespaceScope.Local))
                {
                    do
                    {
                        builder.NamespaceDeclaration(element.LocalName, element.Value);
                    }
                    while (element.MoveToNextNamespace(top ? XPathNamespaceScope.ExcludeXml : XPathNamespaceScope.Local));
                    element.MoveToParent();
                }
            }
            else
            {
                if (type != null && SchemaTypes.IsNamespaceSensitive(type))
                {
                    throw new XdmException("XTTE0950", $"The element {element.Name} has namespace-sensitive content, so it can't be copied with copy-namespaces=\"no\" and validation=\"preserve\"");
                }
                // Only the namespaces the names need.
                if (element.NamespaceURI.Length > 0)
                {
                    builder.NamespaceDeclaration(element.Prefix, element.NamespaceURI);
                }
                if (element.MoveToFirstAttribute())
                {
                    do
                    {
                        if (element.Prefix.Length > 0 && element.Prefix != "xml")
                        {
                            builder.NamespaceDeclaration(element.Prefix, element.NamespaceURI);
                        }
                    }
                    while (element.MoveToNextAttribute());
                    element.MoveToParent();
                }
            }
            if (type != null)
            {
                builder.AnnotateElement(type, SchemaTypes.IsNilled(element));
            }
            if (element.MoveToFirstAttribute())
            {
                do
                {
                    builder.Attribute(element.Prefix, element.LocalName, element.NamespaceURI, element.Value);
                    if (keepTypes && SchemaTypes.TypeOf(element) is { } attributeType)
                    {
                        if (!copyNamespaces && SchemaTypes.IsNamespaceSensitive(attributeType))
                        {
                            throw new XdmException("XTTE0950", $"The attribute {element.Name} has namespace-sensitive content, so it can't be copied with copy-namespaces=\"no\" and validation=\"preserve\"");
                        }
                        builder.AnnotateAttribute(attributeType, element.Value);
                    }
                }
                while (element.MoveToNextAttribute());
                element.MoveToParent();
            }
            bool empty = true;
            if (element.MoveToFirstChild())
            {
                do
                {
                    empty = false;
                    CopyChild(builder, element, copyNamespaces, keepTypes);
                }
                while (element.MoveToNext());
                element.MoveToParent();
            }
            builder.EndElement(emptyTag: empty);
        }

        // A tree the transformation built: its annotations stay when input annotations are stripped.
        private static XPathNavigator Constructed(CompactDocument tree, XPathNavigator original)
        {
            tree._constructed = true;
            // (A copy of a constructed document has no document URI either.)
            tree.NoDocumentUri = original is CompactNavigator { Document.NoDocumentUri: true } || original is not CompactNavigator;
            return tree.CreateNavigator();
        }

        private void OnValidation(object? sender, ValidationEventArgs e)
        {
            if (e.Severity == XmlSeverityType.Warning)
            {
                return; // (an undeclared element or attribute: validated laxly)
            }
            if (!_document && (e.Message.Contains("already used as an ID", StringComparison.Ordinal) || e.Message.Contains("undeclared ID", StringComparison.Ordinal)))
            {
                return; // ID and IDREF constraints are checked only for a document node (XSLT 3.0 §25.4.3)
            }
            _error ??= e.Message;
        }

        IDictionary<string, string> IXmlNamespaceResolver.GetNamespacesInScope(XmlNamespaceScope scope) => _scope.GetNamespacesInScope(scope);

        string? IXmlNamespaceResolver.LookupNamespace(string prefix) => _scope.LookupNamespace(prefix);

        string? IXmlNamespaceResolver.LookupPrefix(string namespaceName) => _scope.LookupPrefix(namespaceName);
    }
}
