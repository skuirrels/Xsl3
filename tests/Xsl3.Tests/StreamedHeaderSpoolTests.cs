// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml;
using System.Xml.Schema;
using System.Xml.XPath;
using Xsl3.Streaming;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

public class StreamedHeaderSpoolTests
{
    private static StreamedDocument Document(CancellationToken cancellation = default) =>
        new(XmlReader.Create(new StringReader("<root/>")), ownsReader: true, cancellation: cancellation);

    private static StreamedDocument.Node Header(StreamedDocument document, long number, string value = "value") =>
        new(number, XPathNodeType.Element, document.Root)
        {
            LocalName = "item", Prefix = "p", Namespace = "urn:spool", BaseUri = "file:///spool/input.xml", Language = "en",
            Attributes = new[] { new StreamedDocument.Attribute("key", "", "", value, null) },
            Namespaces = new[] { new StreamedDocument.Namespace("p", "urn:spool", number) },
            Ended = true, EmptyElement = true,
        };

    [Fact]
    public void IntrinsicHeadersKeepSourceIdentityNamesNamespacesAndTypedAttributes()
    {
        using var document = Document();
        var spool = document.HeaderSpool;
        var root = spool.AddOwner(document.Root);
        var header = Header(document, 1, "12.5");
        header.Attributes = new[] { header.Attributes[0] with { Schema = new XmlSchemaInfo { SchemaType = XmlSchemaType.GetBuiltInSimpleType(XmlTypeCode.Decimal) } } };
        var owner = spool.AddOwner(header, root);
        var original = new StreamedNavigator(document, header);
        var restored = spool.Read(owner);
        Assert.True(original.IsSamePosition(restored));
        Assert.Equal(XmlNodeOrder.Same, original.ComparePosition(restored));
        Assert.Equal("urn:spool", restored.LookupNamespace("p"));
        Assert.Equal(header.BaseUri, restored.BaseURI);
        Assert.Equal("en", restored.XmlLang);
        Assert.Same(document.NameTable.Get("item"), restored.LocalName);
        var attribute = spool.Read(owner, 0);
        Assert.True(original.MoveToFirstAttribute());
        Assert.True(original.IsSamePosition(attribute));
        Assert.Equal(12.5m, attribute.TypedValue);
        Assert.True(attribute.MoveToParent());
        Assert.True(attribute.MoveToParent());
        Assert.True(document.CreateNavigator().IsSamePosition(attribute));
        Assert.False(restored.MoveToFirstChild());
        Assert.Empty(spool.Files);
    }

    [Fact]
    public void MembershipLinksAndOwnerCompletionAreMutableWithoutRetainingNodes()
    {
        using var document = Document();
        var spool = document.HeaderSpool;
        var root = spool.AddOwner(document.Root);
        var header = Header(document, 1);
        header.Ended = header.EmptyElement = false;
        var owner = spool.AddOwner(header, root);
        Assert.Equal("stream-position", Assert.Throws<XdmException>(() => _ = spool.Read(owner).HasChildren).Code);
        var first = spool.AddCell(owner, -1, 2);
        var second = spool.AddCell(owner, 0, 2);
        spool.Link(first, second);
        spool.Select(first, 0);
        spool.Select(first, 0);
        spool.Select(second, 1);
        var bits = new bool[2];
        Assert.False(spool.Read(first, bits, out var readOwner, out int position, out var next));
        Assert.Equal(new[] { true, false }, bits);
        Assert.Equal(owner, readOwner);
        Assert.Equal(-1, position);
        Assert.Equal(second, next);
        header.Ended = true;
        header.FirstChild = 2;
        header.Schema = new XmlSchemaInfo { SchemaType = XmlSchemaType.GetBuiltInComplexType(XmlTypeCode.Item) };
        spool.UpdateOwner(owner, header);
        Assert.True(spool.Read(second, bits, out _, out position, out next));
        spool.Read(second, bits, out _, out _, out _, out bool sealedOwner);
        Assert.False(sealedOwner);
        spool.SealOwner(owner);
        spool.Read(second, bits, out _, out _, out _, out sealedOwner);
        Assert.True(sealedOwner);
        Assert.Equal(new[] { false, true }, bits);
        Assert.Equal(0, position);
        Assert.Null(next);
        var restored = spool.Read(owner);
        Assert.NotNull(restored.SchemaInfo);
        Assert.True(restored.HasChildren);
        // This child number is still live on some sources; a scratch header must
        // never use that coincidence to reopen downward navigation.
        Assert.Equal("stream-position", Assert.Throws<XdmException>(() => restored.MoveToFirstChild()).Code);
        Assert.Equal("stream-position", Assert.Throws<XdmException>(() => _ = restored.Value).Code);
    }

    [Fact]
    public void ManyClosedHeadersSpillWithinOneSharedCacheAndCloseWithTheirSource()
    {
        var document = Document();
        var spool = document.HeaderSpool;
        string[] files;
        try
        {
            var root = spool.AddOwner(document.Root);
            StreamedHeaderSpool.Owner first = default, last = default;
            for (int i = 1; i <= 10000; i++)
            {
                var owner = spool.AddOwner(Header(document, i), root);
                var cell = spool.AddCell(owner, 0, 3);
                spool.Select(cell, i % 3);
                if (i == 1) first = owner;
                last = owner;
            }
            Assert.Equal("value", spool.Read(first, 0).Value);
            Assert.Equal("value", spool.Read(last, 0).Value);
            Assert.InRange(spool.MemoryCapacity, 0, 2 * StreamedReplayStorage.MemoryLimit);
            files = spool.Files.ToArray();
            Assert.Equal(2, files.Length);
            Assert.All(files, file => Assert.True(File.Exists(file)));
            if (!OperatingSystem.IsWindows())
                foreach (string file in files)
                    Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
        }
        finally { document.Dispose(); }
        Assert.All(files, file => Assert.False(File.Exists(file)));
        Assert.Throws<ObjectDisposedException>(() => spool.Read(default(StreamedHeaderSpool.Owner)));
    }

    [Fact]
    public void SchemaInformationIsSnapshottedInsteadOfRetainingAMutableInfoObject()
    {
        using var document = Document();
        var spool = document.HeaderSpool;
        var root = spool.AddOwner(document.Root);
        var header = Header(document, 1);
        var info = new XmlSchemaInfo { SchemaType = XmlSchemaType.GetBuiltInSimpleType(XmlTypeCode.Decimal), IsDefault = true };
        header.Attributes = new[] { header.Attributes[0] with { Value = "12.5", Schema = info } };
        var owner = spool.AddOwner(header, root);
        info.SchemaType = XmlSchemaType.GetBuiltInSimpleType(XmlTypeCode.String);
        info.IsDefault = false;
        var restored = spool.Read(owner, 0);
        Assert.Equal(12.5m, restored.TypedValue);
        Assert.True(restored.SchemaInfo!.IsDefault);
    }

    [Fact]
    public void HandlesAndUpdatesCannotSilentlyChangeSourceIdentity()
    {
        using var document = Document();
        using var other = Document();
        var spool = document.HeaderSpool;
        var root = spool.AddOwner(document.Root);
        var header = Header(document, 1);
        var owner = spool.AddOwner(header, root);
        var cell = spool.AddCell(owner, 0, 2);
        var foreign = other.HeaderSpool.AddOwner(other.Root);
        Assert.Throws<ArgumentException>(() => spool.Read(foreign));
        Assert.Throws<ArgumentException>(() => spool.AddOwner(header));
        Assert.Throws<ArgumentException>(() => spool.AddOwner(Header(document, 2), owner));
        Assert.Throws<ArgumentException>(() => spool.UpdateOwner(owner, Header(document, 2)));
        Assert.Throws<ArgumentOutOfRangeException>(() => spool.AddCell(owner, 1, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => spool.Select(cell, 2));
        Assert.Throws<ArgumentException>(() => spool.Link(cell, cell));
        Assert.Throws<ArgumentException>(() => spool.Read(cell, new bool[1], out _, out _, out _));
        Assert.Equal("value", spool.Read(owner, 0).Value);
    }

    [Fact]
    public void ObserverMetadataIsRejectedExplicitlyRatherThanDropped()
    {
        using var document = Document();
        var spool = document.HeaderSpool;
        var root = spool.AddOwner(document.Root);
        var header = Header(document, 1);
        var owner = spool.AddOwner(header, root);
        header.ObserverValues = new object();
        Assert.Equal("stream-header-metadata", Assert.Throws<XdmException>(() => spool.UpdateOwner(owner, header)).Code);
        Assert.Equal("value", spool.Read(owner, 0).Value);
    }

    [Fact]
    public void CancellationIsObservedBeforeReadingOrUpdatingScratchRecords()
    {
        using var cancellation = new CancellationTokenSource();
        using var document = Document(cancellation.Token);
        var spool = document.HeaderSpool;
        var root = spool.AddOwner(document.Root);
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => spool.Read(root));
        Assert.Throws<OperationCanceledException>(() => spool.AddOwner(Header(document, 1), root));
    }
}
