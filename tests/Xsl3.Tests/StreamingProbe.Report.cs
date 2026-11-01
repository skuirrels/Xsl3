// Licensed under the MIT license. See LICENSE.TXT in the repository root.
using System;
using System.Text;

namespace Xsl3.Tests;

internal static partial class StreamingProbe
{
    internal static string SerializeReport(Result result)
    {
        var text = new StringBuilder();
        text.Append('{');
        StringProperty(text, nameof(Result.Workload), result.Workload, first: true);
        StringProperty(text, nameof(Result.Mode), result.Mode);
        StringProperty(text, nameof(Result.Status), result.Status);
        IntegerProperty(text, nameof(Result.RequestedInputBytes), result.RequestedInputBytes);
        IntegerProperty(text, nameof(Result.InputBytes), result.InputBytes);
        IntegerProperty(text, nameof(Result.Records), result.Records);
        IntegerProperty(text, nameof(Result.LimitBytes), result.LimitBytes);
        IntegerProperty(text, nameof(Result.PeakWorkingSetBytes), result.PeakWorkingSetBytes);
        StringProperty(text, nameof(Result.PeakMemorySource), result.PeakMemorySource);
        IntegerProperty(text, nameof(Result.LiveManagedBytes), result.LiveManagedBytes);
        DoubleProperty(text, nameof(Result.ElapsedMs), result.ElapsedMs);
        DoubleProperty(text, nameof(Result.FirstOutputMs), result.FirstOutputMs);
        PropertyName(text, nameof(Result.InputBytesAtFirstOutput));
        text.Append(result.InputBytesAtFirstOutput is long bytes ? bytes.ToString(NumericFormat) : "null");
        BooleanProperty(text, nameof(Result.ValidOutput), result.ValidOutput);
        PropertyName(text, nameof(Result.Outputs));
        text.Append('{');
        bool first = true;
        foreach (var output in result.Outputs)
        {
            PropertyName(text, output.Key, first);
            first = false;
            text.Append('{');
            IntegerProperty(text, nameof(Fingerprint.Bytes), output.Value.Bytes, first: true);
            StringProperty(text, nameof(Fingerprint.Sha256), output.Value.Sha256);
            text.Append('}');
        }
        text.Append('}');
        IntegerProperty(text, nameof(Result.AvailableManagedBytes), result.AvailableManagedBytes);
        BooleanProperty(text, nameof(Result.DynamicCodeSupported), result.DynamicCodeSupported);
        BooleanProperty(text, nameof(Result.HasTrustedPlatformAssemblies), result.HasTrustedPlatformAssemblies);
        return text.Append('}').ToString();
    }

    private static void PropertyName(StringBuilder text, string name, bool first = false)
    {
        if (!first) text.Append(',');
        StringValue(text, name);
        text.Append(':');
    }

    private static void StringValue(StringBuilder text, string? value)
    {
        if (value == null) { text.Append("null"); return; }
        text.Append('"');
        for (int i = 0; i < value.Length; i++)
        {
            char current = value[i];
            // ASCII JSON permits these characters verbatim. Escape everything else
            // so Console's encoder sees only ASCII, including supplementary scalars.
            if (current >= ' ' && current <= '~' && current != '"' && current != '\\')
                text.Append(current);
            else if (char.IsHighSurrogate(current))
            {
                if (i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                {
                    Escape(text, current);
                    Escape(text, value[++i]);
                }
                else Escape(text, '\uFFFD');
            }
            else Escape(text, char.IsLowSurrogate(current) ? '\uFFFD' : current);
        }
        text.Append('"');
    }

    private static void Escape(StringBuilder text, char value)
    {
        const string hex = "0123456789ABCDEF";
        text.Append("\\u").Append(hex[value >> 12]).Append(hex[value >> 8 & 15])
            .Append(hex[value >> 4 & 15]).Append(hex[value & 15]);
    }

    private static void StringProperty(StringBuilder text, string name, string? value, bool first = false)
    {
        PropertyName(text, name, first);
        StringValue(text, value);
    }

    private static void IntegerProperty(StringBuilder text, string name, long value, bool first = false)
    {
        PropertyName(text, name, first);
        text.Append(value.ToString(NumericFormat));
    }

    private static void DoubleProperty(StringBuilder text, string name, double? value)
    {
        PropertyName(text, name);
        if (value == null) text.Append("null");
        else
        {
            if (!double.IsFinite(value.Value)) throw new ArgumentException("A JSON report number must be finite", nameof(value));
            // Finite BCL round-trip digits are JSON numbers; no locale or parsing fallback.
            text.Append(value.Value.ToString("R", NumericFormat));
        }
    }

    private static void BooleanProperty(StringBuilder text, string name, bool value)
    {
        PropertyName(text, name);
        text.Append(value ? "true" : "false");
    }
}
