// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

/// <summary>The XSLT 3.0 API: XsltCompiler, XsltExecutable, Xslt30Transformer and the XDM values (docs/plan/api.md).</summary>
public class Xslt30ApiTests
{
    private const string Xsl = "http://www.w3.org/1999/XSL/Transform";

    private static XsltExecutable Compile(string body, XsltCompiler? compiler = null) =>
        (compiler ?? new XsltCompiler()).Compile(new StringReader(
            $"<xsl:stylesheet version='3.0' xmlns:xsl='{Xsl}' xmlns:xs='http://www.w3.org/2001/XMLSchema' xmlns:f='urn:f' exclude-result-prefixes='xs f'>" +
            "<xsl:output omit-xml-declaration='yes'/>" + body + "</xsl:stylesheet>"));

    private static XdmNode Parse(string xml) => XdmNode.Load(new StringReader(xml));

    [Fact]
    public void AtomicValuesKeepTheirTypes()
    {
        Assert.Equal(new QName("http://www.w3.org/2001/XMLSchema", "integer"), new XdmAtomicValue(42L).TypeName);
        Assert.Equal(42L, new XdmAtomicValue(42L).Value);
        Assert.Equal(1.5m, new XdmAtomicValue(1.5m).Value);
        Assert.Equal("2026-10-21", new XdmAtomicValue("2026-10-21", new QName("http://www.w3.org/2001/XMLSchema", "date")).StringValue);
        Assert.Throws<ArgumentException>(() => new XdmAtomicValue("21/10/2026", new QName("http://www.w3.org/2001/XMLSchema", "date")));
        Assert.True(new XdmAtomicValue("a").Equals(XdmValue.MakeValue("a")));
    }

    [Fact]
    public void MapsAndArraysAreImmutableValues()
    {
        var map = new XdmMap(new[] { new KeyValuePair<XdmAtomicValue, XdmValue>(new XdmAtomicValue("a"), new XdmAtomicValue(1L)) });
        XdmMap bigger = map.Put(new XdmAtomicValue("b"), XdmValue.MakeValue(new[] { 2, 3 }));
        Assert.Equal(1, map.Size);
        Assert.Equal(2, bigger.Size);
        Assert.Equal("2 3", bigger.Get("b")!.ToString());
        Assert.Null(map.Get("b"));
        var array = new XdmArray(new XdmValue[] { new XdmAtomicValue(1L), XdmValue.Empty });
        Assert.Equal(2, array.Size);
        Assert.Equal(0, array.Get(1).Count);
        Assert.Throws<InvalidOperationException>(() => array.StringValue);
    }

    [Fact]
    public void ApplyTemplatesToANode()
    {
        XsltExecutable executable = Compile("<xsl:template match='doc'><out><xsl:value-of select='count(*)'/></out></xsl:template>");
        var text = new StringWriter();
        executable.CreateTransformer().ApplyTemplates(Parse("<doc><a/><a/></doc>"), text);
        Assert.Equal("<out>2</out>", text.ToString());
    }

    [Fact]
    public void ApplyTemplatesToAtomicValuesInAMode()
    {
        XsltExecutable executable = Compile("<xsl:template match='.[. instance of xs:integer]' mode='m'><xsl:sequence select='. * 10'/></xsl:template>");
        XdmValue result = executable.CreateTransformer().ApplyTemplates(XdmValue.MakeValue(new[] { 1, 2, 3 }), new QName("m"));
        Assert.Equal(new object[] { 10L, 20L, 30L }, result.Select(i => ((XdmAtomicValue)i).Value));
    }

    [Fact]
    public void CallTemplateWithParametersAndATunnel()
    {
        XsltExecutable executable = Compile(
            "<xsl:param name='g' select='1'/>" +
            "<xsl:template name='t'><xsl:param name='p' required='yes'/><xsl:param name='q' tunnel='yes'/><out><xsl:value-of select='$p, $q, $g'/></out></xsl:template>");
        Xslt30Transformer transformer = executable.CreateTransformer();
        transformer.StylesheetParameters[new QName("g")] = new XdmAtomicValue(7L);
        var text = new StringWriter();
        transformer.CallTemplate(new QName("t"), text,
            new Dictionary<QName, XdmValue> { [new QName("p")] = new XdmAtomicValue("x") },
            new Dictionary<QName, XdmValue> { [new QName("q")] = new XdmAtomicValue("y") });
        Assert.Equal("<out>x y 7</out>", text.ToString());
        // A required parameter not given: XTDE0700, as an XsltDynamicException.
        XsltDynamicException e = Assert.Throws<XsltDynamicException>(() => transformer.CallTemplate(new QName("t")));
        Assert.Equal("XTDE0700", e.Code.LocalName);
    }

    [Fact]
    public void CallTemplateDefaultsToTheInitialTemplate()
    {
        XsltExecutable executable = Compile("<xsl:template name='xsl:initial-template'><xsl:sequence select='1 to 3'/></xsl:template>");
        Assert.Equal("1 2 3", executable.CreateTransformer().CallTemplate(null).ToString());
    }

    [Fact]
    public void CallFunctionReturnsItsValue()
    {
        XsltExecutable executable = Compile(
            "<xsl:function name='f:add' visibility='public'><xsl:param name='a'/><xsl:param name='b'/><xsl:sequence select='$a + $b'/></xsl:function>");
        XdmValue sum = executable.CreateTransformer().CallFunction(new QName("urn:f", "add"), new XdmAtomicValue(2L), new XdmAtomicValue(3L));
        Assert.Equal(5L, ((XdmAtomicValue)sum).Value);
        XdmValue map = executable.CreateTransformer().CallFunction(new QName("urn:f", "add"), new XdmAtomicValue(1.5m), new XdmAtomicValue(1L));
        Assert.Equal(2.5m, ((XdmAtomicValue)map).Value);
    }

    [Fact]
    public void ResultsCanBeMaps()
    {
        XsltExecutable executable = Compile("<xsl:template name='xsl:initial-template'><xsl:sequence select=\"map{'a': (1, 2), 'b': [3]}\"/></xsl:template>");
        var map = (XdmMap)executable.CreateTransformer().CallTemplate(null);
        Assert.Equal("1 2", map.Get("a")!.ToString());
        Assert.Equal(3L, ((XdmAtomicValue)((XdmArray)map.Get("b")!).Get(0)).Value);
    }

    [Fact]
    public void DynamicErrorsHaveTheirCodes()
    {
        XsltExecutable executable = Compile(
            "<xsl:template name='xsl:initial-template'><xsl:sequence select=\"error(QName('urn:mine', 'oops'), 'failed', 42)\"/></xsl:template>");
        XsltDynamicException e = Assert.Throws<XsltDynamicException>(() => executable.CreateTransformer().CallTemplate(null));
        Assert.Equal(new QName("urn:mine", "oops"), e.Code);
        Assert.Equal("failed", e.Description);
        Assert.Equal("42", e.ErrorObject.ToString());
    }

    [Fact]
    public void TerminatingMessagesAreDynamicErrors()
    {
        XsltExecutable executable = Compile(
            "<xsl:template name='xsl:initial-template'><xsl:message terminate='yes' error-code='m:stop' xmlns:m='urn:mine'>stopping</xsl:message></xsl:template>");
        var messages = new List<string>();
        Xslt30Transformer transformer = executable.CreateTransformer();
        transformer.MessageHandler = messages.Add;
        XsltDynamicException e = Assert.Throws<XsltDynamicException>(() => transformer.CallTemplate(null));
        Assert.Equal(new QName("urn:mine", "stop"), e.Code);
    }

    [Fact]
    public void StaticErrorsAreListed()
    {
        XsltStaticException e = Assert.Throws<XsltStaticException>(() => Compile("<xsl:template match='/'><xsl:value-of select='$undeclared'/></xsl:template>"));
        Assert.Contains(e.Errors, error => error.Code?.LocalName == "XPST0008" && error.LineNumber > 0);
    }

    [Fact]
    public void StaticParametersAndMessages()
    {
        var compiler = new XsltCompiler();
        compiler.StaticParameters[new QName("debug")] = new XdmAtomicValue(true);
        XsltExecutable executable = Compile(
            "<xsl:param name='debug' static='yes' as='xs:boolean' select='false()'/>" +
            "<xsl:template name='xsl:initial-template'><xsl:message use-when='$debug'>debugging</xsl:message><done/></xsl:template>", compiler);
        var messages = new List<string>();
        Xslt30Transformer transformer = executable.CreateTransformer();
        transformer.MessageHandler = messages.Add;
        Assert.Equal("<done />", transformer.CallTemplate(null).ToString());
        Assert.Equal(new[] { "debugging" }, messages);
    }

    [Fact]
    public void SecondaryResultsGoToTheHandlerUnderThisRunsBaseOutputUri()
    {
        XsltExecutable executable = Compile(
            "<xsl:template name='xsl:initial-template'><xsl:result-document href='part.xml'><part/></xsl:result-document><main/></xsl:template>");
        var parts = new Dictionary<Uri, StringWriter>();
        Xslt30Transformer transformer = executable.CreateTransformer();
        transformer.BaseOutputUri = new Uri("file:///out/main.xml");
        transformer.ResultDocumentHandler = uri => parts[uri] = new StringWriter();
        var text = new StringWriter();
        transformer.CallTemplate(null, text);
        Assert.Equal("<main />", text.ToString());
        Assert.Contains("<part", parts[new Uri("file:///out/part.xml")].ToString());
    }

    /// <summary>A document's xml-stylesheet processing instruction names its stylesheet, here embedded in it.</summary>
    [Fact]
    public void AssociatedStylesheetsAreCompiled()
    {
        string file = Path.Combine(Path.GetTempPath(), "xsl3-associated-" + Guid.NewGuid().ToString("N") + ".xml");
        File.WriteAllText(file,
            "<?xml-stylesheet type='text/xsl' href='#style'?>" +
            "<doc><xsl:stylesheet id='style' version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform'>" +
            "<xsl:output omit-xml-declaration='yes'/><xsl:template match='/'><n><xsl:value-of select='count(//item)'/></n></xsl:template>" +
            "</xsl:stylesheet><item/><item/></doc>");
        try
        {
            XdmNode document;
            using (FileStream stream = File.OpenRead(file))
            {
                document = XdmNode.Load(stream, new Uri(file).AbsoluteUri);
            }
            Assert.Equal(new Uri(file).AbsoluteUri + "#style", XsltCompiler.GetAssociatedStylesheet(document)!.AbsoluteUri);
            var text = new StringWriter();
            new XsltCompiler().CompileAssociatedStylesheet(document).CreateTransformer().ApplyTemplates(document, text);
            Assert.Equal("<n>2</n>", text.ToString());
            Assert.Null(XsltCompiler.GetAssociatedStylesheet(Parse("<doc/>")));
        }
        finally
        {
            File.Delete(file);
        }
    }

    private sealed class Recorder : IXsltTraceListener
    {
        public readonly List<string> Events = new();

        public void Enter(XsltFrame frame) => Events.Add("+" + (frame.Name?.LocalName ?? frame.Match));

        public void Leave(XsltFrame frame) => Events.Add("-" + (frame.Name?.LocalName ?? frame.Match));
    }

    /// <summary>With EnableTracing, a trace listener sees templates and functions start and end.</summary>
    [Fact]
    public void TraceListenersSeeTemplatesAndFunctions()
    {
        var compiler = new XsltCompiler { Settings = new XsltSettings { EnableTracing = true } };
        XsltExecutable executable = Compile(
            "<xsl:function name='f:twice'><xsl:param name='x'/><xsl:sequence select='$x * 2'/></xsl:function>" +
            "<xsl:template match='doc'><out><xsl:call-template name='t'/></out></xsl:template>" +
            "<xsl:template name='t'><xsl:value-of select='f:twice(21)'/></xsl:template>", compiler);
        var recorder = new Recorder();
        Xslt30Transformer transformer = executable.CreateTransformer();
        transformer.TraceListener = recorder;
        var text = new StringWriter();
        transformer.ApplyTemplates(Parse("<doc/>"), text);
        Assert.Equal("<out>42</out>", text.ToString());
        Assert.Equal(new[] { "+doc", "+t", "+twice", "-twice", "-t", "-doc" }, recorder.Events);
    }

    /// <summary>With EnableTracing, a dynamic error carries the templates and functions that were running.</summary>
    [Fact]
    public void DynamicErrorsCarryTheStylesheetStack()
    {
        var compiler = new XsltCompiler { Settings = new XsltSettings { EnableTracing = true } };
        XsltExecutable executable = Compile(
            "<xsl:function name='f:fail'><xsl:param name='x'/><xsl:sequence select='$x div 0'/></xsl:function>" +
            "<xsl:template match='doc'><out><xsl:call-template name='t'/></out></xsl:template>" +
            "<xsl:template name='t'><xsl:value-of select='f:fail(1)'/></xsl:template>", compiler);
        XsltDynamicException e = Assert.Throws<XsltDynamicException>(() => executable.CreateTransformer().ApplyTemplates(Parse("<doc/>"), new StringWriter()));
        Assert.Equal("FOAR0001", e.Code.LocalName);
        Assert.Equal(new[] { "fail", "t", null }, e.StylesheetStack.Select(f => f.Name?.LocalName));
        Assert.Equal("doc", e.StylesheetStack[2].Match);
        Assert.True(e.StylesheetStack[0].LineNumber > 0);
    }

    /// <summary>Without tracing, the stylesheet stack is read from the .NET one: the templates and functions not inlined.</summary>
    [Fact]
    public void DynamicErrorsCarryTheStylesheetStackWithoutTracing()
    {
        XsltExecutable executable = Compile(
            "<xsl:function name='f:fail'><xsl:param name='x'/><xsl:sequence select='$x div 0'/></xsl:function>" +
            "<xsl:template match='doc'><out><xsl:apply-templates/><xsl:call-template name='t'/></out></xsl:template>" +
            "<xsl:template name='t'><xsl:value-of select='f:fail(count(//*) - 1)'/></xsl:template>");
        XsltDynamicException e = Assert.Throws<XsltDynamicException>(() => executable.CreateTransformer().ApplyTemplates(Parse("<doc/>"), new StringWriter()));
        Assert.Equal("FOAR0001", e.Code.LocalName);
        Assert.Contains(e.StylesheetStack, f => f.Name?.LocalName == "t" && f.LineNumber > 0);
        Assert.Contains(e.StylesheetStack, f => f.Match == "doc");
        Assert.True(e.StylesheetStack.ToList().FindIndex(f => f.Name?.LocalName == "t") < e.StylesheetStack.ToList().FindIndex(f => f.Match == "doc"));
    }

    /// <summary>Explain writes the QIL the stylesheet compiles to, with its template rules as functions.</summary>
    [Fact]
    public void ExplainWritesTheCompiledForm()
    {
        var text = new StringWriter();
        Compile("<xsl:template match='doc'><out/></xsl:template>").Explain(text);
        Assert.StartsWith("<", text.ToString().TrimStart());
        Assert.Contains("<QilExpression", text.ToString());
        Assert.Contains("match=&quot;doc&quot;", text.ToString());
    }

#if NET9_0_OR_GREATER
    /// <summary>
    /// Sink B: a stylesheet compiled into a saved assembly, with its constants, output formats, function items and
    /// trace frames, runs from Load(Type) as it does from source.
    /// </summary>
    [Fact]
    public void SavedAssembliesRunAsCompiledFromSource()
    {
        string stylesheet = $"<xsl:stylesheet version='3.0' xmlns:xsl='{Xsl}' xmlns:xs='http://www.w3.org/2001/XMLSchema' xmlns:f='urn:f' exclude-result-prefixes='xs f'>" +
            "<xsl:output omit-xml-declaration='yes'/><xsl:decimal-format name='eu' decimal-separator=',' grouping-separator='.'/>" +
            "<xsl:accumulator name='n' initial-value='0' as='xs:integer'><xsl:accumulator-rule match='i' select='$value + 1'/></xsl:accumulator>" +
            "<xsl:mode use-accumulators='n'/>" +
            "<xsl:function name='f:double' as='xs:integer'><xsl:param name='x' as='xs:integer'/><xsl:sequence select='$x * 2'/></xsl:function>" +
            "<xsl:template match='/'><out><xsl:variable name='m' select=\"map{'a': [1, 2.5, xs:date('2026-10-21')]}\"/>" +
            "<xsl:value-of select=\"sort((3, 1, 2), (), function($k) { -$k }), $m?a?3 + xs:dayTimeDuration('P1D'), format-number(1234.5, '#.##0,0', 'eu'), f:double(21), accumulator-after('n')\"/>" +
            "<xsl:try><xsl:sequence select=\"error(QName('urn:f', 'x'), 'boom')\"/><xsl:catch errors='f:x'>caught</xsl:catch></xsl:try>" +
            "</out></xsl:template></xsl:stylesheet>";
        var settings = new XsltSettings { EnableTracing = true };
        var fromSource = new XslCompiledTransform();
        fromSource.Load(XmlReader.Create(new StringReader(stylesheet)), settings, null);
        var saved = new MemoryStream();
        new XslCompiledTransform().CompileToAssembly(XmlReader.Create(new StringReader(stylesheet)), settings, null, "Xsl3.Tests.Saved", "Saved.Stylesheet", saved);
        // (Not into a collectible context: docs/plan/p4-sink-b.md.)
        var fromAssembly = new XslCompiledTransform();
        fromAssembly.Load(System.Reflection.Assembly.Load(saved.ToArray()).GetType("Saved.Stylesheet", throwOnError: true)!, settings);
        string Run(XslCompiledTransform t)
        {
            var text = new StringWriter();
            t.Transform(XmlReader.Create(new StringReader("<doc><i/><i/></doc>")), null, text);
            return text.ToString();
        }
        Assert.Equal("<out>3 2 1 2026-10-22 1.234,5 42 2caught</out>", Run(fromSource));
        Assert.Equal(Run(fromSource), Run(fromAssembly));
    }

    /// <summary>Compiled with debug information, a saved assembly embeds a portable PDB mapping its IL to the stylesheet's lines.</summary>
    [Fact]
    public void SavedAssembliesWithDebugInformationMapToStylesheetLines()
    {
        string file = Path.Combine(Path.GetTempPath(), "xsl3-pdb-" + Guid.NewGuid().ToString("N") + ".xsl");
        File.WriteAllText(file, $"<xsl:stylesheet version='3.0' xmlns:xsl='{Xsl}'>\n<xsl:template match='/'>\n<out>\n<xsl:value-of select='count(//*)'/>\n</out>\n</xsl:template>\n</xsl:stylesheet>");
        try
        {
            var saved = new MemoryStream();
            using (XmlReader reader = XmlReader.Create(file))
            {
                new XslCompiledTransform(enableDebug: true).CompileToAssembly(reader, null, null, "Xsl3.Tests.Debug", "Debug.Stylesheet", saved);
            }
            using var pe = new System.Reflection.PortableExecutable.PEReader(new MemoryStream(saved.ToArray()));
            var embedded = pe.ReadDebugDirectory().Single(d => d.Type == System.Reflection.PortableExecutable.DebugDirectoryEntryType.EmbeddedPortablePdb);
            using System.Reflection.Metadata.MetadataReaderProvider provider = pe.ReadEmbeddedPortablePdbDebugDirectoryData(embedded);
            System.Reflection.Metadata.MetadataReader pdb = provider.GetMetadataReader();
            Assert.Contains(pdb.Documents, d => pdb.GetString(pdb.GetDocument(d).Name).EndsWith(Path.GetFileName(file), StringComparison.Ordinal));
            var lines = pdb.MethodDebugInformation.SelectMany(m => pdb.GetMethodDebugInformation(m).GetSequencePoints()).Where(p => !p.IsHidden).Select(p => p.StartLine).ToHashSet();
            Assert.Contains(2, lines); // the template
            Assert.Contains(4, lines); // xsl:value-of
            // And it runs.
            var transform = new XslCompiledTransform();
            transform.Load(System.Reflection.Assembly.Load(saved.ToArray()).GetType("Debug.Stylesheet", throwOnError: true)!, null);
            var text = new StringWriter();
            transform.Transform(XmlReader.Create(new StringReader("<a><b/></a>")), null, text);
            Assert.Contains("<out>2</out>", text.ToString());
        }
        finally
        {
            File.Delete(file);
        }
    }

    /// <summary>A saved assembly keeps the frames its methods map to: a dynamic error carries the stylesheet stack.</summary>
    [Fact]
    public void SavedAssembliesKeepTheStylesheetStack()
    {
        var saved = new MemoryStream();
        new XslCompiledTransform().CompileToAssembly(XmlReader.Create(new StringReader(
            $"<xsl:stylesheet version='3.0' xmlns:xsl='{Xsl}'><xsl:template match='/'><out><xsl:call-template name='t'/></out></xsl:template>" +
            "<xsl:template name='t'><xsl:value-of select='1 div (count(//*) - 1)'/></xsl:template></xsl:stylesheet>")),
            null, null, "Xsl3.Tests.Frames", "Frames.Stylesheet", saved);
        var transform = new XslCompiledTransform();
        transform.Load(System.Reflection.Assembly.Load(saved.ToArray()).GetType("Frames.Stylesheet", throwOnError: true)!, null);
        Exception e = Assert.ThrowsAny<Exception>(() => transform.Transform(XmlReader.Create(new StringReader("<a/>")), null, new StringWriter()));
        XsltDynamicException error = XsltDynamicException.From(e)!;
        Assert.Equal(new[] { "t", null }, error.StylesheetStack.Select(f => f.Name?.LocalName));
    }
#endif

    /// <summary>
    /// Registered extension functions: called with XDM values, reported by function-available, their exceptions
    /// catchable as Q{http://xsl3/err}extension-error, and off with EnableExtensionFunctions = false.
    /// </summary>
    [Fact]
    public void RegisteredExtensionFunctions()
    {
        var compiler = new XsltCompiler();
        compiler.RegisterExtensionFunction(new QName("urn:ext", "scale"), 2,
            args => new XdmAtomicValue((decimal)((XdmAtomicValue)args[0][0]).Value * (decimal)(long)((XdmAtomicValue)args[1][0]).Value));
        compiler.RegisterExtensionFunction(new QName("urn:ext", "names"), 1,
            args => new XdmValue(args[0].Select(n => (XdmItem)new XdmAtomicValue(((XdmNode)n).NodeName!.LocalName))));
        compiler.RegisterExtensionFunction(new QName("urn:ext", "fail"), 0, _ => throw new InvalidOperationException("boom"));
        string body =
            "<xsl:template name='xsl:initial-template' xmlns:ext='urn:ext' exclude-result-prefixes='ext'><xsl:variable name='v'><a/><b/></xsl:variable><out><xsl:value-of select=\"ext:scale(1.5, 4), ext:names($v/*), function-available('ext:scale', 2), function-available('ext:scale', 3)\"/>" +
            "|<xsl:try><xsl:sequence select='ext:fail()'/><xsl:catch errors='Q{http://xsl3/err}extension-error'>caught</xsl:catch></xsl:try></out></xsl:template>";
        Assert.Equal("<out>6 a b true false|caught</out>", Compile(body, compiler).CreateTransformer().CallTemplate(null).ToString());

        var off = new XsltCompiler { Settings = new XsltSettings { EnableExtensionFunctions = false } };
        off.RegisterExtensionFunction(new QName("urn:ext", "scale"), 2, args => args[0]);
        Assert.Throws<XsltStaticException>(() => Compile("<xsl:template name='xsl:initial-template' xmlns:ext='urn:ext'><xsl:sequence select='ext:scale(1, 2)'/></xsl:template>", off));
        Assert.Throws<ArgumentException>(() => compiler.RegisterExtensionFunction(new QName("http://www.w3.org/2005/xpath-functions", "x"), 0, _ => XdmValue.Empty));
    }

    [Fact]
    public void AnExecutableServesManyTransformersAndEntryPoints()
    {
        XsltExecutable executable = Compile(
            "<xsl:template match='/'><a/></xsl:template><xsl:template name='b'><b/></xsl:template>");
        XdmNode input = Parse("<doc/>");
        string[] results = Enumerable.Range(0, 8).AsParallel().Select(i =>
        {
            Xslt30Transformer t = executable.CreateTransformer();
            return i % 2 == 0 ? t.ApplyTemplates(input).ToString() : t.CallTemplate(new QName("b")).ToString();
        }).ToArray();
        Assert.All(results.Where((_, i) => i % 2 == 0), r => Assert.Equal("<a />", r));
        Assert.All(results.Where((_, i) => i % 2 == 1), r => Assert.Equal("<b />", r));
    }
}
