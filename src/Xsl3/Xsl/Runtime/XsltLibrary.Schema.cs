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

        /// <summary>Instruction copies apply namespace, annotation and accumulator options independently.</summary>
        public IList<XPathItem> X31CopyWithOptions(IList<XPathItem> items, bool copyNamespaces, bool keepTypes, bool copyAccumulators) =>
            Copies(items, copyNamespaces, keepTypes, copyAccumulators);

        private IList<XPathItem> Copies(IList<XPathItem> items, bool copyNamespaces, bool keepTypes, bool copyAccumulators = true)
        {
            var result = new XPathItem[items.Count];
            for (int i = 0; i < result.Length; i++)
            {
                if (items[i] is not XPathNavigator node)
                {
                    result[i] = items[i];
                    continue;
                }
                XPathNavigator copy;
                if (copyNamespaces && keepTypes && node is Streaming.StreamedNavigator grounded && node.NodeType is XPathNodeType.Element or XPathNodeType.Root)
                    copy = grounded.Ground(copyAccumulators);
                else
                {
                    using var consumption = node is Streaming.StreamedNavigator streamed && node.NodeType is XPathNodeType.Element or XPathNodeType.Root
                        ? streamed.BeginConsumption(replay: true, accumulators: copyAccumulators) : default;
                    copy = NodeValidator.Preserve(node, copyNamespaces, keepTypes);
                }
                if (copy is Tree.CompactNavigator compact) compact.Document.NoDocumentUri = true;
                if (copyAccumulators && node.NodeType is XPathNodeType.Element or XPathNodeType.Root)
                {
                    // The copy keeps its original's accumulator values (as fn:copy-of's does, XSLT 3.0 §20.3.1).
                    _runtime.Documents.RecordCopy(copy, node);
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
        /// (XSLT 3.0 §25.4): <paramref name="validation"/> "strict", "lax" or "strip", or else against the type
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
                schemaType = FindValidationType(schemas, name);
            }
            var result = new XPathItem[items.Count];
            for (int i = 0; i < result.Length; i++)
            {
                // (Atomic values, as xsl:copy-of copies them, aren't validated.)
                try
                {
                    // Xsl3 (P7D): source-document stripping reuses the existing node-copy policy,
                    // retaining source metadata instead of assessing unknown nodes laxly as anyType.
                    result[i] = items[i] is XPathNavigator node
                        ? validation == "strip" ? NodeValidator.Preserve(node, copyNamespaces: true, keepTypes: false)
                            : NodeValidator.Validate(node, schemas, validation == "strict", schemaType)
                        : items[i];
                }
                catch (XdmException e) when (validation == "copy" && e.Code == "XTTE1540" && items[i] is XPathNavigator { NodeType: XPathNodeType.Attribute })
                {
                    // An attribute copied by xsl:copy-of or xsl:copy with a type attribute (si-copy-of-117; a copied
                    // element or document stays XTTE1540, si-copy-of-110).
                    throw new XdmException("XTTE1510", e.Message);
                }
                if (items[i] is XPathNavigator originalNode && !ReferenceEquals(result[i], items[i]))
                {
                    // A copy made with copy-accumulators="yes" keeps its original's accumulator values once validated.
                    if (_runtime.Documents.TryGetCopy(originalNode, out var copied) && Original(originalNode, copied.Top, copied.Original) is XPathNavigator original)
                    {
                        _runtime.Documents.RecordCopy((XPathNavigator)result[i], original, validated: true);
                    }
                }
            }
            return result;
        }

        private static XmlSchemaType? FindValidationType(XmlSchemaSet schemas, XmlQualifiedName name) => name.Namespace == XmlSchema.Namespace
            ? name.Name == "anyType" ? SchemaTypes.AnyType : name.Name == "untypedAtomic" ? SchemaTypes.UntypedAtomic : XmlSchemaType.GetBuiltInSimpleType(name)
            : schemas.GlobalTypes[name] as XmlSchemaType;
    }
}
