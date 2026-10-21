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
        """);
    return 0;
}

string command = args[0];
string? stylesheet = null, source = null, output = null, mode = null;
string? initialTemplate = null;
bool trusted = false, trace = false, unoptimized = false;
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
