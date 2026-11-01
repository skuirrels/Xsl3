// Licensed under the MIT license. See LICENSE.TXT in the repository root.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Xsl3.Tests;

public class StreamingProbeReportTests
{
    private static StreamingProbe.Result Report(double? elapsed, double? first, long? atFirst) => new(
        "identity", "stream", "ok \" \\ \n \u2603 \u2028", long.MaxValue, 209715200, 0,
        20971520, 18000000, "getrusage(RUSAGE_SELF)", long.MinValue, elapsed, first, atFirst, true,
        new Dictionary<string, StreamingProbe.Fingerprint>(StringComparer.Ordinal)
        {
            ["principal \" \\ \n \u2603"] = new(long.MaxValue, "hash \" \\ \u2603")
        });

    private static void EqualJson(JsonElement expected, JsonElement actual)
    {
        Assert.Equal(expected.ValueKind, actual.ValueKind);
        if (expected.ValueKind == JsonValueKind.Object)
        {
            Assert.Equal(expected.EnumerateObject().Count(), actual.EnumerateObject().Count());
            foreach (var property in expected.EnumerateObject())
            {
                Assert.True(actual.TryGetProperty(property.Name, out var value), property.Name);
                EqualJson(property.Value, value);
            }
        }
        else if (expected.ValueKind == JsonValueKind.String) Assert.Equal(expected.GetString(), actual.GetString());
        else Assert.Equal(expected.GetRawText(), actual.GetRawText());
    }

    [Fact]
    public void EveryReportFieldMatchesIndependentBclSerialization()
    {
        foreach (double value in new[] { 0d, -0d, double.Epsilon, -double.Epsilon, double.MinValue, double.MaxValue, 1e-6, 1e6, 1e20, 1234.56789012345 })
        {
            var report = Report(value, value, long.MaxValue);
            using var expected = JsonDocument.Parse(JsonSerializer.Serialize(report));
            using var actual = JsonDocument.Parse(StreamingProbe.SerializeReport(report));
            EqualJson(expected.RootElement, actual.RootElement);
            Assert.Equal(BitConverter.DoubleToInt64Bits(value), BitConverter.DoubleToInt64Bits(actual.RootElement.GetProperty("ElapsedMs").GetDouble()));
        }
    }

    [Fact]
    public void MissingTimingsAndEmptyOutputsRemainExplicit()
    {
        var report = Report(null, null, null) with { Outputs = new(), ValidOutput = false };
        using var expected = JsonDocument.Parse(JsonSerializer.Serialize(report));
        using var actual = JsonDocument.Parse(StreamingProbe.SerializeReport(report));
        EqualJson(expected.RootElement, actual.RootElement);
    }

    [Fact]
    public void EveryUtf16CodeUnitMatchesIndependentBclReplacementAndEscaping()
    {
        var input = new System.Text.StringBuilder();
        for (int i = char.MinValue; i <= char.MaxValue; i++)
            input.Append((char)i).Append('|');
        input.Append("\uD800\uDC00|\uDBFF\uDFFF|\uD800\uDBFF\uDC00\uDFFF|\uDC00\uD800");
        var report = Report(null, null, null) with {
            Workload = input.ToString(), Mode = null!, Status = input.ToString(),
            Outputs = new() { [input.ToString()] = new(1, input.ToString()) },
        };
        string serialized = StreamingProbe.SerializeReport(report);
        Assert.All(serialized, character => Assert.InRange(character, (char)0, (char)127));
        using var expected = JsonDocument.Parse(JsonSerializer.Serialize(report));
        using var actual = JsonDocument.Parse(serialized);
        EqualJson(expected.RootElement, actual.RootElement);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonfiniteTimingsRetainBclRejection(double value)
    {
        Assert.Throws<ArgumentException>(() => StreamingProbe.SerializeReport(Report(value, null, null)));
        Assert.Throws<ArgumentException>(() => StreamingProbe.SerializeReport(Report(null, value, null)));
    }
}
