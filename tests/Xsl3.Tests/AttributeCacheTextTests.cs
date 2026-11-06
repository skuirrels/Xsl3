// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Xml;
using System.Xml.Schema;
using Xsl3.Runtime;
using Xunit;

namespace Xsl3.Tests;

public class AttributeCacheTextTests
{
    [Theory]
    [InlineData(false, 1)]
    [InlineData(false, 40)]
    [InlineData(true, 1)]
    [InlineData(true, 40)]
    public void ReusePreservesEmptyMultipartTypedAndOverriddenAttributes(bool holdLateNamespaces, int width)
    {
        var cache = new XmlAttributeCache { HoldLateNamespaces = holdLateNamespaces };
        for (int cycle = 0; cycle < 3; cycle++)
        {
            var sink = new RecordingWriter();
            XmlRawWriter output = cache;
            int removed = 0;
            cache.OnRemoveWriterEvent = writer =>
            {
                Assert.Same(sink, writer);
                output = writer;
                removed++;
            };
            cache.Init(sink);
            XmlAtomicValue typed = XmlAtomicValueInternals.Create(XmlSchemaType.GetBuiltInSimpleType(XmlTypeCode.Int), cycle + 7);
            Write(cache, "p", "a", "urn:one", "discard", typed, "discard-tail");
            Write(cache, "", "aa", "", "same-first-letter");
            Write(cache, "", "none", "");
            Write(cache, "", "empty", "", "");
            Write(cache, "", "typed", "", typed, ":end");
            Write(cache, "", "mixed", "", "", typed, "tail");
            for (int i = 0; i < width; i++) Write(cache, "", "k" + i, "", "value" + cycle + ":" + i);
            Write(cache, "r", "a", "urn:other", "other-namespace");
            Write(cache, "q", "a", "urn:one", "winner" + cycle, ":last");
            Write(cache, "", "k0", "", "replacement");
            output.WriteNamespaceDeclaration("late", "urn:late");
            output.StartElementContent();

            var expected = new List<string>();
            if (holdLateNamespaces) expected.Add("namespace|late|urn:late");
            Expected(expected, "", "aa", "", "text|same-first-letter");
            Expected(expected, "", "none", "");
            Expected(expected, "", "empty", "", "text|");
            Expected(expected, "", "typed", "", "value|" + typed.Value, "text|:end");
            Expected(expected, "", "mixed", "", "text|", "value|" + typed.Value, "text|tail");
            for (int i = 1; i < width; i++) Expected(expected, "", "k" + i, "", "text|value" + cycle + ":" + i);
            Expected(expected, "r", "a", "urn:other", "text|other-namespace");
            Expected(expected, "q", "a", "urn:one", "text|winner" + cycle, "text|:last");
            Expected(expected, "", "k0", "", "text|replacement");
            if (!holdLateNamespaces) expected.Add("namespace|late|urn:late");
            expected.Add("content");
            Assert.Equal(expected, sink.Events);
            Assert.Equal(1, removed);
            Assert.Same(sink, output);
            Assert.Equal(2, sink.TypedValues.Count);
            Assert.All(sink.TypedValues, value => Assert.Same(typed, value));
        }
    }

    private static void Write(XmlAttributeCache cache, string prefix, string name, string ns, params object[] parts)
    {
        cache.WriteStartAttribute(prefix, name, ns);
        foreach (object part in parts)
            if (part is string text) cache.WriteString(text);
            else cache.WriteValue(part);
        cache.WriteEndAttribute();
    }

    private static void Expected(List<string> events, string prefix, string name, string ns, params string[] parts)
    {
        events.Add($"start|{prefix}|{name}|{ns}");
        events.AddRange(parts);
        events.Add("end");
    }

    private sealed class RecordingWriter : XmlRawWriter
    {
        internal readonly List<string> Events = new();
        internal readonly List<object> TypedValues = new();
        public override void WriteStartAttribute(string? prefix, string localName, string? ns) => Events.Add($"start|{prefix}|{localName}|{ns}");
        public override void WriteEndAttribute() => Events.Add("end");
        public override void WriteString(string? text) => Events.Add("text|" + text);
        public override void WriteValue(object value)
        {
            TypedValues.Add(value);
            Events.Add("value|" + ((XmlAtomicValue)value).Value);
        }
        internal override void WriteNamespaceDeclaration(string prefix, string ns) => Events.Add($"namespace|{prefix}|{ns}");
        internal override void StartElementContent() => Events.Add("content");
        public override void WriteStartElement(string? prefix, string localName, string? ns) => throw new NotSupportedException();
        internal override void WriteEndElement(string prefix, string localName, string ns) => throw new NotSupportedException();
        public override void WriteComment(string? text) => throw new NotSupportedException();
        public override void WriteProcessingInstruction(string name, string? text) => throw new NotSupportedException();
        public override void WriteEntityRef(string name) => throw new NotSupportedException();
        public override void Flush() { }
        public override void Close() { }
    }
}
