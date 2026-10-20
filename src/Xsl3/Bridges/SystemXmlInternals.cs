// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Xml.Schema;
using System.Xml.XPath;
using MS.Internal.Xml.Cache;

namespace System.Xml
{
    // Upstream engine code calls internal members of public System.Xml types. Those members belong to
    // System.Private.Xml and are invisible here, so this file supplies them as C# 14 extension members
    // with the same names: the call sites in vendored and engine code compile without changes.
    //
    // Three kinds of member are bridged:
    //  - Copies: members that only use the public surface (masks, MoveToNonDescendant, ...). The code is
    //    copied from upstream System/Xml/XPath/XPathNavigator.cs and friends.
    //  - Accessors: members whose behaviour lives in System.Private.Xml (XPathNavigator.UniqueId,
    //    internal constructors). They are reached with [UnsafeAccessor], which binds at JIT time, works
    //    under NativeAOT and trimming, and dispatches virtually.
    //  - Reflection: only where a signature mentions an internal type (see DtdInfoBridge).
    //
    // docs/internals.md lists every bridged member. A member that disappears upstream fails with
    // MissingMethodException at the first call, which the upstream test suite exercises on each TFM.

    internal static class XPathNavigatorInternals
    {
        // Get mask that will allow XPathNodeType content matching to be performed using only a shift and an and operation
        private const int AllMask = 0x7FFFFFFF;
        private const int NoAttrNmspMask = AllMask & ~(1 << (int)XPathNodeType.Attribute) & ~(1 << (int)XPathNodeType.Namespace);
        private const int TextMask = (1 << (int)XPathNodeType.Text) | (1 << (int)XPathNodeType.SignificantWhitespace) | (1 << (int)XPathNodeType.Whitespace);
        private static readonly int[] s_contentKindMasks = {
            (1 << (int) XPathNodeType.Root),                        // Root
            (1 << (int) XPathNodeType.Element),                     // Element
            0,                                                      // Attribute (not content)
            0,                                                      // Namespace (not content)
            TextMask,                                               // Text
            (1 << (int) XPathNodeType.SignificantWhitespace),       // SignificantWhitespace
            (1 << (int) XPathNodeType.Whitespace),                  // Whitespace
            (1 << (int) XPathNodeType.ProcessingInstruction),       // ProcessingInstruction
            (1 << (int) XPathNodeType.Comment),                     // Comment
            NoAttrNmspMask,                                         // All
        };

        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_UniqueId")]
        private static extern string StockUniqueId(XPathNavigator navigator);

        extension(XPathNavigator)
        {
            // (R)oot (E)lement (A)ttribute (N)amespace (T)ext (S)ignificantWhitespace (W)hitespace
            // (P)rocessingInstruction (C)omment (X) All
            internal static string NodeTypeLetter => "REANTSWPCX";

            internal static string UniqueIdTbl => "ABCDEFGHIJKLMNOPQRSTUVWXYZ123456";

            internal static int GetContentKindMask(XPathNodeType type)
            {
                return s_contentKindMasks[(int)type];
            }

            internal static int GetKindMask(XPathNodeType type)
            {
                if (type == XPathNodeType.All)
                    return AllMask;
                else if (type == XPathNodeType.Text)
                    return TextMask;

                return (1 << (int)type);
            }

            internal static bool IsText(XPathNodeType type)
            {
                //return ((1 << (int) type) & TextMask) != 0;
                return unchecked((uint)(type - XPathNodeType.Text)) <= (XPathNodeType.Whitespace - XPathNodeType.Text);
            }
        }

        extension(XPathNavigator navigator)
        {
            /// <summary>
            /// The id used by generate-id(). Upstream this is an internal virtual property. The vendored
            /// XPathDocumentNavigator provides its own; every other navigator gets System.Private.Xml's
            /// implementation, so ids for foreign trees match stock XslCompiledTransform exactly.
            /// </summary>
            internal string UniqueId => navigator is XPathDocumentNavigator vendored ? vendored.UniqueId : navigator is Xsl3.Tree.CompactNavigator compact ? compact.UniqueId : StockUniqueId(navigator);

            internal bool MoveToPrevious(string localName, string namespaceURI)
            {
                XPathNavigator navClone = navigator.Clone();

                string? atomizedLocalName = (localName != null) ? navigator.NameTable.Get(localName) : null;
                while (navigator.MoveToPrevious())
                {
                    if (navigator.NodeType == XPathNodeType.Element && atomizedLocalName == navigator.LocalName && namespaceURI == navigator.NamespaceURI)
                        return true;
                }

                navigator.MoveTo(navClone);
                return false;
            }

            internal bool MoveToPrevious(XPathNodeType type)
            {
                XPathNavigator navClone = navigator.Clone();
                int mask = XPathNavigator.GetContentKindMask(type);

                while (navigator.MoveToPrevious())
                {
                    if (((1 << (int)navigator.NodeType) & mask) != 0)
                        return true;
                }

                navigator.MoveTo(navClone);
                return false;
            }

            internal bool MoveToNonDescendant()
            {
                // If current node is document, there is no next non-descendant
                if (navigator.NodeType == XPathNodeType.Root)
                    return false;

                // If sibling exists, it is the next non-descendant
                if (navigator.MoveToNext())
                    return true;

                // The current node is either an attribute, namespace, or last child node
                XPathNavigator navSave = navigator.Clone();

                if (!navigator.MoveToParent())
                    return false;

                switch (navSave.NodeType)
                {
                    case XPathNodeType.Attribute:
                    case XPathNodeType.Namespace:
                        // Next node in document order is first content-child of parent
                        if (navigator.MoveToFirstChild())
                            return true;
                        break;
                }

                while (!navigator.MoveToNext())
                {
                    if (!navigator.MoveToParent())
                    {
                        // Restore original position and return false
                        navigator.MoveTo(navSave);
                        return false;
                    }
                }

                return true;
            }
        }

        [DebuggerDisplay("{ToString()}")]
        internal readonly struct DebuggerDisplayProxy
        {
            private readonly XPathNavigator _nav;
            public DebuggerDisplayProxy(XPathNavigator nav)
            {
                _nav = nav;
            }
            public override string ToString()
            {
                string result = _nav.NodeType.ToString();
                switch (_nav.NodeType)
                {
                    case XPathNodeType.Element:
                        result += $", Name=\"{_nav.Name}\"";
                        break;
                    case XPathNodeType.Attribute:
                    case XPathNodeType.Namespace:
                    case XPathNodeType.ProcessingInstruction:
                        result += $", Name=\"{_nav.Name}\"";
                        result += $", Value=\"{_nav.Value}\"";
                        break;
                    case XPathNodeType.Text:
                    case XPathNodeType.Whitespace:
                    case XPathNodeType.SignificantWhitespace:
                    case XPathNodeType.Comment:
                        result += $", Value=\"{_nav.Value}\"";
                        break;
                }
                return result;
            }
        }
    }

    internal static class XmlNamespaceManagerInternals
    {
        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "GetNamespaceDeclaration")]
        private static extern bool StockGetNamespaceDeclaration(XmlNamespaceManager manager, int idx, out string? prefix, out string? uri);

        extension(XmlNamespaceManager manager)
        {
            internal bool GetNamespaceDeclaration(int idx, [NotNullWhen(true)] out string? prefix, out string? uri)
            {
                return StockGetNamespaceDeclaration(manager, idx, out prefix, out uri);
            }
        }
    }
}

namespace System.Xml.Schema
{
    internal static class XmlAtomicValueInternals
    {
        // XmlAtomicValue's constructors are internal. Upstream (and therefore the legacy 1.0 path) boxes
        // atomic values through them; the 3.0 processor uses XdmAtomicValue instead (SPEC §8.2).
        [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
        internal static extern XmlAtomicValue Create(XmlSchemaType xmlType, bool value);
        [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
        internal static extern XmlAtomicValue Create(XmlSchemaType xmlType, DateTime value);
        [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
        internal static extern XmlAtomicValue Create(XmlSchemaType xmlType, double value);
        [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
        internal static extern XmlAtomicValue Create(XmlSchemaType xmlType, int value);
        [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
        internal static extern XmlAtomicValue Create(XmlSchemaType xmlType, long value);
        [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
        internal static extern XmlAtomicValue Create(XmlSchemaType xmlType, string value);
        [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
        internal static extern XmlAtomicValue Create(XmlSchemaType xmlType, string value, IXmlNamespaceResolver? nsResolver);
        [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
        internal static extern XmlAtomicValue Create(XmlSchemaType xmlType, object value);
        [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
        internal static extern XmlAtomicValue Create(XmlSchemaType xmlType, object value, IXmlNamespaceResolver? nsResolver);
    }
}

namespace System.Xml.Schema
{
    /// <summary>
    /// Stands in for System.Private.Xml's internal XmlUntypedConverter, which the raw writers use for WriteValue.
    /// </summary>
    /// <remarks>
    /// xs:untypedAtomic's public <see cref="XmlSchemaDatatype.ChangeType(object, Type, IXmlNamespaceResolver)"/>
    /// with a string target runs XmlUntypedConverter.Untyped.ToString(value, resolver), so results are identical.
    /// </remarks>
    internal sealed class XmlUntypedConverter
    {
        public static readonly XmlUntypedConverter Untyped = new XmlUntypedConverter();

        private static readonly XmlSchemaDatatype s_untypedAtomic = XmlSchemaType.GetBuiltInSimpleType(XmlTypeCode.UntypedAtomic)!.Datatype!;

        private XmlUntypedConverter() { }

        public string ToString(object value, IXmlNamespaceResolver? nsResolver)
        {
            return (string)(nsResolver == null
                ? s_untypedAtomic.ChangeType(value, typeof(string))
                : s_untypedAtomic.ChangeType(value, typeof(string), nsResolver));
        }
    }
}
