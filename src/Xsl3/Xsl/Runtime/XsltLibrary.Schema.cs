// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Xml;
using System.Xml.Schema;
using System.Xml.XPath;
using Xsl3.Schema;
using Xsl3.Xdm;

namespace Xsl3.Runtime
{
    public sealed partial class XsltLibrary
    {
        /// <summary>
        /// Xsl3 (P6): xsl:copy-of validation="preserve": copies that keep their type annotations; in a sequence they are
        /// the items themselves (copied again only into content).
        /// </summary>
        public IList<XPathItem> X31CopyPreserving(IList<XPathItem> items, bool copyNamespaces) => Copies(items, copyNamespaces, keepTypes: true);

        /// <summary>Xsl3 (P6): xsl:copy-of validation="strip" (where default-validation="preserve" would keep them): untyped copies.</summary>
        public IList<XPathItem> X31CopyStripping(IList<XPathItem> items, bool copyNamespaces) => Copies(items, copyNamespaces, keepTypes: false);

        private IList<XPathItem> Copies(IList<XPathItem> items, bool copyNamespaces, bool keepTypes)
        {
            var result = new XPathItem[items.Count];
            for (int i = 0; i < result.Length; i++)
            {
                if (items[i] is not XPathNavigator node)
                {
                    result[i] = items[i];
                    continue;
                }
                XPathNavigator copy = NodeValidator.Preserve(node, copyNamespaces, keepTypes);
                if (node.NodeType is XPathNodeType.Element or XPathNodeType.Root)
                {
                    // The copy keeps its original's accumulator values (as fn:copy-of's does, XSLT 3.0 §20.3.1).
                    XPathNavigator root = copy.Clone();
                    root.MoveToRoot();
                    (_accumulatorCopies ??= new())[_runtime.GenerateId(root)] = (copy.Clone(), node.Clone());
                }
                result[i] = copy;
            }
            return result;
        }

        /// <summary>
        /// Xsl3 (P6): input-type-annotations="strip": for the rest of the transformation, nodes of trees it didn't build
        /// read as untyped (XmlILCommand clears it at the end).
        /// </summary>
        public int X31StripInputAnnotations()
        {
            Xsl3.Tree.CompactDocument.StripInputAnnotations = true;
            return 0;
        }

        /// <summary>Xsl3 (P6): the stylesheet was compiled schema-aware (json-to-xml's validate option needs it).</summary>
        public int X31SchemaAware()
        {
            _runtime.SchemaAware = true;
            return 0;
        }

        /// <summary>Xsl3 (P6): default-validation="preserve": copies into constructed trees keep their type annotations.</summary>
        public int X31PreserveAnnotations()
        {
            _runtime.PreserveAnnotations = true;
            return 0;
        }

        /// <summary>
        /// Xsl3 (P6): enters (2 preserve, the element made being xs:anyType; 1 preserve; 0 strip) or leaves (-1) the scope of a constructor whose validation attribute,
        /// or default-validation, says whether nodes copied into it keep their type annotations. Returns nothing.
        /// </summary>
        public IList<XPathItem> X31AnnotationScope(double op)
        {
            if (op < 0)
            {
                _runtime.AnnotationScopes?.Pop();
                _runtime.PendingAnyType = false;
            }
            else
            {
                (_runtime.AnnotationScopes ??= new Stack<bool>()).Push(op > 0);
                // (2: the element the constructor starts next is annotated xs:anyType.)
                _runtime.PendingAnyType = op == 2;
            }
            return Sequences.Empty;
        }

        /// <summary>Xsl3 (P6): whether a node is untyped (xs:untyped, xs:untypedAtomic): no schema validated it.</summary>
        public bool X31Untyped(XPathNavigator node) => SchemaTypes.TypeOf(node) == null;

        /// <summary>
        /// Xsl3 (P6): the nodes an instruction with a validation or type attribute constructs or copies, validated
        /// (XSLT 3.0 §25.4): <paramref name="validation"/> "strict" or "lax", or else against the type
        /// <paramref name="type"/> (an EQName). Each element, attribute and document node becomes its validated copy.
        /// </summary>
        public IList<XPathItem> X31Validate(IList<XPathItem> items, double schemasId, string validation, string type)
        {
            var schemas = (XmlSchemaSet)Constant((int)schemasId);
            XmlSchemaType? schemaType = null;
            if (type.Length > 0)
            {
                int close = type.IndexOf('}');
                var name = new XmlQualifiedName(type[(close + 1)..], type[2..close]);
                if (name.Namespace == XmlSchema.Namespace && name.Name == "untypedAtomic")
                {
                    // An element of type xs:untypedAtomic: simple content, and untyped.
                    foreach (XPathItem item in items)
                    {
                        if (item is XPathNavigator { NodeType: XPathNodeType.Element } e && e.Clone() is var child && child.MoveToChild(XPathNodeType.Element))
                        {
                            throw new XdmException("XTTE1540", $"{e.Name} has element content, so it isn't valid as xs:untypedAtomic");
                        }
                    }
                    // (Elements are annotated xs:untypedAtomic; attributes stay untyped, which is the same.)
                    var annotated = new XPathItem[items.Count];
                    for (int i = 0; i < annotated.Length; i++)
                    {
                        annotated[i] = items[i] is XPathNavigator { NodeType: XPathNodeType.Element } element
                            ? NodeValidator.Validate(element, schemas, strict: true, SchemaTypes.UntypedAtomic) : items[i];
                    }
                    return annotated;
                }
                schemaType = name.Namespace == XmlSchema.Namespace
                    ? (name.Name == "anyType" ? SchemaTypes.AnyType : XmlSchemaType.GetBuiltInSimpleType(name))
                    : schemas.GlobalTypes[name] as XmlSchemaType;
            }
            var result = new XPathItem[items.Count];
            for (int i = 0; i < result.Length; i++)
            {
                // (Atomic values, as xsl:copy-of copies them, aren't validated.)
                result[i] = items[i] is XPathNavigator node ? NodeValidator.Validate(node, schemas, validation == "strict", schemaType) : items[i];
                if (_accumulatorCopies != null && items[i] is XPathNavigator && !ReferenceEquals(result[i], items[i]))
                {
                    // A copy made with copy-accumulators="yes" keeps its original's accumulator values once validated.
                    XPathNavigator root = ((XPathNavigator)items[i]).Clone();
                    root.MoveToRoot();
                    if (_accumulatorCopies.TryGetValue(_runtime.GenerateId(root), out var copied) && Original((XPathNavigator)items[i], copied.Top, copied.Original) is XPathNavigator original)
                    {
                        XPathNavigator validatedRoot = ((XPathNavigator)result[i]).Clone();
                        validatedRoot.MoveToRoot();
                        _accumulatorCopies[_runtime.GenerateId(validatedRoot)] = (((XPathNavigator)result[i]).Clone(), original.Clone());
                        (_validatedAccumulatorCopies ??= new HashSet<string>(System.StringComparer.Ordinal)).Add(_runtime.GenerateId(validatedRoot));
                    }
                }
            }
            return result;
        }
    }
}
