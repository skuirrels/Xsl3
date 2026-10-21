// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using MS.Internal.Xml.Cache;

namespace System.Xml
{
    /// <summary>
    /// Reads the DTD ID-attribute declarations from an <see cref="XmlReader"/>.
    /// </summary>
    /// <remarks>
    /// Upstream XPathDocument calls the internal <c>XmlReader.DtdInfo</c> property and walks the internal
    /// <c>IDtdInfo</c> interface. Both live in System.Private.Xml, so they are reached by reflection here.
    /// This runs at most once per loaded document that has a DOCTYPE. See docs/internals.md.
    /// </remarks>
    internal static class DtdInfoBridge
    {
        private const string Asm = "System.Private.Xml";
        private const DynamicallyAccessedMemberTypes Members = DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.PublicProperties;

        private static readonly PropertyInfo? s_dtdInfo = typeof(XmlReader).GetProperty("DtdInfo", BindingFlags.Instance | BindingFlags.NonPublic);
        // Xsl3: annotated, so the trimmer keeps the members looked up below (a literal type name is analyzable).
        [DynamicallyAccessedMembers(Members)]
        private static readonly Type? s_iDtdInfo = Type.GetType("System.Xml.IDtdInfo, System.Private.Xml");
        [DynamicallyAccessedMembers(Members)]
        private static readonly Type? s_iAttrList = Type.GetType("System.Xml.IDtdAttributeListInfo, System.Private.Xml");
        [DynamicallyAccessedMembers(Members)]
        private static readonly Type? s_iAttr = Type.GetType("System.Xml.IDtdAttributeInfo, System.Private.Xml");

        [DynamicDependency("get_DtdInfo", typeof(XmlReader))]
        [DynamicDependency(Members, "System.Xml.IDtdInfo", Asm)]
        [DynamicDependency(Members, "System.Xml.IDtdAttributeListInfo", Asm)]
        [DynamicDependency(Members, "System.Xml.IDtdAttributeInfo", Asm)]
        public static void CreateIdTables(XPathDocumentBuilder builder, XmlReader reader)
        {
            // Our own wrapping readers can't forward the internal property, so go to the innermost reader.
            while (reader is XmlWrappingReader wrapping)
            {
                reader = wrapping.InnerReader;
            }

            object? dtdInfo = s_dtdInfo?.GetValue(reader);
            if (dtdInfo == null || s_iDtdInfo == null || s_iAttrList == null || s_iAttr == null)
            {
                return;
            }

            builder.CreateIdTables(GetIdAttributes(dtdInfo));
        }

        /// <summary>Xsl3: the ID attributes the reader's DTD declares, as (element local name, element prefix, attribute local name, attribute prefix).</summary>
        [DynamicDependency("get_DtdInfo", typeof(XmlReader))]
        [DynamicDependency(Members, "System.Xml.IDtdInfo", Asm)]
        [DynamicDependency(Members, "System.Xml.IDtdAttributeListInfo", Asm)]
        [DynamicDependency(Members, "System.Xml.IDtdAttributeInfo", Asm)]
        public static List<(string, string, string, string)> GetIdAttributes(XmlReader reader)
        {
            while (reader is XmlWrappingReader wrapping)
            {
                reader = wrapping.InnerReader;
            }
            object? dtdInfo = s_dtdInfo?.GetValue(reader);
            return dtdInfo == null || s_iDtdInfo == null || s_iAttrList == null || s_iAttr == null
                ? new List<(string, string, string, string)>()
                : GetIdAttributes(dtdInfo);
        }

        // Xsl3: the attribute declarations of an element's attribute list, and an attribute's tokenized type (IDREF, IDREFS).
        private static readonly PropertyInfo? s_attDefs = Type.GetType("System.Xml.Schema.SchemaElementDecl, System.Private.Xml")?.GetProperty("AttDefs", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly PropertyInfo? s_tokenizedType = Type.GetType("System.Xml.Schema.SchemaAttDef, System.Private.Xml")?.GetProperty("TokenizedType", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>
        /// Xsl3: the IDREF and IDREFS attributes the reader's DTD declares, as (element local name, element prefix,
        /// attribute local name, attribute prefix), for fn:idref. Empty when the DTD's internals can't be reached.
        /// </summary>
        [DynamicDependency("get_DtdInfo", typeof(XmlReader))]
        [DynamicDependency(Members, "System.Xml.IDtdInfo", Asm)]
        [DynamicDependency(Members, "System.Xml.IDtdAttributeInfo", Asm)]
        [DynamicDependency("get_AttDefs", "System.Xml.Schema.SchemaElementDecl", Asm)]
        [DynamicDependency("get_TokenizedType", "System.Xml.Schema.SchemaAttDef", Asm)]
        public static List<(string, string, string, string)> GetIdrefAttributes(XmlReader reader)
        {
            var result = new List<(string, string, string, string)>();
            while (reader is XmlWrappingReader wrapping)
            {
                reader = wrapping.InnerReader;
            }
            object? dtdInfo = s_dtdInfo?.GetValue(reader);
            if (dtdInfo == null || s_iDtdInfo == null || s_iAttrList == null || s_iAttr == null || s_attDefs == null || s_tokenizedType == null)
            {
                return result;
            }
            var attrLists = (System.Collections.IEnumerable)s_iDtdInfo.GetMethod("GetAttributeLists")!.Invoke(dtdInfo, null)!;
            PropertyInfo listLocalName = s_iAttrList.GetProperty("LocalName")!, listPrefix = s_iAttrList.GetProperty("Prefix")!;
            PropertyInfo attrLocalName = s_iAttr.GetProperty("LocalName")!, attrPrefix = s_iAttr.GetProperty("Prefix")!;
            foreach (object attrList in attrLists)
            {
                if (s_attDefs.GetValue(attrList) is not System.Collections.IDictionary attDefs)
                {
                    continue;
                }
                foreach (object attDef in attDefs.Values)
                {
                    if (s_tokenizedType.GetValue(attDef) is XmlTokenizedType.IDREF or XmlTokenizedType.IDREFS)
                    {
                        result.Add(((string)listLocalName.GetValue(attrList)!, (string)listPrefix.GetValue(attrList)!,
                                    (string)attrLocalName.GetValue(attDef)!, (string)attrPrefix.GetValue(attDef)!));
                    }
                }
            }
            return result;
        }

        /// <summary>Xsl3: whether the bridge to the DTD's attribute types binds on this runtime (BridgeTests).</summary>
        internal static bool IdrefBridgeBinds => s_attDefs != null && s_tokenizedType != null;

        private static List<(string, string, string, string)> GetIdAttributes(object dtdInfo)
        {
            var result = new List<(string, string, string, string)>();
            var attrLists = (System.Collections.IEnumerable)s_iDtdInfo!.GetMethod("GetAttributeLists")!.Invoke(dtdInfo, null)!;
            MethodInfo lookupId = s_iAttrList!.GetMethod("LookupIdAttribute")!;
            PropertyInfo listLocalName = s_iAttrList.GetProperty("LocalName")!, listPrefix = s_iAttrList.GetProperty("Prefix")!;
            PropertyInfo attrLocalName = s_iAttr!.GetProperty("LocalName")!, attrPrefix = s_iAttr.GetProperty("Prefix")!;

            foreach (object attrList in attrLists)
            {
                object? idAttribute = lookupId.Invoke(attrList, null);
                if (idAttribute != null)
                {
                    result.Add(((string)listLocalName.GetValue(attrList)!, (string)listPrefix.GetValue(attrList)!,
                                (string)attrLocalName.GetValue(idAttribute)!, (string)attrPrefix.GetValue(idAttribute)!));
                }
            }

            return result;
        }
    }
}
