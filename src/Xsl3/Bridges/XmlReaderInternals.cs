// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace System.Xml
{
    /// <summary>
    /// Reads state of System.Private.Xml's internal XmlTextReaderImpl (the reader behind XmlReader.Create
    /// and XmlTextReader) that upstream's QueryReaderSettings and WhitespaceRuleReader inspect directly.
    /// </summary>
    /// <remarks>
    /// The type is internal and appears in the signatures, so this uses reflection. It runs once per
    /// transformation or document() load, never per node. See docs/internals.md.
    /// </remarks>
    internal static class XmlReaderInternals
    {
        private const string Asm = "System.Private.Xml";
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        private static readonly Type? s_implType = Type.GetType("System.Xml.XmlTextReaderImpl, System.Private.Xml");
        private static readonly MethodInfo? s_getResolver = s_implType?.GetMethod("GetResolver", Instance, Type.EmptyTypes);
        private static readonly PropertyInfo? s_whitespaceHandling = s_implType?.GetProperty("WhitespaceHandling", Instance);
        private static readonly PropertyInfo? s_textReaderImpl = typeof(XmlTextReader).GetProperty("Impl", Instance);

        /// <summary>Upstream: <c>(reader as XmlTextReaderImpl)?.GetResolver()</c>.</summary>
        [DynamicDependency("GetResolver", "System.Xml.XmlTextReaderImpl", Asm)]
        public static bool TryGetTextReaderImplResolver(XmlReader reader, out XmlResolver? resolver)
        {
            if (s_implType != null && s_implType.IsInstanceOfType(reader) && s_getResolver != null)
            {
                resolver = (XmlResolver?)s_getResolver.Invoke(reader, null);
                return true;
            }

            resolver = null;
            return false;
        }

        /// <summary>Upstream: <c>xmlTextReader.Impl.GetResolver()</c>.</summary>
        [DynamicDependency("Impl", typeof(XmlTextReader))]
        [DynamicDependency("GetResolver", "System.Xml.XmlTextReaderImpl", Asm)]
        public static XmlResolver? GetResolver(XmlTextReader reader)
        {
            object? impl = s_textReaderImpl?.GetValue(reader);
            return impl != null && s_getResolver != null ? (XmlResolver?)s_getResolver.Invoke(impl, null) : null;
        }

        /// <summary>Upstream: <c>reader is XmlTextReaderImpl impl &amp;&amp; impl.WhitespaceHandling == WhitespaceHandling.None</c>.</summary>
        [DynamicDependency("WhitespaceHandling", "System.Xml.XmlTextReaderImpl", Asm)]
        public static bool IsTextReaderImplStrippingAllWhitespace(XmlReader reader)
        {
            return s_implType != null && s_implType.IsInstanceOfType(reader) && s_whitespaceHandling != null &&
                   (WhitespaceHandling)s_whitespaceHandling.GetValue(reader)! == WhitespaceHandling.None;
        }
    }
}
