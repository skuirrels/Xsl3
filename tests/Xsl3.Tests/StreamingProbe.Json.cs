// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Xsl3.Tests;

[JsonSourceGenerationOptions(Converters = new[] { typeof(StreamingProbe.ReportDoubleConverter) })]
[JsonSerializable(typeof(StreamingProbe.Result))]
internal partial class StreamingProbeJsonContext : JsonSerializerContext;

internal static partial class StreamingProbe
{
    internal sealed class ReportDoubleConverter : JsonConverter<double>
    {
        public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetDouble();

        public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options)
        {
            if (!double.IsFinite(value))
            {
                // Preserve the normal JSON writer's rejection of non-finite numbers.
                writer.WriteNumberValue(value);
                return;
            }
            // Keep reporting from loading CultureInfo through Utf8Formatter. This
            // remains a validated JSON number with the BCL's round-trip digits.
            writer.WriteRawValue(value.ToString("R", NumericFormat));
        }
    }
}
