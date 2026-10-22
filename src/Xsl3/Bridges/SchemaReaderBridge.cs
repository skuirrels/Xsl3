// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Xml.Schema;

namespace System.Xml;

/// <summary>The schema reader's partial-type constructor, needed for a processor-stipulated source type.</summary>
internal static class SchemaReaderBridge
{
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors)]
    private static readonly Type? s_reader = Type.GetType("System.Xml.XsdValidatingReader, System.Private.Xml");
    private static readonly ConstructorInfo? s_partial = s_reader?.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
        new[] { typeof(XmlReader), typeof(XmlResolver), typeof(XmlReaderSettings), typeof(XmlSchemaObject) }, null);

    internal static XmlReader Create(XmlReader input, XmlReaderSettings settings, XmlSchemaType? type)
    {
        if (s_partial == null) throw new NotSupportedException("The XML runtime has no partial-type validating reader");
        try { return (XmlReader)s_partial.Invoke(new object?[] { input, null, settings, type }); }
        catch (TargetInvocationException e) when (e.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(e.InnerException).Throw();
            throw;
        }
    }
}
