// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Xml;
using Xsl3;
using Xsl3.Tests;

if (args.Length is < 1 or > 2 || args.Length == 2 && args[1] != "--bare-variable")
{
    Console.Error.WriteLine("Usage: generator <new-output-directory> [--bare-variable]");
    return 64;
}
bool bareVariable = args.Length == 2;
string directory = Path.GetFullPath(args[0]);
if (Directory.Exists(directory)) throw new IOException("Output directory must be new: " + directory);
Directory.CreateDirectory(directory);
var bindings = new StringBuilder("// Generated from the shared P7G contracts.\nusing System;\nusing Xsl3;\ninternal static class SavedIdentity {\ninternal static XslCompiledTransform Load(string name) {\nvar transform = new XslCompiledTransform();\nswitch (name) {\n");
int index = 0;
string? previous = Environment.GetEnvironmentVariable("XSL3_STREAM_STRICT");
try
{
    foreach (var (declaration, validation) in CopiedAttributeIdentityFixtures.Contracts())
    foreach (bool forward in new[] { false, true })
    foreach (bool debug in new[] { false, true })
    {
        string name = CopiedAttributeIdentityFixtures.StyleName(declaration, validation, forward, debug);
        string type = "Saved." + name;
        string sheet = CopiedAttributeIdentityFixtures.RoundTripSheet(validation, bareVariable);
        File.WriteAllText(Path.Combine(directory, name + ".xsl"), sheet);
        using var input = XmlReader.Create(new StringReader(sheet));
        using var output = new MemoryStream();
        // Confined to this compiler process. The emitted entry point retains the mode.
        Environment.SetEnvironmentVariable("XSL3_STREAM_STRICT", forward ? "1" : "0");
        new XslCompiledTransform(debug).CompileToAssembly(input, new XsltSettings(), null, name, type, output);
        byte[] image = output.ToArray();
        Type generated = Assembly.Load(image).GetType(type, throwOnError: true)!;
        byte[] data = (byte[])generated.GetField("staticData", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        // Decode with the runtime itself, rather than inferring execution mode from an
        // assembly name or the presence of a helper used only by source-document styles.
        Type decoder = typeof(XslCompiledTransform).Assembly.GetType("Xsl3.Runtime.XmlQueryStaticData", throwOnError: true)!;
        object decoded = Activator.CreateInstance(decoder, new object?[] { data, null })!;
        bool actualForward = (bool)decoder.GetProperty("StreamedInitialMode", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(decoded)!;
        if (actualForward != forward) throw new InvalidOperationException("Wrong saved execution mode: " + name);
        File.WriteAllBytes(Path.Combine(directory, name + ".dll"), image);
        bindings.Append("case \"").Append(name).Append("\": transform.Load(typeof(global::")
            .Append(type).Append("), new XsltSettings()); break;\n");
        index++;
    }
}
finally { Environment.SetEnvironmentVariable("XSL3_STREAM_STRICT", previous); }
bindings.Append("default: throw new ArgumentException(\"Unknown saved identity contract\", nameof(name));\n}\nreturn transform;\n}\n}\n");
File.WriteAllText(Path.Combine(directory, "SavedIdentity.g.cs"), bindings.ToString());
Console.WriteLine($"Generated {index} normal/debug grounded/forward identity styles; bare-variable={bareVariable}.");
return 0;
