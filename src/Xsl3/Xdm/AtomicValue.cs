// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Numerics;
using System.Xml.Schema;
using System.Xml.XPath;

namespace Xsl3.Xdm
{
    /// <summary>
    /// An XDM atomic value (SPEC §8.2), boxed. It is an <see cref="XPathItem"/> so that it flows through the
    /// engine's existing <c>item</c> and <c>item*</c> storage (docs/plan/p1-xdm-xpath.md, decision 2).
    /// Subclasses hold the value in its natural CLR form; <see cref="Type"/> carries the (possibly derived)
    /// atomic type. Values are immutable.
    /// </summary>
    internal abstract class AtomicValue : XPathItem
    {
        protected AtomicValue(AtomicType type)
        {
            Type = type;
        }

        public AtomicType Type { get; }

        // XPathItem.Value is the string value: the result of casting to xs:string (F&O 3.1 §19.1.2).

        /// <summary>The value as a CLR object (string, bool, BigInteger, decimal, double, …).</summary>
        public abstract object ClrValue { get; }

        // XPathItem
        public sealed override bool IsNode => false;
        public sealed override XmlSchemaType? XmlType => Type.SchemaType;
        public sealed override string ToString() => Value;
        public override object TypedValue => ClrValue;
        public override Type ValueType => ClrValue.GetType();
        public override bool ValueAsBoolean => Casting.Cast(this, AtomicTypes.Boolean).AsBoolean();
        public override DateTime ValueAsDateTime => throw new InvalidCastException($"{Type.Name} has no System.DateTime form");
        public override double ValueAsDouble => ((DoubleValue)Casting.Cast(this, AtomicTypes.Double)).Double;
        public override int ValueAsInt => (int)ValueAsLong;
        public override long ValueAsLong => (long)((IntegerValue)Casting.Cast(this, AtomicTypes.Integer)).Integer;

        public override object ValueAs(Type returnType, IXmlNamespaceResolver? nsResolver)
        {
            if (returnType == typeof(string)) return Value;
            if (returnType.IsInstanceOfType(this)) return this;
            if (returnType.IsInstanceOfType(ClrValue)) return ClrValue;
            if (returnType == typeof(bool)) return ValueAsBoolean;
            if (returnType == typeof(double)) return ValueAsDouble;
            if (returnType == typeof(long)) return ValueAsLong;
            if (returnType == typeof(int)) return ValueAsInt;
            throw new InvalidCastException($"Cannot convert {Type.Name} to {returnType}");
        }

        internal bool AsBoolean() => ((BooleanValue)this).Boolean;

        /// <summary>Xsl3 (P6): the same value labelled with a type derived from its own (facets already checked).</summary>
        internal virtual AtomicValue WithType(AtomicType type) => throw new InvalidOperationException($"{Type.Name} can't be relabelled");

        // Convenience constructors.
        public static StringValue OfString(string s) => new(AtomicTypes.String, s);
        public static StringValue OfUntyped(string s) => new(AtomicTypes.UntypedAtomic, s);
        public static BooleanValue OfBoolean(bool b) => b ? BooleanValue.True : BooleanValue.False;
        public static IntegerValue OfInteger(BigInteger i) => new(AtomicTypes.Integer, i);
        public static DecimalValue OfDecimal(decimal d) => new(AtomicTypes.Decimal, d);
        public static DoubleValue OfDouble(double d) => new(d);
        public static FloatValue OfFloat(float f) => new(f);
    }

    /// <summary>xs:string and its subtypes, xs:untypedAtomic and xs:anyURI.</summary>
    internal sealed class StringValue : AtomicValue
    {
        internal override AtomicValue WithType(AtomicType type) => new StringValue(type, String);

        public StringValue(AtomicType type, string value) : base(type)
        {
            String = value;
        }

        public string String { get; }

        public override string Value => String;
        public override object ClrValue => String;
    }

    internal sealed class BooleanValue : AtomicValue
    {
        internal BooleanValue(AtomicType type, bool value) : base(type)
        {
            Boolean = value;
        }

        internal override AtomicValue WithType(AtomicType type) => new BooleanValue(type, Boolean);

        public static readonly BooleanValue True = new(true);
        public static readonly BooleanValue False = new(false);

        private BooleanValue(bool value) : base(AtomicTypes.Boolean)
        {
            Boolean = value;
        }

        public bool Boolean { get; }

        public override string Value => Boolean ? "true" : "false";
        public override object ClrValue => Boolean;
        public override bool ValueAsBoolean => Boolean;
    }

    /// <summary>xs:integer and its subtypes. Unbounded (SPEC §8.2 allows a long fast path; that is a P4 concern).</summary>
    internal sealed class IntegerValue : AtomicValue
    {
        internal override AtomicValue WithType(AtomicType type) => new IntegerValue(type, Integer);

        public IntegerValue(AtomicType type, BigInteger value) : base(type)
        {
            Integer = value;
        }

        public BigInteger Integer { get; }

        public override string Value => Integer.ToString(System.Globalization.CultureInfo.InvariantCulture);
        public override object ClrValue => Integer;
        public override long ValueAsLong => (long)Integer;
        public override double ValueAsDouble => Numeric.ToDouble(Integer);
    }

    internal sealed class DecimalValue : AtomicValue
    {
        internal override AtomicValue WithType(AtomicType type) => new DecimalValue(type, Decimal);

        public DecimalValue(AtomicType type, decimal value) : base(type)
        {
            Decimal = value;
        }

        public decimal Decimal { get; }

        public override string Value => Lexical.DecimalToString(Decimal);
        public override object ClrValue => Decimal;
        public override double ValueAsDouble => Numeric.ToDouble(Decimal);
    }

    internal sealed class DoubleValue : AtomicValue
    {
        internal DoubleValue(AtomicType type, double value) : base(type)
        {
            Double = value;
        }

        internal override AtomicValue WithType(AtomicType type) => new DoubleValue(type, Double);

        public DoubleValue(double value) : base(AtomicTypes.Double)
        {
            Double = value;
        }

        public double Double { get; }

        public override string Value => Lexical.DoubleToString(Double);
        public override object ClrValue => Double;
        public override double ValueAsDouble => Double;
    }

    internal sealed class FloatValue : AtomicValue
    {
        internal FloatValue(AtomicType type, float value) : base(type)
        {
            Float = value;
        }

        internal override AtomicValue WithType(AtomicType type) => new FloatValue(type, Float);

        public FloatValue(float value) : base(AtomicTypes.Float)
        {
            Float = value;
        }

        public float Float { get; }

        public override string Value => Lexical.FloatToString(Float);
        public override object ClrValue => Float;
        public override double ValueAsDouble => Float;
    }

    /// <summary>xs:duration, xs:yearMonthDuration, xs:dayTimeDuration.</summary>
    internal sealed class DurationValue : AtomicValue
    {
        internal override AtomicValue WithType(AtomicType type) => new DurationValue(type, Duration);

        public DurationValue(AtomicType type, XsDuration value) : base(type)
        {
            Duration = value;
        }

        public XsDuration Duration { get; }

        public override string Value => Duration.ToString(Type);
        public override object ClrValue => Duration;
    }

    /// <summary>The eight date/time types and xs:dateTimeStamp.</summary>
    internal sealed class DateTimeValue : AtomicValue
    {
        internal override AtomicValue WithType(AtomicType type) => new DateTimeValue(type, DateTime);

        public DateTimeValue(AtomicType type, XsDateTime value) : base(type)
        {
            DateTime = value;
        }

        public XsDateTime DateTime { get; }

        public override string Value => DateTime.ToString(Type.Primitive);
        public override object ClrValue => DateTime;
    }

    /// <summary>xs:hexBinary and xs:base64Binary.</summary>
    internal sealed class BinaryValue : AtomicValue
    {
        internal override AtomicValue WithType(AtomicType type) => new BinaryValue(type, _bytes);

        private readonly byte[] _bytes;

        public BinaryValue(AtomicType type, byte[] bytes) : base(type)
        {
            _bytes = bytes;
        }

        public ReadOnlySpan<byte> Bytes => _bytes;

        public override string Value => Type.Primitive == Primitive.HexBinary ? Lexical.HexToString(_bytes) : Convert.ToBase64String(_bytes);
        public override object ClrValue => _bytes.Clone();
    }

    /// <summary>xs:QName and xs:NOTATION.</summary>
    internal sealed class QNameValue : AtomicValue
    {
        internal override AtomicValue WithType(AtomicType type) => new QNameValue(type, QName);

        public QNameValue(AtomicType type, QName value) : base(type)
        {
            QName = value;
        }

        public QName QName { get; }

        public override string Value => QName.ToString();
        public override object ClrValue => QName;
    }
}
