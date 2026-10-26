// Licensed under the MIT license. See LICENSE.TXT in the repository root.

// A developer tool for working on Xsl3 itself (not the packaged CLI of SPEC §21.3):
//   Xsl3.Dev run  <stylesheet> [<input>] [-it <template or Q{ns}template>] [-im <mode>] [-p <package file>]... [-param name=value]...
//                                                                       transform to stdout (a number value is a double)
//   Xsl3.Dev qil  <stylesheet>                                            the QIL the compiler produces
//   Xsl3.Dev qilopt <stylesheet>                                          the QIL after the optimizer, as code generation sees it
// Set XSL3_XSLT2=1 to compile 2.0+ stylesheets with the 2.0 path; XSL3_DEV_TIME=n times n runs of `run` instead;
// XSL3_DEV_SA=1 to make the processor schema-aware.

using System;
using System.IO;
using System.Xml;
using System.Xml.XPath;
using Xsl3;
using Xsl3.Qil;
using Xsl3.Xslt;

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: xsl3 run <stylesheet> [<input>] [-it <template>] [-im <mode>] | xsl3 qil <stylesheet>");
    return 2;
}

string command = args[0], stylesheet = Path.GetFullPath(args[1]);
string? input = null, initialTemplate = null, initialMode = null;
var packages = new System.Collections.Generic.List<(string, string, Uri)>();
var arguments = new XsltArgumentList();
for (int i = 2; i < args.Length; i++)
{
    switch (args[i])
    {
        case "-it": initialTemplate = args[++i]; break;
        case "-im": initialMode = args[++i]; break;
        case "-p":
            // A package xsl:use-package can find: its name and version are its xsl:package's.
            string file = Path.GetFullPath(args[++i]);
            var root = System.Xml.Linq.XDocument.Load(file).Root!;
            packages.Add(((string?)root.Attribute("name") ?? "", (string?)root.Attribute("package-version") ?? "1", new Uri(file)));
            break;
        case "-param":
            string[] nv = args[++i].Split('=', 2);
            arguments.AddParam(nv[0], "", double.TryParse(nv[1], System.Globalization.CultureInfo.InvariantCulture, out double d) ? d : nv[1]);
            break;
        default: input = args[i]; break;
    }
}

var settings = new XsltSettings(enableDocumentFunction: true, enableScript: false)
{
    // XSL3_DEV_W3C=1: the W3C runner's settings.
    EnableEvaluate = Environment.GetEnvironmentVariable("XSL3_DEV_W3C") == "1",
    VersionMode = Environment.GetEnvironmentVariable("XSL3_DEV_W3C") == "1" ? XsltVersionMode.BackwardsCompatible : XsltVersionMode.Legacy,
    // XSL3_DEV_SA=1: schema-aware (P6).
    SchemaAware = Environment.GetEnvironmentVariable("XSL3_DEV_SA") == "1",
};
if (command == "compile")
{
    // compile style.xsl out.dll: a saved assembly (Sink B); "runcompiled out.dll input.xml" runs it.
    using XmlReader reader = XmlReader.Create(new Uri(stylesheet).AbsoluteUri, new XmlReaderSettings { DtdProcessing = DtdProcessing.Parse });
    using FileStream dll = File.Create(input!);
    new XslCompiledTransform { InitialTemplate = initialTemplate == null ? null : new XmlQualifiedName(initialTemplate) }
        .CompileToAssembly(reader, settings, new XmlUrlResolver(), Path.GetFileNameWithoutExtension(input!), "Compiled.Stylesheet", dll);
    return 0;
}
if (command == "jitall")
{
    // jitall out.dll: JIT-compiles every method of a saved assembly, naming the ones the runtime rejects.
    Type saved = System.Reflection.Assembly.LoadFrom(stylesheet).GetType("Compiled.Stylesheet", throwOnError: true)!;
    int bad = 0;
    foreach (System.Reflection.MethodInfo method in saved.GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly))
    {
        try
        {
            System.Runtime.CompilerServices.RuntimeHelpers.PrepareMethod(method.MethodHandle);
        }
        catch (Exception e)
        {
            bad++;
            Console.WriteLine($"{method.Name}: {e.GetType().Name}: {e.Message}");
        }
    }
    Console.WriteLine($"{bad} method(s) rejected");
    return bad == 0 ? 0 : 1;
}
if (command == "runcompiled")
{
    // XSL3_DEV_COLLECTIBLE=1: into a collectible load context, as the W3C runner's round trip does.
    Type type = (Environment.GetEnvironmentVariable("XSL3_DEV_COLLECTIBLE") == "1"
        ? new System.Runtime.Loader.AssemblyLoadContext("compiled", isCollectible: true).LoadFromStream(new MemoryStream(File.ReadAllBytes(stylesheet)))
        : System.Reflection.Assembly.LoadFrom(stylesheet)).GetType("Compiled.Stylesheet", throwOnError: true)!;
    var compiled = new XslCompiledTransform { InitialTemplate = initialTemplate == null ? null : new XmlQualifiedName(initialTemplate) };
    compiled.Load(type, settings);
    if (initialTemplate != null)
    {
        // As the W3C runner does: no input, a stream.
        var bytes = new MemoryStream();
        void RunIt() => compiled.Transform(XslCompiledTransform.AbsentInput, arguments, bytes, new XmlUrlResolver());
        if (Environment.GetEnvironmentVariable("XSL3_DEV_THREAD") == "1")
        {
            var thread = new System.Threading.Thread(RunIt, 64 * 1024 * 1024);
            thread.Start();
            thread.Join();
        }
        else
        {
            RunIt();
        }
        Console.Write(System.Text.Encoding.UTF8.GetString(bytes.ToArray()));
        return 0;
    }
    using XmlReader compiledInput = input != null ? XmlReader.Create(input) : XmlReader.Create(new StringReader("<empty/>"));
    compiled.Transform(compiledInput, arguments, Console.Out, new XmlUrlResolver());
    Console.WriteLine();
    return 0;
}
if (command is "qil" or "qilopt")
{
    var compiler = new Compiler(settings, debug: false, scriptAssemblyPath: null);
    var errors = compiler.Compile(new Uri(stylesheet).AbsoluteUri, new XmlUrlResolver(), new XmlUrlResolver(), out QilExpression? qil);
    foreach (CompilerError e in errors)
    {
        Console.Error.WriteLine($"{e.FileName}({e.Line},{e.Column}): {e.ErrorText}");
    }
    if (qil == null) return 1;
    if (command == "qilopt")
    {
        qil = new Xsl3.IlGen.XmlILOptimizerVisitor(qil, !qil.IsDebug).Optimize();
    }
    using XmlWriter w = XmlWriter.Create(Console.Out, new XmlWriterSettings { Indent = true });
    new QilXmlWriter(w).ToXml(qil);
    return 0;
}

var transform = new XslCompiledTransform
{
    InitialTemplate = initialTemplate == null ? null
        : initialTemplate.StartsWith("Q{", StringComparison.Ordinal) ? new XmlQualifiedName(initialTemplate[(initialTemplate.IndexOf('}') + 1)..], initialTemplate[2..initialTemplate.IndexOf('}')])
        : new XmlQualifiedName(initialTemplate),
    InitialMode = initialMode == null ? null : new XmlQualifiedName(initialMode),
    Packages = packages,
};
transform.Load(stylesheet, settings, new XmlUrlResolver());
if (int.TryParse(Environment.GetEnvironmentVariable("XSL3_DEV_TIME"), out int runs) && runs > 0 && input != null)
{
    // XSL3_DEV_TIME=n: n transforms of the input (as an XPathDocument) to nowhere; prints the median in ms.
    var document = new XPathDocument(input);
    var times = new double[runs];
    for (int i = -3; i < runs; i++)
    {
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        transform.Transform(document, arguments, Stream.Null);
        if (i >= 0) times[i] = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }
    Array.Sort(times);
    Console.WriteLine($"{Path.GetFileName(stylesheet)}: median {times[runs / 2]:F2} ms, min {times[0]:F2} ms");
    return 0;
}
// An XmlReader, so that xsl:strip-space applies to the input. XSL3_DEV_XSD=schema.xsd validates it (P6).
var readerSettings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Parse };
if (Environment.GetEnvironmentVariable("XSL3_DEV_XSD") is { Length: > 0 } xsd)
{
    System.Xml.Schema.XmlSchemaSet schemas = Xsl3.Schema.SchemaDocuments.CreateSet(new XmlUrlResolver());
    foreach (string file in xsd.Split(','))
    {
        schemas.Add(null, new Uri(Path.GetFullPath(file)).AbsoluteUri);
    }
    readerSettings.ValidationType = ValidationType.Schema;
    readerSettings.Schemas = schemas;
}
using XmlReader source = input != null
    ? XmlReader.Create(input, readerSettings)
    : XmlReader.Create(new StringReader("<empty/>"));
// The TextWriter overload serializes as the stylesheet's output definition says (xhtml, html5, json, …).
transform.Transform(source, arguments, Console.Out, new XmlUrlResolver());
Console.WriteLine();
return 0;
