// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.XPath;
using Xsl3.Tree;
using Xunit;

namespace Xsl3.Tests;

/// <summary>
/// The compact tree (SPEC §14.1) reads as XPathDocument does: both are walked in step over every node and compared on
/// what a navigator reports, and on the moves the engine makes (name tests, following scans, order).
/// </summary>
public class CompactTreeTests
{
    private const string Mixed = """
        <?xml version="1.0"?>
        <!DOCTYPE doc [
          <!ATTLIST item key ID #IMPLIED>
        ]>
        <?top pi?>
        <doc xmlns="urn:d" xmlns:a="urn:a" a:x="1" plain="two">
          <!-- a comment -->
          <item key="k1" a:y="3">text &amp; more<![CDATA[ <cdata> ]]>tail</item>
          <item key="k2"/>
          <a:item xmlns:a="urn:a2" xmlns:b="urn:b">
            <b:leaf xml:lang="en" xml:space="preserve">  spaced  </b:leaf>
            <leaf xmlns="">undeclared default</leaf>
          </a:item>
          <deep><x><y><z>1</z><z>2</z></y></x><x/></deep>
          <?inner data here?>
          <mixed>a<b>b</b>c<b/>d</mixed>
        </doc>
        <!-- after -->
        """;

    public static IEnumerable<object[]> Documents()
    {
        yield return new object[] { Mixed, XmlSpace.Default };
        yield return new object[] { Mixed, XmlSpace.Preserve };
        yield return new object[] { "<r><a/><a><b/></a><c>x</c></r>", XmlSpace.Default };
        yield return new object[] { "<r>\n  <w>  </w>\n  <t> t </t>\n</r>", XmlSpace.Preserve };
        var sb = new StringBuilder("<orders>");
        for (int i = 0; i < 50; i++)
        {
            sb.Append($"<order id=\"{i}\"><customer>C{i % 7}</customer><line sku=\"P{i % 5}\" qty=\"{i % 3 + 1}\"/></order>");
        }
        yield return new object[] { sb.Append("</orders>").ToString(), XmlSpace.Default };
    }

    private static (XPathNavigator Expected, XPathNavigator Actual) Load(string xml, XmlSpace space)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Parse };
        using XmlReader r1 = XmlReader.Create(new StringReader(xml), settings, "file:///base/doc.xml");
        var expected = new XPathDocument(r1, space).CreateNavigator();
        using XmlReader r2 = XmlReader.Create(new StringReader(xml), settings, "file:///base/doc.xml");
        var actual = CompactDocument.Load(r2, space).CreateNavigator();
        return (expected, actual);
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public void EveryNodeReadsTheSame(string xml, XmlSpace space)
    {
        var (expected, actual) = Load(xml, space);
        var expectedNodes = new List<XPathNavigator>();
        var actualNodes = new List<XPathNavigator>();
        Walk(expected, actual, expectedNodes, actualNodes);

        // Document order agrees on every pair, attributes and namespaces included.
        for (int i = 0; i < expectedNodes.Count; i++)
        {
            for (int j = 0; j < expectedNodes.Count; j += 1 + expectedNodes.Count / 40)
            {
                XmlNodeOrder order = expectedNodes[i].ComparePosition(expectedNodes[j]);
                Assert.True(order == actualNodes[i].ComparePosition(actualNodes[j]),
                    $"{Describe(expectedNodes[i])} vs {Describe(expectedNodes[j])}: {order}, compact {actualNodes[i].ComparePosition(actualNodes[j])}");
                Assert.Equal(expectedNodes[i].IsSamePosition(expectedNodes[j]), actualNodes[i].IsSamePosition(actualNodes[j]));
                Assert.Equal(expectedNodes[i].IsDescendant(expectedNodes[j]), actualNodes[i].IsDescendant(actualNodes[j]));
            }
        }
    }

    /// <summary>Compares the two current nodes, then recurses into namespaces, attributes and children.</summary>
    private static void Walk(XPathNavigator e, XPathNavigator a, List<XPathNavigator> en, List<XPathNavigator> an)
    {
        Same(e, a);
        en.Add(e.Clone());
        an.Add(a.Clone());
        foreach (XPathNamespaceScope scope in new[] { XPathNamespaceScope.All, XPathNamespaceScope.ExcludeXml, XPathNamespaceScope.Local })
        {
            XPathNavigator ec = e.Clone(), ac = a.Clone();
            bool more = ec.MoveToFirstNamespace(scope);
            Assert.Equal(more, ac.MoveToFirstNamespace(scope));
            while (more)
            {
                Same(ec, ac);
                XPathNavigator owner = ac.Clone();
                Assert.True(owner.MoveToParent());
                Assert.True(owner.IsSamePosition(a));
                if (scope == XPathNamespaceScope.All)
                {
                    en.Add(ec.Clone());
                    an.Add(ac.Clone());
                }
                more = ec.MoveToNextNamespace(scope);
                Assert.Equal(more, ac.MoveToNextNamespace(scope));
            }
        }
        {
            XPathNavigator ec = e.Clone(), ac = a.Clone();
            bool more = ec.MoveToFirstAttribute();
            Assert.Equal(more, ac.MoveToFirstAttribute());
            while (more)
            {
                Same(ec, ac);
                en.Add(ec.Clone());
                an.Add(ac.Clone());
                Assert.Equal(e.GetAttribute(ec.LocalName, ec.NamespaceURI), a.GetAttribute(ac.LocalName, ac.NamespaceURI));
                XPathNavigator byName = a.Clone();
                Assert.True(byName.MoveToAttribute(ac.LocalName, ac.NamespaceURI));
                Assert.True(byName.IsSamePosition(ac));
                more = ec.MoveToNextAttribute();
                Assert.Equal(more, ac.MoveToNextAttribute());
            }
        }
        {
            XPathNavigator ec = e.Clone(), ac = a.Clone();
            bool more = ec.MoveToFirstChild();
            Assert.Equal(more, ac.MoveToFirstChild());
            while (more)
            {
                Walk(ec, ac, en, an);
                Moves(ec, ac);
                XPathNavigator ep = ec.Clone(), ap = ac.Clone();
                Assert.Equal(ep.MoveToParent(), ap.MoveToParent());
                Assert.True(ap.IsSamePosition(a));
                ep = ec.Clone();
                ap = ac.Clone();
                bool previous = ep.MoveToPrevious();
                Assert.Equal(previous, ap.MoveToPrevious());
                if (previous)
                {
                    Same(ep, ap);
                }
                more = ec.MoveToNext();
                Assert.Equal(more, ac.MoveToNext());
            }
        }
    }

    /// <summary>The name and kind tests, from a content node, give the same nodes.</summary>
    private static void Moves(XPathNavigator e, XPathNavigator a)
    {
        foreach (var (local, ns) in new[] { ("item", "urn:d"), ("leaf", ""), ("z", "urn:d"), ("customer", ""), ("b", "urn:d"), ("nothing", "") })
        {
            XPathNavigator ec = e.Clone(), ac = a.Clone();
            Assert.Equal(ec.MoveToChild(local, ns), ac.MoveToChild(local, ns));
            Same(ec, ac);
            ec = e.Clone();
            ac = a.Clone();
            Assert.Equal(ec.MoveToNext(local, ns), ac.MoveToNext(local, ns));
            Same(ec, ac);
            ec = e.Clone();
            ac = a.Clone();
            bool found;
            while ((found = ec.MoveToFollowing(local, ns)) | ac.MoveToFollowing(local, ns))
            {
                Assert.True(found);
                Same(ec, ac);
            }
        }
        foreach (XPathNodeType type in new[] { XPathNodeType.Element, XPathNodeType.Text, XPathNodeType.Comment, XPathNodeType.All })
        {
            XPathNavigator ec = e.Clone(), ac = a.Clone();
            Assert.Equal(ec.MoveToChild(type), ac.MoveToChild(type));
            Same(ec, ac);
            ec = e.Clone();
            ac = a.Clone();
            XPathNavigator eEnd = e.Clone(), aEnd = a.Clone();
            bool hasEnd = eEnd.MoveToParent() & aEnd.MoveToParent() && eEnd.MoveToNext() & aEnd.MoveToNext();
            bool found;
            while ((found = ec.MoveToFollowing(type, hasEnd ? eEnd : null)) | ac.MoveToFollowing(type, hasEnd ? aEnd : null))
            {
                Assert.True(found);
                Same(ec, ac);
            }
        }
    }

    private static string Describe(XPathNavigator n)
    {
        XPathNavigator p = n.Clone();
        p.MoveToParent();
        return $"{n.NodeType} {n.Name}={n.Value} (of {p.Name})";
    }

    private static void Same(XPathNavigator e, XPathNavigator a)
    {
        Assert.Equal(e.NodeType, a.NodeType);
        Assert.Equal(e.LocalName, a.LocalName);
        Assert.Equal(e.NamespaceURI, a.NamespaceURI);
        Assert.Equal(e.Prefix, a.Prefix);
        Assert.Equal(e.Name, a.Name);
        Assert.Equal(e.Value, a.Value);
        Assert.Equal(e.BaseURI, a.BaseURI);
        Assert.Equal(e.IsEmptyElement, a.IsEmptyElement);
        Assert.Equal(e.HasAttributes, a.HasAttributes);
        Assert.Equal(e.HasChildren, a.HasChildren);
        Assert.Equal(e.XmlLang, a.XmlLang);
    }

    [Fact]
    public void DtdIdsAndXmlId()
    {
        var (expected, actual) = Load(Mixed, XmlSpace.Default);
        foreach (string id in new[] { "k1", "k2", "missing" })
        {
            XPathNavigator e = expected.Clone(), a = actual.Clone();
            Assert.Equal(e.MoveToId(id), a.MoveToId(id));
            Same(e, a);
        }
    }

    [Fact]
    public void TakesLessMemoryThanXPathDocument()
    {
        var sb = new StringBuilder("<orders>");
        for (int i = 0; i < 5000; i++)
        {
            sb.Append($"<order id=\"{i}\"><customer>Customer {i % 250:D5}</customer><total>{i % 997}.{i % 100:D2}</total><line sku=\"P{i % 500}\" qty=\"{i % 9 + 1}\"/></order>");
        }
        string xml = sb.Append("</orders>").ToString();
        long before = System.GC.GetTotalMemory(true);
        var x = new XPathDocument(new StringReader(xml));
        long xpathDocument = System.GC.GetTotalMemory(true) - before;
        System.GC.KeepAlive(x);
        x = null;
        before = System.GC.GetTotalMemory(true);
        var c = CompactDocument.Load(new StringReader(xml));
        long compact = System.GC.GetTotalMemory(true) - before;
        System.GC.KeepAlive(c);
        Assert.True(compact <= xpathDocument * 3 / 4, $"compact tree {compact} bytes, XPathDocument {xpathDocument}");
    }
}
