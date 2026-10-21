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
    internal abstract class FunctionItem : XPathItem
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
        public override Type ValueType => typeof(FunctionItem);
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

        // Xsl3 (P4): the key of a string, for lookups only (MapItem.GetString): equal to the key of an xs:string,
        // xs:untypedAtomic or xs:anyURI value with these characters.
        public MapKey(string s)
        {
            Value = null!;
            Kind = 2;
            Normal = s;
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
    internal sealed class MapItem : FunctionItem
    {
        private readonly record struct Entry(AtomicValue Key, IList<XPathItem> Value, long Order);

        private static long s_order;

        public static readonly MapItem Empty = new(ImmutableDictionary<MapKey, Entry>.Empty);

        // Xsl3 (P4): a map built in one go (MapItem.Of: map:merge, map constructors, JSON) keeps a hash table and no
        // persistent dictionary until it is first updated; maps made by updates share structure through the
        // persistent one. Exactly one of the two is the map's contents (_entries may also be set alongside
        // _lookup, as a cache, when an updated map is read often).
        private ImmutableDictionary<MapKey, Entry>? _entries;
        private Dictionary<MapKey, Entry>? _lookup;

        private MapItem(ImmutableDictionary<MapKey, Entry> entries)
        {
            _entries = entries;
        }

        private MapItem(Dictionary<MapKey, Entry> lookup)
        {
            _lookup = lookup;
        }

        public int Count => _lookup?.Count ?? _entries!.Count;

        public IEnumerable<AtomicValue> Keys => Ordered.Select(e => e.Key);

        public IEnumerable<KeyValuePair<AtomicValue, IList<XPathItem>>> Entries => Ordered.Select(e => new KeyValuePair<AtomicValue, IList<XPathItem>>(e.Key, e.Value));

        private IEnumerable<Entry> Ordered
        {
            get
            {
                IEnumerable<Entry> values = _lookup != null ? _lookup.Values : _entries!.Values;
                return Count <= 1 ? values : values.OrderBy(e => e.Order);
            }
        }

        public bool ContainsKey(AtomicValue key) => Lookup(new MapKey(key), out _);

        public IList<XPathItem>? Get(AtomicValue key) => Lookup(new MapKey(key), out var e) ? e.Value : null;

        /// <summary>The value for the key that is the string <paramref name="key"/> (as xs:string), or null.</summary>
        public IList<XPathItem>? GetString(string key) => Lookup(new MapKey(key), out var e) ? e.Value : null;

        // Xsl3 (P4): an updated map that is read again and again (a lookup table in a loop) gets a hash table of its
        // entries, which the map's immutability makes safe to keep; the persistent dictionary is a tree, slower to
        // search. Built after a few reads, so maps made, read once and updated don't pay for it. Races only build it twice.
        private int _reads;

        private bool Lookup(MapKey key, out Entry entry)
        {
            Dictionary<MapKey, Entry>? lookup = _lookup;
            if (lookup == null && _entries!.Count >= 8 && ++_reads == 16)
            {
                _lookup = lookup = new Dictionary<MapKey, Entry>(_entries);
            }
            return lookup != null ? lookup.TryGetValue(key, out entry) : _entries!.TryGetValue(key, out entry);
        }

        // The persistent dictionary, made from the hash table the first time the map is updated.
        private ImmutableDictionary<MapKey, Entry> Persistent => _entries ??= _lookup!.ToImmutableDictionary();

        // A replaced entry keeps its place.
        public MapItem Put(AtomicValue key, IList<XPathItem> value)
        {
            var k = new MapKey(key);
            long order = Lookup(k, out Entry existing) ? existing.Order : System.Threading.Interlocked.Increment(ref s_order);
            return new(Persistent.SetItem(k, new Entry(key, value, order)));
        }

        public MapItem Remove(AtomicValue key) => new(Persistent.Remove(new MapKey(key)));

        public static MapItem Of(IEnumerable<KeyValuePair<AtomicValue, IList<XPathItem>>> entries)
        {
            var lookup = new Dictionary<MapKey, Entry>();
            foreach (var e in entries)
            {
                var k = new MapKey(e.Key);
                long order = lookup.TryGetValue(k, out Entry existing) ? existing.Order : System.Threading.Interlocked.Increment(ref s_order);
                lookup[k] = new Entry(e.Key, e.Value, order);
            }
            return lookup.Count == 0 ? Empty : new MapItem(lookup);
        }

        public override int Arity => 1;

        public override SequenceTypeTest ParameterType(int index) => new(new AtomicTypeTest(AtomicTypes.AnyAtomicType), Cardinality.ExactlyOne);

        public override SequenceTypeTest ReturnType => SequenceTypeTest.AnySequence;

        public override string Describe() => $"a map with {Count} entries";
    }

    /// <summary>An XDM array (XPath 3.1 §2.8.5): immutable; its members are sequences.</summary>
    internal sealed class ArrayItem : FunctionItem
    {
        public static readonly ArrayItem Empty = new(ImmutableList<IList<XPathItem>>.Empty);

        private readonly ImmutableList<IList<XPathItem>> _members;

        private ArrayItem(ImmutableList<IList<XPathItem>> members)
        {
            _members = members;
        }

        public static ArrayItem Of(IEnumerable<IList<XPathItem>> members) => new(ImmutableList.CreateRange(members));

        public int Count => _members.Count;

        public IList<XPathItem> this[int index] => _members[index];

        public IReadOnlyList<IList<XPathItem>> Members => _members;

        /// <summary>The member at a 1-based position (FOAY0001 if there is none).</summary>
        public IList<XPathItem> Get(BigInteger position) =>
            position >= 1 && position <= Count ? _members[(int)position - 1] : throw new XdmException("FOAY0001", $"Array index {position} is out of bounds (the array has {Count} members)");

        public ArrayItem Append(IList<XPathItem> member) => new(_members.Add(member));

        public ArrayItem Put(BigInteger position, IList<XPathItem> member)
        {
            Get(position);
            return new(_members.SetItem((int)position - 1, member));
        }

        public ArrayItem Insert(int index, IList<XPathItem> member) => new(_members.Insert(index, member));

        public ArrayItem RemoveAt(int index) => new(_members.RemoveAt(index));

        public ArrayItem Sub(int start, int length) => new(_members.GetRange(start, length));

        public override int Arity => 1;

        public override SequenceTypeTest ParameterType(int index) => new(new AtomicTypeTest(AtomicTypes.Integer), Cardinality.ExactlyOne);

        public override SequenceTypeTest ReturnType => SequenceTypeTest.AnySequence;

        public override string Describe() => $"an array with {Count} members";
    }
}
