// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;
using System.Xml.Schema;
using Xsl3.Runtime;
using Xunit;

namespace Xsl3.Tests;

public class DocumentFragmentTests
{
    private const string Resource = "file:///fragment/input.xml";
    private const string Input = "<outer xmlns:p='urn:value' xml:base='child/'><before>skip</before><selected xml:id='chosen'><p:value>one</p:value><p:value>two</p:value></selected><after>ignore</after></outer>";
    private static string Sheet(string body, string declarations = "") => $$"""
        <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
          <xsl:output omit-xml-declaration="yes"/>{{declarations}}
          <xsl:template match="/" name="xsl:initial-template">{{body}}</xsl:template>
        </xsl:stylesheet>
        """;
    private static string Source(string body, string href = Resource + "#chosen", string attributes = "") =>
        $"<xsl:source-document streamable='yes' href='{href}' {attributes}>{body}</xsl:source-document>";

    [Theory]
    [InlineData(false, false, "stream")]
    [InlineData(true, false, "stream")]
    [InlineData(false, true, "stream")]
    [InlineData(true, true, "stream")]
    [InlineData(false, true, "reader")]
    [InlineData(true, true, "reader")]
    [InlineData(false, true, "tree")]
    public void SourceDocumentsSelectOnlyTheIdentifiedElement(bool forward, bool modern, string kind)
    {
        var resolver = new Resolver(Input, kind);
        string actual = Run(Sheet(Source("<out><xsl:copy-of select='.'/></out>")), resolver, forward, modern);
        XElement root = XElement.Parse(actual);
        XElement selected = Assert.Single(root.Elements());
        Assert.Equal("selected", selected.Name.LocalName);
        Assert.Equal(new[] { "one", "two" }, selected.Elements(XName.Get("value", "urn:value")).Select(element => element.Value));
        Assert.Equal(Resource, Assert.Single(resolver.Requests).AbsoluteUri);
        if (kind == "stream") Assert.True(Assert.Single(resolver.Streams).Closed);
        if (kind == "reader") Assert.NotEqual(ReadState.Closed, resolver.Reader!.ReadState);
        if (kind == "tree") Assert.Equal("outer", resolver.Tree!.CreateNavigator()!.SelectSingleNode("/*")!.LocalName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelectedDocumentsKeepResourceNamespacesAndInheritedBaseUri(bool forward)
    {
        const string body = "<xsl:for-each select='selected'><out uri='{document-uri(root(.))}' base='{base-uri(.)}' prefix='{namespace-uri-for-prefix(\"p\", .)}'/></xsl:for-each>";
        XElement result = XElement.Parse(Run(Sheet(Source(body)), new Resolver(Input), forward));
        Assert.Equal(Resource + "#chosen", (string?)result.Attribute("uri"));
        Assert.Equal("file:///fragment/child/", (string?)result.Attribute("base"));
        Assert.Equal("urn:value", (string?)result.Attribute("prefix"));
    }

    [Theory]
    [InlineData("xml:id", "", "stream")]
    [InlineData("xml:id", "", "tree")]
    [InlineData("key", "<!DOCTYPE doc [<!ATTLIST selected key ID #REQUIRED>]>", "stream")]
    [InlineData("key", "<!DOCTYPE doc [<!ELEMENT doc ANY><!ELEMENT selected (#PCDATA)><!ELEMENT other (#PCDATA)><!ATTLIST selected key ID #REQUIRED>]>", "tree")]
    public void ShorthandIdsComeFromXmlIdOrDeclaredIdMetadata(string attribute, string dtd, string kind)
    {
        string xml = dtd + "<doc><selected " + attribute + "='chosen'>value</selected><other>skip</other></doc>";
        var resolver = new Resolver(xml, kind);
        string body = "<xsl:value-of select='doc(\"" + Resource + "#chosen\")/selected'/>";
        Assert.Equal("value", Run(Sheet(body), resolver, false));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AValidatedProviderCanSupplySchemaIdMetadata(bool forward)
    {
        var resolver = new Resolver("<doc><selected key='chosen'>value</selected></doc>", "typed-reader");
        Assert.Equal("value", Run(Sheet(Source("<xsl:value-of select='selected'/>", attributes: "validation='preserve'")), resolver, forward));
        Assert.NotEqual(ReadState.Closed, resolver.Reader!.ReadState);
    }

    [Theory]
    [InlineData(false, "stream")]
    [InlineData(true, "stream")]
    [InlineData(false, "tree")]
    public void SelectedDtdNodesKeepTheirIdPropertyWhenGrounded(bool forward, string kind)
    {
        const string xml = "<!DOCTYPE doc [<!ELEMENT doc ANY><!ELEMENT selected ANY><!ELEMENT child (#PCDATA)><!ATTLIST selected key ID #REQUIRED><!ATTLIST child key ID #REQUIRED>]>"
            + "<doc><selected key='chosen'><child key='inside'>value</child></selected></doc>";
        string body = "<xsl:variable name='copy' select='copy-of(.)'/><xsl:value-of select='(count(id(\"chosen\", $copy)), count(id(\"inside\", $copy)))'/>";
        Assert.Equal("1 1", Run(Sheet(Source(body)), new Resolver(xml, kind), forward));
    }

    [Theory]
    [InlineData(false, "stream")]
    [InlineData(true, "stream")]
    [InlineData(false, "compact-tree")]
    public void SelectedDtdNodesKeepTheirIdrefPropertyWhenGrounded(bool forward, string kind)
    {
        const string xml = "<!DOCTYPE doc [<!ELEMENT doc ANY><!ELEMENT selected ANY><!ELEMENT child (#PCDATA)><!ATTLIST selected key ID #REQUIRED links IDREFS #IMPLIED><!ATTLIST child key ID #REQUIRED>]>"
            + "<doc><selected key='chosen' links='inside chosen'><child key='inside'>value</child></selected></doc>";
        string body = "<xsl:variable name='copy' select='copy-of(.)'/><xsl:value-of select='(count(idref(\"chosen\", $copy)), count(idref(\"inside\", $copy)))'/>";
        Assert.Equal("1 1", Run(Sheet(Source(body)), new Resolver(xml, kind), forward));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    public void PositionedReaderRetainsItsAlreadyParsedDtd(bool forward, bool modern, bool docFunction)
    {
        const string xml = "<!DOCTYPE doc [<!ATTLIST selected key ID #REQUIRED links IDREFS #IMPLIED><!ATTLIST child key ID #REQUIRED>]>"
            + "<doc><selected key='chosen' links='inside'><child key='inside'>value</child></selected></doc>";
        const string body = "<xsl:variable name='copy' select='copy-of(.)'/><xsl:value-of select='($copy/selected/child, count(id(\"inside\", $copy)), count(idref(\"inside\", $copy)))'/>";
        string selection = docFunction
            ? "<xsl:for-each select='doc(\"" + Resource + "#chosen\")'>" + body + "</xsl:for-each>"
            : Source(body);
        var resolver = new Resolver(xml, "positioned-reader");
        try
        {
            Assert.Equal("value 1 1", Run(Sheet(selection), resolver, forward, modern));
            Assert.NotEqual(ReadState.Closed, resolver.Reader!.ReadState);
        }
        finally { resolver.Reader?.Dispose(); }
    }

    [Theory]
    [InlineData("tree", false, false)]
    [InlineData("tree", true, false)]
    [InlineData("compact-tree", false, false)]
    [InlineData("compact-tree", true, false)]
    [InlineData("tree", false, true)]
    [InlineData("tree", true, true)]
    [InlineData("compact-tree", false, true)]
    [InlineData("compact-tree", true, true)]
    public void MatchingAttributeValuesDoNotInventIdDeclarations(string kind, bool modern, bool xmlId)
    {
        string dtd = xmlId ? "" : "<!DOCTYPE doc [<!ELEMENT doc ANY><!ELEMENT selected ANY><!ELEMENT child EMPTY>"
            + "<!ATTLIST selected key ID #REQUIRED><!ATTLIST child key ID #REQUIRED alias CDATA #IMPLIED>]>";
        string attribute = xmlId ? "xml:id" : "key";
        string xml = dtd + $"<doc><selected {attribute}='chosen'><child {attribute}='first' alias='not-an-id'/><child {attribute}='second' alias='second'/></selected></doc>";
        const string body = "<xsl:variable name='copy' select='copy-of(.)'/><xsl:variable name='second' select='copy-of($copy)'/><xsl:value-of select='(count(id(\"chosen\", $copy)), count(id(\"first\", $copy)), count(id(\"second\", $copy)), count(id(\"not-an-id\", $copy)), count(id(\"first\", $second)), count(id(\"second\", $second)), count(id(\"not-an-id\", $second)))'/>";
        Assert.Equal("1 1 1 0 1 1 0", Run(Sheet(Source(body)), new Resolver(xml, kind), false, modern));
        string lookup = "<xsl:for-each select='doc(\"" + Resource + "#chosen\")'>" + body + "</xsl:for-each>";
        Assert.Equal("1 1 1 0 1 1 0", Run(Sheet(lookup), new Resolver(xml, kind), false, modern));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void SameNamedAttributesKeepTheirLocalSchemaIdentity(bool modern, bool strip)
    {
        const string xml = "<doc><selected key='chosen'><left><item key='first' ref='chosen'/></left><right><item key='second' ref='chosen'/></right></selected></doc>";
        const string schema = "<xs:schema xmlns:xs='http://www.w3.org/2001/XMLSchema'><xs:element name='doc'><xs:complexType><xs:sequence>"
            + "<xs:element name='selected'><xs:complexType><xs:sequence>"
            + "<xs:element name='left'><xs:complexType><xs:sequence><xs:element name='item'><xs:complexType><xs:attribute name='key' type='xs:ID'/><xs:attribute name='ref' type='xs:IDREF'/></xs:complexType></xs:element></xs:sequence></xs:complexType></xs:element>"
            + "<xs:element name='right'><xs:complexType><xs:sequence><xs:element name='item'><xs:complexType><xs:attribute name='key' type='xs:string'/><xs:attribute name='ref' type='xs:string'/></xs:complexType></xs:element></xs:sequence></xs:complexType></xs:element>"
            + "</xs:sequence><xs:attribute name='key' type='xs:ID'/></xs:complexType></xs:element>"
            + "</xs:sequence></xs:complexType></xs:element></xs:schema>";
        var schemas = new XmlSchemaSet();
        using var schemaReader = XmlReader.Create(new StringReader(schema));
        schemas.Add(null, schemaReader);
        string validation = strip ? "strip" : "preserve";
        string body = "<xsl:variable name='copy'><xsl:copy-of select='.' validation='" + validation + "'/></xsl:variable>"
            + "<xsl:variable name='again' select='copy-of($copy)'/><xsl:value-of select='(count(id(\"first\", $again)), count(id(\"second\", $again)), count(idref(\"chosen\", $again)))'/>";
        var resolver = new Resolver(xml, "schema-tree") { Schemas = schemas };
        Assert.Equal("1 0 1", Run(Sheet(Source(body)), resolver, false, modern));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void XmlBaseOnTheSelectedElementIsAppliedOnlyOnce(bool forward)
    {
        var resolver = new Resolver(Input.Replace("xml:id='chosen'", "xml:id='chosen' xml:base='part/'"));
        string body = "<xsl:for-each select='selected'><out base='{base-uri(.)}'/></xsl:for-each>";
        Assert.Equal("file:///fragment/child/part/", (string?)XElement.Parse(Run(Sheet(Source(body)), resolver, forward)).Attribute("base"));
    }

    [Fact]
    public void DifferentFragmentsCannotShareACacheEntry()
    {
        var resolver = new Resolver("<doc><part xml:id='a'>first</part><part xml:id='b'>second</part></doc>");
        const string first = "doc('file:///fragment/input.xml#a')";
        const string second = "doc('file:///fragment/input.xml#b')";
        string sheet = Sheet("<out><xsl:value-of select=\"(" + first + "/part, " + second + "/part, " + first + " is " + second + ")\"/></out>");
        Assert.Equal("<out>first second false</out>", Run(sheet, resolver, false));
        Assert.Equal(2, resolver.Requests.Count);
    }

    [Fact]
    public void DocumentFunctionFragmentsSelectNodesOfOneCompleteResource()
    {
        var resolver = new Resolver("<doc><part xml:id='a'>first</part><part xml:id='b'>second</part></doc>");
        const string first = "document('file:///fragment/input.xml#a')";
        const string second = "document('file:///fragment/input.xml#b')";
        string sheet = Sheet("<out><xsl:value-of select=\"(" + first + ", " + second + ", root(" + first + ") is root(" + second + "), count(document('file:///fragment/input.xml#absent')))\"/></out>");
        Assert.Equal("<out>first second true 0</out>", Run(sheet, resolver, false));
        Assert.Single(resolver.Requests);
    }

    [Fact]
    public void RepeatedDocCallsReuseTheSelectedDocumentAndEmptyFragmentsReuseTheResource()
    {
        const string expression = "doc('file:///fragment/input.xml#chosen') is doc('file:///fragment/input.xml#chosen')";
        const string whole = "doc('file:///fragment/input.xml#') is doc('file:///fragment/input.xml')";
        var resolver = new Resolver(Input);
        string sheet = Sheet("<out><xsl:value-of select=\"(" + expression + ", " + whole + ")\"/></out>");
        Assert.Equal("<out>true true</out>", Run(sheet, resolver, false));
        Assert.Equal(2, resolver.Requests.Count);
        Assert.All(resolver.Requests, uri => Assert.Equal(Resource, uri.AbsoluteUri));
    }

    [Theory]
    [InlineData(false, "chosen", "<outer><selected id='chosen'/></outer>")]
    [InlineData(true, "chosen", "<outer><selected id='chosen'/></outer>")]
    [InlineData(false, "missing", Input)]
    [InlineData(true, "missing", Input)]
    public void MissingIdsAndOrdinaryIdNamedAttributesAreNotInvented(bool forward, string id, string xml)
    {
        var resolver = new Resolver(xml);
        string sheet = Sheet(Source("<xsl:copy-of select='.'/>", Resource + "#" + id));
        Assert.Contains("FODC0002", Assert.Throws<XsltDynamicException>(() => Run(sheet, resolver, forward)).Message);
        Assert.True(Assert.Single(resolver.Streams).Closed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnsupportedFragmentSyntaxDoesNotOpenTheResource(bool forward)
    {
        var resolver = new Resolver(Input);
        string sheet = Sheet(Source("<xsl:copy-of select='.'/>", Resource + "#xpointer(/)"));
        Assert.Contains("FODC0002", Assert.Throws<XsltDynamicException>(() => Run(sheet, resolver, forward)).Message);
        Assert.Empty(resolver.Requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PercentEncodedUnicodeIdIsDecodedBeforeMatching(bool forward)
    {
        var resolver = new Resolver("<doc><selected xml:id='café'>value</selected></doc>");
        Assert.Equal("value", Run(Sheet(Source("<xsl:value-of select='selected'/>", Resource + "#caf%C3%A9")), resolver, forward));
    }

    [Fact]
    public void InitialUriSelectionUsesTheSameFragmentPolicy()
    {
        string sheet = Sheet("<out><xsl:copy-of select='.'/></out>", "<xsl:mode streamable='yes'/>");
        var resolver = new Resolver(Input);
        var transformer = new XsltCompiler { Settings = new() { StreamedExecution = true, EnableDocumentFunction = true } }.Compile(new StringReader(sheet)).CreateTransformer();
        transformer.DocumentResolver = resolver;
        using var output = new StringWriter();
        transformer.ApplyTemplates(new Uri(Resource + "#chosen"), output);
        Assert.Equal("selected", Assert.Single(XElement.Parse(output.ToString()).Elements()).Name.LocalName);
        Assert.Equal(Resource, Assert.Single(resolver.Requests).AbsoluteUri);
        Assert.True(Assert.Single(resolver.Streams).Closed);
    }

    [Fact]
    public void CancellationIsObservedWhileScanningThePrefix()
    {
        using var cancellation = new CancellationTokenSource();
        var resolver = new Resolver("<doc>" + string.Concat(System.Linq.Enumerable.Repeat("<before/>", 10000)) + "<selected xml:id='chosen'/></doc>")
            { OnRead = () => cancellation.Cancel() };
        var transformer = new XsltCompiler { Settings = new() { StreamedExecution = true, EnableDocumentFunction = true } }
            .Compile(new StringReader(Sheet(Source("<xsl:copy-of select='.'/>") ))).CreateTransformer();
        transformer.DocumentResolver = resolver;
        transformer.CancellationToken = cancellation.Token;
        Assert.Contains("cancelled", Assert.Throws<XsltDynamicException>(() => transformer.CallTemplate(null, new StringWriter())).Message);
        Assert.True(Assert.Single(resolver.Streams).Closed);
    }

    [Fact]
    public void CancelledNavigatorSelectionDoesNotInspectTheTree()
    {
        using var input = XmlReader.Create(new StringReader(Input));
        XPathNavigator tree = new XPathDocument(input).CreateNavigator();
        Assert.Throws<OperationCanceledException>(() => DocumentFragment.Parse(new Uri(Resource + "#chosen"))
            .Select(tree, new CancellationToken(canceled: true)));
        Assert.Equal(XPathNodeType.Root, tree.NodeType);
    }

#if NET10_0_OR_GREATER
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavedStylesheetsKeepFragmentSelection(bool debug)
    {
        var resolver = new Resolver(Input);
        Assert.Equal("onetwo", Run(Sheet(Source("<xsl:value-of select='selected/p:value' xmlns:p='urn:value' separator=''/>") ), resolver, true, modern: false, saved: true, debug: debug));
    }
#endif

    private static string Run(string sheet, Resolver resolver, bool forward, bool modern = true, bool saved = false, bool debug = false)
    {
        var settings = new XsltSettings { StreamedExecution = forward, EnableDocumentFunction = true };
        using var output = new StringWriter();
        if (modern)
        {
            var transformer = new XsltCompiler { Settings = settings }.Compile(new StringReader(sheet)).CreateTransformer();
            transformer.DocumentResolver = resolver;
            transformer.CallTemplate(null, output);
        }
        else
        {
            var transform = new XslCompiledTransform(enableDebug: debug);
            using var source = XmlReader.Create(new StringReader(sheet));
            if (saved)
            {
#if NET10_0_OR_GREATER
                using var assembly = new MemoryStream();
                transform.CompileToAssembly(source, settings, null, "Xsl3.Tests.Fragment", "Saved.Fragment", assembly);
                transform.Load(System.Reflection.Assembly.Load(assembly.ToArray()).GetType("Saved.Fragment", true)!, settings);
#endif
            }
            else transform.Load(source, settings, null);
            using var input = XmlReader.Create(new StringReader("<start/>"));
            transform.Transform(input, null, output, resolver);
        }
        return output.ToString();
    }

    private sealed class Resolver(string xml, string kind = "stream") : XmlResolver
    {
        internal readonly List<Uri> Requests = new();
        internal readonly List<TrackingStream> Streams = new();
        internal XmlReader? Reader;
        internal XPathDocument? Tree;
        internal XmlSchemaSet? Schemas;
        internal Action? OnRead;
        public override ICredentials? Credentials { set { } }
        public override object GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn)
        {
            Requests.Add(absoluteUri);
            if (kind == "schema-tree")
            {
                using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { ValidationType = ValidationType.Schema, Schemas = Schemas! }, Resource);
                return Xsl3.Tree.CompactDocument.Load(reader, XmlSpace.Preserve, typed: true);
            }
            if (kind == "typed-reader")
            {
                const string schema = "<xs:schema xmlns:xs='http://www.w3.org/2001/XMLSchema'><xs:element name='doc'><xs:complexType><xs:sequence>"
                    + "<xs:element name='selected'><xs:complexType><xs:simpleContent><xs:extension base='xs:string'><xs:attribute name='key' type='xs:ID'/></xs:extension></xs:simpleContent></xs:complexType></xs:element>"
                    + "</xs:sequence></xs:complexType></xs:element></xs:schema>";
                var schemas = new XmlSchemaSet();
                using var source = XmlReader.Create(new StringReader(schema)); schemas.Add(null, source);
                return Reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { ValidationType = ValidationType.Schema, Schemas = schemas }, Resource);
            }
            if (kind is "reader" or "positioned-reader")
            {
                Reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Parse }, Resource);
                if (kind == "positioned-reader") Reader.MoveToContent();
                return Reader;
            }
            if (kind == "tree")
            {
                using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Parse }, Resource);
                return Tree = new XPathDocument(reader);
            }
            if (kind == "compact-tree")
            {
                using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Parse }, Resource);
                return Xsl3.Tree.CompactDocument.Load(reader);
            }
            var stream = new TrackingStream(xml, () => OnRead?.Invoke()); Streams.Add(stream); return stream;
        }
    }
    private sealed class TrackingStream(string xml, Action read) : MemoryStream(Encoding.UTF8.GetBytes(xml))
    {
        internal bool Closed;
        public override int Read(byte[] buffer, int offset, int count) { read(); return base.Read(buffer, offset, count); }
        public override int Read(Span<byte> buffer) { read(); return base.Read(buffer); }
        protected override void Dispose(bool disposing) { Closed = true; base.Dispose(disposing); }
    }
}
