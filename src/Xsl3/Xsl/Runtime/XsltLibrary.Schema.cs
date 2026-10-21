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
        public IList<XPathItem> X31CopyPreserving(IList<XPathItem> items, bool copyNamespaces)
        {
            var result = new XPathItem[items.Count];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = items[i] is XPathNavigator node ? NodeValidator.Preserve(node, copyNamespaces) : items[i];
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

        /// <summary>Xsl3 (P6): default-validation="preserve": copies into constructed trees keep their type annotations.</summary>
        public int X31PreserveAnnotations()
        {
            _runtime.PreserveAnnotations = true;
            return 0;
        }

        /// <summary>Xsl3 (P6): whether a node is untyped (xs:untyped, xs:untypedAtomic): no schema validated it.</summary>
        public bool X31Untyped(XPathNavigator node) => SchemaTypes.TypeOf(node) == null;

        /// <summary>
        /// Xsl3 (P6): the nodes an instruction with a validation or type attribute constructs or copies, validated
        /// (XSLT 3.0 §25.4): <paramref name="validation"/> "strict" or "lax", or else against the type
        /// <paramref name="type"/> (an EQName). Each element, attribute and document node becomes its validated copy.
        /// </summary>
        public IList<XPathNavigator> X31Validate(IList<XPathItem> items, double schemasId, string validation, string type)
        {
            var schemas = (XmlSchemaSet)Constant((int)schemasId);
            XmlSchemaType? schemaType = null;
            if (type.Length > 0)
            {
                int close = type.IndexOf('}');
                var name = new XmlQualifiedName(type[(close + 1)..], type[2..close]);
                schemaType = name.Namespace == XmlSchema.Namespace
                    ? (name.Name == "anyType" ? SchemaTypes.AnyType : XmlSchemaType.GetBuiltInSimpleType(name))
                    : schemas.GlobalTypes[name] as XmlSchemaType;
            }
            var result = new XPathNavigator[items.Count];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = items[i] is XPathNavigator node
                    ? NodeValidator.Validate(node, schemas, validation == "strict", schemaType)
                    : throw Err.XPTY0004("Only nodes can be validated");
            }
            return result;
        }
    }
}
