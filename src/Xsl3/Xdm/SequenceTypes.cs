// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
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
            foreach (XPathItem item in seq)
            {
                if (!Item.Matches(item))
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

        public override bool Matches(XPathItem item) => item is AtomicValue a ? a.Type.DerivesFrom(Type) : (!item.IsNode && Sequences.Atomize(item).Type.DerivesFrom(Type));

        public override string ToString() => Type.Name.ToString();
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
            if (!AnyType)
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

    /// <summary>Function, map and array tests. Function items arrive in phase P3; until then nothing matches.</summary>
    internal sealed class FunctionItemTest : ItemTest
    {
        public static readonly FunctionItemTest Instance = new();

        public override bool Matches(XPathItem item) => false;

        public override string ToString() => "function(*)";
    }
}
