// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Numerics;
using System.Xml.Schema;

namespace Xsl3.Xdm
{
    /// <summary>
    /// The primitive type an atomic type derives from, which decides its value space and its row in the
    /// casting table (F&amp;O 3.1 §19.1). xs:integer, the two duration subtypes and xs:untypedAtomic are listed
    /// as primitives because the casting and operator rules treat them as such.
    /// </summary>
    internal enum Primitive
    {
        AnyAtomic,
        UntypedAtomic,
        String,
        Boolean,
        Decimal,
        Integer,
        Float,
        Double,
        Duration,
        YearMonthDuration,
        DayTimeDuration,
        DateTime,
        Time,
        Date,
        GYearMonth,
        GYear,
        GMonthDay,
        GDay,
        GMonth,
        HexBinary,
        Base64Binary,
        AnyUri,
        QName,
        Notation,
    }

    /// <summary>The whiteSpace facet: how the lexical form is normalised before it is validated (XSD 1.0 §4.3.6).</summary>
    internal enum WhitespaceFacet { Preserve, Replace, Collapse }

    /// <summary>The lexical constraint of a type derived from xs:string.</summary>
    internal enum StringConstraint { None, NormalizedString, Token, Language, NmToken, Name, NCName }

    /// <summary>
    /// An atomic type (XPath 3.1 §2.7.2): a built-in one, a singleton in <see cref="AtomicTypes"/>, or one an imported
    /// schema defines (P6), one instance per schema type (Schema/SchemaTypes.cs). They compare by reference.
    /// </summary>
    internal sealed class AtomicType
    {
        internal AtomicType(string localName, AtomicType? baseType, Primitive primitive, XmlTypeCode typeCode)
        {
            Name = QName.Xs(localName);
            Base = baseType;
            Primitive = primitive;
            TypeCode = typeCode;
            Whitespace = primitive == Primitive.String || primitive == Primitive.UntypedAtomic ? WhitespaceFacet.Preserve : WhitespaceFacet.Collapse;
            // Facets are inherited by derived types (same primitive), not by the primitives from xs:anyAtomicType.
            if (baseType != null && baseType.Primitive != Primitive.AnyAtomic)
            {
                Whitespace = baseType.Whitespace;
                StringConstraint = baseType.StringConstraint;
                MinInclusive = baseType.MinInclusive;
                MaxInclusive = baseType.MaxInclusive;
            }
        }

        /// <summary>
        /// Xsl3 (P6): an atomic type a schema defines, derived by restriction from <paramref name="baseType"/>. Its own
        /// facets are checked by <paramref name="schemaType"/>; the inherited ones by the base type.
        /// </summary>
        internal AtomicType(QName name, AtomicType baseType, XmlSchemaSimpleType schemaType, WhitespaceFacet? whitespace)
            : this(baseType.Name.LocalName, baseType, baseType.Primitive, baseType.TypeCode)
        {
            Name = name;
            UserSchemaType = schemaType;
            RequiresTimezone = baseType.RequiresTimezone;
            if (whitespace is WhitespaceFacet ws)
            {
                Whitespace = ws;
            }
        }

        public QName Name { get; }

        /// <summary>Xsl3 (P6): the schema's definition of a type it defines; null for the built-in types.</summary>
        public XmlSchemaSimpleType? UserSchemaType { get; }

        /// <summary>Whether a schema defines this type (or one of its ancestors).</summary>
        public bool IsUserDefined => UserSchemaType != null;

        public AtomicType? Base { get; }

        public Primitive Primitive { get; }

        /// <summary>The matching System.Xml type code, or <see cref="XmlTypeCode.None"/> (e.g. xs:dateTimeStamp).</summary>
        public XmlTypeCode TypeCode { get; }

        public WhitespaceFacet Whitespace { get; internal set; }

        public StringConstraint StringConstraint { get; internal set; }

        /// <summary>Range facets of the integer types; null when unbounded.</summary>
        public BigInteger? MinInclusive { get; internal set; }

        public BigInteger? MaxInclusive { get; internal set; }

        /// <summary>xs:anyAtomicType and xs:NOTATION can't be cast to or constructed (XPST0080).</summary>
        public bool IsAbstract => Primitive is Primitive.AnyAtomic or Primitive.Notation && !IsUserDefined;

        /// <summary>xs:dateTimeStamp requires a timezone.</summary>
        public bool RequiresTimezone { get; internal set; }

        public bool IsNumeric => Primitive is Primitive.Decimal or Primitive.Integer or Primitive.Float or Primitive.Double;

        /// <summary>Whether this type is <paramref name="other"/> or derived from it.</summary>
        public bool DerivesFrom(AtomicType other)
        {
            for (AtomicType? t = this; t != null; t = t.Base)
            {
                // (Xsl3, P6: a schema's type may be met through two schema sets, the stylesheet's and the input's: the
                // same named type then has two instances.)
                if (ReferenceEquals(t, other) || t.IsUserDefined && other.IsUserDefined && t.Name.Equals(other.Name) &&
                    t.Name.NamespaceUri != Xsl3.Schema.SchemaTypes.AnonymousNamespace)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>The System.Xml schema type, used for <see cref="System.Xml.XPath.XPathItem.XmlType"/>.</summary>
        public XmlSchemaType? SchemaType => _schemaType ??= UserSchemaType ?? (TypeCode == XmlTypeCode.None ? XmlSchemaType.GetBuiltInSimpleType(XmlTypeCode.AnyAtomicType) : XmlSchemaType.GetBuiltInSimpleType(TypeCode));

        private XmlSchemaType? _schemaType;

        public override string ToString() => Name.ToString();
    }

    /// <summary>The built-in atomic types of XPath 3.1 §2.7.2 / XML Schema 1.1 Part 2 §3.</summary>
    internal static class AtomicTypes
    {
        public static readonly AtomicType AnyAtomicType = new("anyAtomicType", null, Primitive.AnyAtomic, XmlTypeCode.AnyAtomicType);
        public static readonly AtomicType UntypedAtomic = new("untypedAtomic", AnyAtomicType, Primitive.UntypedAtomic, XmlTypeCode.UntypedAtomic);

        public static readonly AtomicType String = new("string", AnyAtomicType, Primitive.String, XmlTypeCode.String);
        public static readonly AtomicType NormalizedString = new("normalizedString", String, Primitive.String, XmlTypeCode.NormalizedString) { Whitespace = WhitespaceFacet.Replace, StringConstraint = StringConstraint.NormalizedString };
        public static readonly AtomicType Token = new("token", NormalizedString, Primitive.String, XmlTypeCode.Token) { Whitespace = WhitespaceFacet.Collapse, StringConstraint = StringConstraint.Token };
        public static readonly AtomicType Language = new("language", Token, Primitive.String, XmlTypeCode.Language) { StringConstraint = StringConstraint.Language };
        public static readonly AtomicType NMTOKEN = new("NMTOKEN", Token, Primitive.String, XmlTypeCode.NmToken) { StringConstraint = StringConstraint.NmToken };
        public static readonly AtomicType Name = new("Name", Token, Primitive.String, XmlTypeCode.Name) { StringConstraint = StringConstraint.Name };
        public static readonly AtomicType NCName = new("NCName", Name, Primitive.String, XmlTypeCode.NCName) { StringConstraint = StringConstraint.NCName };
        public static readonly AtomicType ID = new("ID", NCName, Primitive.String, XmlTypeCode.Id);
        public static readonly AtomicType IDREF = new("IDREF", NCName, Primitive.String, XmlTypeCode.Idref);
        public static readonly AtomicType ENTITY = new("ENTITY", NCName, Primitive.String, XmlTypeCode.Entity);

        public static readonly AtomicType Boolean = new("boolean", AnyAtomicType, Primitive.Boolean, XmlTypeCode.Boolean);

        public static readonly AtomicType Decimal = new("decimal", AnyAtomicType, Primitive.Decimal, XmlTypeCode.Decimal);
        public static readonly AtomicType Integer = new("integer", Decimal, Primitive.Integer, XmlTypeCode.Integer);
        public static readonly AtomicType NonPositiveInteger = new("nonPositiveInteger", Integer, Primitive.Integer, XmlTypeCode.NonPositiveInteger) { MaxInclusive = 0 };
        public static readonly AtomicType NegativeInteger = new("negativeInteger", NonPositiveInteger, Primitive.Integer, XmlTypeCode.NegativeInteger) { MaxInclusive = -1 };
        public static readonly AtomicType Long = new("long", Integer, Primitive.Integer, XmlTypeCode.Long) { MinInclusive = long.MinValue, MaxInclusive = long.MaxValue };
        public static readonly AtomicType Int = new("int", Long, Primitive.Integer, XmlTypeCode.Int) { MinInclusive = int.MinValue, MaxInclusive = int.MaxValue };
        public static readonly AtomicType Short = new("short", Int, Primitive.Integer, XmlTypeCode.Short) { MinInclusive = short.MinValue, MaxInclusive = short.MaxValue };
        public static readonly AtomicType Byte = new("byte", Short, Primitive.Integer, XmlTypeCode.Byte) { MinInclusive = sbyte.MinValue, MaxInclusive = sbyte.MaxValue };
        public static readonly AtomicType NonNegativeInteger = new("nonNegativeInteger", Integer, Primitive.Integer, XmlTypeCode.NonNegativeInteger) { MinInclusive = 0 };
        public static readonly AtomicType UnsignedLong = new("unsignedLong", NonNegativeInteger, Primitive.Integer, XmlTypeCode.UnsignedLong) { MaxInclusive = ulong.MaxValue };
        public static readonly AtomicType UnsignedInt = new("unsignedInt", UnsignedLong, Primitive.Integer, XmlTypeCode.UnsignedInt) { MaxInclusive = uint.MaxValue };
        public static readonly AtomicType UnsignedShort = new("unsignedShort", UnsignedInt, Primitive.Integer, XmlTypeCode.UnsignedShort) { MaxInclusive = ushort.MaxValue };
        public static readonly AtomicType UnsignedByte = new("unsignedByte", UnsignedShort, Primitive.Integer, XmlTypeCode.UnsignedByte) { MaxInclusive = byte.MaxValue };
        public static readonly AtomicType PositiveInteger = new("positiveInteger", NonNegativeInteger, Primitive.Integer, XmlTypeCode.PositiveInteger) { MinInclusive = 1 };

        public static readonly AtomicType Float = new("float", AnyAtomicType, Primitive.Float, XmlTypeCode.Float);
        public static readonly AtomicType Double = new("double", AnyAtomicType, Primitive.Double, XmlTypeCode.Double);

        public static readonly AtomicType Duration = new("duration", AnyAtomicType, Primitive.Duration, XmlTypeCode.Duration);
        public static readonly AtomicType YearMonthDuration = new("yearMonthDuration", Duration, Primitive.YearMonthDuration, XmlTypeCode.YearMonthDuration);
        public static readonly AtomicType DayTimeDuration = new("dayTimeDuration", Duration, Primitive.DayTimeDuration, XmlTypeCode.DayTimeDuration);

        public static readonly AtomicType DateTime = new("dateTime", AnyAtomicType, Primitive.DateTime, XmlTypeCode.DateTime);
        public static readonly AtomicType DateTimeStamp = new("dateTimeStamp", DateTime, Primitive.DateTime, XmlTypeCode.None) { RequiresTimezone = true };
        public static readonly AtomicType Time = new("time", AnyAtomicType, Primitive.Time, XmlTypeCode.Time);
        public static readonly AtomicType Date = new("date", AnyAtomicType, Primitive.Date, XmlTypeCode.Date);
        public static readonly AtomicType GYearMonth = new("gYearMonth", AnyAtomicType, Primitive.GYearMonth, XmlTypeCode.GYearMonth);
        public static readonly AtomicType GYear = new("gYear", AnyAtomicType, Primitive.GYear, XmlTypeCode.GYear);
        public static readonly AtomicType GMonthDay = new("gMonthDay", AnyAtomicType, Primitive.GMonthDay, XmlTypeCode.GMonthDay);
        public static readonly AtomicType GDay = new("gDay", AnyAtomicType, Primitive.GDay, XmlTypeCode.GDay);
        public static readonly AtomicType GMonth = new("gMonth", AnyAtomicType, Primitive.GMonth, XmlTypeCode.GMonth);

        public static readonly AtomicType HexBinary = new("hexBinary", AnyAtomicType, Primitive.HexBinary, XmlTypeCode.HexBinary);
        public static readonly AtomicType Base64Binary = new("base64Binary", AnyAtomicType, Primitive.Base64Binary, XmlTypeCode.Base64Binary);
        public static readonly AtomicType AnyUri = new("anyURI", AnyAtomicType, Primitive.AnyUri, XmlTypeCode.AnyUri);
        public static readonly AtomicType QName = new("QName", AnyAtomicType, Primitive.QName, XmlTypeCode.QName);
        public static readonly AtomicType Notation = new("NOTATION", AnyAtomicType, Primitive.Notation, XmlTypeCode.Notation);

        public static IReadOnlyList<AtomicType> All { get; } = new[]
        {
            AnyAtomicType, UntypedAtomic, String, NormalizedString, Token, Language, NMTOKEN, Name, NCName, ID, IDREF, ENTITY,
            Boolean, Decimal, Integer, NonPositiveInteger, NegativeInteger, Long, Int, Short, Byte, NonNegativeInteger,
            UnsignedLong, UnsignedInt, UnsignedShort, UnsignedByte, PositiveInteger, Float, Double,
            Duration, YearMonthDuration, DayTimeDuration, DateTime, DateTimeStamp, Time, Date,
            GYearMonth, GYear, GMonthDay, GDay, GMonth, HexBinary, Base64Binary, AnyUri, QName, Notation,
        };

        private static readonly Dictionary<string, AtomicType> s_byLocalName = BuildIndex();

        private static Dictionary<string, AtomicType> BuildIndex()
        {
            var index = new Dictionary<string, AtomicType>(System.StringComparer.Ordinal);
            foreach (AtomicType t in All)
            {
                index.Add(t.Name.LocalName, t);
            }
            return index;
        }

        /// <summary>The built-in atomic type with this expanded name, or null.</summary>
        public static AtomicType? Find(string namespaceUri, string localName) =>
            namespaceUri == Xdm.QName.Ns.Xs && s_byLocalName.TryGetValue(localName, out AtomicType? t) ? t : null;

        /// <summary>The type of a primitive's values when nothing more specific is known.</summary>
        public static AtomicType Of(Primitive primitive) => primitive switch
        {
            Primitive.AnyAtomic => AnyAtomicType,
            Primitive.UntypedAtomic => UntypedAtomic,
            Primitive.String => String,
            Primitive.Boolean => Boolean,
            Primitive.Decimal => Decimal,
            Primitive.Integer => Integer,
            Primitive.Float => Float,
            Primitive.Double => Double,
            Primitive.Duration => Duration,
            Primitive.YearMonthDuration => YearMonthDuration,
            Primitive.DayTimeDuration => DayTimeDuration,
            Primitive.DateTime => DateTime,
            Primitive.Time => Time,
            Primitive.Date => Date,
            Primitive.GYearMonth => GYearMonth,
            Primitive.GYear => GYear,
            Primitive.GMonthDay => GMonthDay,
            Primitive.GDay => GDay,
            Primitive.GMonth => GMonth,
            Primitive.HexBinary => HexBinary,
            Primitive.Base64Binary => Base64Binary,
            Primitive.AnyUri => AnyUri,
            Primitive.QName => QName,
            _ => Notation,
        };
    }
}
