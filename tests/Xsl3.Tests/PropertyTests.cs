// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Xml.XPath;
using Xsl3.XPath31.Functions;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

/// <summary>
/// Property tests (remaining-scope 8.2): laws checked over many generated values, with fixed seeds so that a failure
/// reproduces. Each failure names the value that broke the law.
/// </summary>
public class PropertyTests
{
    private const int Cases = 2000;

    // ------------------------------------------------------------ generators

    private static AtomicValue RandomValue(Random r)
    {
        switch (r.Next(14))
        {
            case 0: return AtomicValue.OfInteger(new BigInteger(r.NextInt64()) * r.Next(-3, 4));
            case 1: return AtomicValue.OfDecimal(new decimal(r.Next(), r.Next(), r.Next(0, 10_000), r.Next(2) == 0, (byte)r.Next(0, 12)));
            case 2: return AtomicValue.OfDouble(RandomDouble(r));
            case 3: return AtomicValue.OfFloat((float)RandomDouble(r));
            case 4: return AtomicValue.OfBoolean(r.Next(2) == 0);
            case 5: return AtomicValue.OfString(RandomString(r));
            case 6: return Casting.FromString(RandomDate(r).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + RandomZone(r), AtomicTypes.Date);
            case 7: return Casting.FromString(RandomDate(r).ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture) + (r.Next(2) == 0 ? "." + r.Next(1, 999) : "") + RandomZone(r), AtomicTypes.DateTime);
            case 8: return Casting.FromString($"{r.Next(24):D2}:{r.Next(60):D2}:{r.Next(60):D2}{RandomZone(r)}", AtomicTypes.Time);
            case 9: return Casting.FromString($"{(r.Next(2) == 0 ? "-" : "")}P{r.Next(100)}DT{r.Next(24)}H{r.Next(60)}M{r.Next(60)}.{r.Next(1, 1000)}S", AtomicTypes.DayTimeDuration);
            case 10: return Casting.FromString($"{(r.Next(2) == 0 ? "-" : "")}P{r.Next(50)}Y{r.Next(12)}M", AtomicTypes.YearMonthDuration);
            case 11: return Casting.FromString($"P{r.Next(5)}Y{r.Next(12)}M{r.Next(30)}DT{r.Next(24)}H", AtomicTypes.Duration);
            case 12: return Casting.FromString(Convert.ToHexString(RandomBytes(r)), AtomicTypes.HexBinary);
            default: return Casting.FromString(Convert.ToBase64String(RandomBytes(r)), AtomicTypes.Base64Binary);
        }
    }

    private static double RandomDouble(Random r) => r.Next(10) switch
    {
        0 => double.NaN,
        1 => r.Next(2) == 0 ? double.PositiveInfinity : double.NegativeInfinity,
        2 => r.Next(2) == 0 ? 0.0 : -0.0,
        3 => BitConverter.Int64BitsToDouble(r.NextInt64()) is var d && double.IsFinite(d) ? d : 1.5,
        _ => Math.Round((r.NextDouble() - 0.5) * Math.Pow(10, r.Next(-8, 16)), r.Next(0, 6)),
    };

    private static string RandomString(Random r)
    {
        const string alphabet = "abcXYZ019 éßÆΩЖ中𝔸\t-_.";
        var chars = new List<char>();
        for (int i = r.Next(0, 12); i > 0; i--)
        {
            int start = r.Next(alphabet.Length);
            chars.Add(alphabet[start]);
            if (char.IsHighSurrogate(alphabet[start]) && start + 1 < alphabet.Length)
            {
                chars.Add(alphabet[start + 1]);
            }
            else if (char.IsLowSurrogate(alphabet[start]))
            {
                chars.RemoveAt(chars.Count - 1);
            }
        }
        return new string(chars.ToArray());
    }

    private static DateTime RandomDate(Random r) => new DateTime(1, 1, 1).AddDays(r.Next(0, 3_652_058)).AddSeconds(r.Next(86_400));

    private static string RandomZone(Random r) => r.Next(3) switch
    {
        0 => "",
        1 => "Z",
        _ => (r.Next(2) == 0 ? "+" : "-") + $"{r.Next(14):D2}:{(r.Next(2) == 0 ? "00" : "30")}",
    };

    private static byte[] RandomBytes(Random r)
    {
        var bytes = new byte[r.Next(0, 10)];
        r.NextBytes(bytes);
        return bytes;
    }

    private static bool Eq(AtomicValue a, AtomicValue b) => Comparison.Compare(a, b, ValueComparisonOp.Eq, 0);

    private static bool IsNaN(AtomicValue v) => v is DoubleValue { Double: double.NaN } or FloatValue { Float: float.NaN };

    // ------------------------------------------------------------ properties

    /// <summary>Casting a value to xs:string and back to its type gives an equal value (NaN apart, which equals nothing).</summary>
    [Fact]
    public void CastsToStringAndBackRoundTrip()
    {
        var r = new Random(20261021);
        for (int i = 0; i < Cases; i++)
        {
            AtomicValue v = RandomValue(r);
            AtomicValue back = Casting.Cast(Casting.Cast(v, AtomicTypes.String), v.Type);
            Assert.True(IsNaN(v) ? IsNaN(back) : Eq(v, back), $"{v.Type.Name} '{v.Value}' came back as '{back.Value}'");
            Assert.Equal(v.Value, back.Value);
        }
    }

    /// <summary>castable as agrees with cast: when it says yes the cast succeeds, when it says no the cast raises an error.</summary>
    [Fact]
    public void CastableAgreesWithCast()
    {
        var r = new Random(1);
        AtomicType[] targets = AtomicTypes.All.Where(t => !t.IsAbstract && t != AtomicTypes.QName).ToArray();
        for (int i = 0; i < Cases; i++)
        {
            AtomicValue v = RandomValue(r);
            AtomicType target = targets[r.Next(targets.Length)];
            bool castable = Casting.IsCastable(v, target);
            Exception? error = Record.Exception(() => Casting.Cast(v, target));
            Assert.True(castable == (error == null), $"{v.Type.Name} '{v.Value}' as {target.Name}: castable {castable}, cast {(error == null ? "succeeded" : error.Message)}");
            Assert.True(error is null or XdmException, $"{v.Type.Name} '{v.Value}' as {target.Name}: {error?.GetType().Name}");
        }
    }

    /// <summary>Value comparison is a total order on comparable values: reflexive (NaN apart), antisymmetric and transitive.</summary>
    [Fact]
    public void ValueComparisonIsAnOrder()
    {
        var r = new Random(2);
        for (int i = 0; i < Cases; i++)
        {
            // Three values of one comparable kind: numbers, strings, or dates in one timezone.
            AtomicValue[] values = r.Next(3) switch
            {
                0 => Enumerable.Range(0, 3).Select(_ => (AtomicValue)AtomicValue.OfDouble(RandomDouble(r))).Where(v => !IsNaN(v)).ToArray(),
                1 => Enumerable.Range(0, 3).Select(_ => (AtomicValue)AtomicValue.OfString(RandomString(r))).ToArray(),
                _ => Enumerable.Range(0, 3).Select(_ => Casting.FromString(RandomDate(r).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture), AtomicTypes.DateTime)).ToArray(),
            };
            if (values.Length < 3)
            {
                continue;
            }
            bool Lt(AtomicValue a, AtomicValue b) => Comparison.Compare(a, b, ValueComparisonOp.Lt, 0);
            foreach (AtomicValue a in values)
            {
                Assert.True(Eq(a, a), $"'{a.Value}' doesn't equal itself");
                foreach (AtomicValue b in values)
                {
                    int relations = (Lt(a, b) ? 1 : 0) + (Lt(b, a) ? 1 : 0) + (Eq(a, b) ? 1 : 0);
                    Assert.True(relations == 1, $"'{a.Value}' and '{b.Value}': exactly one of <, >, = should hold");
                    foreach (AtomicValue c in values)
                    {
                        Assert.False(Lt(a, b) && Lt(b, c) && !Lt(a, c), $"'{a.Value}' < '{b.Value}' < '{c.Value}' but not '{a.Value}' < '{c.Value}'");
                    }
                }
            }
        }
    }

    /// <summary>Maps are persistent: updates make new maps, the old ones keep their entries, and lookups agree with a dictionary.</summary>
    [Fact]
    public void MapsArePersistent()
    {
        var r = new Random(3);
        for (int run = 0; run < 50; run++)
        {
            MapItem map = r.Next(2) == 0 ? MapItem.Empty
                : MapItem.Of(Enumerable.Range(0, r.Next(20)).Select(i => new KeyValuePair<AtomicValue, IList<XPathItem>>(AtomicValue.OfInteger(i), new XPathItem[] { AtomicValue.OfInteger(i * 10) })));
            var model = map.Entries.ToDictionary(e => e.Key.Value, e => e.Value[0].Value);
            var history = new List<(MapItem Map, Dictionary<string, string> Model)> { (map, new Dictionary<string, string>(model)) };
            for (int step = 0; step < 60; step++)
            {
                AtomicValue key = AtomicValue.OfInteger(r.Next(30));
                if (r.Next(3) == 0)
                {
                    map = map.Remove(key);
                    model.Remove(key.Value);
                }
                else
                {
                    string value = r.Next(1000).ToString(CultureInfo.InvariantCulture);
                    map = map.Put(key, new XPathItem[] { AtomicValue.OfString(value) });
                    model[key.Value] = value;
                }
                // Read often enough that the lookup table kicks in.
                for (int read = 0; read < 3; read++)
                {
                    AtomicValue probe = AtomicValue.OfInteger(r.Next(30));
                    Assert.Equal(model.TryGetValue(probe.Value, out string? expected) ? expected : null, map.Get(probe)?[0].Value);
                }
                history.Add((map, new Dictionary<string, string>(model)));
            }
            foreach (var (old, oldModel) in history)
            {
                Assert.Equal(oldModel.Count, old.Size());
                foreach (var (key, value) in oldModel)
                {
                    Assert.Equal(value, old.Get(AtomicValue.OfInteger(BigInteger.Parse(key, CultureInfo.InvariantCulture)))?[0].Value);
                }
            }
        }
    }

    /// <summary>Equal keys of different numeric types find the same entry (op:same-key): 1, 1.0 and 1e0.</summary>
    [Fact]
    public void NumericMapKeysCompareByValue()
    {
        var r = new Random(4);
        for (int i = 0; i < Cases; i++)
        {
            long n = r.Next(-1_000_000, 1_000_000);
            MapItem map = MapItem.Of(new[] { new KeyValuePair<AtomicValue, IList<XPathItem>>(AtomicValue.OfInteger(n), new XPathItem[] { AtomicValue.OfBoolean(true) }) });
            Assert.NotNull(map.Get(AtomicValue.OfDecimal(n)));
            Assert.NotNull(map.Get(AtomicValue.OfDouble(n)));
            Assert.Null(map.Get(AtomicValue.OfString(n.ToString(CultureInfo.InvariantCulture))));
        }
    }

    /// <summary>A collation is consistent: antisymmetric, and transitive on sampled triples.</summary>
    [Theory]
    [InlineData(Collation.CodepointUri)]
    [InlineData(Collation.HtmlAsciiCaseInsensitiveUri)]
    [InlineData("http://www.w3.org/2013/collation/UCA?lang=en;strength=secondary")]
    [InlineData("http://www.w3.org/2013/collation/UCA?lang=de;strength=primary")]
    public void CollationsAreConsistent(string uri)
    {
        Collation collation = Collation.Resolve(uri);
        var r = new Random(5);
        for (int i = 0; i < Cases; i++)
        {
            string a = RandomString(r), b = RandomString(r), c = RandomString(r);
            Assert.Equal(Math.Sign(collation.Compare(a, b)), -Math.Sign(collation.Compare(b, a)));
            if (collation.Compare(a, b) <= 0 && collation.Compare(b, c) <= 0)
            {
                Assert.True(collation.Compare(a, c) <= 0, $"'{a}' <= '{b}' <= '{c}' under {uri}, but not '{a}' <= '{c}'");
            }
        }
    }
}

internal static class MapItemExtensions
{
    public static int Size(this MapItem map) => map.Count;
}
