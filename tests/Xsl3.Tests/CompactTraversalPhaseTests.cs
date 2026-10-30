// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Xml;
using System.Xml.XPath;
using Xsl3.Tree;
using Xunit;

namespace Xsl3.Tests;

public sealed class CompactTraversalPhaseTests
{
    private static void Check(XPathNavigator root)
    {
        long next = 0;
        void Visit(XPathNavigator node)
        {
            var phases = ((CompactNavigator)node).TraversalPhases;
            Assert.Equal(next++, phases.Before);
            XPathNavigator child = node.Clone();
            if (child.MoveToFirstChild())
                do { Visit(child); } while (child.MoveToNext());
            Assert.Equal(next++, phases.After);
        }
        Visit(root);
        Assert.Equal(2L * root.Select("descendant-or-self::node()").Count, next);
    }

    [Theory]
    [InlineData("<root/> ")]
    [InlineData("<?before x?><root a='v' xmlns:p='urn:p'>text<p:child/><!--comment--><next><?inside x?>tail</next></root>")]
    public void PhasesMatchTheObservedDepthFirstEvents(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml));
        Check(CompactDocument.Load(reader, XmlSpace.Preserve).CreateNavigator());
    }

    [Fact]
    public void FragmentPhasesStartAtTheirElementWithoutADocumentWrapper()
    {
        var (writer, root) = CompactDocument.TemporaryTree(new NameTable(), "urn:fragment", fragment: true, noDocumentUri: true, compact: true);
        writer.WriteStartElement("", "outer", "");
        writer.WriteStartElement("", "inner", "");
        writer.WriteString("text");
        writer.WriteEndElement("", "inner", "");
        writer.WriteEndElement("", "outer", "");
        writer.Close();
        Assert.Equal(XPathNodeType.Element, root().NodeType);
        Check(root());
    }

    [Fact]
    public void AttributeAndNamespacePositionsHaveNoContentTraversalEvents()
    {
        using var reader = XmlReader.Create(new StringReader("<root a='v' xmlns:p='urn:p'/>"));
        var nav = (CompactNavigator)CompactDocument.Load(reader).CreateNavigator();
        Assert.True(nav.MoveToFirstChild());
        Assert.True(nav.MoveToFirstAttribute());
        Assert.Throws<InvalidOperationException>(() => nav.TraversalPhases);
        Assert.True(nav.MoveToParent());
        Assert.True(nav.MoveToNamespace("p"));
        Assert.Throws<InvalidOperationException>(() => nav.TraversalPhases);
    }
}
