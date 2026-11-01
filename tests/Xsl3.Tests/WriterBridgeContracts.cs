// Licensed under the MIT license. See LICENSE.TXT in the repository root.
using System;
using System.IO;
using System.Reflection;
using System.Linq;
using Xsl3.Runtime;
using System.Xml;

namespace Xsl3.Tests;

internal static class WriterBridgeContracts
{
    internal static int Verify()
    {
        int checks = 0;
        void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
            checks++;
        }
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        PropertyInfo standalone = typeof(XmlWriterSettings).GetProperty("Standalone", flags)!;
        PropertyInfo indent = typeof(XmlWriterSettings).GetProperty("IndentInternal", flags)!;
        int Actual(XmlWriterSettings settings, PropertyInfo property) => Convert.ToInt32(property.GetValue(settings));
        var reference = new XmlWriterSettings();
        Check(Actual(reference, standalone) == (int)XmlStandalone.Omit, "BCL constructor standalone default");
        Check(Actual(reference, indent) == (int)TriState.Unknown, "BCL constructor indent default");
        foreach (XmlStandalone value in new[] { XmlStandalone.Omit, XmlStandalone.No, XmlStandalone.Yes })
        {
            var settings = XmlWriterSettingsInternals.CreateDefaultSettings();
            settings.Standalone = value;
            Check(settings.Standalone == value && Actual(settings, standalone) == (int)value, "bridge and real standalone field agree");
            var copied = settings.CloneWithQueryDefaults();
            Check(!copied.ReadOnly && copied.Standalone == value && Actual(copied, standalone) == (int)value, "controlled clone preserves real values");
            var publicCopy = settings.Clone();
            Check(publicCopy.Standalone == value && Actual(publicCopy, standalone) == (int)value, "ordinary public clone fallback");
            copied.Indent = true;
            copied.Reset();
            Check(copied.Standalone == XmlStandalone.Omit && Actual(copied, standalone) == (int)XmlStandalone.Omit
                && copied.IndentInternal == TriState.Unknown, "public reset must not leave stale non-default metadata");
            string Render(XmlWriterSettings options)
            {
                using var text = new StringWriter();
                using (var writer = XmlWriter.Create(text, options))
                {
                    writer.WriteStartDocument(); writer.WriteElementString("doc", "value"); writer.WriteEndDocument();
                }
                return text.ToString();
            }
            var stock = new XmlWriterSettings();
            standalone.SetValue(stock, Enum.ToObject(standalone.PropertyType, (int)value));
            Check(Render(settings) == Render(stock), "ordinary BCL writer output matches an independently populated stock settings object");
        }
        var frozen = XmlWriterSettingsInternals.CreateDefaultSettings();
        frozen.ReadOnly = true;
        reference.ReadOnly = true;
        Exception Capture(Action action)
        {
            try { action(); }
            catch (Exception error) { return error; }
            throw new InvalidOperationException("Expected read-only failure");
        }
        Exception expected = Capture(() => standalone.SetValue(reference, Enum.ToObject(standalone.PropertyType, (int)XmlStandalone.Omit)));
        Exception actual = Capture(() => frozen.Standalone = XmlStandalone.Omit);
        Check(actual.GetType() == expected.GetType(), "read-only exception wrapper is preserved");
        Check(actual.InnerException?.GetType() == expected.InnerException?.GetType(), "read-only primary exception is preserved");
        Check(!frozen.CloneWithQueryDefaults().ReadOnly, "controlled clone stays publicly mutable");
        var mutable = XmlWriterSettingsInternals.CreateDefaultSettings();
        mutable.Indent = true;
        Check(mutable.IndentInternal == TriState.True, "public indent setter remains authoritative");
        mutable.Reset();
        Check(mutable.IndentInternal == TriState.Unknown, "public reset restores the real unknown indent state");
        frozen.IndentInternal = TriState.True;
        Check(frozen.ReadOnly && Actual(frozen, indent) == (int)TriState.True,
            "private indent setter retains its read-only auto-property behavior");
        frozen.IndentInternal = TriState.False;
        Check(frozen.ReadOnly && Actual(frozen, indent) == (int)TriState.False,
            "private indent setter preserves the false state on read-only objects");
        byte[] Serialize(XmlWriterSettings settings)
        {
            using var bytes = new MemoryStream();
            using var writer = new XmlQueryDataWriter(bytes);
            settings.GetObjectData(writer); writer.Flush();
            return bytes.ToArray();
        }
        foreach (TriState indentState in new[] { TriState.Unknown, TriState.False, TriState.True })
        foreach (XmlStandalone standaloneState in new[] { XmlStandalone.Omit, XmlStandalone.No, XmlStandalone.Yes })
        foreach (bool readOnly in new[] { false, true })
        {
            var original = new XmlWriterSettings();
            original.IndentInternal = indentState;
            original.Standalone = standaloneState;
            original.ReadOnly = readOnly;
            byte[] bytes = Serialize(original);
            using var reader = new XmlQueryDataReader(new MemoryStream(bytes));
            XmlWriterSettings loaded = XmlWriterSettingsInternals.Create(reader);
            Check(Serialize(loaded).SequenceEqual(bytes) && Actual(loaded, indent) == (int)indentState
                && Actual(loaded, standalone) == (int)standaloneState,
                "saved writer header preserves actual tri-state/standalone/read-only fields byte for byte");
        }
        return checks;
    }
}
