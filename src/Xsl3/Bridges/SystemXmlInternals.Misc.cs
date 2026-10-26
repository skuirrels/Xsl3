// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;
using System.Runtime.CompilerServices;

// More bridges for internal members of public System.Xml types. See SystemXmlInternals.cs for the approach.

namespace System.Xml.Schema
{
    // Built-in schema types are compared by reference throughout XmlQueryTypeFactory, so these must be
    // the very instances System.Private.Xml uses. Each comes from the public lookup that upstream's own
    // implementation answers with that singleton (see XmlSchemaType.GetBuiltIn*Type and
    // DatatypeImplementation.CreateBuiltinTypes).
    internal static class XmlSchemaComplexTypeInternals
    {
        private static readonly XmlSchemaComplexType s_anyType = XmlSchemaType.GetBuiltInComplexType(XmlTypeCode.Item)!;
        private static readonly XmlSchemaComplexType s_untypedAnyType = XmlSchemaType.GetBuiltInComplexType(new XmlQualifiedName("untypedAny", XmlReservedNs.NsXQueryDataType))!;

        extension(XmlSchemaComplexType)
        {
            internal static XmlSchemaComplexType AnyType => s_anyType;

            internal static XmlSchemaComplexType UntypedAnyType => s_untypedAnyType;
        }
    }

    /// <summary>Stands in for the built-in type accessors of System.Private.Xml's internal DatatypeImplementation.</summary>
    internal static class DatatypeImplementation
    {
        internal static XmlSchemaSimpleType AnySimpleType { get; } = XmlSchemaType.GetBuiltInSimpleType(new XmlQualifiedName("anySimpleType", XmlReservedNs.NsXs))!;

        internal static XmlSchemaSimpleType AnyAtomicType { get; } = XmlSchemaType.GetBuiltInSimpleType(XmlTypeCode.AnyAtomicType);

        internal static XmlSchemaSimpleType UntypedAtomicType { get; } = XmlSchemaType.GetBuiltInSimpleType(XmlTypeCode.UntypedAtomic);
    }
}

namespace System.Xml
{
    internal enum ExceptionType
    {
        ArgumentException,
        XmlException,
    }

    internal static class XmlConvertInternals
    {
        private static readonly char[] s_whitespaceChars = new char[] { ' ', '\t', '\n', '\r' };

        extension(XmlConvert)
        {
            internal static Uri ToUri(string? s)
            {
                if (!string.IsNullOrEmpty(s))
                {
                    // string.Empty is a valid uri but not "   "
                    s = s.Trim(s_whitespaceChars);
                    if (s.Length == 0 || s.Contains("##"))
                    {
                        throw new FormatException(SR.Format(SR.XmlConvert_BadFormat, s, "Uri"));
                    }
                }

                Uri? uri;
                if (!Uri.TryCreate(s, UriKind.RelativeOrAbsolute, out uri))
                {
                    throw new FormatException(SR.Format(SR.XmlConvert_BadFormat, s, "Uri"));
                }

                return uri;
            }

            internal static Exception CreateException(string res, string arg, ExceptionType exceptionType, int lineNo = 0, int linePos = 0)
            {
                switch (exceptionType)
                {
                    case ExceptionType.ArgumentException:
                        return new ArgumentException(string.Format(res, arg));
                    case ExceptionType.XmlException:
                    default:
                        return XmlExceptionInternals.Create(res, arg, lineNo, linePos);
                }
            }

            internal static Exception CreateException(string res, string[] args, ExceptionType exceptionType, int lineNo = 0, int linePos = 0)
            {
                switch (exceptionType)
                {
                    case ExceptionType.ArgumentException:
                        return new ArgumentException(string.Format(res, args));
                    case ExceptionType.XmlException:
                    default:
                        return XmlExceptionInternals.Create(res, args, lineNo, linePos);
                }
            }

            internal static Exception CreateInvalidSurrogatePairException(char low, char hi, ExceptionType exceptionType = ExceptionType.ArgumentException, int lineNo = 0, int linePos = 0)
            {
                string[] args = new string[] {
                    ((uint)hi).ToString("X", CultureInfo.InvariantCulture),
                    ((uint)low).ToString("X", CultureInfo.InvariantCulture)
                };
                return XmlConvert.CreateException(SR.Xml_InvalidSurrogatePairWithArgs, args, exceptionType, lineNo, linePos);
            }

            internal static Exception CreateInvalidHighSurrogateCharException(char hi, ExceptionType exceptionType = ExceptionType.ArgumentException, int lineNo = 0, int linePos = 0)
            {
                return XmlConvert.CreateException(SR.Xml_InvalidSurrogateHighChar, ((uint)hi).ToString("X", CultureInfo.InvariantCulture), exceptionType, lineNo, linePos);
            }

            internal static Exception CreateInvalidCharException(char[] data, int length, int invCharPos, ExceptionType exceptionType)
            {
                return XmlConvert.CreateException(SR.Xml_InvalidCharacter, XmlException.BuildCharExceptionArgs(data, length, invCharPos), exceptionType, 0, invCharPos + 1);
            }

            internal static Exception CreateInvalidCharException(string data, int invCharPos, ExceptionType exceptionType = ExceptionType.ArgumentException)
            {
                return XmlConvert.CreateException(SR.Xml_InvalidCharacter, XmlException.BuildCharExceptionArgs(data, invCharPos), exceptionType, 0, invCharPos + 1);
            }

            internal static Exception CreateInvalidCharException(char invChar, char nextChar, ExceptionType exceptionType = ExceptionType.ArgumentException)
            {
                return XmlConvert.CreateException(SR.Xml_InvalidCharacter, XmlException.BuildCharExceptionArgs(invChar, nextChar), exceptionType);
            }

            internal static string[] SplitString(string value)
            {
                return value.Split(s_whitespaceChars, StringSplitOptions.RemoveEmptyEntries);
            }

            internal static string[] SplitString(string value, StringSplitOptions splitStringOptions)
            {
                return value.Split(s_whitespaceChars, splitStringOptions);
            }

            internal static Exception? TryToDouble(string s, out double result)
            {
                ReadOnlySpan<char> value = s.AsSpan().Trim(s_whitespaceChars);
                switch (value)
                {
                    case "-INF":
                        result = double.NegativeInfinity;
                        return null;
                    case "INF":
                        result = double.PositiveInfinity;
                        return null;
                    default:
                        if (!double.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, NumberFormatInfo.InvariantInfo, out result))
                        {
                            return new FormatException(SR.Format(SR.XmlConvert_BadFormat, s, "Double"));
                        }

                        if (result == 0 && value[0] == '-')
                        {
                            result = -0d;
                        }

                        return null;
                }
            }
        }
    }

    internal static class XmlQualifiedNameInternals
    {
        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "Init")]
        private static extern void StockInit(XmlQualifiedName qname, string? name, string? ns);

        extension(XmlQualifiedName qname)
        {
            // Mutates the name in place (it also resets the cached hash code), which is why it can't be copied.
            internal void Init(string? name, string? ns) => StockInit(qname, name, ns);

            internal void Verify()
            {
                XmlConvert.VerifyNCName(qname.Name);
                if (qname.Namespace.Length != 0)
                {
                    XmlConvert.ToUri(qname.Namespace);
                }
            }
        }
    }

    internal static class XmlReaderSettingsInternals
    {
        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "GetXmlResolver_CheckConfig")]
        internal static extern XmlResolver? GetEffectiveResolver(XmlReaderSettings settings);

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "GetDefaultPermissiveResolver")]
        private static extern XmlResolver StockGetDefaultPermissiveResolver(XmlReaderSettings? _);

        extension(XmlReaderSettings)
        {
            // Delegates to System.Private.Xml so the stock Switch.System.Xml.* AppContext switches keep applying.
            internal static XmlResolver GetDefaultPermissiveResolver() => StockGetDefaultPermissiveResolver(null);
        }
    }

    /// <summary>
    /// Creates <see cref="XmlException"/>s through its internal constructors, which format the message from a
    /// resource string and arguments. The overloads mirror the public and internal constructors one to one, so
    /// replacing <c>new XmlException(...)</c> with <c>XmlExceptionInternals.Create(...)</c> binds the same way.
    /// </summary>
    internal static class XmlExceptionInternals
    {
        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "BuildCharExceptionArgs")]
        private static extern string[] StockBuildCharExceptionArgs(XmlException? _, string data, int invCharIndex);
        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "BuildCharExceptionArgs")]
        private static extern string[] StockBuildCharExceptionArgs(XmlException? _, char[] data, int length, int invCharIndex);
        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "BuildCharExceptionArgs")]
        private static extern string[] StockBuildCharExceptionArgs(XmlException? _, char invChar, char nextChar);

        extension(XmlException)
        {
            internal static bool IsCatchableException(Exception e)
            {
                System.Diagnostics.Debug.Assert(e != null, "Unexpected null exception");
                return !(
                    e is OutOfMemoryException ||
                    e is NullReferenceException
                );
            }

            internal static string[] BuildCharExceptionArgs(string data, int invCharIndex) => StockBuildCharExceptionArgs(null, data, invCharIndex);
            internal static string[] BuildCharExceptionArgs(char[] data, int length, int invCharIndex) => StockBuildCharExceptionArgs(null, data, length, invCharIndex);
            internal static string[] BuildCharExceptionArgs(char invChar, char nextChar) => StockBuildCharExceptionArgs(null, invChar, nextChar);
        }

        // Public constructors
        internal static XmlException Create() => new XmlException();
        internal static XmlException Create(string? message) => new XmlException(message);
        internal static XmlException Create(string? message, Exception? innerException) => new XmlException(message, innerException);
        internal static XmlException Create(string? message, Exception? innerException, int lineNumber, int linePosition) => new XmlException(message, innerException, lineNumber, linePosition);

        // Internal constructors (each confirmed present in the shipped, trimmed System.Private.Xml on .NET 8 and 10)
        [UnsafeAccessor(UnsafeAccessorKind.Constructor)] internal static extern XmlException Create(string message, Exception? innerException, int lineNumber, int linePosition, string? sourceUri);
        [UnsafeAccessor(UnsafeAccessorKind.Constructor)] internal static extern XmlException Create(string res, string[]? args);
        [UnsafeAccessor(UnsafeAccessorKind.Constructor)] internal static extern XmlException Create(string res, string? arg);
        [UnsafeAccessor(UnsafeAccessorKind.Constructor)] internal static extern XmlException Create(string res, string? arg, string? sourceUri);
        [UnsafeAccessor(UnsafeAccessorKind.Constructor)] internal static extern XmlException Create(string res, string? arg, IXmlLineInfo? lineInfo);
        [UnsafeAccessor(UnsafeAccessorKind.Constructor)] internal static extern XmlException Create(string res, string? arg, Exception? innerException, IXmlLineInfo? lineInfo);
        [UnsafeAccessor(UnsafeAccessorKind.Constructor)] internal static extern XmlException Create(string res, string[]? args, IXmlLineInfo? lineInfo);
        [UnsafeAccessor(UnsafeAccessorKind.Constructor)] internal static extern XmlException Create(string res, string[]? args, IXmlLineInfo? lineInfo, string? sourceUri);
        [UnsafeAccessor(UnsafeAccessorKind.Constructor)] internal static extern XmlException Create(string res, string? arg, int lineNumber, int linePosition);
        [UnsafeAccessor(UnsafeAccessorKind.Constructor)] internal static extern XmlException Create(string res, string? arg, int lineNumber, int linePosition, string? sourceUri);
        [UnsafeAccessor(UnsafeAccessorKind.Constructor)] internal static extern XmlException Create(string res, string[]? args, int lineNumber, int linePosition);
        [UnsafeAccessor(UnsafeAccessorKind.Constructor)] internal static extern XmlException Create(string res, string[]? args, int lineNumber, int linePosition, string? sourceUri);
        [UnsafeAccessor(UnsafeAccessorKind.Constructor)] internal static extern XmlException Create(string res, string[]? args, Exception? innerException, int lineNumber, int linePosition);
        [UnsafeAccessor(UnsafeAccessorKind.Constructor)] internal static extern XmlException Create(string res, string[]? args, Exception? innerException, int lineNumber, int linePosition, string? sourceUri);
    }
}
