// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Xml.XPath;
using Xsl3.Xdm;

namespace Xsl3.XPath31.Functions
{
    // map:* and array:* (F&O 3.1 §17), except those that take function items, which compile to QIL.
    internal static partial class FunctionLibrary
    {
        private static MapItem Map(IList<XPathItem> arg) => (MapItem)arg[0];

        private static ArrayItem Array(IList<XPathItem> arg) => (ArrayItem)arg[0];

        private static AtomicValue Key(IList<XPathItem> arg) => (AtomicValue)arg[0];

        /// <summary>
        /// A value a map or array keeps: its nodes cloned, since a navigator in an argument can be the caller's
        /// iterator, which moves on (map:merge(p ! map:entry(@k, .)) would otherwise see every value on the last p).
        /// </summary>
        private static IList<XPathItem> Keep(IList<XPathItem> value)
        {
            // A runtime sequence can also be a buffer the generated code reuses (one native value wrapped as a list).
            bool reused = value is Xsl3.Runtime.XmlQuerySequence<XPathItem>;
            for (int i = 0; i < value.Count; i++)
            {
                if (reused || value[i] is XPathNavigator)
                {
                    var copy = new XPathItem[value.Count];
                    for (int j = 0; j < copy.Length; j++)
                    {
                        copy[j] = value[j] is XPathNavigator n ? n.Clone() : value[j];
                    }
                    return copy;
                }
            }
            return value;
        }

        private static BigInteger I(IList<XPathItem> arg) => ((IntegerValue)arg[0]).Integer;

        private static void RegisterMapsAndArrays()
        {
            Define("map:merge($maps as map(*)*) as map(*)", (c, a) => One(Merge(a[0], null)));
            Define("map:merge($maps as map(*)*, $options as map(*)) as map(*)", (c, a) => One(Merge(a[0], Map(a[1]))));
            Define("map:size($map as map(*)) as xs:integer", (c, a) => Int(Map(a[0]).Count));
            Define("map:keys($map as map(*)) as xs:anyAtomicType*", (c, a) => Map(a[0]).Keys.Cast<XPathItem>().ToList());
            Define("map:contains($map as map(*), $key as xs:anyAtomicType) as xs:boolean", (c, a) => Bool(Map(a[0]).ContainsKey(Key(a[1]))));
            Define("map:get($map as map(*), $key as xs:anyAtomicType) as item()*", (c, a) => Map(a[0]).Get(Key(a[1])) ?? Empty);
            Define("map:find($input as item()*, $key as xs:anyAtomicType) as array(*)", (c, a) => One(ArrayItem.Of(Find(a[0], Key(a[1])))));
            Define("map:put($map as map(*), $key as xs:anyAtomicType, $value as item()*) as map(*)", (c, a) => One(Map(a[0]).Put(Key(a[1]), Keep(a[2]))));
            Define("map:entry($key as xs:anyAtomicType, $value as item()*) as map(*)", (c, a) => One(MapItem.Empty.Put(Key(a[0]), Keep(a[1]))));
            Define("map:remove($map as map(*), $keys as xs:anyAtomicType*) as map(*)", (c, a) =>
            {
                MapItem map = Map(a[0]);
                foreach (XPathItem k in a[1])
                {
                    map = map.Remove((AtomicValue)k);
                }
                return One(map);
            });

            Define("array:size($array as array(*)) as xs:integer", (c, a) => Int(Array(a[0]).Count));
            Define("array:get($array as array(*), $position as xs:integer) as item()*", (c, a) => Array(a[0]).Get(I(a[1])));
            Define("array:put($array as array(*), $position as xs:integer, $member as item()*) as array(*)", (c, a) => One(Array(a[0]).Put(I(a[1]), Keep(a[2]))));
            Define("array:append($array as array(*), $appendage as item()*) as array(*)", (c, a) => One(Array(a[0]).Append(Keep(a[1]))));
            Define("array:subarray($array as array(*), $start as xs:integer) as array(*)", (c, a) =>
                One(Subarray(Array(a[0]), I(a[1]), Array(a[0]).Count - I(a[1]) + 1)));
            Define("array:subarray($array as array(*), $start as xs:integer, $length as xs:integer) as array(*)", (c, a) =>
                One(Subarray(Array(a[0]), I(a[1]), I(a[2]))));
            Define("array:remove($array as array(*), $positions as xs:integer*) as array(*)", (c, a) =>
            {
                ArrayItem array = Array(a[0]);
                var positions = new SortedSet<int>();
                foreach (XPathItem p in a[1])
                {
                    BigInteger i = ((IntegerValue)p).Integer;
                    array.Get(i); // FOAY0001 when out of bounds
                    positions.Add((int)i - 1);
                }
                foreach (int i in positions.Reverse())
                {
                    array = array.RemoveAt(i);
                }
                return One(array);
            });
            Define("array:insert-before($array as array(*), $position as xs:integer, $member as item()*) as array(*)", (c, a) =>
            {
                ArrayItem array = Array(a[0]);
                BigInteger p = I(a[1]);
                if (p < 1 || p > array.Count + 1)
                {
                    throw new XdmException("FOAY0001", $"Array position {p} is out of bounds (the array has {array.Count} members)");
                }
                return One(array.Insert((int)p - 1, Keep(a[2])));
            });
            Define("array:head($array as array(*)) as item()*", (c, a) =>
                Array(a[0]).Count > 0 ? Array(a[0])[0] : throw new XdmException("FOAY0001", "array:head of an empty array"));
            Define("array:tail($array as array(*)) as array(*)", (c, a) =>
                Array(a[0]).Count > 0 ? One(Array(a[0]).RemoveAt(0)) : throw new XdmException("FOAY0001", "array:tail of an empty array"));
            Define("array:reverse($array as array(*)) as array(*)", (c, a) => One(ArrayItem.Of(Array(a[0]).Members.Reverse())));
            Define("array:join($arrays as array(*)*) as array(*)", (c, a) => One(ArrayItem.Of(a[0].SelectMany(x => ((ArrayItem)x).Members))));
            Define("array:flatten($input as item()*) as item()*", (c, a) => FlattenAll(a[0]));
            Define("array:sort($array as array(*)) as array(*)", (c, a) => One(SortArray(Array(a[0]), c)));
            Define("array:sort($array as array(*), $collation as xs:string?) as array(*)", (c, a) =>
                One(SortArray(Array(a[0]), a[1].Count == 0 ? c : c.WithDefaultCollation(Coll(a[1], c)))));
        }

        // map:merge (F&O 3.1 §17.1.1): duplicates are use-first (the default), use-last, use-any, combine or reject.
        private static MapItem Merge(IList<XPathItem> maps, MapItem? options)
        {
            string duplicates = "use-first";
            if (options?.Get(AtomicValue.OfString("duplicates")) is IList<XPathItem> d)
            {
                if (d.Count != 1 || d[0] is not AtomicValue v || !v.Type.DerivesFrom(AtomicTypes.String) && v.Type.Primitive != Primitive.UntypedAtomic)
                {
                    throw Err.XPTY0004("The duplicates option of map:merge must be a string");
                }
                duplicates = v.Value;
                if (duplicates is not ("use-first" or "use-last" or "use-any" or "combine" or "reject"))
                {
                    throw new XdmException("FOJS0005", $"'{duplicates}' is not a value of the duplicates option");
                }
            }
            if (maps.Count == 1)
            {
                return (MapItem)maps[0];
            }
            var entries = new Dictionary<MapKey, KeyValuePair<AtomicValue, IList<XPathItem>>>();
            var order = new List<MapKey>();
            foreach (XPathItem item in maps)
            {
                foreach (var e in ((MapItem)item).Entries)
                {
                    var key = new MapKey(e.Key);
                    if (!entries.TryGetValue(key, out var existing))
                    {
                        entries[key] = e;
                        order.Add(key);
                        continue;
                    }
                    switch (duplicates)
                    {
                        case "reject":
                            throw new XdmException("FOJS0003", $"map:merge: the key '{e.Key.Value}' is in two maps");
                        case "use-last":
                            // The last entry wins, its key too (op:same-key keys can differ in type: QT3 same-key-001).
                            entries[key] = e;
                            break;
                        case "combine":
                            entries[key] = new(existing.Key, existing.Value.Concat(e.Value).ToList());
                            break;
                    }
                }
            }
            return MapItem.Of(order.Select(k => entries[k]));
        }

        // map:find: the values of the key in every map, searching the items, arrays' members and maps' values.
        private static List<IList<XPathItem>> Find(IList<XPathItem> input, AtomicValue key)
        {
            var result = new List<IList<XPathItem>>();
            void Search(IList<XPathItem> items)
            {
                foreach (XPathItem item in items)
                {
                    switch (item)
                    {
                        case MapItem map:
                            if (map.Get(key) is IList<XPathItem> v)
                            {
                                result.Add(v);
                            }
                            foreach (var e in map.Entries)
                            {
                                Search(e.Value);
                            }
                            break;
                        case ArrayItem array:
                            foreach (IList<XPathItem> m in array.Members)
                            {
                                Search(m);
                            }
                            break;
                    }
                }
            }
            Search(input);
            return result;
        }

        private static ArrayItem Subarray(ArrayItem array, BigInteger start, BigInteger length)
        {
            if (start < 1 || start > array.Count + 1)
            {
                throw new XdmException("FOAY0001", $"array:subarray: start {start} is out of bounds (the array has {array.Count} members)");
            }
            if (length < 0)
            {
                throw new XdmException("FOAY0002", "array:subarray: the length is negative");
            }
            if (start + length > array.Count + 1)
            {
                throw new XdmException("FOAY0001", $"array:subarray: start {start} and length {length} go beyond the array's {array.Count} members");
            }
            return array.Sub((int)start - 1, (int)length);
        }

        private static IList<XPathItem> FlattenAll(IList<XPathItem> input)
        {
            var result = new List<XPathItem>();
            foreach (XPathItem item in input)
            {
                if (item is ArrayItem array)
                {
                    result.AddRange(Sequences.Flatten(array));
                }
                else
                {
                    result.Add(item);
                }
            }
            return result;
        }

        private static ArrayItem SortArray(ArrayItem array, FunctionContext c)
        {
            List<IList<XPathItem>> members = array.Members.ToList();
            int[] order = SortOrder(members.Select(m => Sequences.Atomize(m)).ToList(), c);
            return ArrayItem.Of(order.Select(i => members[i]));
        }

        /// <summary>
        /// The stable order of items by their sort keys (fn:sort, F&amp;O 3.1 §14.2.2): keys compare item by item,
        /// untyped values as strings, NaN first; a shorter key that is a prefix sorts first. XPTY0004 for
        /// incomparable keys.
        /// </summary>
        internal static int[] SortOrder(IList<IList<XPathItem>> keys, FunctionContext c)
        {
            int CompareKeys(IList<XPathItem> x, IList<XPathItem> y)
            {
                for (int i = 0; i < System.Math.Min(x.Count, y.Count); i++)
                {
                    var a = (AtomicValue)x[i];
                    var b = (AtomicValue)y[i];
                    if (a.Type.Primitive == Primitive.UntypedAtomic) a = Casting.Cast(a, AtomicTypes.String);
                    if (b.Type.Primitive == Primitive.UntypedAtomic) b = Casting.Cast(b, AtomicTypes.String);
                    bool aNaN = a is DoubleValue { Double: double.NaN } or FloatValue { Float: float.NaN };
                    bool bNaN = b is DoubleValue { Double: double.NaN } or FloatValue { Float: float.NaN };
                    if (aNaN || bNaN)
                    {
                        if (aNaN && bNaN) continue;
                        return aNaN ? -1 : 1; // NaN sorts first
                    }
                    if (Comparison.Compare(a, b, ValueComparisonOp.Lt, c.ImplicitTimezoneMinutes, c.DefaultCollation)) return -1;
                    if (Comparison.Compare(b, a, ValueComparisonOp.Lt, c.ImplicitTimezoneMinutes, c.DefaultCollation)) return 1;
                }
                return x.Count.CompareTo(y.Count);
            }
            int[] order = Enumerable.Range(0, keys.Count).ToArray();
            try
            {
                System.Array.Sort(order, (x, y) =>
                {
                    int r = CompareKeys(keys[x], keys[y]);
                    return r != 0 ? r : x.CompareTo(y); // stable
                });
            }
            catch (System.InvalidOperationException e) when (e.InnerException is XdmException inner)
            {
                throw inner; // incomparable keys: XPTY0004
            }
            return order;
        }
    }
}
