// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Xml;
using System.Xml.Schema;
using System.Xml.XPath;
using Xsl3.Streaming;
using Xsl3.XPath31;
using Xsl3.Xslt;
using Xunit;

namespace Xsl3.Tests;

public class StreamedForkProjectionTests
{
    // Shape: document/doc/row headers and complete value subtrees. The names are
    // fixture inputs to the compiler/runtime proof, never runtime special cases.
    private static ForkInputProjection Projection() => new(new string?[] {
        "-1", "", "", "0", "0", "doc", "", "0", "1", "row", "", "0", "2", "value", "", "1",
    });

    private static XslNode Node(XslNodeType type, string? select = null, params XslNode[] children)
    {
        var node = new XslNode(type, null, select, XslVersion.Current);
        foreach (var child in children) node.AddContent(child);
        return node;
    }

    [Theory]
    [InlineData("doc/row[1]")]
    [InlineData("doc//row")]
    [InlineData("doc/*")]
    [InlineData("$rows")]
    [InlineData("doc/row/ancestor::doc")]
    [InlineData("doc/row | doc/other")]
    public void UnprovenSelectionsKeepTheFullInput(string select)
    {
        var prong = Node(XslNodeType.ForEach, select, Node(XslNodeType.ValueOf, "@id"));
        Assert.Null(ForkProjectionAnalysis.Analyze(new[] { prong }, new()));
    }

    [Theory]
    [InlineData("count(*)")]
    [InlineData("snapshot(.)")]
    [InlineData("name()")]
    [InlineData("$value")]
    public void UnprovenExpressionsKeepTheFullInput(string select)
    {
        var prong = Node(XslNodeType.ForEach, "doc/row", Node(XslNodeType.ValueOf, select));
        Assert.Null(ForkProjectionAnalysis.Analyze(new[] { prong }, new()));
    }

    [Fact]
    public void NamespaceScopesAndWholeSubtreesAreRepresentedInTheUnion()
    {
        var context = new XPathStaticContext { DefaultElementNamespace = "urn:outer" };
        context.Namespaces["p"] = "urn:outer";
        var child = Node(XslNodeType.ValueOf, "p:value");
        child.Namespaces = new NsDecl(null, "p", "urn:value");
        var prong = Node(XslNodeType.ForEach, "doc/p:row", child, Node(XslNodeType.ValueOf, "@id"));
        prong.Namespaces = new NsDecl(null, "p", "urn:row");
        string?[] data = Assert.IsType<string?[]>(ForkProjectionAnalysis.Analyze(new[] { prong }, context));
        Assert.Equal(new string?[] { "-1", "", "", "0", "0", "doc", "urn:outer", "0",
            "1", "row", "urn:row", "0", "2", "value", "urn:value", "1" }, data);
        Assert.Equal("urn:outer", context.Namespaces["p"]);
    }

    [Fact]
    public void TextValueOfTheForkFocusRequiresTheWholeInput()
    {
        Assert.Null(ForkProjectionAnalysis.Analyze(new[] { Node(XslNodeType.ValueOf, ".") }, new()));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(64)]
    public void ViewsOmitUnselectedEventsAndKeepValuesHeadersAndIdentity(int capacity)
    {
        const string xml = "<doc xmlns:p='urn:p' xml:lang='en'><ignored><row/></ignored><row id='a'>"
            + "<discard>hidden</discard><value>before<p:n/>after<!--note--><?pi q?></value></row>"
            + "<row id='b'><value/></row><tail/></doc>";
        using var reader = XmlReader.Create(new StringReader(xml), new(), "file:///projection/input.xml");
        using var document = new StreamedDocument(reader);
        using var fork = document.Fork((StreamedNavigator)document.CreateNavigator(), Projection());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var copies = new string?[2]; var positions = new XPathNavigator?[2];
        fork.Run(Enumerable.Range(0, 2).Select<int, Action<StreamedNavigator, CancellationToken>>(index => (view, _) => {
            Assert.True(view.MoveToFirstChild());
            Assert.Equal("urn:p", view.GetNamespace("p"));
            Assert.Equal("en", view.XmlLang);
            Assert.Equal("file:///projection/input.xml", view.BaseURI);
            Assert.True(view.MoveToFirstChild());
            Assert.Equal("row", view.LocalName);
            Assert.Equal("a", view.GetAttribute("id", ""));
            positions[index] = view.Clone();
            Assert.True(view.MoveToFirstChild());
            Assert.Equal("value", view.LocalName);
            using var content = view.ReadSubtree();
            var copy = new XmlDocument(); copy.Load(content); copies[index] = copy.OuterXml;
            Assert.True(view.MoveToParent());
            Assert.True(view.MoveToNext());
            Assert.Equal("b", view.GetAttribute("id", ""));
            Assert.False(view.MoveToNext());
        }).ToArray(), capacity, cancellation.Token);
        Assert.Equal(copies[0], copies[1]);
        Assert.Contains("before", copies[0]); Assert.Contains("after", copies[0]);
        Assert.Contains("<!--note-->", copies[0]); Assert.Contains("<?pi q?>", copies[0]);
        Assert.True(positions[0]!.IsSamePosition(positions[1]!));
        Assert.Equal(XmlNodeOrder.Same, positions[0]!.ComparePosition(positions[1]!));
        Assert.NotEqual(ReadState.Closed, reader.ReadState);
    }

    [Fact]
    public void AnAlreadyInspectedChildDisablesProjection()
    {
        using var reader = XmlReader.Create(new StringReader("<doc><omitted/><row/></doc>"));
        using var document = new StreamedDocument(reader);
        var focus = (StreamedNavigator)document.CreateNavigator();
        Assert.True(focus.MoveToFirstChild());
        using var fork = document.Fork((StreamedNavigator)document.CreateNavigator(), Projection());
        fork.Run(new Action<StreamedNavigator, CancellationToken>[] { (view, _) => {
            Assert.True(view.MoveToFirstChild()); Assert.True(view.MoveToFirstChild());
            Assert.Equal("omitted", view.LocalName);
        } }, 1);
    }

    private sealed class Observer : IStreamedNodeObserver
    {
        internal int Omitted;
        public void Start(StreamedNavigator node) { if (node.LocalName == "omitted") Omitted++; }
        public void End(StreamedNavigator node) { }
        public void Dispose() { }
    }

    [Fact]
    public void ObserversReceiveTheUnprojectedInput()
    {
        using var reader = XmlReader.Create(new StringReader("<doc><omitted/><row/></doc>"));
        using var document = new StreamedDocument(reader);
        var observer = new Observer(); document.Observe(observer);
        using var fork = document.Fork((StreamedNavigator)document.CreateNavigator(), Projection());
        fork.Run(new Action<StreamedNavigator, CancellationToken>[] { (view, _) => {
            using var content = view.ReadSubtree(); var copy = new XmlDocument(); copy.Load(content);
            Assert.Contains("<omitted", copy.OuterXml);
        } }, 1);
        Assert.Equal(1, observer.Omitted);
    }

    [Fact]
    public void MalformedOmittedContentStillFails()
    {
        using var reader = XmlReader.Create(new StringReader("<doc><omitted><bad></omitted><row/></doc>"));
        using var document = new StreamedDocument(reader);
        using var fork = document.Fork((StreamedNavigator)document.CreateNavigator(), Projection());
        Assert.Throws<XmlException>(() => fork.Run(new Action<StreamedNavigator, CancellationToken>[] {
            (view, token) => { _ = view.Value; }, (view, token) => { _ = view.Value; },
        }, 1));
        Assert.NotEqual(ReadState.Closed, reader.ReadState);
    }

    [Fact]
    public void SchemaErrorsInOmittedContentStillFail()
    {
        var schemas = new XmlSchemaSet();
        schemas.Add(null, XmlReader.Create(new StringReader("<xs:schema xmlns:xs='http://www.w3.org/2001/XMLSchema'>"
            + "<xs:element name='doc'><xs:complexType><xs:sequence><xs:element name='omitted' type='xs:integer'/>"
            + "<xs:element name='row'/></xs:sequence></xs:complexType></xs:element></xs:schema>")));
        using var reader = XmlReader.Create(new StringReader("<doc><omitted>invalid</omitted><row/></doc>"),
            new() { ValidationType = ValidationType.Schema, Schemas = schemas });
        using var document = new StreamedDocument(reader);
        using var fork = document.Fork((StreamedNavigator)document.CreateNavigator(), Projection());
        Assert.Throws<XmlSchemaValidationException>(() => fork.Run(new Action<StreamedNavigator, CancellationToken>[] {
            (view, token) => { _ = view.Value; }, (view, token) => { _ = view.Value; },
        }, 1));
    }

    private sealed class ReadCallback(XmlReader reader, Action<ReadCallback> callback) : XmlWrappingReader(reader)
    {
        internal int Reads;
        public override bool Read()
        {
            bool result = base.Read(); Reads++;
            callback(this);
            return result;
        }
    }

    [Fact]
    public void CancellationIsCheckedWhileOmittedEventsAreBeingFiltered()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var reader = new ReadCallback(XmlReader.Create(new StringReader("<doc><omitted><inside/></omitted><row/></doc>")),
            input => { if (input.LocalName == "omitted") cancellation.Cancel(); });
        using var document = new StreamedDocument(reader);
        using var fork = document.Fork((StreamedNavigator)document.CreateNavigator(), Projection());
        Assert.Throws<OperationCanceledException>(() => fork.Run(new Action<StreamedNavigator, CancellationToken>[] {
            (view, token) => { _ = view.Value; }, (view, token) => { _ = view.Value; },
        }, 1, cancellation.Token));
        Assert.Equal("omitted", reader.LocalName);
        Assert.NotEqual(ReadState.Closed, reader.ReadState);
    }

    [Fact]
    public void FinishingEveryProngStopsFilteringAnUnselectedTail()
    {
        using var finished = new CountdownEvent(2);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        bool reachedTail = false; int omittedChildren = 0;
        string xml = "<doc><row/><omitted>" + string.Concat(Enumerable.Repeat("<inside/>", 128)) + "</omitted></doc>";
        using var reader = new ReadCallback(XmlReader.Create(new StringReader(xml)), input => {
            if (input.LocalName == "omitted") reachedTail = true;
            // The accepted prefix must be delivered within the original logical
            // batch capacity, even when none of the following events are selected.
            if (input.LocalName == "inside" && ++omittedChildren == 64)
                Assert.True(finished.Wait(TimeSpan.FromSeconds(10)));
        });
        using var document = new StreamedDocument(reader);
        using var fork = document.Fork((StreamedNavigator)document.CreateNavigator(), Projection());
        fork.Run(Enumerable.Range(0, 2).Select<int, Action<StreamedNavigator, CancellationToken>>(_ => (view, token) => {
            try
            {
                Assert.True(view.MoveToFirstChild()); Assert.True(view.MoveToFirstChild());
                Assert.Equal("row", view.LocalName);
            }
            finally { finished.Signal(); }
        }).ToArray(), 64, cancellation.Token);
        Assert.True(reachedTail);
        Assert.InRange(omittedChildren, 1, 64);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(128)]
    public void AnEntirelyUnselectedPopulationEndsWithoutPublishingEmptyBatches(int count)
    {
        string xml = "<doc>" + string.Concat(Enumerable.Repeat("<omitted><inside/></omitted>", count)) + "</doc>";
        using var reader = XmlReader.Create(new StringReader(xml));
        using var document = new StreamedDocument(reader);
        using var fork = document.Fork((StreamedNavigator)document.CreateNavigator(), Projection());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        fork.Run(Enumerable.Range(0, 2).Select<int, Action<StreamedNavigator, CancellationToken>>(_ => (view, token) => {
            Assert.True(view.MoveToFirstChild()); Assert.Equal("doc", view.LocalName);
            Assert.False(view.MoveToFirstChild());
        }).ToArray(), 7, cancellation.Token);
        Assert.NotEqual(ReadState.Closed, reader.ReadState);
    }

    [Theory]
    [MemberData(nameof(Deliveries))]
    public void ConstructorErrorsKeepTheirCodeAndReleaseFollowingProngs(bool debug, int delivery, int capacity)
    {
        const string sheet = """
            <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:xs="http://www.w3.org/2001/XMLSchema">
              <xsl:mode streamable="yes"/><xsl:output method="text"/>
              <xsl:template match="/"><xsl:fork>
                <xsl:sequence><xsl:for-each select="doc/row"><xsl:value-of select="xs:integer(@id)"/></xsl:for-each></xsl:sequence>
                <xsl:sequence><xsl:for-each select="doc/row"><xsl:value-of select="@id"/></xsl:for-each></xsl:sequence>
              </xsl:fork></xsl:template>
            </xsl:stylesheet>
            """;
        string xml = "<doc><row id='invalid'/>" + string.Concat(Enumerable.Repeat("<omitted><inside/></omitted>", 128)) + "</doc>";
        foreach (bool forward in new[] { false, true })
        {
            Exception error = Assert.IsAssignableFrom<Exception>(Record.Exception(() => Run(sheet, xml, forward, debug, delivery, capacity)));
            Assert.Equal("FORG0001", XsltDynamicException.From(error)!.Code.LocalName);
        }
    }

    public static IEnumerable<object[]> Deliveries()
    {
        foreach (bool debug in new[] { false, true })
        foreach (int delivery in new[] { 0, 1, 2 })
        foreach (int capacity in new[] { 1, 7, 64 })
        {
#if !NET10_0_OR_GREATER
            if (delivery == 2) continue;
#endif
            yield return new object[] { debug, delivery, capacity };
        }
    }

    [Theory]
    [MemberData(nameof(Deliveries))]
    public void CompiledUnionAndFallbackAgreeWithTree(bool debug, int delivery, int capacity)
    {
        const string xml = "<doc xmlns='urn:doc' xmlns:r='urn:row'><r:row id='a' amount='2'><skip><value>wrong</value></skip>"
            + "<value xmlns='urn:value'>before<em>middle</em>after</value></r:row><r:row id='b' amount='3'><value xmlns='urn:value'/></r:row></doc>";
        // Namespace shadowing, default XPath namespace, constructor arithmetic and mixed
        // content exercise the proven path. The second form deliberately requires fallback.
        foreach (string value in new[] { "v:value", "string(.)" })
        {
            string sheet = $$"""
                <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                  xmlns:xs="http://www.w3.org/2001/XMLSchema" xmlns:r="urn:wrong" xmlns:v="urn:wrong" xpath-default-namespace="urn:doc">
                  <xsl:mode streamable="yes"/><xsl:output method="text"/>
                  <xsl:template match="/"><xsl:fork>
                    <xsl:sequence><xsl:for-each select="doc/r:row" xmlns:r="urn:row"><xsl:value-of select="@id"/><xsl:text>|</xsl:text></xsl:for-each></xsl:sequence>
                    <xsl:sequence><xsl:for-each select="doc/r:row" xmlns:r="urn:row"><xsl:value-of select="xs:integer(@amount) + 1"/><xsl:text>:</xsl:text>
                      <xsl:value-of select="{{value}}" xmlns:v="urn:value"/><xsl:text>|</xsl:text></xsl:for-each></xsl:sequence>
                  </xsl:fork></xsl:template>
                </xsl:stylesheet>
                """;
            string expected = Run(sheet, xml, false, debug, delivery, capacity);
            if (value == "v:value") Assert.Equal("a|b|3:beforemiddleafter|4:|", expected);
            Assert.Equal(expected, Run(sheet, xml, true, debug, delivery, capacity));
        }
    }

    [Theory]
    [MemberData(nameof(Deliveries))]
    public void InheritedExpandedTextCannotHideAnInputDependency(bool debug, int delivery, int capacity)
    {
        foreach (int scope in new[] { 0, 1, 2 })
        {
            string sheet = $$"""
                <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" {{(scope == 0 ? "expand-text='yes'" : "")}}>
                  <xsl:mode streamable="yes"/><xsl:output method="text"/>
                  <xsl:template match="/" {{(scope == 1 ? "expand-text='yes'" : "")}}><xsl:fork {{(scope == 2 ? "expand-text='yes'" : "")}}>
                    <xsl:sequence><xsl:for-each select="doc/row"><xsl:value-of select="@id"/></xsl:for-each></xsl:sequence>
                    <xsl:sequence><xsl:for-each select="doc/row"><xsl:text>{@id}={.}</xsl:text></xsl:for-each></xsl:sequence>
                  </xsl:fork></xsl:template>
                </xsl:stylesheet>
                """;
            const string xml = "<doc><row id='a'>before<hidden>middle</hidden>after</row></doc>";
            Assert.Equal("aa=beforemiddleafter", Run(sheet, xml, false, debug, delivery, capacity));
            Assert.Equal("aa=beforemiddleafter", Run(sheet, xml, true, debug, delivery, capacity));
        }
    }

    [Theory]
    [MemberData(nameof(Deliveries))]
    public void ProngsKeepDifferentChildNamesAndNamespacesInTheirOriginalResultOrder(bool debug, int delivery, int capacity)
    {
        const string sheet = """
            <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:p="urn:other">
              <xsl:mode streamable="yes"/><xsl:output method="text"/>
              <xsl:template match="/"><xsl:fork>
                <xsl:sequence><xsl:for-each select="doc/b"><xsl:value-of select="."/><xsl:text>|</xsl:text></xsl:for-each></xsl:sequence>
                <xsl:sequence><xsl:for-each select="doc/a"><xsl:value-of select="@id"/><xsl:text>|</xsl:text></xsl:for-each></xsl:sequence>
                <xsl:sequence><xsl:for-each select="doc/p:a"><xsl:value-of select="."/><xsl:text>|</xsl:text></xsl:for-each></xsl:sequence>
              </xsl:fork></xsl:template>
            </xsl:stylesheet>
            """;
        const string xml = "<doc xmlns:p='urn:other'><b>B1</b><a id='A1'><ignored>hidden</ignored></a>"
            + "<p:a>P<p:n>MID</p:n>END</p:a><b>B2</b></doc>";
        const string expected = "B1|B2|A1|PMIDEND|";
        Assert.Equal(expected, Run(sheet, xml, false, debug, delivery, capacity));
        Assert.Equal(expected, Run(sheet, xml, true, debug, delivery, capacity));
    }

    private static string Run(string sheet, string xml, bool forward, bool debug, int delivery, int capacity)
    {
        var settings = new XsltSettings { StreamedExecution = forward, IncludeDebugInformation = debug,
            StreamingBufferCapacity = capacity, Timeout = TimeSpan.FromSeconds(10) };
        using var input = XmlReader.Create(new StringReader(xml)); using var output = new StringWriter();
        if (delivery == 0)
            new XsltCompiler { Settings = settings }.Compile(new StringReader(sheet)).CreateTransformer().ApplyTemplates(input, output);
        else
        {
            var transform = new XslCompiledTransform(debug);
            using var stylesheet = XmlReader.Create(new StringReader(sheet));
#if NET10_0_OR_GREATER
            if (delivery == 2)
            {
                using var image = new MemoryStream();
                transform.CompileToAssembly(stylesheet, settings, null, "ForkProjection" + Guid.NewGuid().ToString("N"), "Saved.ForkProjection", image);
                transform.Load(Assembly.Load(image.ToArray()).GetType("Saved.ForkProjection", true)!, settings);
            }
            else
#endif
                transform.Load(stylesheet, settings, null);
            transform.Transform(input, null, output);
        }
        Assert.NotEqual(ReadState.Closed, input.ReadState);
        return output.ToString();
    }
}
