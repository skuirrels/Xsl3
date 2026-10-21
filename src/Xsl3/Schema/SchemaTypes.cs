// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Xml;
using System.Xml.Schema;
using System.Xml.XPath;
using Xsl3.Tree;
using Xsl3.Xdm;

namespace Xsl3.Schema
{
    /// <summary>
    /// Xsl3 (P6, docs/plan/p6-schema.md): the bridge between System.Xml.Schema's compiled types and the XDM. A schema's
    /// simple types become <see cref="AtomicType"/>s (one per schema type), and a node's type annotation gives its typed
    /// value. Nothing here runs for a stylesheet that imports no schema over input that isn't validated.
    /// </summary>
    internal static class SchemaTypes
    {
        private static readonly ConditionalWeakTable<XmlSchemaSimpleType, AtomicType> s_atomic = new();
        private static readonly NameTable s_names = new();
        private static int s_anonymous;

        /// <summary>
        /// The schemas the processor itself supplies for a namespace (the XML namespace's attributes; the fn namespace's
        /// results of fn:analyze-string and fn:json-to-xml), read afresh for each import.
        /// </summary>
        public static System.Collections.Generic.IEnumerable<XmlSchema> BuiltInSchemas(string namespaceUri)
        {
            string[] names = namespaceUri switch
            {
                "http://www.w3.org/XML/1998/namespace" => new[] { "xml" },
                "http://www.w3.org/2005/xpath-functions" => new[] { "analyze-string", "schema-for-json" },
                _ => Array.Empty<string>(),
            };
            foreach (string name in names)
            {
                using System.IO.Stream stream = typeof(SchemaTypes).Assembly.GetManifestResourceStream("Xsl3.Schema." + name + ".xsd")!;
                yield return XmlSchema.Read(stream, null)!;
            }
        }

        /// <summary>An empty, compiled schema set: the built-in types only (a schema-aware stylesheet that imports none).</summary>
        public static XmlSchemaSet NoSchemas => s_noSchemas.Value;

        private static readonly Lazy<XmlSchemaSet> s_noSchemas = new(() =>
        {
            var set = new XmlSchemaSet();
            set.Compile();
            return set;
        });

        /// <summary>The fn namespace's built-in schemas, compiled once (json-to-xml's validate option).</summary>
        public static XmlSchemaSet FunctionsSchemas => s_functionsSchemas.Value;

        private static readonly Lazy<XmlSchemaSet> s_functionsSchemas = new(() =>
        {
            var set = new XmlSchemaSet();
            foreach (XmlSchema schema in BuiltInSchemas("http://www.w3.org/2005/xpath-functions"))
            {
                set.Add(schema);
            }
            set.Compile();
            return set;
        });

        /// <summary>The namespace of the names given to anonymous types.</summary>
        public const string AnonymousNamespace = "http://xsl3/anonymous-type";

        /// <summary>xs:anyType.</summary>
        public static readonly XmlSchemaComplexType AnyType = XmlSchemaType.GetBuiltInComplexType(new XmlQualifiedName("anyType", XmlSchema.Namespace))!;

        public static bool IsBuiltIn(XmlSchemaType type) => type.QualifiedName.Namespace == XmlSchema.Namespace;

        /// <summary>The atomic type of a simple type of atomic variety, built-in or a schema's; null for any other type.</summary>
        public static AtomicType? AtomicTypeOf(XmlSchemaType type)
        {
            if (type is not XmlSchemaSimpleType simple || simple.Datatype?.Variety != XmlSchemaDatatypeVariety.Atomic)
            {
                return null;
            }
            if (IsBuiltIn(simple))
            {
                return AtomicTypes.Find(QName.Ns.Xs, simple.QualifiedName.Name);
            }
            return s_atomic.GetValue(simple, Create);
        }

        private static AtomicType Create(XmlSchemaSimpleType type)
        {
            AtomicType baseType = (type.BaseXmlSchemaType is XmlSchemaSimpleType b ? AtomicTypeOf(b) : null) ??
                AtomicTypes.Find(QName.Ns.Xs, XmlSchemaType.GetBuiltInSimpleType(type.TypeCode)?.QualifiedName.Name ?? "") ?? AtomicTypes.String;
            WhitespaceFacet? whitespace = null;
            if (type.Content is XmlSchemaSimpleTypeRestriction restriction)
            {
                foreach (XmlSchemaObject facet in restriction.Facets)
                {
                    if (facet is XmlSchemaWhiteSpaceFacet ws)
                    {
                        whitespace = ws.Value switch { "collapse" => WhitespaceFacet.Collapse, "replace" => WhitespaceFacet.Replace, _ => WhitespaceFacet.Preserve };
                    }
                }
            }
            return new AtomicType(NameOf(type), baseType, type, whitespace);
        }

        /// <summary>A type's name; an anonymous type gets one of its own, in <see cref="AnonymousNamespace"/>.</summary>
        public static QName NameOf(XmlSchemaType type) => type.QualifiedName.IsEmpty
            ? new QName(AnonymousNamespace, "t" + Interlocked.Increment(ref s_anonymous).ToString(System.Globalization.CultureInfo.InvariantCulture))
            : new QName(type.QualifiedName.Namespace, type.QualifiedName.Name);

        /// <summary>Whether <paramref name="lexical"/> satisfies the facets of a schema's atomic type.</summary>
        public static bool SatisfiesFacets(AtomicType type, string lexical, IXmlNamespaceResolver? namespaces)
        {
            XmlSchemaDatatype? datatype = type.UserSchemaType?.Datatype;
            if (datatype == null)
            {
                return true;
            }
            try
            {
                datatype.ParseValue(lexical, s_names, namespaces ?? s_noNamespaces);
                return true;
            }
            catch (Exception e) when (e is XmlSchemaException or FormatException or OverflowException)
            {
                return false;
            }
        }

        private static readonly IXmlNamespaceResolver s_noNamespaces = new XmlNamespaceManager(new NameTable());

        /// <summary>A node's type annotation, or null when it is untyped (xs:untyped, xs:untypedAtomic).</summary>
        public static XmlSchemaType? TypeOf(XPathNavigator node)
        {
            if (node is CompactNavigator compact)
            {
                return compact.SchemaType;
            }
            IXmlSchemaInfo? info = node.SchemaInfo;
            return info is { Validity: XmlSchemaValidity.Valid } ? info.SchemaType : null;
        }

        /// <summary>Whether a node is nilled (XDM 3.1 §5.8).</summary>
        public static bool IsNilled(XPathNavigator node) =>
            node is CompactNavigator compact ? compact.IsNilled : node.SchemaInfo?.IsNil == true;

        /// <summary>
        /// The typed value of a node whose annotation is <paramref name="type"/> (XDM 3.1 §3.3.1.2): the atomic values
        /// of its simple type or simple content, its string value as xs:untypedAtomic for mixed content, nothing for
        /// empty content or a nilled element, and FOTY0012 for element-only content.
        /// </summary>
        public static IList<XPathItem> TypedValue(XPathNavigator node, XmlSchemaType type)
        {
            if (type is XmlSchemaComplexType complex)
            {
                if (IsNilled(node))
                {
                    return Sequences.Empty;
                }
                switch (complex.ContentType)
                {
                    case XmlSchemaContentType.Empty:
                        return Sequences.Empty;
                    case XmlSchemaContentType.ElementOnly:
                        throw new XdmException("FOTY0012", $"Element {node.Name} has element-only content (type {NameOf(type)}), so it has no typed value");
                    case XmlSchemaContentType.TextOnly when SimpleContent(complex) is { } content:
                        return Simple(content, node.Value, node);
                    default:
                        return Sequences.One(AtomicValue.OfUntyped(node.Value));
                }
            }
            return Simple((XmlSchemaSimpleType)type, node.Value, node);
        }

        // The simple type of a complex type's simple content.
        private static XmlSchemaSimpleType? SimpleContent(XmlSchemaComplexType type)
        {
            for (XmlSchemaType? t = type; t != null; t = t.BaseXmlSchemaType)
            {
                if (t is XmlSchemaSimpleType simple)
                {
                    return simple;
                }
            }
            return null;
        }

        /// <summary>The atomic values of <paramref name="value"/> as an instance of a simple type (list and union types too).</summary>
        public static IList<XPathItem> Simple(XmlSchemaSimpleType type, string value, IXmlNamespaceResolver? namespaces)
        {
            switch (type.Datatype?.Variety)
            {
                case XmlSchemaDatatypeVariety.List:
                {
                    XmlSchemaSimpleType item = ListItemType(type);
                    string[] tokens = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    var items = new List<XPathItem>(tokens.Length);
                    foreach (string token in tokens)
                    {
                        items.AddRange(Simple(item, token, namespaces));
                    }
                    return items;
                }
                case XmlSchemaDatatypeVariety.Union:
                    foreach (XmlSchemaSimpleType member in UnionMemberTypes(type))
                    {
                        try
                        {
                            return Simple(member, value, namespaces);
                        }
                        catch (XdmException)
                        {
                        }
                    }
                    throw new XdmException("FORG0001", $"'{value}' is not valid for the union type {NameOf(type)}");
                default:
                    if (AtomicTypeOf(type) is not { } atomic || atomic == AtomicTypes.AnyAtomicType)
                    {
                        return Sequences.One(AtomicValue.OfUntyped(value)); // xs:anySimpleType
                    }
                    return Sequences.One(Casting.FromString(value, atomic, namespaces));
            }
        }

        /// <summary>The item type of a list type (or of a restriction of one).</summary>
        public static XmlSchemaSimpleType ListItemType(XmlSchemaSimpleType type)
        {
            for (XmlSchemaSimpleType? t = type; t != null; t = t.BaseXmlSchemaType as XmlSchemaSimpleType)
            {
                if (t.Content is XmlSchemaSimpleTypeList list)
                {
                    return list.BaseItemType ?? (XmlSchemaSimpleType)XmlSchemaType.GetBuiltInSimpleType(XmlTypeCode.AnyAtomicType)!;
                }
            }
            // The built-in list types (xs:NMTOKENS, xs:IDREFS, xs:ENTITIES).
            return XmlSchemaType.GetBuiltInSimpleType(type.TypeCode switch
            {
                XmlTypeCode.Idref => XmlTypeCode.Idref,
                XmlTypeCode.Entity => XmlTypeCode.Entity,
                _ => XmlTypeCode.NmToken,
            })!;
        }

        /// <summary>Whether a list type allows no items (it has a length or minLength facet of 0).</summary>
        public static bool AllowsEmptyList(XmlSchemaSimpleType type)
        {
            for (XmlSchemaSimpleType? t = type; t != null; t = t.BaseXmlSchemaType as XmlSchemaSimpleType)
            {
                if (t.Content is XmlSchemaSimpleTypeRestriction restriction)
                {
                    foreach (XmlSchemaObject facet in restriction.Facets)
                    {
                        if (facet is XmlSchemaLengthFacet or XmlSchemaMinLengthFacet && ((XmlSchemaFacet)facet).Value?.Trim() == "0")
                        {
                            return true;
                        }
                        if (facet is XmlSchemaLengthFacet or XmlSchemaMinLengthFacet)
                        {
                            return false;
                        }
                    }
                }
            }
            return true;
        }

        /// <summary>The member types of a union type (or of a restriction of one), in order.</summary>
        public static IEnumerable<XmlSchemaSimpleType> UnionMemberTypes(XmlSchemaSimpleType type)
        {
            for (XmlSchemaSimpleType? t = type; t != null; t = t.BaseXmlSchemaType as XmlSchemaSimpleType)
            {
                if (t.Content is XmlSchemaSimpleTypeUnion union)
                {
                    return union.BaseMemberTypes ?? Array.Empty<XmlSchemaSimpleType>();
                }
            }
            return Array.Empty<XmlSchemaSimpleType>();
        }

        /// <summary>
        /// How a type normalizes the whitespace of its values' lexical forms (the whiteSpace facet), for the schema
        /// normalized value of a typed node; null when it keeps it (and for complex content).
        /// </summary>
        public static WhitespaceFacet? Normalization(XmlSchemaType type)
        {
            XmlSchemaSimpleType? simple = type as XmlSchemaSimpleType ??
                (type is XmlSchemaComplexType { ContentType: XmlSchemaContentType.TextOnly } complex ? SimpleContent(complex) : null);
            if (simple == null)
            {
                return null;
            }
            if (simple.Datatype?.Variety == XmlSchemaDatatypeVariety.List)
            {
                return WhitespaceFacet.Collapse;
            }
            if (simple.Datatype?.Variety == XmlSchemaDatatypeVariety.Union)
            {
                return null; // (each member normalizes its own)
            }
            return AtomicTypeOf(simple)?.Whitespace is WhitespaceFacet facet and not WhitespaceFacet.Preserve ? facet : null;
        }

        /// <summary>Whether a type is xs:ID or derived from it (an element of simple content counts its content type).</summary>
        public static bool IsId(XmlSchemaType type) =>
            (type as XmlSchemaSimpleType ?? (type is XmlSchemaComplexType { ContentType: XmlSchemaContentType.TextOnly } complex ? SimpleContent(complex) : null))
                is { } simple && AtomicTypeOf(simple)?.DerivesFrom(AtomicTypes.ID) == true;

        /// <summary>Whether a type's values are IDREFs (xs:IDREF, xs:IDREFS, or a type derived from or a list of them).</summary>
        public static bool IsIdref(XmlSchemaType type)
        {
            if (type is XmlSchemaComplexType complex)
            {
                return complex.ContentType == XmlSchemaContentType.TextOnly && SimpleContent(complex) is { } content && IsIdref(content);
            }
            var simple = (XmlSchemaSimpleType)type;
            return simple.Datatype?.Variety switch
            {
                XmlSchemaDatatypeVariety.List => IsIdref(ListItemType(simple)),
                XmlSchemaDatatypeVariety.Union => false,
                _ => AtomicTypeOf(simple)?.DerivesFrom(AtomicTypes.IDREF) == true,
            };
        }

        /// <summary>Whether values of a type hold QNames or NOTATIONs (namespace-sensitive content, XSLT 3.0 §11.9.2).</summary>
        public static bool IsNamespaceSensitive(XmlSchemaType type)
        {
            if (type is XmlSchemaComplexType complex)
            {
                return complex.ContentType == XmlSchemaContentType.TextOnly && SimpleContent(complex) is { } content && IsNamespaceSensitive(content);
            }
            var simple = (XmlSchemaSimpleType)type;
            switch (simple.Datatype?.Variety)
            {
                case XmlSchemaDatatypeVariety.List:
                    return IsNamespaceSensitive(ListItemType(simple));
                case XmlSchemaDatatypeVariety.Union:
                    foreach (XmlSchemaSimpleType member in UnionMemberTypes(simple))
                    {
                        if (IsNamespaceSensitive(member))
                        {
                            return true;
                        }
                    }
                    return false;
                default:
                    return AtomicTypeOf(simple)?.Primitive is Primitive.QName or Primitive.Notation;
            }
        }

        // The same anonymous type read into two schema sets (the stylesheet's and the input's): the same place in the
        // same schema document.
        private static bool SameAnonymous(XmlSchemaType a, XmlSchemaType b) =>
            a.QualifiedName.IsEmpty && b.QualifiedName.IsEmpty && a.SourceUri != null && a.SourceUri == b.SourceUri &&
            a.LineNumber == b.LineNumber && a.LinePosition == b.LinePosition && a.LineNumber > 0;

        /// <summary>Whether <paramref name="type"/> is <paramref name="ancestor"/> or derived from it (by any method).</summary>
        public static bool DerivesFrom(XmlSchemaType? type, XmlSchemaType ancestor)
        {
            if (ReferenceEquals(ancestor, AnyType))
            {
                return true;
            }
            for (XmlSchemaType? t = type; t != null; t = t.BaseXmlSchemaType)
            {
                if (ReferenceEquals(t, ancestor) || (!ancestor.QualifiedName.IsEmpty && t.QualifiedName == ancestor.QualifiedName) || SameAnonymous(t, ancestor))
                {
                    return true;
                }
            }
            // A member of a union type is substitutable for it.
            if (ancestor is XmlSchemaSimpleType { Datatype.Variety: XmlSchemaDatatypeVariety.Union } union && type != null)
            {
                foreach (XmlSchemaSimpleType member in UnionMemberTypes(union))
                {
                    if (DerivesFrom(type, member))
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }

    /// <summary>The schema information of an annotated node of a compact tree.</summary>
    internal sealed class NodeSchemaInfo(XmlSchemaType type, bool nilled) : IXmlSchemaInfo
    {
        public XmlSchemaValidity Validity => XmlSchemaValidity.Valid;

        public bool IsDefault => false;

        public bool IsNil => nilled;

        public XmlSchemaSimpleType? MemberType => null;

        public XmlSchemaType SchemaType => type;

        public XmlSchemaElement? SchemaElement => null;

        public XmlSchemaAttribute? SchemaAttribute => null;
    }

    /// <summary>
    /// Xsl3 (P6): a writer of a constructed tree that can carry type annotations: the compact tree's writer, and the
    /// event cache a tree may be recorded in before it is built.
    /// </summary>
    internal interface IAnnotatingWriter
    {
        /// <summary>The type annotation of the element just started.</summary>
        void AnnotateElement(XmlSchemaType type, bool nilled);

        /// <summary>The type annotation of an attribute of the element just started, applied when it is written.</summary>
        void AnnotateAttribute(string ns, string local, XmlSchemaType type);

        /// <summary>An attribute of the element just started is an ID, though untyped (validation="strip" keeps is-id).</summary>
        void MarkIdAttribute(string ns, string local);
    }

    internal static class AnnotatingWriters
    {
        /// <summary>The writer itself, or the one an attribute cache wraps, if it can carry type annotations.</summary>
        public static IAnnotatingWriter? Of(XmlWriter? writer) =>
            writer as IAnnotatingWriter ?? (writer as Xsl3.Runtime.XmlAttributeCache)?.Wrapped as IAnnotatingWriter;
    }
}
