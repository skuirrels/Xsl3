// Licensed under the MIT license. See LICENSE.TXT in the repository root.

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

        public StaticTypeResolver(System.Func<string, string?> resolvePrefix, string defaultElementNamespace = "")
        {
            _resolvePrefix = resolvePrefix;
            DefaultElementNamespace = defaultElementNamespace;
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
                    if (t == null)
                    {
                        // A known non-atomic type (xs:untyped, xs:anyType, list types) is a type error, anything else unknown.
                        bool knownNonAtomic = q.NamespaceUri == QName.Ns.Xs && q.LocalName is "untyped" or "anyType" or "anySimpleType" or "NMTOKENS" or "IDREFS" or "ENTITIES" or "error";
                        throw new XPathStaticException(knownNonAtomic ? "XPST0051" : "XPST0051", $"{a.Name} is not an atomic type", -1);
                    }
                    return new AtomicTypeTest(t);
                }
                case AnyFunctionTest or TypedFunctionTest or AnyMapTest or TypedMapTest or AnyArrayTest or TypedArrayTest:
                    return FunctionItemTest.Instance;
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
                    return new NodeKindTest(XPathNodeType.Element, name?.NamespaceUri, name?.LocalName, anyType: AdmitsUntyped(e.TypeName, element: true));
                }
                case AttributeTest a:
                {
                    QName? name = a.Name == null ? null : Expand(a.Name, "");
                    return new NodeKindTest(XPathNodeType.Attribute, name?.NamespaceUri, name?.LocalName, anyType: AdmitsUntyped(a.TypeName, element: false));
                }
                case SchemaElementTest s:
                    throw new XPathStaticException("XPST0008", $"schema-element({s.Name}): no schema is imported", -1);
                case SchemaAttributeTest s:
                    throw new XPathStaticException("XPST0008", $"schema-attribute({s.Name}): no schema is imported", -1);
            }
            throw new System.InvalidOperationException("Unknown kind test " + kind);
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
                if (AtomicTypes.Find(q.NamespaceUri, q.LocalName) != null || q.LocalName is "anyType" or "untyped" or "anySimpleType")
                {
                    return false;
                }
            }
            throw new XPathStaticException("XPST0008", $"Type {typeName} is not defined", -1);
        }
    }
}
