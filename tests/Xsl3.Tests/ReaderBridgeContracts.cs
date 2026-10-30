// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Xml;

namespace Xsl3.Tests;

// Shared by ordinary units and the trimmed native consumer. Fixture resources stay local to this test.
internal static class ReaderBridgeContracts
{
    internal static int Verify()
    {
        int checks = 0;
        void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
            checks++;
        }

        var resolver = new DtdResolver();
        const string source = "<!DOCTYPE doc SYSTEM 'external.dtd'><doc>&word;</doc>";
        using (var input = XmlReader.Create(new StringReader("<doc/>"),
            new XmlReaderSettings { XmlResolver = resolver, DtdProcessing = DtdProcessing.Parse }))
        {
            Check(XmlReaderInternals.TryGetTextReaderImplResolver(input, out XmlResolver? found)
                && ReferenceEquals(resolver, found), "XmlReader.Create resolver identity");
            Check(!XmlReaderInternals.IsTextReaderImplStrippingAllWhitespace(input), "XmlReader.Create whitespace policy");
            var copied = new QueryReaderSettings(input);
            using var reader = copied.CreateReader(new MemoryStream(Encoding.UTF8.GetBytes(source)), "https://example.invalid/source.xml");
            Check(ReadText(reader) == "resolved", "copied modern-reader resolver must resolve external DTD");
        }
        using (var input = new XmlTextReader(new StringReader("<doc/>"))
            { XmlResolver = resolver, DtdProcessing = DtdProcessing.Parse, WhitespaceHandling = WhitespaceHandling.None })
        {
            Check(ReferenceEquals(resolver, XmlReaderInternals.GetResolver(input)), "XmlTextReader resolver identity");
            var impl = (XmlReader)typeof(XmlTextReader).GetProperty("Impl", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(input)!;
            Check(XmlReaderInternals.IsTextReaderImplStrippingAllWhitespace(impl), "legacy reader whitespace policy");
            var copied = new QueryReaderSettings(input);
            using var reader = copied.CreateReader(new MemoryStream(Encoding.UTF8.GetBytes(source)), "https://example.invalid/source.xml");
            Check(ReadText(reader) == "resolved", "copied legacy-reader resolver must resolve external DTD");
        }
        using (var input = XmlReader.Create(new StringReader("<doc/>"), new XmlReaderSettings { XmlResolver = null }))
            Check(XmlReaderInternals.TryGetTextReaderImplResolver(input, out XmlResolver? found) && found == null,
                "modern reader explicit null resolver");
        using (var input = new XmlTextReader(new StringReader("<doc/>")) { XmlResolver = null })
            Check(XmlReaderInternals.GetResolver(input) == null, "legacy reader explicit null resolver");
        Check(resolver.Reads == 2, "both copied readers must use the supplied resolver");
        return checks;
    }

    private static string ReadText(XmlReader reader)
    {
        var text = new StringBuilder();
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Text) text.Append(reader.Value);
            // XmlTextReader can expose general entities separately; honor that captured policy.
            else if (reader.NodeType == XmlNodeType.EntityReference) reader.ResolveEntity();
        }
        return text.ToString();
    }

    private sealed class DtdResolver : XmlResolver
    {
        internal int Reads;
        public override ICredentials? Credentials { set { } }
        public override object GetEntity(Uri uri, string? role, Type? type)
        {
            if (uri.AbsoluteUri != "https://example.invalid/external.dtd")
                throw new XmlException("Unexpected test resource: " + uri);
            Reads++;
            return new MemoryStream(Encoding.UTF8.GetBytes("<!ELEMENT doc (#PCDATA)><!ENTITY word 'resolved'>"));
        }
    }
}
