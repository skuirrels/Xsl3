// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Linq;
using System.Xml;
using System.Xml.Schema;
using System.Xml.XPath;
using Xsl3.Xdm;

namespace Xsl3.XPath31
{
    /// <summary>
    /// Resolves the lexical names in SequenceTypes and SingleTypes against a namespace context, producing
    /// the run-time tests of Xdm/SequenceTypes.cs. Unknown prefixes are XPST0081, unknown types XPST0051.
    /// </summary>
    internal sealed class StaticTypeResolver
    {
        private readonly System.Func<string, string?> _resolvePrefix;

        // Xsl3 (P6): the imported schemas (compiled), whose types and declarations sequence types can name; or null.
        private readonly XmlSchemaSet? _schemas;

        public StaticTypeResolver(System.Func<string, string?> resolvePrefix, string defaultElementNamespace = "", XmlSchemaSet? schemas = null)
        {
            _resolvePrefix = resolvePrefix;
            DefaultElementNamespace = defaultElementNamespace;
            _schemas = schemas;
        }

        /// <summary>Xsl3 (P6): a type an imported schema defines (not a built-in one), or null.</summary>
        public XmlSchemaType? FindSchemaType(QName q) =>
            _schemas != null && q.NamespaceUri != QName.Ns.Xs && _schemas.IsCompiled ? _schemas.GlobalTypes[new XmlQualifiedName(q.LocalName, q.NamespaceUri)] as XmlSchemaType : null;

        /// <summary>
        /// Xsl3 (P6): the target of a cast to, or constructor function of, a simple type an imported schema defines;
        /// null if no schema defines one by that name.
        /// </summary>
        public CastTarget? FindSchemaCastTarget(QName q, IXmlNamespaceResolver? namespaces)
        {
            if (FindSchemaType(q) is not XmlSchemaSimpleType simple)
            {
                return null;
            }
            return Xsl3.Schema.SchemaTypes.AtomicTypeOf(simple) is { } atomic
                ? new CastTarget(atomic, namespaces)
                : new CastTarget(AtomicTypes.String, namespaces, CastKind.Schema) { Schema = simple };
        }

        public string DefaultElementNamespace { get; }

        /// <summary>Expands a lexical QName. Unprefixed names take <paramref name="defaultNamespace"/>.</summary>
        public QName Expand(LexicalQName name, string defaultNamespace)
        {
            if (name.BracedUri != null)
            {
                return new QName(name.BracedUri, name.LocalName);
            }
            if (name.Prefix == null)
            {
                return new QName(defaultNamespace, name.LocalName);
            }
            string? uri = _resolvePrefix(name.Prefix);
            if (uri == null)
            {
                throw new XPathStaticException("XPST0081", $"Namespace prefix '{name.Prefix}' is not declared", -1);
            }
            return new QName(uri, name.LocalName, name.Prefix);
        }

        /// <summary>The target of <c>cast as</c> / <c>castable as</c> and constructor functions: a built-in atomic type, xs:numeric or a built-in list type.</summary>
        public static (AtomicType Type, CastKind Kind)? FindCastTarget(QName q)
        {
            if (q.NamespaceUri == QName.Ns.Xs)
            {
                switch (q.LocalName)
                {
                    case "numeric": return (AtomicTypes.Double, CastKind.Numeric);
                    case "NMTOKENS": return (AtomicTypes.NMTOKEN, CastKind.List);
                    case "IDREFS": return (AtomicTypes.IDREF, CastKind.List);
                    case "ENTITIES": return (AtomicTypes.ENTITY, CastKind.List);
                }
            }
            AtomicType? t = AtomicTypes.Find(q.NamespaceUri, q.LocalName);
            return t == null ? null : (t, CastKind.Atomic);
        }

        public (AtomicType Type, CastKind Kind) ResolveCastTarget(LexicalQName name)
        {
            QName q = Expand(name, DefaultElementNamespace);
            return FindCastTarget(q) ?? throw new XPathStaticException("XPST0051", $"{name} is not a known atomic type", -1);
        }

        /// <summary>The target of <c>cast as</c> / <c>castable as</c>: a built-in atomic type.</summary>
        public AtomicType ResolveAtomicType(LexicalQName name)
        {
            // Unprefixed type names use the default element/type namespace.
            QName q = Expand(name, DefaultElementNamespace);
            return AtomicTypes.Find(q.NamespaceUri, q.LocalName)
                ?? throw new XPathStaticException("XPST0051", $"{name} is not a known atomic type", -1);
        }

        public SequenceTypeTest Resolve(SequenceType type)
        {
            if (type.Item == null)
            {
                return SequenceTypeTest.EmptySequence;
            }
            Cardinality cardinality = type.Occurrence switch
            {
                Occurrence.ZeroOrOne => Cardinality.ZeroOrOne,
                Occurrence.ZeroOrMore => Cardinality.ZeroOrMore,
                Occurrence.OneOrMore => Cardinality.OneOrMore,
                _ => Cardinality.ExactlyOne,
            };
            return new SequenceTypeTest(ResolveItem(type.Item), cardinality);
        }

        public ItemTest ResolveItem(ItemType item)
        {
            switch (item)
            {
                case AnyItemType:
                    return AnyItemTest.Instance;
                case AtomicOrUnionType a:
                {
                    QName q = Expand(a.Name, DefaultElementNamespace);
                    if (q.NamespaceUri == QName.Ns.Xs && q.LocalName == "numeric")
                    {
                        return NumericTest.Instance;
                    }
                    AtomicType? t = AtomicTypes.Find(q.NamespaceUri, q.LocalName);
                    if (t == null && FindSchemaType(q) is XmlSchemaSimpleType simple)
                    {
                        // Xsl3 (P6): an atomic or union type an imported schema defines.
                        if (Xsl3.Schema.SchemaTypes.AtomicTypeOf(simple) is { } atomic)
                        {
                            return new AtomicTypeTest(atomic);
                        }
                        if (simple.Datatype?.Variety == XmlSchemaDatatypeVariety.Union)
                        {
                            return new UnionTypeTest(simple);
                        }
                        throw new XPathStaticException("XPST0051", $"{a.Name} is a list type, not an atomic or union type", -1);
                    }
                    if (t == null)
                    {
                        // A known non-atomic type (xs:untyped, xs:anyType, list types) is a type error, anything else unknown.
                        bool knownNonAtomic = q.NamespaceUri == QName.Ns.Xs && q.LocalName is "untyped" or "anyType" or "anySimpleType" or "NMTOKENS" or "IDREFS" or "ENTITIES" or "error";
                        throw new XPathStaticException(knownNonAtomic ? "XPST0051" : "XPST0051", $"{a.Name} is not an atomic type", -1);
                    }
                    return new AtomicTypeTest(t);
                }
                case AnyFunctionTest:
                    return FunctionItemTest.Instance;
                case TypedFunctionTest f:
                    return new FunctionItemTest(f.Parameters.Select(Resolve).ToList(), Resolve(f.Return));
                case AnyMapTest:
                    return MapTest.Any;
                case TypedMapTest m:
                {
                    ItemTest key = ResolveItem(new AtomicOrUnionType(m.KeyType));
                    AtomicType keyType = key is AtomicTypeTest at ? at.Type : AtomicTypes.AnyAtomicType;
                    return new MapTest(keyType, Resolve(m.ValueType));
                }
                case AnyArrayTest:
                    return ArrayTest.Any;
                case TypedArrayTest a:
                    return new ArrayTest(Resolve(a.MemberType));
                case KindTest k:
                    return ResolveKind(k);
            }
            throw new System.InvalidOperationException("Unknown item type " + item);
        }

        public NodeKindTest ResolveKind(KindTest kind)
        {
            switch (kind)
            {
                case AnyKindTest:
                    return new NodeKindTest(null);
                case TextTest:
                    return new NodeKindTest(XPathNodeType.Text);
                case CommentTest:
                    return new NodeKindTest(XPathNodeType.Comment);
                case NamespaceNodeTest:
                    return new NodeKindTest(XPathNodeType.Namespace);
                case PITest pi:
                    return new NodeKindTest(XPathNodeType.ProcessingInstruction, null, pi.Name);
                case DocumentTest d:
                    return new NodeKindTest(XPathNodeType.Root, documentElement: d.ElementTest == null ? null : ResolveKind(d.ElementTest));
                case ElementTest e:
                {
                    QName? name = e.Name == null ? null : Expand(e.Name, DefaultElementNamespace);
                    return new NodeKindTest(XPathNodeType.Element, name?.NamespaceUri, name?.LocalName, anyType: AdmitsUntyped(e.TypeName, element: true))
                    {
                        TypeName = e.TypeName == null ? null : Expand(e.TypeName, DefaultElementNamespace).LocalName,
                        Nillable = e.TypeName == null || e.Nillable,
                        SchemaType = e.TypeName == null ? null : NamedSchemaType(Expand(e.TypeName, DefaultElementNamespace)),
                    };
                }
                case AttributeTest a:
                {
                    QName? name = a.Name == null ? null : Expand(a.Name, "");
                    return new NodeKindTest(XPathNodeType.Attribute, name?.NamespaceUri, name?.LocalName, anyType: AdmitsUntyped(a.TypeName, element: false))
                    {
                        TypeName = a.TypeName == null ? null : Expand(a.TypeName, DefaultElementNamespace).LocalName,
                        SchemaType = a.TypeName == null ? null : NamedSchemaType(Expand(a.TypeName, DefaultElementNamespace)),
                    };
                }
                case SchemaElementTest s:
                {
                    QName name = Expand(s.Name, DefaultElementNamespace);
                    if (FindDeclaration(name, element: true) is not XmlSchemaElement declaration)
                    {
                        throw new XPathStaticException("XPST0008", $"schema-element({s.Name}): no element {name} is declared in the imported schemas", -1);
                    }
                    var names = new System.Collections.Generic.HashSet<(string, string)> { (name.NamespaceUri, name.LocalName) };
                    foreach (XmlSchemaElement member in _schemas!.GlobalElements.Values)
                    {
                        // The elements in its substitution group, directly or through others.
                        for (XmlSchemaElement? e = member; e != null && !e.SubstitutionGroup.IsEmpty; e = _schemas.GlobalElements[e.SubstitutionGroup] as XmlSchemaElement)
                        {
                            if (e.SubstitutionGroup.Name == name.LocalName && e.SubstitutionGroup.Namespace == name.NamespaceUri)
                            {
                                names.Add((member.QualifiedName.Namespace, member.QualifiedName.Name));
                                break;
                            }
                        }
                    }
                    return new NodeKindTest(XPathNodeType.Element, anyType: ReferenceEquals(declaration.ElementSchemaType, Xsl3.Schema.SchemaTypes.AnyType))
                    {
                        TypeName = "schema-element(" + name.EQName + ")",
                        SchemaType = declaration.ElementSchemaType,
                        Names = names,
                        Nillable = declaration.IsNillable,
                    };
                }
                case SchemaAttributeTest s:
                {
                    QName name = Expand(s.Name, "");
                    if (FindDeclaration(name, element: false) is not XmlSchemaAttribute declaration)
                    {
                        throw new XPathStaticException("XPST0008", $"schema-attribute({s.Name}): no attribute {name} is declared in the imported schemas", -1);
                    }
                    return new NodeKindTest(XPathNodeType.Attribute, name.NamespaceUri, name.LocalName, anyType: false)
                    {
                        TypeName = "schema-attribute(" + name.EQName + ")",
                        SchemaType = declaration.AttributeSchemaType,
                    };
                }
            }
            throw new System.InvalidOperationException("Unknown kind test " + kind);
        }

        // Xsl3 (P6): a global element or attribute declaration of the imported schemas.
        private XmlSchemaObject? FindDeclaration(QName name, bool element)
        {
            if (_schemas == null || !_schemas.IsCompiled)
            {
                return null;
            }
            var q = new XmlQualifiedName(name.LocalName, name.NamespaceUri);
            return element ? _schemas.GlobalElements[q] : _schemas.GlobalAttributes[q];
        }

        // Xsl3 (P6): the type a name in element(N, T) / attribute(N, T) stands for, built-in or the schemas'; null for
        // xs:untyped and xs:untypedAtomic, which only untyped nodes have.
        private XmlSchemaType? NamedSchemaType(QName q)
        {
            if (q.NamespaceUri == QName.Ns.Xs)
            {
                var xq = new XmlQualifiedName(q.LocalName, XmlSchema.Namespace);
                return q.LocalName is "untyped" or "untypedAtomic" ? null
                    : q.LocalName == "anyType" ? Xsl3.Schema.SchemaTypes.AnyType
                    : XmlSchemaType.GetBuiltInSimpleType(xq);
            }
            return FindSchemaType(q);
        }

        // Without schema-awareness elements are annotated xs:untyped and attributes xs:untypedAtomic, so a type
        // name in element(N, T) / attribute(N, T) matches only if it is one of their supertypes.
        private bool AdmitsUntyped(LexicalQName? typeName, bool element)
        {
            if (typeName == null)
            {
                return true;
            }
            QName q = Expand(typeName, DefaultElementNamespace);
            if (q.NamespaceUri == QName.Ns.Xs)
            {
                if (element ? q.LocalName is "untyped" or "anyType" : q.LocalName is "untypedAtomic" or "anySimpleType" or "anyAtomicType")
                {
                    return true;
                }
                if (AtomicTypes.Find(q.NamespaceUri, q.LocalName) != null || q.LocalName is "anyType" or "untyped" or "anySimpleType" or "NMTOKENS" or "IDREFS" or "ENTITIES")
                {
                    return false;
                }
            }
            else if (FindSchemaType(q) != null)
            {
                return false; // (a type an imported schema defines: untyped nodes aren't of it)
            }
            throw new XPathStaticException("XPST0008", $"Type {typeName} is not defined", -1);
        }
    }
}
