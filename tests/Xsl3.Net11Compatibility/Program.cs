// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Reflection;
using System.Xml;
using System.Xml.Linq;
using Xsl3;

if (Environment.Version.Major != 11)
    throw new InvalidOperationException($"Expected .NET 11, got {Environment.Version}.");

string expected = string.Join(",", XDocument.Load("Input.xml").Root!.Elements("item").Select(item => item.Value));

var legacy = new XslCompiledTransform();
using (var style = XmlReader.Create("Report.xsl"))
    legacy.Load(style, new XsltSettings { StreamedExecution = true }, null);
CheckTransform(legacy, "XslCompiledTransform");

var compiler = new XsltCompiler { Settings = new XsltSettings { StreamedExecution = true } };
var transformer = compiler.Compile(new Uri(Path.GetFullPath("Report.xsl"))).CreateTransformer();
using (var reader = XmlReader.Create("Input.xml"))
using (var output = new StringWriter())
{
    transformer.ApplyTemplates(reader, output);
    CheckOutput("XsltCompiler", output.ToString());
}

var saved = new XslCompiledTransform();
saved.Load(typeof(Stylesheets.Report), new XsltSettings());
CheckTransform(saved, "MSBuild saved stylesheet");

CheckOutput("CLI transform", File.ReadAllText("cli-output.txt"));
var cliAssembly = Assembly.LoadFrom(Path.GetFullPath("CliReport.dll"));
var cliSaved = new XslCompiledTransform();
cliSaved.Load(cliAssembly.GetType("Stylesheets.Report", throwOnError: true)!, new XsltSettings());
CheckTransform(cliSaved, "CLI saved stylesheet");
Console.WriteLine($"Verified package consumers on .NET {Environment.Version}.");

void CheckTransform(XslCompiledTransform transform, string name)
{
    using var reader = XmlReader.Create("Input.xml");
    using var output = new StringWriter();
    transform.Transform(reader, null, output);
    CheckOutput(name, output.ToString());
}

void CheckOutput(string name, string actual)
{
    if (actual != expected)
        throw new InvalidOperationException($"{name} produced '{actual}', expected '{expected}'.");
}
