// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Xml;
using System.Xml.XPath;
using Xsl3.Runtime;
using Xsl3.Streaming;
using Xunit;

namespace Xsl3.Tests;

public class StreamedNodeKindTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CursorKindsMatchTreeAcrossMovementCloningAndHeaderQueries(bool replay)
    {
        const string xml = "<?before v?><doc xmlns:p='urn:p' a='1' p:b='2' xml:space='preserve'> a<![CDATA[b]]>c<!--note--><?pi value?><n/><plain xml:space='default'> </plain> \t</doc><?after v?>";
        using var input = new CountingReader(XmlReader.Create(new StringReader(xml)));
        using var document = new StreamedDocument(input);
        var cursor = (StreamedNavigator)document.CreateNavigator();
        if (replay) cursor = cursor.BufferSubtree();
        using var treeInput = XmlReader.Create(new StringReader(xml));
        var tree = new XPathDocument(treeInput, XmlSpace.Preserve).CreateNavigator();
        var expected = Trace(tree, null);
        var actual = Trace(cursor, input);
        Assert.Equal(expected, actual);
        Assert.Equal(XPathNodeType.Root, cursor.NodeType);
        Assert.False(cursor.MoveToParent());
        Assert.Equal(XPathNodeType.Root, cursor.NodeType);
        Assert.NotEqual(ReadState.Closed, input.ReadState);
    }

    private static List<string> Trace(XPathNavigator cursor, CountingReader? input)
    {
        var trace = new List<string>();
        Visit();
        return trace;

        void Visit()
        {
            int reads = input?.Reads ?? 0;
            XPathNodeType kind = cursor.NodeType;
            Assert.Equal(kind, cursor.Clone().NodeType);
            var moved = cursor.Clone();
            moved.MoveToRoot();
            Assert.Equal(XPathNodeType.Root, moved.NodeType);
            Assert.True(moved.MoveTo(cursor));
            Assert.Equal(kind, moved.NodeType);
            Assert.True(moved.IsSamePosition(cursor));
            if (kind == XPathNodeType.Element)
            {
                var header = cursor.Clone();
                Assert.False(header.MoveToAttribute("missing", ""));
                Assert.Equal(kind, header.NodeType);
                Assert.False(header.MoveToNamespace("missing"));
                Assert.Equal(kind, header.NodeType);
                int attributes = 0;
                if (header.MoveToFirstAttribute())
                {
                    string firstName = header.LocalName, firstNamespace = header.NamespaceURI;
                    do
                    {
                        Assert.Equal(XPathNodeType.Attribute, header.NodeType);
                        Assert.Equal(XPathNodeType.Attribute, header.Clone().NodeType);
                        if (cursor is StreamedNavigator streamed)
                            Assert.Equal(XPathNodeType.Attribute, streamed.AtHeaderAttribute(attributes).NodeType);
                        attributes++;
                    } while (header.MoveToNextAttribute());
                    Assert.Equal(XPathNodeType.Attribute, header.NodeType);
                    Assert.True(header.MoveToParent());
                    Assert.Equal(kind, header.NodeType);
                    Assert.True(header.MoveToAttribute(firstName, firstNamespace));
                    Assert.Equal(XPathNodeType.Attribute, header.NodeType);
                    Assert.True(header.MoveToParent());
                }
                if (cursor is StreamedNavigator content)
                    Assert.Equal(kind, content.AtHeaderAttribute(-1).NodeType);
                if (header.MoveToFirstNamespace(XPathNamespaceScope.All))
                {
                    do
                    {
                        Assert.Equal(XPathNodeType.Namespace, header.NodeType);
                        Assert.Equal(XPathNodeType.Namespace, header.Clone().NodeType);
                    } while (header.MoveToNextNamespace(XPathNamespaceScope.All));
                    Assert.Equal(XPathNodeType.Namespace, header.NodeType);
                    Assert.True(header.MoveToParent());
                    Assert.Equal(kind, header.NodeType);
                }
                Assert.True(header.MoveToNamespace("xml"));
                Assert.Equal(XPathNodeType.Namespace, header.NodeType);
                header.MoveToRoot();
                Assert.Equal(XPathNodeType.Root, header.NodeType);
            }
            Assert.Equal(reads, input?.Reads ?? 0);
            trace.Add(kind + "|" + cursor.LocalName + "|" + (kind is XPathNodeType.Element or XPathNodeType.Root ? "" : cursor.Value));
            if (cursor.MoveToFirstChild())
            {
                do { Visit(); } while (cursor.MoveToNext());
                Assert.True(cursor.MoveToParent());
                Assert.Equal(kind, cursor.NodeType);
            }
        }
    }

    [Theory]
    [InlineData("element", false)]
    [InlineData("attribute", false)]
    [InlineData("namespace", false)]
    [InlineData("element", true)]
    [InlineData("attribute", true)]
    [InlineData("namespace", true)]
    public void FrozenForkPositionsKeepKindsAfterSourceAndCloneMovement(string position, bool replay)
    {
        using var input = new CountingReader(XmlReader.Create(new StringReader("<doc xmlns:p='urn:p' id='a'><p:n>value</p:n></doc>")));
        using var source = new StreamedDocument(input);
        var cursor = (StreamedNavigator)source.CreateNavigator();
        Assert.True(cursor.MoveToFirstChild());
        if (replay) cursor = cursor.BufferSubtree();
        var expected = XPathNodeType.Element;
        if (position == "attribute") { Assert.True(cursor.MoveToAttribute("id", "")); expected = XPathNodeType.Attribute; }
        if (position == "namespace") { Assert.True(cursor.MoveToNamespace("p")); expected = XPathNodeType.Namespace; }
        var copy = (StreamedNavigator)cursor.Clone();
        var frozen = cursor.FreezeForkPosition(new StreamedDocument.ForkCapture(), CancellationToken.None);
        cursor.MoveToRoot();
        Assert.Equal(XPathNodeType.Root, cursor.NodeType);
        Assert.Equal(expected, copy.NodeType);
        Assert.Equal(expected, copy.InFork(source).NodeType);
        var bound = frozen.InFork(null, new StreamedDocument.ForkViews());
        using var boundDocument = bound.Document;
        Assert.Equal(expected, bound.NodeType);
        Assert.True(copy.IsSamePosition(bound));
        if (replay)
        {
            var rebound = copy.InFork(boundDocument);
            Assert.Equal(expected, rebound.NodeType);
            Assert.True(bound.IsSamePosition(rebound));
        }
        else
        {
            // A header-only view is not a live fork input. Binding its frozen
            // position succeeds above; seeking through the live-fork API must fail.
            Assert.Equal("stream-position", Assert.Throws<Xsl3.Xdm.XdmException>(() => copy.InFork(boundDocument)).Code);
        }
        int reads = input.Reads;
        Assert.False(bound.MoveTo(new XPathDocument(new StringReader("<other/>")).CreateNavigator()));
        Assert.Equal(expected, bound.NodeType);
        source.Dispose();
        Assert.Equal(expected, copy.NodeType);
        Assert.Equal(expected, bound.Clone().NodeType);
        bound.MoveToRoot();
        Assert.Equal(XPathNodeType.Root, bound.NodeType);
        Assert.Equal(reads, input.Reads);
        Assert.NotEqual(ReadState.Closed, input.ReadState);
    }

    private sealed class CountingReader(XmlReader input) : XmlWrappingReader(input)
    {
        internal int Reads { get; private set; }
        public override bool Read() { Reads++; return base.Read(); }
    }
}
