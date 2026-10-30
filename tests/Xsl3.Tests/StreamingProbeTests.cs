// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml;
using Xunit;

namespace Xsl3.Tests;

public class StreamingProbeTests
{
    [Fact]
    public void ReportNumbersKeepTheBclJsonRepresentationAndRoundTripTheirBits()
    {
        foreach (double value in new[] { 0d, -0d, double.Epsilon, -double.Epsilon, double.MinValue, double.MaxValue, 1e-6, 1e6, 1e20, 1234.56789012345 })
        {
            string actual = JsonSerializer.Serialize(value, StreamingProbeJsonContext.Default.Double);
            Assert.Equal(JsonSerializer.Serialize(value), actual);
            using var json = JsonDocument.Parse(actual);
            Assert.Equal(JsonValueKind.Number, json.RootElement.ValueKind);
            Assert.Equal(BitConverter.DoubleToInt64Bits(value), BitConverter.DoubleToInt64Bits(json.RootElement.GetDouble()));
        }
        foreach (double value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            Assert.Throws<ArgumentException>(() => JsonSerializer.Serialize(value, StreamingProbeJsonContext.Default.Double));
    }

    [Fact]
    public void CheckedInStreamingMicroCorpusMatchesTheSharedFixtureDefinitions()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "streaming-corpus");
        Assert.Equal(StreamingProbe.Workloads.OrderBy(x => x), Directory.GetFiles(directory, "*.xsl").Select(Path.GetFileNameWithoutExtension).OrderBy(x => x));
        foreach (string workload in StreamingProbe.Workloads)
            Assert.Equal(StreamingProbe.Stylesheet(workload), File.ReadAllText(Path.Combine(directory, workload + ".xsl")));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(8192)]
    public void GeneratedInputHasExactLengthAndTheSameRecordsAcrossReadSizes(int chunk)
    {
        using var input = new StreamingProbe.GeneratedInput(4096);
        using var output = new MemoryStream(); var buffer = new byte[chunk]; int count;
        while ((count = input.Read(buffer)) != 0) output.Write(buffer, 0, count);
        Assert.Equal(4096, output.Length); Assert.Equal(4096, input.ReadBytes); Assert.False(input.CanSeek);
        using var reader = XmlReader.Create(new MemoryStream(output.ToArray())); long rows = 0;
        while (reader.Read()) if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "row")
        {
            rows++; Assert.Equal(rows.ToString("D20", CultureInfo.InvariantCulture), reader.GetAttribute("id"));
            Assert.Equal(StreamingProbe.GeneratedInput.Amount(rows).ToString(CultureInfo.InvariantCulture), reader.GetAttribute("amount"));
        }
        Assert.Equal(input.Records, rows); Assert.Equal(0, input.Read(buffer));
    }

    [Fact]
    public void MultiGigabyteInputsDoNotRequireMaterializingTheCorpus()
    {
        using var input = new StreamingProbe.GeneratedInput(3L * 1024 * 1024 * 1024);
        Span<byte> prefix = stackalloc byte[64]; Assert.Equal(prefix.Length, input.Read(prefix));
        Assert.Equal(64, input.Position); Assert.True(input.Length > int.MaxValue);
        Assert.StartsWith("<doc><row", Encoding.UTF8.GetString(prefix));
        Assert.Throws<NotSupportedException>(() => input.Seek(0, SeekOrigin.Begin));
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("adjacent")]
    [InlineData("accumulator")]
    [InlineData("iterate")]
    [InlineData("merge")]
    [InlineData("fork")]
    [InlineData("source-document")]
    [InlineData("type-assertion")]
    [InlineData("type-inspection")]
    [InlineData("climbing-union")]
    [InlineData("climbing-intersection")]
    [InlineData("copy-select")]
    [InlineData("conditional-content")]
    [InlineData("template-parameter")]
    [InlineData("function-parameter")]
    [InlineData("overlap-values")]
    public void EveryWorkloadAgreesWithItsIndependentOracleAndTheTreeRun(string workload)
    {
        var tree = StreamingProbe.Run(workload, false, 65536, 0);
        var stream = StreamingProbe.Run(workload, true, 65536, 0);
        Assert.Equal("ok", tree.Status); Assert.Equal("ok", stream.Status);
        Assert.True(tree.ValidOutput); Assert.True(stream.ValidOutput);
        Assert.Equal(tree.Records, stream.Records);
        Assert.Equal(tree.Outputs.Count, stream.Outputs.Count);
        foreach (var output in tree.Outputs) Assert.Equal(output.Value, stream.Outputs[output.Key]);
        Assert.Equal(65536, stream.InputBytes); Assert.True(stream.PeakWorkingSetBytes > 0);
        Assert.NotNull(stream.FirstOutputMs); Assert.True(stream.InputBytesAtFirstOutput > 0);
    }

    [Theory]
    [InlineData("climbing-union")]
    [InlineData("climbing-intersection")]
    [InlineData("type-assertion")]
    [InlineData("copy-select")]
    [InlineData("conditional-content")]
    [InlineData("template-parameter")]
    [InlineData("function-parameter")]
    [InlineData("overlap-values")]
    public void StreamingConsumersEmitBeforeReadingTheRemainingPopulation(string workload)
    {
        const long bytes = 4 * 1024 * 1024;
        var result = StreamingProbe.Run(workload, true, bytes, 0);
        Assert.Equal("ok", result.Status);
        Assert.InRange(result.InputBytesAtFirstOutput!.Value, 1, bytes / 2);
    }

    [Fact]
    public void AnOddMergeByteBudgetInterleavesUnequalInputsWithoutLosingTheLastRecord()
    {
        long bytes = 2 * (StreamingProbe.GeneratedInput.Header.Length + StreamingProbe.GeneratedInput.Footer.Length
            + 2 * StreamingProbe.GeneratedInput.RecordLength) - 1;
        var result = StreamingProbe.Run("merge", true, bytes, 0);
        Assert.Equal("ok", result.Status); Assert.True(result.ValidOutput);
        Assert.Equal(bytes, result.InputBytes); Assert.Equal(3, result.Records);
    }

    [Theory]
    [InlineData("identity", "stream", "65536", "-1")]
    [InlineData("identity", "stream", "-65536", "0")]
    [InlineData("identity", "stream", "9223372036854775808", "0")]
    [InlineData("identity", "stream", "1", "0")]
    [InlineData("identity", "unknown", "65536", "0")]
    [InlineData("unknown", "stream", "65536", "0")]
    public void MalformedArgumentsCannotProduceSuccessfulProbeResults(string workload, string mode, string bytes, string limit)
        => Assert.Equal(64, StreamingProbe.RunCommand(new[] { workload, mode, bytes, limit }));

    [Fact]
    public void AnImpossibleBudgetIsReportedWithoutPretendingTheLargeInputRan()
    {
        var result = StreamingProbe.Run("identity", true, 65536, 1);
        Assert.Equal("budget-exceeded-before-measurement", result.Status);
        Assert.Equal(0, result.InputBytes); Assert.Null(result.ElapsedMs); Assert.False(result.ValidOutput);
        Assert.Empty(result.Outputs); Assert.True(result.PeakWorkingSetBytes > result.LimitBytes);
    }

    [Fact]
    public void FingerprintsDetectChangedBytesAndCannotBeWrittenAfterCompletion()
    {
        using var left = new StreamingProbe.HashSink(); using var right = new StreamingProbe.HashSink();
        left.Write("abc"u8); right.Write("abd"u8);
        Assert.NotEqual(left.Finish(), right.Finish()); Assert.Equal(left.Finish(), left.Finish());
        Assert.Throws<InvalidOperationException>(() => left.Write(new byte[] { 1 }));
    }
}
