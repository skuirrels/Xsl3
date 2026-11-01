// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
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

#if NET10_0_OR_GREATER
        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_DtdInfo")]
        [return: UnsafeAccessorType("System.Xml.IDtdInfo, System.Private.Xml")]
        private static extern object? GetDtdInfoCore(XmlReader reader);

        private static object? GetDtdInfo(XmlReader reader)
        {
            try { return GetDtdInfoCore(reader); }
            catch (MissingMethodException) { return null; } // same absent-member result as the reflection bridge
            catch (TypeLoadException) { return null; }
        }
#else
        private static readonly PropertyInfo? s_dtdInfo = typeof(XmlReader).GetProperty("DtdInfo", BindingFlags.Instance | BindingFlags.NonPublic);
        private static object? GetDtdInfo(XmlReader reader) => s_dtdInfo?.GetValue(reader);
#endif
        // A missing DTD needs only the reader's DtdInfo property. Keep schema-
        // internal type/property bindings cold until an actual DTD is encountered.
        private static class Metadata
        {
            // Literal type identities and annotations preserve only the requested bridge members.
            [DynamicallyAccessedMembers(Members)]
            internal static readonly Type? s_iDtdInfo = Type.GetType("System.Xml.IDtdInfo, System.Private.Xml");
            [DynamicallyAccessedMembers(Members)]
            internal static readonly Type? s_iAttrList = Type.GetType("System.Xml.IDtdAttributeListInfo, System.Private.Xml");
            [DynamicallyAccessedMembers(Members)]
            internal static readonly Type? s_iAttr = Type.GetType("System.Xml.IDtdAttributeInfo, System.Private.Xml");
            // Xsl3: the attribute declarations of an element's attribute list, and an attribute's tokenized type (IDREF, IDREFS).
            internal static readonly PropertyInfo? s_attDefs = Type.GetType("System.Xml.Schema.SchemaElementDecl, System.Private.Xml")?.GetProperty("AttDefs", BindingFlags.Instance | BindingFlags.NonPublic);
            internal static readonly PropertyInfo? s_tokenizedType = Type.GetType("System.Xml.Schema.SchemaAttDef, System.Private.Xml")?.GetProperty("TokenizedType", BindingFlags.Instance | BindingFlags.NonPublic);
            internal static readonly PropertyInfo? s_undeclaredElementDecls = Type.GetType("System.Xml.Schema.SchemaInfo, System.Private.Xml")?
                .GetProperty("UndeclaredElementDecls", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            // Xsl3: the DTD's element declarations (SchemaInfo.ElementDecls) and an element's content model.
            internal static readonly PropertyInfo? s_elementDecls = Type.GetType("System.Xml.Schema.SchemaInfo, System.Private.Xml")?.GetProperty("ElementDecls", BindingFlags.Instance | BindingFlags.NonPublic);
            internal static readonly PropertyInfo? s_contentValidator = Type.GetType("System.Xml.Schema.SchemaElementDecl, System.Private.Xml")?.GetProperty("ContentValidator", BindingFlags.Instance | BindingFlags.NonPublic);
            internal static readonly PropertyInfo? s_contentType = Type.GetType("System.Xml.Schema.ContentValidator, System.Private.Xml")?.GetProperty("ContentType", BindingFlags.Instance | BindingFlags.Public);

            // Precise initialization prevents beforefieldinit from hoisting the
            // reflection bindings ahead of the caller's null-DTD check.
            static Metadata() { }
        }

#if !NET10_0_OR_GREATER
        [DynamicDependency("get_DtdInfo", typeof(XmlReader))]
#endif
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

            object? dtdInfo = GetDtdInfo(reader);
            if (dtdInfo == null || Metadata.s_iDtdInfo == null || Metadata.s_iAttrList == null || Metadata.s_iAttr == null)
            {
                return;
            }

            builder.CreateIdTables(GetIdAttributes(dtdInfo));
        }

        /// <summary>Xsl3: the ID attributes the reader's DTD declares, as (element local name, element prefix, attribute local name, attribute prefix).</summary>
#if !NET10_0_OR_GREATER
        [DynamicDependency("get_DtdInfo", typeof(XmlReader))]
#endif
        [DynamicDependency(Members, "System.Xml.IDtdInfo", Asm)]
        [DynamicDependency(Members, "System.Xml.IDtdAttributeListInfo", Asm)]
        [DynamicDependency(Members, "System.Xml.IDtdAttributeInfo", Asm)]
        public static List<(string, string, string, string)> GetIdAttributes(XmlReader reader, bool includeUndeclared = false)
        {
            while (reader is XmlWrappingReader wrapping)
            {
                reader = wrapping.InnerReader;
            }
            object? dtdInfo = GetDtdInfo(reader);
            return dtdInfo == null || Metadata.s_iDtdInfo == null || Metadata.s_iAttrList == null || Metadata.s_iAttr == null
                ? new List<(string, string, string, string)>()
                : GetIdAttributes(dtdInfo, includeUndeclared);
        }


        /// <summary>
        /// Xsl3: the IDREF and IDREFS attributes the reader's DTD declares, as (element local name, element prefix,
        /// attribute local name, attribute prefix), for fn:idref. Empty when the DTD's internals can't be reached.
        /// </summary>
#if !NET10_0_OR_GREATER
        [DynamicDependency("get_DtdInfo", typeof(XmlReader))]
#endif
        [DynamicDependency(Members, "System.Xml.IDtdInfo", Asm)]
        [DynamicDependency(Members, "System.Xml.IDtdAttributeInfo", Asm)]
        [DynamicDependency("get_AttDefs", "System.Xml.Schema.SchemaElementDecl", Asm)]
        [DynamicDependency("get_TokenizedType", "System.Xml.Schema.SchemaAttDef", Asm)]
        public static List<(string, string, string, string)> GetIdrefAttributes(XmlReader reader, bool includeUndeclared = false)
        {
            var result = new List<(string, string, string, string)>();
            while (reader is XmlWrappingReader wrapping)
            {
                reader = wrapping.InnerReader;
            }
            object? dtdInfo = GetDtdInfo(reader);
            if (dtdInfo == null || Metadata.s_iDtdInfo == null || Metadata.s_iAttrList == null || Metadata.s_iAttr == null || Metadata.s_attDefs == null || Metadata.s_tokenizedType == null)
            {
                return result;
            }
            var attrLists = AttributeLists(dtdInfo, includeUndeclared);
            PropertyInfo listLocalName = Metadata.s_iAttrList.GetProperty("LocalName")!, listPrefix = Metadata.s_iAttrList.GetProperty("Prefix")!;
            PropertyInfo attrLocalName = Metadata.s_iAttr.GetProperty("LocalName")!, attrPrefix = Metadata.s_iAttr.GetProperty("Prefix")!;
            foreach (object attrList in attrLists)
            {
                if (Metadata.s_attDefs.GetValue(attrList) is not System.Collections.IDictionary attDefs)
                {
                    continue;
                }
                foreach (object attDef in attDefs.Values)
                {
                    if (Metadata.s_tokenizedType.GetValue(attDef) is XmlTokenizedType.IDREF or XmlTokenizedType.IDREFS)
                    {
                        result.Add(((string)listLocalName.GetValue(attrList)!, (string)listPrefix.GetValue(attrList)!,
                                    (string)attrLocalName.GetValue(attDef)!, (string)attrPrefix.GetValue(attDef)!));
                    }
                }
            }
            return result;
        }


        /// <summary>
        /// Xsl3: the elements the reader's DTD declares with element-only content, as (local name, prefix): their
        /// whitespace children are element content whitespace, which the data model leaves out (XDM 3.1 §6.7.3).
        /// Empty when the DTD's internals can't be reached.
        /// </summary>
#if !NET10_0_OR_GREATER
        [DynamicDependency("get_DtdInfo", typeof(XmlReader))]
#endif
        [DynamicDependency("get_ElementDecls", "System.Xml.Schema.SchemaInfo", Asm)]
        [DynamicDependency("get_ContentValidator", "System.Xml.Schema.SchemaElementDecl", Asm)]
        [DynamicDependency("get_ContentType", "System.Xml.Schema.ContentValidator", Asm)]
        public static HashSet<(string, string)> GetElementOnlyElements(XmlReader reader)
        {
            var result = new HashSet<(string, string)>();
            while (reader is XmlWrappingReader wrapping)
            {
                reader = wrapping.InnerReader;
            }
            object? dtdInfo = GetDtdInfo(reader);
            if (dtdInfo == null || Metadata.s_elementDecls == null || Metadata.s_contentValidator == null || Metadata.s_contentType == null ||
                Metadata.s_elementDecls.GetValue(dtdInfo) is not System.Collections.IDictionary decls)
            {
                return result;
            }
            foreach (System.Collections.DictionaryEntry decl in decls)
            {
                // A DTD's names have no namespace: the qualified name's namespace slot holds the prefix.
                if (decl.Key is XmlQualifiedName name && decl.Value != null && Metadata.s_contentValidator.GetValue(decl.Value) is object validator &&
                    Metadata.s_contentType.GetValue(validator) is Schema.XmlSchemaContentType.ElementOnly)
                {
                    result.Add((name.Name, name.Namespace));
                }
            }
            return result;
        }

        /// <summary>Xsl3: whether the bridge to the DTD's attribute types binds on this runtime (BridgeTests).</summary>
        internal static bool IdrefBridgeBinds => Metadata.s_attDefs != null && Metadata.s_tokenizedType != null;

        [DynamicDependency("get_UndeclaredElementDecls", "System.Xml.Schema.SchemaInfo", Asm)]
        private static List<object> AttributeLists(object dtdInfo, bool includeUndeclared)
        {
            var attrLists = new List<object>();
            foreach (object declaration in (System.Collections.IEnumerable)Metadata.s_iDtdInfo!.GetMethod("GetAttributeLists")!.Invoke(dtdInfo, null)!)
                attrLists.Add(declaration);
            // An ATTLIST can declare an ID without a separate ELEMENT declaration. The parser
            // retains those attribute lists separately; they are still authoritative ID metadata.
            if (includeUndeclared && Metadata.s_undeclaredElementDecls?.GetValue(dtdInfo) is System.Collections.IDictionary undeclared)
                foreach (object declaration in undeclared.Values) attrLists.Add(declaration);
            return attrLists;
        }

        private static List<(string, string, string, string)> GetIdAttributes(object dtdInfo, bool includeUndeclared = false)
        {
            var result = new List<(string, string, string, string)>();
            var attrLists = AttributeLists(dtdInfo, includeUndeclared);
            MethodInfo lookupId = Metadata.s_iAttrList!.GetMethod("LookupIdAttribute")!;
            PropertyInfo listLocalName = Metadata.s_iAttrList.GetProperty("LocalName")!, listPrefix = Metadata.s_iAttrList.GetProperty("Prefix")!;
            PropertyInfo attrLocalName = Metadata.s_iAttr!.GetProperty("LocalName")!, attrPrefix = Metadata.s_iAttr.GetProperty("Prefix")!;

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
