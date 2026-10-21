// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Linq;
using System.Xml.XPath;

namespace Xsl3.Xdm
{
    internal enum Cardinality { ExactlyOne, ZeroOrOne, ZeroOrMore, OneOrMore }

    /// <summary>A resolved SequenceType (XPath 3.1 §2.5.4), for matching at run time.</summary>
    internal sealed class SequenceTypeTest
    {
        public static readonly SequenceTypeTest AnySequence = new(AnyItemTest.Instance, Cardinality.ZeroOrMore);
        public static readonly SequenceTypeTest EmptySequence = new(null, Cardinality.ZeroOrOne);

        public SequenceTypeTest(ItemTest? item, Cardinality cardinality)
        {
            Item = item;
            Cardinality = cardinality;
        }

        /// <summary>Null for <c>empty-sequence()</c>.</summary>
        public ItemTest? Item { get; }

        public Cardinality Cardinality { get; }

        public bool AllowsEmpty => Item == null || Cardinality is Cardinality.ZeroOrOne or Cardinality.ZeroOrMore;

        public bool AllowsMany => Item != null && Cardinality is Cardinality.ZeroOrMore or Cardinality.OneOrMore;

        public bool Matches(IList<XPathItem> seq)
        {
            if (Item == null)
            {
                return seq.Count == 0;
            }
            if (seq.Count == 0)
            {
                return AllowsEmpty;
            }
            if (seq.Count > 1 && !AllowsMany)
            {
                return false;
            }
            if (Item is AnyItemTest)
            {
                return true;
            }
            // Xsl3 (P4): by index (no enumerator for node sequences).
            for (int i = 0; i < seq.Count; i++)
            {
                if (!Item.Matches(seq[i]))
                {
                    return false;
                }
            }
            return true;
        }

        public override string ToString() => Item == null ? "empty-sequence()" : Item + Cardinality switch
        {
            Cardinality.ZeroOrOne => "?",
            Cardinality.ZeroOrMore => "*",
            Cardinality.OneOrMore => "+",
            _ => "",
        };
    }

    internal abstract class ItemTest
    {
        public abstract bool Matches(XPathItem item);
    }

    internal sealed class AnyItemTest : ItemTest
    {
        public static readonly AnyItemTest Instance = new();

        public override bool Matches(XPathItem item) => true;

        public override string ToString() => "item()";
    }

    /// <summary>An atomic or union type: a value matches if its type derives from the named type.</summary>
    internal sealed class AtomicTypeTest : ItemTest
    {
        public AtomicTypeTest(AtomicType type)
        {
            Type = type;
        }

        public AtomicType Type { get; }

        public override bool Matches(XPathItem item) => item is AtomicValue a ? a.Type.DerivesFrom(Type) : (!item.IsNode && item is not FunctionItem && Sequences.Atomize(item).Type.DerivesFrom(Type));

        public override string ToString() => Type.Name.ToString();
    }

    /// <summary>Xsl3 (P6): a union type a schema defines: a value matches if its type derives from one of the member types.</summary>
    internal sealed class UnionTypeTest : ItemTest
    {
        private readonly AtomicType[] _members;

        public UnionTypeTest(System.Xml.Schema.XmlSchemaSimpleType type)
        {
            Type = type;
            var members = new List<AtomicType>();
            void Add(System.Xml.Schema.XmlSchemaSimpleType t)
            {
                foreach (System.Xml.Schema.XmlSchemaSimpleType member in Xsl3.Schema.SchemaTypes.UnionMemberTypes(t))
                {
                    if (Xsl3.Schema.SchemaTypes.AtomicTypeOf(member) is { } atomic)
                    {
                        members.Add(atomic);
                    }
                    else if (member.Datatype?.Variety == System.Xml.Schema.XmlSchemaDatatypeVariety.Union)
                    {
                        Add(member);
                    }
                }
            }
            Add(type);
            _members = members.ToArray();
        }

        public System.Xml.Schema.XmlSchemaSimpleType Type { get; }

        public override bool Matches(XPathItem item)
        {
            AtomicValue? value = item as AtomicValue ?? (!item.IsNode && item is not FunctionItem ? Sequences.Atomize(item) : null);
            if (value == null)
            {
                return false;
            }
            foreach (AtomicType member in _members)
            {
                if (value.Type.DerivesFrom(member))
                {
                    return true;
                }
            }
            return false;
        }

        public override string ToString() => Type.QualifiedName.ToString();
    }

    /// <summary>xs:numeric, the union of xs:double, xs:float and xs:decimal (XPath 3.1 §2.7.2).</summary>
    internal sealed class NumericTest : ItemTest
    {
        public static readonly NumericTest Instance = new();

        public override bool Matches(XPathItem item) => item is AtomicValue a && a.Type.IsNumeric;

        public override string ToString() => "xs:numeric";
    }

    /// <summary>
    /// A kind test: node(), text(), element(N), attribute(*, T), … Name parts that are null match any name.
    /// Without schema-awareness, element type annotations are xs:untyped and attribute ones xs:untypedAtomic.
    /// </summary>
    internal sealed class NodeKindTest : ItemTest
    {
        public NodeKindTest(XPathNodeType? kind, string? namespaceUri = null, string? localName = null, bool anyType = true, NodeKindTest? documentElement = null)
        {
            Kind = kind;
            NamespaceUri = namespaceUri;
            LocalName = localName;
            AnyType = anyType;
            DocumentElement = documentElement;
        }

        /// <summary>Null for node().</summary>
        public XPathNodeType? Kind { get; }

        public string? NamespaceUri { get; }

        public string? LocalName { get; }

        /// <summary>Whether the type annotation in the test admits untyped nodes.</summary>
        public bool AnyType { get; }

        /// <summary>For document-node(element(...)): the test the document element must pass.</summary>
        public NodeKindTest? DocumentElement { get; }

        /// <summary>The local name of the xs: type in element(N, T) or attribute(N, T); null without one.</summary>
        public string? TypeName { get; init; }

        /// <summary>Whether the test admits nilled elements: element(N) and element(N, T?).</summary>
        public bool Nillable { get; init; } = true;

        /// <summary>
        /// Xsl3 (P6): the type named in element(N, T) or attribute(N, T), or declared for schema-element(N) and
        /// schema-attribute(N): a node a schema validated matches if its type annotation derives from it. Null for a
        /// type that only untyped nodes have (xs:untyped, xs:untypedAtomic).
        /// </summary>
        public System.Xml.Schema.XmlSchemaType? SchemaType { get; init; }

        /// <summary>Xsl3 (P6): for schema-element(N), N and the names of the elements in its substitution group.</summary>
        public HashSet<(string Ns, string Local)>? Names { get; init; }

        public override bool Matches(XPathItem item)
        {
            if (item is not XPathNavigator nav)
            {
                return false;
            }
            if (Kind is XPathNodeType kind)
            {
                XPathNodeType actual = nav.NodeType;
                bool kindMatches = kind == XPathNodeType.Text
                    ? actual is XPathNodeType.Text or XPathNodeType.Whitespace or XPathNodeType.SignificantWhitespace
                    : actual == kind;
                if (!kindMatches)
                {
                    return false;
                }
            }
            if (LocalName != null && nav.LocalName != LocalName)
            {
                return false;
            }
            if (NamespaceUri != null && nav.NamespaceURI != NamespaceUri)
            {
                return false;
            }
            if (Names != null && !Names.Contains((nav.NamespaceURI, nav.LocalName)))
            {
                return false;
            }
            if (TypeName != null)
            {
                // Untyped nodes match if the type admits xs:untyped (xs:untypedAtomic); typed ones if their annotation
                // derives from it (Xsl3, P6).
                System.Xml.Schema.XmlSchemaType? annotation = Xsl3.Schema.SchemaTypes.TypeOf(nav);
                if (annotation == null ? !AnyType : SchemaType == null || !Xsl3.Schema.SchemaTypes.DerivesFrom(annotation, SchemaType))
                {
                    return false;
                }
                if (!Nillable && annotation != null && Xsl3.Schema.SchemaTypes.IsNilled(nav))
                {
                    return false;
                }
            }
            else if (!AnyType)
            {
                return false;
            }
            if (DocumentElement != null)
            {
                XPathNavigator child = nav.Clone();
                if (!child.MoveToFirstChild())
                {
                    return false;
                }
                int elements = 0;
                XPathNavigator? element = null;
                do
                {
                    if (child.NodeType == XPathNodeType.Element)
                    {
                        elements++;
                        element = child.Clone();
                    }
                    else if (child.NodeType == XPathNodeType.Text)
                    {
                        return false;
                    }
                }
                while (child.MoveToNext());
                return elements == 1 && DocumentElement.Matches(element!);
            }
            return true;
        }

        public override string ToString() => Kind?.ToString() ?? "node()";
    }

    /// <summary>function(*) and function(T1, …, Tn) as R (XPath 3.1 §2.5.6.3). Maps and arrays are functions too.</summary>
    internal sealed class FunctionItemTest : ItemTest
    {
        public static readonly FunctionItemTest Instance = new(null, null);

        public FunctionItemTest(IReadOnlyList<SequenceTypeTest>? parameters, SequenceTypeTest? returnType)
        {
            Parameters = parameters;
            ReturnType = returnType;
        }

        /// <summary>Null for function(*).</summary>
        public IReadOnlyList<SequenceTypeTest>? Parameters { get; }

        public SequenceTypeTest? ReturnType { get; }

        public override bool Matches(XPathItem item)
        {
            if (item is not FunctionItem f)
            {
                return false;
            }
            if (Parameters == null)
            {
                return true;
            }
            if (f.Arity != Parameters.Count)
            {
                return false;
            }
            switch (f)
            {
                case MapItem map:
                    // A map is function(xs:anyAtomicType) as item()*: its values must match the return type, which must
                    // allow the empty sequence a missing key gives.
                    return SubtypeOf.Item(Parameters[0].Item, new AtomicTypeTest(AtomicTypes.AnyAtomicType)) && ReturnType!.AllowsEmpty &&
                        map.Entries.All(e => ReturnType.Matches(e.Value));
                case ArrayItem array:
                    return SubtypeOf.Item(Parameters[0].Item, new AtomicTypeTest(AtomicTypes.Integer)) && array.Members.All(m => ReturnType!.Matches(m));
            }
            for (int i = 0; i < Parameters.Count; i++)
            {
                // Parameters are contravariant: the test's parameter types must be subtypes of the function's.
                if (!SubtypeOf.Sequence(Parameters[i], f.ParameterType(i)))
                {
                    return false;
                }
            }
            return SubtypeOf.Sequence(f.ReturnType, ReturnType!);
        }

        public override string ToString() => Parameters == null ? "function(*)" : $"function({string.Join(", ", Parameters)}) as {ReturnType}";
    }

    /// <summary>map(*) and map(K, V).</summary>
    internal sealed class MapTest : ItemTest
    {
        public static readonly MapTest Any = new(null, null);

        public MapTest(AtomicType? keyType, SequenceTypeTest? valueType)
        {
            KeyType = keyType;
            ValueType = valueType;
        }

        public AtomicType? KeyType { get; }

        public SequenceTypeTest? ValueType { get; }

        public override bool Matches(XPathItem item) =>
            item is MapItem map && (KeyType == null || map.Entries.All(e => e.Key.Type.DerivesFrom(KeyType) && ValueType!.Matches(e.Value)));

        public override string ToString() => KeyType == null ? "map(*)" : $"map({KeyType.Name}, {ValueType})";
    }

    /// <summary>array(*) and array(T).</summary>
    internal sealed class ArrayTest : ItemTest
    {
        public static readonly ArrayTest Any = new(null);

        public ArrayTest(SequenceTypeTest? memberType)
        {
            MemberType = memberType;
        }

        public SequenceTypeTest? MemberType { get; }

        public override bool Matches(XPathItem item) => item is ArrayItem array && (MemberType == null || array.Members.All(m => MemberType.Matches(m)));

        public override string ToString() => MemberType == null ? "array(*)" : $"array({MemberType})";
    }

    /// <summary>The subtype relationship between sequence types (XPath 3.1 §2.5.6), as far as it is decidable statically.</summary>
    internal static class SubtypeOf
    {
        public static bool Sequence(SequenceTypeTest a, SequenceTypeTest b)
        {
            if (a.Item == null)
            {
                return b.AllowsEmpty;
            }
            if (b.Item == null)
            {
                return false;
            }
            bool cardinality = b.Cardinality switch
            {
                Cardinality.ZeroOrMore => true,
                Cardinality.ZeroOrOne => a.Cardinality is Cardinality.ExactlyOne or Cardinality.ZeroOrOne,
                Cardinality.OneOrMore => a.Cardinality is Cardinality.ExactlyOne or Cardinality.OneOrMore,
                _ => a.Cardinality == Cardinality.ExactlyOne,
            };
            return cardinality && Item(a.Item, b.Item);
        }

        // Whether the xs: type a derives from the xs: type b, as far as the built-in types go.
        private static bool TypeDerives(string a, string b)
        {
            if (a == b || b == "anyType")
            {
                return true;
            }
            if (b == "anySimpleType")
            {
                return a != "untyped";
            }
            return a != "anySimpleType" && AtomicTypes.Find(QName.Ns.Xs, a) is AtomicType at && AtomicTypes.Find(QName.Ns.Xs, b) is AtomicType bt && at.DerivesFrom(bt);
        }

        public static bool Item(ItemTest? a, ItemTest? b)
        {
            if (b is null or AnyItemTest)
            {
                return true;
            }
            switch (a, b)
            {
                case (AtomicTypeTest x, AtomicTypeTest y):
                    return x.Type.DerivesFrom(y.Type);
                case (AtomicTypeTest x, NumericTest):
                    return x.Type.IsNumeric;
                case (NumericTest, NumericTest):
                    return true;
                case (NumericTest, AtomicTypeTest y):
                    return y.Type == AtomicTypes.AnyAtomicType;
                case (NodeKindTest x, NodeKindTest y):
                    return (y.Kind == null || x.Kind == y.Kind) && (y.LocalName == null || x.LocalName == y.LocalName) &&
                        (y.NamespaceUri == null || x.NamespaceUri == y.NamespaceUri) && (y.DocumentElement == null || x.DocumentElement != null && Item(x.DocumentElement, y.DocumentElement)) &&
                        (y.TypeName == null || TypeDerives(x.TypeName ?? (x.Kind == XPathNodeType.Attribute ? "anySimpleType" : "anyType"), y.TypeName)) &&
                        (y.Nillable || !x.Nillable || x.Kind is not (XPathNodeType.Element or null));
                case (MapTest x, MapTest y):
                    return y.KeyType == null || x.KeyType != null && x.KeyType.DerivesFrom(y.KeyType) && Sequence(x.ValueType!, y.ValueType!);
                case (ArrayTest x, ArrayTest y):
                    return y.MemberType == null || x.MemberType != null && Sequence(x.MemberType, y.MemberType);
                case (MapTest or ArrayTest or FunctionItemTest, FunctionItemTest { Parameters: null }):
                    return true;
                case (FunctionItemTest x, FunctionItemTest y):
                    if (x.Parameters == null || x.Parameters.Count != y.Parameters!.Count)
                    {
                        return false;
                    }
                    for (int i = 0; i < x.Parameters.Count; i++)
                    {
                        if (!Sequence(y.Parameters[i], x.Parameters[i]))
                        {
                            return false;
                        }
                    }
                    return Sequence(x.ReturnType!, y.ReturnType!);
                case (MapTest x, FunctionItemTest { Parameters.Count: 1 } y):
                    return Item(y.Parameters![0].Item, new AtomicTypeTest(AtomicTypes.AnyAtomicType)) &&
                        Sequence(x.ValueType ?? SequenceTypeTest.AnySequence, y.ReturnType!) && y.ReturnType!.AllowsEmpty;
                case (ArrayTest x, FunctionItemTest { Parameters.Count: 1 } y):
                    return Item(y.Parameters![0].Item, new AtomicTypeTest(AtomicTypes.Integer)) && Sequence(x.MemberType ?? SequenceTypeTest.AnySequence, y.ReturnType!);
                default:
                    return false;
            }
        }
    }
}
