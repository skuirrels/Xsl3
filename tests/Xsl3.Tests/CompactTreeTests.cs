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

    [Theory]
    [InlineData(513)]
    [InlineData(5000)]
    [InlineData(33000)]
    [InlineData(66000)]
    public void LargeTreesKeepContentAttributesAndAxesAcrossColumnGrowth(int rows)
    {
        var xml = new StringBuilder("<doc xmlns:p='urn:outer'>");
        for (int i = 0; i < rows; i++)
            xml.Append(System.FormattableString.Invariant($"<r a='{i}' p:b='value' xmlns:q='urn:{i % 3}'><v>{i}</v>before<q:e/>after<!--comment--><?p data?></r>"));
        xml.Append("</doc>");
        var (expected, actual) = Load(xml.ToString(), XmlSpace.Preserve);
        var en = expected.Select("descendant-or-self::node()");
        var an = actual.Select("descendant-or-self::node()");
        int count = 0;
        while (en.MoveNext())
        {
            Assert.True(an.MoveNext());
            XPathNavigator e = en.Current!, a = an.Current!;
            Same(e, a);
            Assert.Equal(e.LookupNamespace("q"), a.LookupNamespace("q"));
            var ep = e.Clone(); var ap = a.Clone();
            Assert.Equal(ep.MoveToParent(), ap.MoveToParent());
            Same(ep, ap, value: false);
            ep = e.Clone(); ap = a.Clone();
            Assert.Equal(ep.MoveToNext(), ap.MoveToNext());
            Same(ep, ap);
            var ea = e.Clone(); var aa = a.Clone();
            bool more = ea.MoveToFirstAttribute();
            Assert.Equal(more, aa.MoveToFirstAttribute());
            while (more)
            {
                Same(ea, aa);
                more = ea.MoveToNextAttribute();
                Assert.Equal(more, aa.MoveToNextAttribute());
            }
            count++;
        }
        Assert.False(an.MoveNext());
        Assert.Equal(rows * 8 + 2, count);
        // The optimized named following scan must use every page, too.
        expected.MoveToRoot(); actual.MoveToRoot();
        count = 0;
        while (expected.MoveToFollowing("r", ""))
        {
            Assert.True(actual.MoveToFollowing("r", ""));
            Same(expected, actual);
            count++;
        }
        Assert.False(actual.MoveToFollowing("r", ""));
        Assert.Equal(rows, count);
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

    private static void Same(XPathNavigator e, XPathNavigator a, bool value = true)
    {
        Assert.Equal(e.NodeType, a.NodeType);
        Assert.Equal(e.LocalName, a.LocalName);
        Assert.Equal(e.NamespaceURI, a.NamespaceURI);
        Assert.Equal(e.Prefix, a.Prefix);
        Assert.Equal(e.Name, a.Name);
        if (value) Assert.Equal(e.Value, a.Value);
        Assert.Equal(e.BaseURI, a.BaseURI);
        Assert.Equal(e.IsEmptyElement, a.IsEmptyElement);
        Assert.Equal(e.HasAttributes, a.HasAttributes);
        Assert.Equal(e.HasChildren, a.HasChildren);
        Assert.Equal(e.XmlLang, a.XmlLang);
    }

    /// <summary>The same raw-writer events build the same tree, through XPathDocumentBuilder and the compact writer.</summary>
    [Theory]
    [InlineData("document")]
    [InlineData("element")]
    [InlineData("text")]
    [InlineData("attribute")]
    [InlineData("namespace")]
    [InlineData("empty")]
    public void TemporaryTreesReadTheSame(string shape)
    {
        var nameTable = new NameTable();
        XPathNavigator Build(bool compact)
        {
            var (w, root) = CompactDocument.TemporaryTree(nameTable, "file:///base/style.xsl", fragment: shape != "document", noDocumentUri: true, compact);
            switch (shape)
            {
                case "document":
                case "element":
                    w.WriteStartElement("p", "e", "urn:p");
                    w.WriteNamespaceDeclaration("p", "urn:p");
                    w.WriteNamespaceDeclaration("q", "urn:q");
                    w.WriteStartAttribute("", "a", "");
                    w.WriteString("1");
                    w.WriteString("2");
                    w.WriteEndAttribute();
                    w.WriteStartAttribute("xml", "id", "http://www.w3.org/XML/1998/namespace");
                    w.WriteString("  i  d ");
                    w.WriteEndAttribute();
                    w.StartElementContent();
                    w.WriteString("text ");
                    w.WriteString("more");
                    w.WriteComment("c");
                    w.WriteStartElement("", "child", "");
                    w.WriteNamespaceDeclaration("", "");
                    w.StartElementContent();
                    w.WriteEndElement("", "child", "");
                    w.WriteStartElement("", "full", "");
                    w.StartElementContent();
                    w.WriteFullEndElement("", "full", "");
                    w.WriteProcessingInstruction("pi", "data");
                    w.WriteWhitespace("  ");
                    w.WriteCData("<x>");
                    w.WriteFullEndElement("p", "e", "urn:p");
                    break;
                case "text":
                    w.WriteString("a");
                    w.WriteWhitespace(" ");
                    w.WriteString("b");
                    break;
                case "attribute":
                    w.WriteStartAttribute("", "lone", "");
                    w.WriteString("v");
                    w.WriteEndAttribute();
                    break;
                case "namespace":
                    w.WriteNamespaceDeclaration("n", "urn:n");
                    break;
            }
            w.Close();
            return root();
        }
        XPathNavigator expected = Build(compact: false), actual = Build(compact: true);
        Assert.IsType<CompactNavigator>(actual);
        var en = new List<XPathNavigator>();
        var an = new List<XPathNavigator>();
        Walk(expected, actual, en, an);
        for (int i = 0; i < en.Count; i++)
        {
            for (int j = 0; j < en.Count; j++)
            {
                Assert.Equal(en[i].ComparePosition(en[j]), an[i].ComparePosition(an[j]));
            }
        }
        XPathNavigator e = expected.Clone(), a = actual.Clone();
        Assert.Equal(e.MoveToParent(), a.MoveToParent());
        e.MoveToRoot();
        a.MoveToRoot();
        Same(e, a);
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
}

/// <summary>
/// Memory use. The live heap is only a fair measure in a process that has done nothing else: in the test host, what
/// other tests left behind (name pools freed a collection late, large-object-heap state) moved the numbers by more
/// than the margin. So the measurement runs in a child process (<see cref="Program"/>), after a warm-up that loads
/// both builders' types, static tables and names, and keeps the least of three measurements.
/// </summary>
public class CompactTreeMemoryTests
{
    [Fact]
    public void TakesLessMemoryThanXPathDocument()
    {
        string host = System.Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } path ? path : "dotnet";
        var start = new System.Diagnostics.ProcessStartInfo(host)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(typeof(Program).Assembly.Location);
        start.ArgumentList.Add(Program.MemoryProbe);
        using var process = System.Diagnostics.Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, output);
        string[] numbers = output.Trim().Split(' ');
        long xpathDocument = long.Parse(numbers[0]), compact = long.Parse(numbers[1]);
        Assert.True(compact <= xpathDocument * 3 / 4, $"compact tree {compact} bytes, XPathDocument {xpathDocument}");
    }

    /// <summary>Prints "xpathDocumentBytes compactBytes" for the orders document.</summary>
    internal static void Probe()
    {
        var sb = new StringBuilder("<orders>");
        for (int i = 0; i < 5000; i++)
        {
            sb.Append($"<order id=\"{i}\"><customer>Customer {i % 250:D5}</customer><total>{i % 997}.{i % 100:D2}</total><line sku=\"P{i % 500}\" qty=\"{i % 9 + 1}\"/></order>");
        }
        string xml = sb.Append("</orders>").ToString();
        Measure(() => new XPathDocument(new StringReader(xml)));
        Measure(() => CompactDocument.Load(new StringReader(xml)));
        long xpathDocument = long.MaxValue, compact = long.MaxValue;
        for (int run = 0; run < 3; run++)
        {
            xpathDocument = System.Math.Min(xpathDocument, Measure(() => new XPathDocument(new StringReader(xml))));
            compact = System.Math.Min(compact, Measure(() => CompactDocument.Load(new StringReader(xml))));
        }
        System.Console.WriteLine($"{xpathDocument} {compact}");
    }

    // The bytes a tree keeps alive.
    private static long Measure(System.Func<object> build)
    {
        long before = System.GC.GetTotalMemory(true);
        object tree = build();
        long after = System.GC.GetTotalMemory(true);
        System.GC.KeepAlive(tree);
        return after - before;
    }
}
