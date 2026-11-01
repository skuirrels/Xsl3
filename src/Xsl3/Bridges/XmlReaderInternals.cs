// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace System.Xml
{
    /// <summary>
    /// Reads state of System.Private.Xml's internal XmlTextReaderImpl (the reader behind XmlReader.Create
    /// and XmlTextReader) that upstream's QueryReaderSettings and WhitespaceRuleReader inspect directly.
    /// </summary>
    /// <remarks>
    /// .NET 10 binds inaccessible reference types with UnsafeAccessorType; .NET 8 retains reflection.
    /// This runs once per transformation or document() load, never per node. See docs/internals.md.
    /// </remarks>
    internal static class XmlReaderInternals
    {
        private const string Asm = "System.Private.Xml";
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        private const string ImplTypeName = "System.Xml.XmlTextReaderImpl, System.Private.Xml";
        private static readonly Type? s_implType = Type.GetType(ImplTypeName);
#if NET10_0_OR_GREATER
        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "GetResolver")]
        private static extern XmlResolver? GetResolverCore([UnsafeAccessorType(ImplTypeName)] object reader);
        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_WhitespaceHandling")]
        private static extern WhitespaceHandling GetWhitespaceHandlingCore([UnsafeAccessorType(ImplTypeName)] object reader);
        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_Impl")]
        [return: UnsafeAccessorType(ImplTypeName)]
        private static extern object? GetImplementationCore(XmlTextReader reader);
#else
        // Keep the literal type identity at each lookup so trimming preserves the requested
        // members, rather than every method and property on the XML parser implementation.
        private static readonly MethodInfo? s_getResolver = Type.GetType(ImplTypeName)?.GetMethod("GetResolver", Instance, Type.EmptyTypes);
        private static readonly PropertyInfo? s_whitespaceHandling = Type.GetType(ImplTypeName)?.GetProperty("WhitespaceHandling", Instance);
        private static readonly PropertyInfo? s_textReaderImpl = typeof(XmlTextReader).GetProperty("Impl", Instance);

#endif

        /// <summary>Upstream: <c>(reader as XmlTextReaderImpl)?.GetResolver()</c>.</summary>
#if !NET10_0_OR_GREATER
        [DynamicDependency("GetResolver", "System.Xml.XmlTextReaderImpl", Asm)]
#endif
        public static bool TryGetTextReaderImplResolver(XmlReader reader, out XmlResolver? resolver)
        {
#if NET10_0_OR_GREATER
            if (s_implType != null && s_implType.IsInstanceOfType(reader))
            {
                try { resolver = GetResolverCore(reader); return true; }
                catch (MissingMethodException) { }
            }
#else
            if (s_implType != null && s_implType.IsInstanceOfType(reader) && s_getResolver != null)
            {
                resolver = (XmlResolver?)s_getResolver.Invoke(reader, null);
                return true;
            }
#endif

            resolver = null;
            return false;
        }

        /// <summary>Upstream: <c>xmlTextReader.Impl.GetResolver()</c>.</summary>
#if !NET10_0_OR_GREATER
        [DynamicDependency("Impl", typeof(XmlTextReader))]
#endif
#if !NET10_0_OR_GREATER
        [DynamicDependency("GetResolver", "System.Xml.XmlTextReaderImpl", Asm)]
#endif
        public static XmlResolver? GetResolver(XmlTextReader reader)
        {
#if NET10_0_OR_GREATER
            try
            {
                object? impl = GetImplementationCore(reader);
                return impl == null ? null : GetResolverCore(impl);
            }
            catch (MissingMethodException) { return null; }
#else
            object? impl = s_textReaderImpl?.GetValue(reader);
            return impl != null && s_getResolver != null ? (XmlResolver?)s_getResolver.Invoke(impl, null) : null;
#endif
        }

        /// <summary>Upstream: <c>reader is XmlTextReaderImpl impl &amp;&amp; impl.WhitespaceHandling == WhitespaceHandling.None</c>.</summary>
#if !NET10_0_OR_GREATER
        [DynamicDependency("WhitespaceHandling", "System.Xml.XmlTextReaderImpl", Asm)]
#endif
        public static bool IsTextReaderImplStrippingAllWhitespace(XmlReader reader)
        {
#if NET10_0_OR_GREATER
            if (s_implType == null || !s_implType.IsInstanceOfType(reader)) return false;
            try { return GetWhitespaceHandlingCore(reader) == WhitespaceHandling.None; }
            catch (MissingMethodException) { return false; }
#else
            return s_implType != null && s_implType.IsInstanceOfType(reader) && s_whitespaceHandling != null &&
                   (WhitespaceHandling)s_whitespaceHandling.GetValue(reader)! == WhitespaceHandling.None;
#endif
        }
    }
}
