// Licensed under the MIT license. See LICENSE.TXT in the repository root.
using System.Buffers;
using System.Text;
using System.Text.Json;

namespace Xsl3.Tests;

internal static partial class StreamingProbe
{
    internal static string SerializeReport(Result result)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString(nameof(Result.Workload), result.Workload);
            writer.WriteString(nameof(Result.Mode), result.Mode);
            writer.WriteString(nameof(Result.Status), result.Status);
            writer.WriteNumber(nameof(Result.RequestedInputBytes), result.RequestedInputBytes);
            writer.WriteNumber(nameof(Result.InputBytes), result.InputBytes);
            writer.WriteNumber(nameof(Result.Records), result.Records);
            writer.WriteNumber(nameof(Result.LimitBytes), result.LimitBytes);
            writer.WriteNumber(nameof(Result.PeakWorkingSetBytes), result.PeakWorkingSetBytes);
            writer.WriteString(nameof(Result.PeakMemorySource), result.PeakMemorySource);
            writer.WriteNumber(nameof(Result.LiveManagedBytes), result.LiveManagedBytes);
            writer.WritePropertyName(nameof(Result.ElapsedMs));
            if (result.ElapsedMs is double elapsed) WriteReportDouble(writer, elapsed);
            else writer.WriteNullValue();
            writer.WritePropertyName(nameof(Result.FirstOutputMs));
            if (result.FirstOutputMs is double firstOutput) WriteReportDouble(writer, firstOutput);
            else writer.WriteNullValue();
            if (result.InputBytesAtFirstOutput is long firstBytes) writer.WriteNumber(nameof(Result.InputBytesAtFirstOutput), firstBytes);
            else writer.WriteNull(nameof(Result.InputBytesAtFirstOutput));
            writer.WriteBoolean(nameof(Result.ValidOutput), result.ValidOutput);
            writer.WriteStartObject(nameof(Result.Outputs));
            foreach (var output in result.Outputs)
            {
                writer.WriteStartObject(output.Key);
                writer.WriteNumber(nameof(Fingerprint.Bytes), output.Value.Bytes);
                writer.WriteString(nameof(Fingerprint.Sha256), output.Value.Sha256);
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
            writer.WriteNumber(nameof(Result.AvailableManagedBytes), result.AvailableManagedBytes);
            writer.WriteBoolean(nameof(Result.DynamicCodeSupported), result.DynamicCodeSupported);
            writer.WriteBoolean(nameof(Result.HasTrustedPlatformAssemblies), result.HasTrustedPlatformAssemblies);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}
