// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using Xsl3;
using Xsl3.Xdm;

// xsl3: the command line (SPEC §21.3). Options follow Saxon's -name:value style.
if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    Console.WriteLine("""
        xsl3 transform -xsl:style.xsl -s:input.xml [-o:output] [-it[:name]] [-im:mode] [--trusted] [--trace] [name=value ...]
        xsl3 explain -xsl:style.xsl [--unoptimized]
        xsl3 compile -xsl:style.xsl -o:Style.dll [-type:Namespace.Type] [--trusted] [--version1] [--debug]
        """);
    return 0;
}

string command = args[0];
string? stylesheet = null, source = null, output = null, mode = null;
string? initialTemplate = null, typeName = null;
bool trusted = false, trace = false, unoptimized = false, version1 = false, debug = false;
var parameters = new Dictionary<QName, XdmValue>();
for (int i = 1; i < args.Length; i++)
{
    string arg = args[i];
    switch (arg)
    {
        case var a when a.StartsWith("-xsl:", StringComparison.Ordinal): stylesheet = a[5..]; break;
        case var a when a.StartsWith("-s:", StringComparison.Ordinal): source = a[3..]; break;
        case var a when a.StartsWith("-o:", StringComparison.Ordinal): output = a[3..]; break;
        case var a when a.StartsWith("-im:", StringComparison.Ordinal): mode = a[4..]; break;
        case "-it": initialTemplate = ""; break;
        case var a when a.StartsWith("-it:", StringComparison.Ordinal): initialTemplate = a[4..]; break;
        case "--trusted": trusted = true; break;
        case "--trace": trace = true; break;
        case "--unoptimized": unoptimized = true; break;
        case "--version1": version1 = true; break;
        case "--debug": debug = true; break;
        case var a when a.StartsWith("-type:", StringComparison.Ordinal): typeName = a[6..]; break;
        case var a when a.Contains('=') && !a.StartsWith('-'):
            parameters[Name(a[..a.IndexOf('=')])] = new XdmAtomicValue(a[(a.IndexOf('=') + 1)..]);
            break;
        default:
            Console.Error.WriteLine($"xsl3: unknown option {arg}");
            return 2;
    }
}
if (stylesheet == null)
{
    Console.Error.WriteLine("xsl3: -xsl:file is required");
    return 2;
}

try
{
    var settings = trusted
        ? new XsltSettings(enableDocumentFunction: true, enableScript: false) { EnableEvaluate = true, EnableResultDocument = true, EnableEnvironmentVariables = true }
        : new XsltSettings();
    settings.EnableTracing = trace;
    if (version1)
    {
        settings.VersionMode = XsltVersionMode.BackwardsCompatible;
    }
    if (command == "compile")
    {
        return Compile(stylesheet, output, typeName, settings, debug);
    }
    XsltExecutable executable = new XsltCompiler { Settings = settings }.Compile(new Uri(Path.GetFullPath(stylesheet)));
    switch (command)
    {
        case "explain":
            executable.Explain(Console.Out, optimized: !unoptimized);
            Console.WriteLine();
            return 0;
        case "transform":
            Xslt30Transformer transformer = executable.CreateTransformer();
            transformer.SetStylesheetParameters(parameters);
            transformer.MessageHandler = message => Console.Error.WriteLine(message);
            transformer.TraceHandler = (value, label) => Console.Error.WriteLine($"{label}: {value}");
            if (trace)
            {
                transformer.TraceListener = new ConsoleTrace();
            }
            transformer.BaseOutputUri = new Uri(Path.GetFullPath(output ?? Path.Combine(Directory.GetCurrentDirectory(), "output")));
            using (Stream destination = output != null ? File.Create(output) : Console.OpenStandardOutput())
            {
                if (source != null && initialTemplate == null)
                {
                    using FileStream input = File.OpenRead(source);
                    transformer.ApplyTemplates(XdmNode.Load(input, new Uri(Path.GetFullPath(source)).AbsoluteUri), destination, mode == null ? null : Name(mode));
                }
                else
                {
                    if (source != null)
                    {
                        using FileStream input = File.OpenRead(source);
                        transformer.GlobalContextItem = XdmNode.Load(input, new Uri(Path.GetFullPath(source)).AbsoluteUri);
                    }
                    transformer.CallTemplate(string.IsNullOrEmpty(initialTemplate) ? null : Name(initialTemplate), destination);
                }
            }
            return 0;
        default:
            Console.Error.WriteLine($"xsl3: unknown command {command} (transform, explain)");
            return 2;
    }
}
catch (XsltStaticException e)
{
    foreach (XsltStaticError error in e.Errors)
    {
        Console.Error.WriteLine(error);
    }
    if (e.Errors.Count == 0)
    {
        Console.Error.WriteLine(e.Message);
    }
    return 1;
}
catch (XsltDynamicException e)
{
    Console.Error.WriteLine($"Error {e.Code.EQName}: {e.Description}");
    foreach (XsltFrame frame in e.StylesheetStack)
    {
        Console.Error.WriteLine("  at " + frame);
    }
    return 1;
}

// compile: the stylesheet as a saved assembly holding one type, for XslCompiledTransform.Load(Type) (Sink B).
static int Compile(string stylesheet, string? output, string? typeName, XsltSettings settings, bool debug)
{
#if NET9_0_OR_GREATER
    if (output == null)
    {
        Console.Error.WriteLine("xsl3: compile needs -o:file.dll");
        return 2;
    }
    string assemblyName = Path.GetFileNameWithoutExtension(output);
    typeName ??= assemblyName;
    using var reader = System.Xml.XmlReader.Create(new Uri(Path.GetFullPath(stylesheet)).AbsoluteUri,
        new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Parse });
    using FileStream file = File.Create(output);
    try
    {
        new XslCompiledTransform(enableDebug: debug).CompileToAssembly(reader, settings, new System.Xml.XmlUrlResolver(), assemblyName, typeName, file);
    }
    catch (XsltException e)
    {
        Console.Error.WriteLine(e.Message);
        return 1;
    }
    return 0;
#else
    Console.Error.WriteLine("xsl3: compile needs .NET 9 or later");
    return 2;
#endif
}

// A name given as Q{uri}local or local.
static QName Name(string text) =>
    text.StartsWith("Q{", StringComparison.Ordinal) ? new QName(text[2..text.IndexOf('}')], text[(text.IndexOf('}') + 1)..]) : new QName(text);

/// <summary>--trace: templates and functions as they start and end, indented by depth.</summary>
internal sealed class ConsoleTrace : IXsltTraceListener
{
    private int _depth;

    public void Enter(XsltFrame frame) => Console.Error.WriteLine(new string(' ', 2 * _depth++) + "> " + frame);

    public void Leave(XsltFrame frame) => Console.Error.WriteLine(new string(' ', 2 * --_depth) + "< " + frame);
}
