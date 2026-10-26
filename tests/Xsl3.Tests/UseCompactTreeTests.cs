// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.XPath;
using Xsl3.Tree;
using Xunit;

namespace Xsl3.Tests;

/// <summary>
/// XslCompiledTransform.UseCompactTree: a 1.0 stylesheet builds the documents it parses itself (its input from a URI or
/// reader, and document()) as compact trees, and its output is the same as with XPathDocument's, except for the strings
/// generate-id() returns. A caller's own tree is used as given, and 2.0+ stylesheets always use the compact tree.
/// </summary>
public sealed class UseCompactTreeTests : IDisposable
{
    private const string Xsl = "http://www.w3.org/1999/XSL/Transform";

    private const string Input = """
        <?xml version="1.0"?>
        <!DOCTYPE doc [
          <!ATTLIST item key ID #IMPLIED>
          <!NOTATION gif SYSTEM "image/gif">
          <!ENTITY pic SYSTEM "pic.gif" NDATA gif>
        ]>
        <?top pi?>
        <doc xmlns:a="urn:a" a:x="1" plain="two" xml:lang="en">
          <!-- a comment -->
          <item key="k1" cat="x" a:y="3">text &amp; more<![CDATA[ <cdata> ]]>tail</item>
          <item key="k2" cat="y"/>
          <item key="k3" cat="x" xml:lang="fr">trois</item>
          <a:group xmlns:b="urn:b">
            <b:leaf xml:space="preserve">  spaced  </b:leaf>
            <pre>   </pre>
            <leaf>   </leaf>
          </a:group>
          <deep><x><y><z>1</z><z>2</z></y></x><x/></deep>
          <?inner data here?>
          <mixed>a<b>b</b>c<b/>d</mixed>
        </doc>
        <!-- after -->
        """;

    private const string Other = "<other><item cat='x'>o1</item><item cat='z'>o2</item></other>";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "xsl3-compact-" + Guid.NewGuid().ToString("N"));
    private readonly string _input;
    private readonly string _inputWithoutDtd;

    public UseCompactTreeTests()
    {
        Directory.CreateDirectory(_dir);
        _input = Path.Combine(_dir, "input.xml");
        File.WriteAllText(_input, Input);
        // A transform reading its input from a URI prohibits DTDs, as stock does.
        _inputWithoutDtd = Path.Combine(_dir, "input-without-dtd.xml");
        File.WriteAllText(_inputWithoutDtd, System.Text.RegularExpressions.Regex.Replace(Input, @"<!DOCTYPE.*?\]>", "", System.Text.RegularExpressions.RegexOptions.Singleline));
        File.WriteAllText(Path.Combine(_dir, "other.xml"), Other);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    public enum Source { Uri, Reader, XPathDocument }

    /// <summary>The 1.0 stylesheets whose output must not depend on the tree: every axis, keys, sorting, numbering,
    /// whitespace stripping, lang(), unparsed entities and document(). (id() finds nothing on either tree, as on stock:
    /// .NET's readers don't pass on the DTD's ID attributes.)</summary>
    public static readonly Dictionary<string, string> Stylesheets = new()
    {
        ["identity"] = """
            <xsl:template match="@*|node()"><xsl:copy><xsl:apply-templates select="@*|node()"/></xsl:copy></xsl:template>
            """,
        ["strip-space"] = """
            <xsl:strip-space elements="*"/><xsl:preserve-space elements="pre"/>
            <xsl:template match="@*|node()"><xsl:copy><xsl:apply-templates select="@*|node()"/></xsl:copy></xsl:template>
            """,
        ["axes"] = """
            <xsl:template match="/">
              <out><xsl:for-each select="//node() | //@*">
                <n kind="{count(self::*)}{count(self::text())}{count(self::comment())}{count(self::processing-instruction())}"
                   name="{name()}" pre="{count(preceding::node())}" fol="{count(following::node())}"
                   anc="{count(ancestor-or-self::node())}" ps="{count(preceding-sibling::node())}" fs="{count(following-sibling::node())}"
                   desc="{count(descendant::node())}" par="{name(..)}" ns="{count(namespace::*)}">
                  <xsl:for-each select="namespace::*"><xsl:value-of select="concat(name(), '=', ., ';')"/></xsl:for-each>
                  <xsl:value-of select="string-length(string(.))"/>
                </n>
              </xsl:for-each></out>
            </xsl:template>
            """,
        ["reverse-axes"] = """
            <xsl:template match="/">
              <out><xsl:for-each select="//z[last()]">
                <xsl:for-each select="ancestor::node()"><a><xsl:value-of select="concat(position(), name())"/></a></xsl:for-each>
                <xsl:for-each select="preceding::node()[position() &lt; 6]"><p><xsl:value-of select="concat(position(), name(), .)"/></p></xsl:for-each>
              </xsl:for-each>
              <xsl:value-of select="name((//b)[last()]/preceding-sibling::*[1])"/></out>
            </xsl:template>
            """,
        ["muenchian-sort"] = """
            <xsl:key name="by-cat" match="item" use="@cat"/>
            <xsl:template match="/">
              <out><xsl:for-each select="//item[generate-id() = generate-id(key('by-cat', @cat)[1])]">
                <xsl:sort select="@cat" order="descending"/>
                <g cat="{@cat}"><xsl:for-each select="key('by-cat', @cat)"><xsl:sort select="@key"/><xsl:value-of select="@key"/></xsl:for-each></g>
              </xsl:for-each></out>
            </xsl:template>
            """,
        ["id-lang-entity"] = """
            <xsl:template match="/">
              <out ids="{count(id('k3 k1 nothing'))}" first="{id('k3 k1')[1]/@key}" en="{count(//*[lang('en')])}" fr="{count(//*[lang('fr')])}"
                   uri="{unparsed-entity-uri('pic')}" none="{unparsed-entity-uri('none')}"/>
            </xsl:template>
            """,
        ["number"] = """
            <xsl:template match="/"><out><xsl:apply-templates select="//z | //item | //b"/></out></xsl:template>
            <xsl:template match="*">
              <n><xsl:number level="multiple" count="*"/>|<xsl:number level="any"/>|<xsl:number count="item|z" format="a"/></n>
            </xsl:template>
            """,
        ["string-recursion"] = """
            <xsl:template match="/"><out><xsl:call-template name="rev"><xsl:with-param name="s" select="normalize-space(/)"/></xsl:call-template></out></xsl:template>
            <xsl:template name="rev"><xsl:param name="s"/>
              <xsl:if test="$s"><xsl:call-template name="rev"><xsl:with-param name="s" select="substring($s, 2)"/></xsl:call-template><xsl:value-of select="substring($s, 1, 1)"/></xsl:if>
            </xsl:template>
            """,
        ["generate-id-identity"] = """
            <xsl:key name="k" match="item" use="@cat"/>
            <xsl:template match="/">
              <xsl:variable name="all" select="//node() | //@*"/>
              <out same-key="{generate-id(key('k', 'x')[1]) = generate-id(//item[1])}" same-step="{generate-id(key('k', 'y')) = generate-id(/doc/item[2])}"
                   same-attr="{generate-id(//item[1]/@cat) = generate-id(//item[@key = 'k1']/@cat)}" differ="{generate-id(//item[1]) != generate-id(//item[2])}"
                   root="{generate-id() = generate-id(/)}" empty="{generate-id(/nothing)}" doc="{generate-id(document('other.xml')) != generate-id(/)}"/>
            </xsl:template>
            """,
        ["document"] = """
            <xsl:key name="k" match="item" use="@cat"/>
            <xsl:template match="/">
              <xsl:variable name="other" select="document('other.xml')"/>
              <out>
                <xsl:for-each select="$other//item"><xsl:value-of select="concat(@cat, '.')"/></xsl:for-each>
                <xsl:for-each select="$other"><xsl:value-of select="count(key('k', 'x'))"/></xsl:for-each>
                <xsl:value-of select="count($other | /)"/>
                <xsl:copy-of select="$other/other/item[1]"/>
              </out>
            </xsl:template>
            """,
    };

    // A transform reading its input from a URI has no resolver for document() (XmlResolver.ThrowingResolver, as stock).
    public static IEnumerable<object[]> StylesheetsAndSources() =>
        from name in Stylesheets.Keys
        from source in new[] { Source.Uri, Source.Reader }
        where source != Source.Uri || !Stylesheets[name].Contains("document(")
        select new object[] { name, source };

    [Theory]
    [MemberData(nameof(StylesheetsAndSources))]
    public void OutputMatchesXPathDocument(string name, Source source)
    {
        string expected = Run(Load(Stylesheets[name], compact: false), source);
        string actual = Run(Load(Stylesheets[name], compact: true), source);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(false, Source.Uri)]
    [InlineData(true, Source.Uri)]
    [InlineData(false, Source.Reader)]
    [InlineData(true, Source.Reader)]
    public void InputFollowsTheSetting(bool compact, Source source)
    {
        string tree = Run(Load(Probe("."), compact), source, Probes());
        Assert.Equal(ExpectedTree(compact), tree);
    }

    [Theory]
    [InlineData(false, Source.Reader)]
    [InlineData(true, Source.Reader)]
    [InlineData(true, Source.XPathDocument)]
    public void DocumentFunctionFollowsTheSetting(bool compact, Source source)
    {
        string tree = Run(Load(Probe("document('other.xml')"), compact), source, Probes());
        Assert.Equal(ExpectedTree(compact), tree);
    }

    /// <summary>
    /// The order between documents is implementation-dependent (XSLT 1.0 §12.1; XPath 1.0 §5). Compact trees are in the
    /// order they were built, so the input comes before document()'s; XPathDocument's are in the order a comparison
    /// first meets them, as in stock. This is the second visible difference between the trees.
    /// </summary>
    [Fact]
    public void OrderBetweenDocuments()
    {
        const string body = """
            <xsl:template match="/">
              <xsl:for-each select="document('other.xml')//item | //item"><xsl:value-of select="concat(@cat, '.')"/></xsl:for-each>
            </xsl:template>
            """;
        if (CompactDocument.Override == null)
        {
            Assert.Equal("x.z.x.y.x.", Run(Load(body, compact: false), Source.Reader));
        }
        Assert.Equal(CompactDocument.Override == false ? "x.z.x.y.x." : "x.y.x.x.z.", Run(Load(body, compact: true), Source.Reader));
    }

    [Fact]
    public void CallersXPathDocumentIsUsedAsGiven()
    {
        string tree = Run(Load(Probe("."), compact: true), Source.XPathDocument, Probes());
        Assert.Equal("XPathDocumentNavigator", tree);
    }

    [Fact]
    public void SettingIsReadByLoad()
    {
        var transform = new XslCompiledTransform { UseCompactTree = true };
        transform.Load(StylesheetReader(Probe(".")), XsltSettings.TrustedXslt, new XmlUrlResolver());
        transform.UseCompactTree = false;
        Assert.Equal(ExpectedTree(true), Run(transform, Source.Uri, Probes()));

        transform.Load(StylesheetReader(Probe(".")), XsltSettings.TrustedXslt, new XmlUrlResolver());
        Assert.Equal(ExpectedTree(false), Run(transform, Source.Uri, Probes()));
    }

    /// <summary>
    /// generate-id() gives every node of every document a distinct id that is an XML name (XSLT 1.0 §12.4), whichever way
    /// the node is reached.
    /// </summary>
    [Fact]
    public void GeneratedIdsAreDistinctNames()
    {
        const string body = """
            <xsl:template match="/">
              <xsl:for-each select="//node() | //@* | //namespace::* | / | document('other.xml')//node() | document('other.xml')">
                <xsl:value-of select="generate-id()"/><xsl:text> </xsl:text>
              </xsl:for-each>
            </xsl:template>
            """;
        string[] ids = Run(Load(body, compact: true), Source.Reader).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(ids.Length > 50);
        foreach (string id in ids)
        {
            Assert.Equal(id, XmlConvert.VerifyNCName(id));
            Assert.True(char.IsAsciiLetter(id[0]), id);
        }

        // Namespace nodes are per element, so the same namespace on two elements gives two ids; nothing else repeats.
        Assert.Equal(ids.Length, ids.Distinct().Count());

        // The same node reached by different paths has the same id.
        Assert.Equal("""<out same-key="true" same-step="true" same-attr="true" differ="true" root="true" empty="" doc="true" />""",
            Run(Load(Stylesheets["generate-id-identity"], compact: true), Source.Reader));
    }

    /// <summary>
    /// The trees differ in the strings generate-id() returns (the one visible difference), so the tests above observe the
    /// tree that was built.
    /// </summary>
    [Fact]
    public void GeneratedIdsDifferBetweenTrees()
    {
        const string body = """<xsl:template match="/"><xsl:value-of select="generate-id(//item[2])"/></xsl:template>""";
        string fromXPathDocument = Run(Load(body, compact: false), Source.Uri);
        string fromCompact = Run(Load(body, compact: true), Source.Uri);
        if (CompactDocument.Override == null)
        {
            Assert.NotEqual(fromXPathDocument, fromCompact);
        }
        Assert.Equal(fromCompact, Run(Load(body, compact: true), Source.Reader));
    }

    /// <summary>2.0+ stylesheets build compact trees whatever the setting.</summary>
    [Fact]
    public void Version2AlwaysUsesTheCompactTree()
    {
        const string body = """<xsl:template match="/"><xsl:value-of select="generate-id(//item[2])"/></xsl:template>""";
        string compact10 = Run(Load(body, compact: true), Source.Uri);
        string version2 = Run(Load(body, compact: false, version: "2.0"), Source.Uri);
        Assert.Equal(compact10, version2);
    }

#if NET10_0_OR_GREATER
    /// <summary>A stylesheet saved with CompileToAssembly keeps the setting it was compiled with.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavedStylesheetKeepsTheSetting(bool compact)
    {
        using var assembly = new MemoryStream();
        string typeName = "Saved.CompactTree" + compact;
        new XslCompiledTransform { UseCompactTree = compact }.CompileToAssembly(StylesheetReader(Probe(".")),
            XsltSettings.TrustedXslt, null, "Xsl3.Tests.CompactTree" + compact, typeName, assembly);
        var transform = new XslCompiledTransform();
        transform.Load(System.Reflection.Assembly.Load(assembly.ToArray()).GetType(typeName, true)!, XsltSettings.TrustedXslt);
        Assert.Equal(ExpectedTree(compact), Run(transform, Source.Uri, Probes()));
    }
#endif

    private static string ExpectedTree(bool compact) => CompactDocument.Override ?? compact ? "CompactNavigator" : "XPathDocumentNavigator";

    /// <summary>Reports the type of navigator over the first node of "select".</summary>
    public sealed class TreeProbe
    {
        public string Tree(XPathNodeIterator nodes) => nodes.MoveNext() ? nodes.Current!.GetType().Name : "(none)";
    }

    private static string Probe(string select) =>
        $"""<xsl:template match="/" xmlns:p="urn:probe"><xsl:value-of select="p:Tree({select})"/></xsl:template>""";

    private static XsltArgumentList Probes()
    {
        var arguments = new XsltArgumentList();
        arguments.AddExtensionObject("urn:probe", new TreeProbe());
        return arguments;
    }

    private static string Stylesheet(string body, string version = "1.0") =>
        $"""<xsl:stylesheet version="{version}" xmlns:xsl="{Xsl}"><xsl:output omit-xml-declaration="yes"/>{body}</xsl:stylesheet>""";

    // The stylesheet's base URI is in the test's directory, so document('other.xml') finds the file there.
    private XmlReader StylesheetReader(string body, string version = "1.0") =>
        XmlReader.Create(new StringReader(Stylesheet(body, version)), null, new Uri(Path.Combine(_dir, "style.xsl")).AbsoluteUri);

    private XslCompiledTransform Load(string body, bool compact, string version = "1.0")
    {
        var transform = new XslCompiledTransform { UseCompactTree = compact };
        transform.Load(StylesheetReader(body, version), XsltSettings.TrustedXslt, new XmlUrlResolver());
        return transform;
    }

    private string Run(XslCompiledTransform transform, Source source, XsltArgumentList? arguments = null)
    {
        var output = new StringWriter();
        using (XmlWriter writer = XmlWriter.Create(output, transform.OutputSettings))
        {
            switch (source)
            {
                case Source.Uri:
                    transform.Transform(_inputWithoutDtd, arguments, writer);
                    break;
                case Source.Reader:
                    using (XmlReader reader = XmlReader.Create(_input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Parse }))
                    {
                        transform.Transform(reader, arguments, writer, new XmlUrlResolver());
                    }
                    break;
                default:
                    using (XmlReader reader = XmlReader.Create(_input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Parse }))
                    {
                        transform.Transform(new XPathDocument(reader, XmlSpace.Preserve), arguments, writer, new XmlUrlResolver());
                    }
                    break;
            }
        }
        return output.ToString();
    }
}
