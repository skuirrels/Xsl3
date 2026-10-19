// Licensed under the MIT license. See LICENSE.TXT in the repository root.

// A developer tool for working on Xsl3 itself (not the packaged CLI of SPEC §21.3):
//   Xsl3.Dev run  <stylesheet> [<input>] [-it <template>] [-im <mode>] [-p <package file>]... [-param name=value]...
//                                                                       transform to stdout (a number value is a double)
//   Xsl3.Dev qil  <stylesheet>                                            the QIL the compiler produces
// Set XSL3_XSLT2=1 to compile 2.0+ stylesheets with the 2.0 path.

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

var settings = new XsltSettings(enableDocumentFunction: true, enableScript: false);
if (command == "qil")
{
    var compiler = new Compiler(settings, debug: false, scriptAssemblyPath: null);
    var errors = compiler.Compile(new Uri(stylesheet).AbsoluteUri, new XmlUrlResolver(), new XmlUrlResolver(), out QilExpression qil);
    foreach (CompilerError e in errors)
    {
        Console.Error.WriteLine($"{e.FileName}({e.Line},{e.Column}): {e.ErrorText}");
    }
    using XmlWriter w = XmlWriter.Create(Console.Out, new XmlWriterSettings { Indent = true });
    new QilXmlWriter(w).ToXml(qil);
    return 0;
}

var transform = new XslCompiledTransform
{
    InitialTemplate = initialTemplate == null ? null : new XmlQualifiedName(initialTemplate),
    InitialMode = initialMode == null ? null : new XmlQualifiedName(initialMode),
    Packages = packages,
};
transform.Load(stylesheet, settings, new XmlUrlResolver());
// An XmlReader, so that xsl:strip-space applies to the input.
using XmlReader source = input != null
    ? XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Parse })
    : XmlReader.Create(new StringReader("<empty/>"));
// The TextWriter overload serializes as the stylesheet's output definition says (xhtml, html5, json, …).
transform.Transform(source, arguments, Console.Out, new XmlUrlResolver());
Console.WriteLine();
return 0;
