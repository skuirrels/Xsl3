// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using System.Xml.Schema;
using System.Xml.XPath;

namespace Xsl3.Xdm
{
    /// <summary>
    /// A function item (XPath 3.1 §2.8.1): a map, an array, or a function proper. Like atomic values, function
    /// items are <see cref="XPathItem"/>s so that they travel through the engine's boxed <c>item*</c> values
    /// (docs/plan/p3-xslt30.md, decision 1). They have no string value (FOTY0014) and no typed value (FOTY0013).
    /// </summary>
    internal abstract class XdmFunction : XPathItem
    {
        public abstract int Arity { get; }

        /// <summary>The function's name, or null for an anonymous function (an inline function, a map, …).</summary>
        public virtual QName? Name => null;

        /// <summary>The declared parameter types (item()* where undeclared).</summary>
        public abstract SequenceTypeTest ParameterType(int index);

        public abstract SequenceTypeTest ReturnType { get; }

        /// <summary>A short description for error messages.</summary>
        public abstract string Describe();

        public sealed override bool IsNode => false;
        public override XmlSchemaType? XmlType => null;
        public override string Value => throw new XdmException("FOTY0014", $"{Describe()} has no string value");
        public override object TypedValue => throw new XdmException("FOTY0013", $"{Describe()} can't be atomized");
        public override Type ValueType => typeof(XdmFunction);
        public override bool ValueAsBoolean => throw new XdmException("FORG0006", $"{Describe()} has no effective boolean value");
        public override DateTime ValueAsDateTime => throw new InvalidCastException();
        public override double ValueAsDouble => throw new InvalidCastException();
        public override int ValueAsInt => throw new InvalidCastException();
        public override long ValueAsLong => throw new InvalidCastException();
        public override object ValueAs(Type returnType, System.Xml.IXmlNamespaceResolver? nsResolver) =>
            returnType.IsInstanceOfType(this) ? this : throw new InvalidCastException($"Cannot convert {Describe()} to {returnType}");
        public override string ToString() => Describe();
    }

    /// <summary>
    /// A map key under op:same-key (XPath 3.1 §17.1.1): numbers by mathematical value (NaN equal to NaN),
    /// strings, URIs and untyped values by code points, dates and times by instant (with or without a timezone
    /// both), other values by their own equality.
    /// </summary>
    internal readonly struct MapKey : IEquatable<MapKey>
    {
        public MapKey(AtomicValue value)
        {
            Value = value;
            (Kind, Normal) = Normalize(value);
        }

        public AtomicValue Value { get; }

        private int Kind { get; }

        private object Normal { get; }

        private static (int, object) Normalize(AtomicValue v)
        {
            switch (v)
            {
                case IntegerValue i:
                    return (1, (decimal?)TryDecimal(i.Integer) ?? (object)i.Integer);
                case DecimalValue d:
                    return (1, d.Decimal);
                case DoubleValue d:
                    return (1, NumberNormal(d.Double));
                case FloatValue f:
                    return (1, NumberNormal(f.Float));
            }
            switch (v.Type.Primitive)
            {
                case Primitive.String or Primitive.UntypedAtomic or Primitive.AnyUri:
                    return (2, v.Value);
                case Primitive.Boolean:
                    return (3, ((BooleanValue)v).Boolean);
                case Primitive.DateTime or Primitive.Date or Primitive.Time or Primitive.GYear or Primitive.GYearMonth or Primitive.GMonth
                    or Primitive.GMonthDay or Primitive.GDay:
                    var dt = (DateTimeValue)v;
                    return (10 + (int)v.Type.Primitive, (dt.DateTime.TimezoneMinutes.HasValue, dt.DateTime.ToTimeline(v.Type.Primitive, 0)));
                case Primitive.Duration or Primitive.YearMonthDuration or Primitive.DayTimeDuration:
                    var du = ((DurationValue)v).Duration;
                    return (4, (du.Months, du.Seconds));
                default:
                    return (40 + (int)v.Type.Primitive, v.Value);
            }
        }

        private static decimal? TryDecimal(BigInteger i) => i >= (BigInteger)decimal.MinValue && i <= (BigInteger)decimal.MaxValue ? (decimal)i : null;

        private static object NumberNormal(double d)
        {
            if (double.IsNaN(d))
            {
                return double.NaN;
            }
            if (double.IsInfinity(d) || d != Math.Floor(d) && Math.Abs(d) > 1e28 || Math.Abs(d) > 7.9e28)
            {
                return d;
            }
            // A double equals a decimal when their mathematical values are equal: compare as decimal when exact.
            decimal m = (decimal)d;
            return (double)m == d ? m : d;
        }

        public bool Equals(MapKey other) => Kind == other.Kind && Equals(Normal, other.Normal);

        public override bool Equals(object? obj) => obj is MapKey k && Equals(k);

        public override int GetHashCode() => HashCode.Combine(Kind, Normal);
    }

    /// <summary>
    /// An XDM map (XPath 3.1 §2.8.4): immutable; updates share structure with the original. Entries come back in
    /// the order they were added (the order is implementation-dependent; this one is at least deterministic).
    /// </summary>
    internal sealed class XdmMap : XdmFunction
    {
        private readonly record struct Entry(AtomicValue Key, IList<XPathItem> Value, long Order);

        private static long s_order;

        public static readonly XdmMap Empty = new(ImmutableDictionary<MapKey, Entry>.Empty);

        private readonly ImmutableDictionary<MapKey, Entry> _entries;

        private XdmMap(ImmutableDictionary<MapKey, Entry> entries)
        {
            _entries = entries;
        }

        public int Count => _entries.Count;

        public IEnumerable<AtomicValue> Keys => Ordered.Select(e => e.Key);

        public IEnumerable<KeyValuePair<AtomicValue, IList<XPathItem>>> Entries => Ordered.Select(e => new KeyValuePair<AtomicValue, IList<XPathItem>>(e.Key, e.Value));

        private IEnumerable<Entry> Ordered => _entries.Count <= 1 ? _entries.Values : _entries.Values.OrderBy(e => e.Order);

        public bool ContainsKey(AtomicValue key) => _entries.ContainsKey(new MapKey(key));

        public IList<XPathItem>? Get(AtomicValue key) => _entries.TryGetValue(new MapKey(key), out var e) ? e.Value : null;

        // A replaced entry keeps its place.
        public XdmMap Put(AtomicValue key, IList<XPathItem> value)
        {
            var k = new MapKey(key);
            long order = _entries.TryGetValue(k, out Entry existing) ? existing.Order : System.Threading.Interlocked.Increment(ref s_order);
            return new(_entries.SetItem(k, new Entry(key, value, order)));
        }

        public XdmMap Remove(AtomicValue key) => new(_entries.Remove(new MapKey(key)));

        public static XdmMap Of(IEnumerable<KeyValuePair<AtomicValue, IList<XPathItem>>> entries)
        {
            var builder = ImmutableDictionary.CreateBuilder<MapKey, Entry>();
            foreach (var e in entries)
            {
                var k = new MapKey(e.Key);
                long order = builder.TryGetValue(k, out Entry existing) ? existing.Order : System.Threading.Interlocked.Increment(ref s_order);
                builder[k] = new Entry(e.Key, e.Value, order);
            }
            return new XdmMap(builder.ToImmutable());
        }

        public override int Arity => 1;

        public override SequenceTypeTest ParameterType(int index) => new(new AtomicTypeTest(AtomicTypes.AnyAtomicType), Cardinality.ExactlyOne);

        public override SequenceTypeTest ReturnType => SequenceTypeTest.AnySequence;

        public override string Describe() => $"a map with {Count} entries";
    }

    /// <summary>An XDM array (XPath 3.1 §2.8.5): immutable; its members are sequences.</summary>
    internal sealed class XdmArray : XdmFunction
    {
        public static readonly XdmArray Empty = new(ImmutableList<IList<XPathItem>>.Empty);

        private readonly ImmutableList<IList<XPathItem>> _members;

        private XdmArray(ImmutableList<IList<XPathItem>> members)
        {
            _members = members;
        }

        public static XdmArray Of(IEnumerable<IList<XPathItem>> members) => new(ImmutableList.CreateRange(members));

        public int Count => _members.Count;

        public IList<XPathItem> this[int index] => _members[index];

        public IReadOnlyList<IList<XPathItem>> Members => _members;

        /// <summary>The member at a 1-based position (FOAY0001 if there is none).</summary>
        public IList<XPathItem> Get(BigInteger position) =>
            position >= 1 && position <= Count ? _members[(int)position - 1] : throw new XdmException("FOAY0001", $"Array index {position} is out of bounds (the array has {Count} members)");

        public XdmArray Append(IList<XPathItem> member) => new(_members.Add(member));

        public XdmArray Put(BigInteger position, IList<XPathItem> member)
        {
            Get(position);
            return new(_members.SetItem((int)position - 1, member));
        }

        public XdmArray Insert(int index, IList<XPathItem> member) => new(_members.Insert(index, member));

        public XdmArray RemoveAt(int index) => new(_members.RemoveAt(index));

        public XdmArray Sub(int start, int length) => new(_members.GetRange(start, length));

        public override int Arity => 1;

        public override SequenceTypeTest ParameterType(int index) => new(new AtomicTypeTest(AtomicTypes.Integer), Cardinality.ExactlyOne);

        public override SequenceTypeTest ReturnType => SequenceTypeTest.AnySequence;

        public override string Describe() => $"an array with {Count} members";
    }
}
