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
        private static readonly Type? s_iDtdInfo = Type.GetType("System.Xml.IDtdInfo, System.Private.Xml");
        private static readonly Type? s_iAttrList = Type.GetType("System.Xml.IDtdAttributeListInfo, System.Private.Xml");
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
