// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using Xsl3.Tree;
using Xunit;

namespace Xsl3.Tests;

public sealed class CompactValuePoolTests
{
    private static CompactDocument Load(IEnumerable<string> values)
    {
        var xml = new StringBuilder();
        using (var writer = XmlWriter.Create(xml))
        {
            writer.WriteStartElement("doc");
            foreach (string value in values)
            {
                writer.WriteStartElement("v");
                writer.WriteAttributeString("a", value);
                writer.WriteString(value);
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
        }
        using var reader = XmlReader.Create(new StringReader(xml.ToString()));
        return CompactDocument.Load(reader, XmlSpace.Preserve);
    }

    [Fact]
    public void ShortValuesRemainInternedAcrossGrowthAndAcrossAttributesAndText()
    {
        string[] unique = Enumerable.Range(0, 10000).Select(i => i.ToString("D20", CultureInfo.InvariantCulture)).ToArray();
        string[] input = unique.Concat(unique.Take(5000).Reverse()).ToArray();
        CompactDocument document = Load(input);
        Assert.Equal(unique.Length, document._values.Length);
        var first = new Dictionary<string, string>(StringComparer.Ordinal);
        var rows = document.CreateNavigator().Select("/doc/v");
        int index = 0;
        while (rows.MoveNext())
        {
            var element = rows.Current!;
            string attribute = element.GetAttribute("a", "");
            Assert.Equal(input[index++], attribute);
            var text = element.Clone();
            Assert.True(text.MoveToFirstChild());
            Assert.Equal(attribute, text.Value);
            Assert.Same(attribute, text.Value);
            if (first.TryGetValue(attribute, out string? previous)) Assert.Same(previous, attribute);
            else first.Add(attribute, attribute);
        }
        Assert.Equal(input.Length, index);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(255)]
    public void ShortValuesSurviveHashBucketCollisionsAndGrowth(int bucket)
    {
        // Derive collisions from this process's randomized ordinal hash. All
        // values start in the same bucket throughout the table's growth.
        var values = new List<string>();
        for (int i = 0; values.Count < 64; i++)
        {
            string value = i.ToString("D20", CultureInfo.InvariantCulture);
            if ((StringComparer.Ordinal.GetHashCode(value) & 255) == bucket)
                values.Add(value);
        }
        string[] input = values.Concat(Enumerable.Reverse(values)).ToArray();
        CompactDocument document = Load(input);
        Assert.Equal(values.Count, document._values.Length);
        var rows = document.CreateNavigator().Select("/doc/v");
        int index = 0;
        while (rows.MoveNext())
        {
            string value = rows.Current!.GetAttribute("a", "");
            Assert.Equal(input[index++], value);
            Assert.Same(document._values[values.IndexOf(value)], value);
        }
        Assert.Equal(input.Length, index);
    }

    [Fact]
    public void DifferentShortValuesWithTheSameFullHashRemainDistinct()
    {
        // Runtime hashes are randomized. Discover a pair using this process's
        // comparer rather than relying on strings that collide on one machine.
        static (string First, string Second) FindCollision()
        {
            var hashes = new Dictionary<int, string>();
            for (long i = 0; ; i++)
            {
                string value = i.ToString("D20", CultureInfo.InvariantCulture);
                int hash = StringComparer.Ordinal.GetHashCode(value);
                if (hashes.TryGetValue(hash, out string? previous))
                    return (previous, value);
                hashes.Add(hash, value);
            }
        }

        var (first, second) = FindCollision();
        Assert.NotEqual(first, second);
        Assert.Equal(StringComparer.Ordinal.GetHashCode(first), StringComparer.Ordinal.GetHashCode(second));
        CompactDocument document = Load(new[] { first, second, first, second });
        Assert.Equal(new[] { first, second }, document._values);
        var rows = document.CreateNavigator().Select("/doc/v");
        foreach (string expected in new[] { first, second, first, second })
        {
            Assert.True(rows.MoveNext());
            Assert.Equal(expected, rows.Current!.GetAttribute("a", ""));
            Assert.Equal(expected, rows.Current.Value);
        }
        Assert.False(rows.MoveNext());
    }

    [Fact]
    public void ShortValuesKeepTheirIndicesWhenLongValuesAreInterleaved()
    {
        string[] unique = Enumerable.Range(0, 1000).Select(i => i.ToString("D20", CultureInfo.InvariantCulture)).ToArray();
        string longValue = new('x', 40);
        string[] input = unique.SelectMany(value => new[] { longValue, value }).Concat(Enumerable.Reverse(unique)).ToArray();
        CompactDocument document = Load(input);
        Assert.Equal(unique.Length * 3, document._values.Length);
        var rows = document.CreateNavigator().Select("/doc/v");
        int index = 0;
        while (rows.MoveNext())
        {
            string value = rows.Current!.GetAttribute("a", "");
            Assert.Equal(input[index++], value);
        }
        Assert.Equal(input.Length, index);
    }

    [Theory]
    [InlineData(0x20)]
    [InlineData(0x2000)]
    public void SingleUnitValuesRemainInternedAcrossGrowth(int firstCodeUnit)
    {
        string[] unique = new[] { "" }.Concat(Enumerable.Range(firstCodeUnit, 2048)
            .Select(codeUnit => ((char)codeUnit).ToString())).ToArray();
        string[] input = unique.Concat(Enumerable.Reverse(unique)).ToArray();
        CompactDocument document = Load(input);
        Assert.Equal(unique, document._values);
        var rows = document.CreateNavigator().Select("/doc/v");
        foreach (string expected in input)
        {
            Assert.True(rows.MoveNext());
            string actual = rows.Current!.GetAttribute("a", "");
            Assert.Equal(expected, actual);
            Assert.Same(document._values[Array.IndexOf(unique, expected)], actual);
        }
        Assert.False(rows.MoveNext());
    }

    [Fact]
    public void ValueInterningKeepsOrdinalCaseAndUnicodeDistinctions()
    {
        string[] unique = { "a", "A", "e\u0301", "\u00e9", "\u03bb", "\ud83d\ude00", "", " " };
        CompactDocument document = Load(unique.Concat(Enumerable.Reverse(unique)));
        Assert.Equal(unique.Length, document._values.Length);
        Assert.All(unique, value => Assert.Contains(value, document._values));
        var rows = document.CreateNavigator().Select("/doc/v");
        var actual = new List<string>();
        while (rows.MoveNext()) actual.Add(rows.Current!.GetAttribute("a", ""));
        Assert.Equal(unique.Concat(Enumerable.Reverse(unique)), actual);
    }

    [Theory]
    [InlineData(31, 1)]
    [InlineData(32, 1)]
    [InlineData(33, 4)]
    public void TheExistingShortValueBoundaryIsRetained(int length, int storedValues)
    {
        // Builder's existing policy interns values up to 32 UTF-16 code units.
        string value = new('x', length);
        CompactDocument document = Load(new[] { value, value });
        Assert.Equal(storedValues, document._values.Length);
        Assert.All(document._values, stored => Assert.Equal(value, stored));
    }
}
