// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Text;
using System.Xml;
using Xsl3;
using Xsl3.Tests;

if (args.Length != 1) { Console.Error.WriteLine("Usage: generator <new-output-directory>"); return 64; }
string directory = Path.GetFullPath(args[0]);
if (Directory.Exists(directory)) throw new IOException("Generated probe directory must be new: " + directory);
Directory.CreateDirectory(directory);
string? saved = Environment.GetEnvironmentVariable("XSL3_STREAM_STRICT");
var bindings = new StringBuilder("// Generated from the shared probe fixture; do not edit.\nusing System;\nnamespace Xsl3.Tests;\ninternal static partial class StreamingProbe {\nprivate static partial Invocation CreateInvocation(string workload, bool forward) {\nvar unit = new XslCompiledTransform();\nvar settings = new XsltSettings { EnableDocumentFunction = true, EnableResultDocument = true };\nswitch ((workload, forward)) {\n");
try
{
    foreach (string workload in StreamingProbe.Workloads)
        foreach (bool forward in new[] { false, true })
        {
            // The development execution switch is confined to this compiler process. The native
            // consumer loads the saved entry points through public APIs without that switch.
            Environment.SetEnvironmentVariable("XSL3_STREAM_STRICT", forward ? "1" : "0");
            string mode = forward ? "stream" : "tree";
            string name = "Probe_" + workload.Replace('-', '_') + "_" + mode;
            string type = "Xsl3.StreamingProbe.Stylesheets." + name;
            string stylesheet = StreamingProbe.Stylesheet(workload);
            File.WriteAllText(Path.Combine(directory, name + ".xsl"), stylesheet);
            using var input = XmlReader.Create(new StringReader(stylesheet));
            using var output = File.Create(Path.Combine(directory, name + ".dll"));
            new XslCompiledTransform().CompileToAssembly(input,
                new XsltSettings { EnableDocumentFunction = true, EnableResultDocument = true }, null, name, type, output);
            bindings.Append("case (\"").Append(workload).Append("\", ").Append(forward ? "true" : "false")
                .Append("): unit.Load(typeof(global::").Append(type).Append("), settings); break;\n");
        }
}
finally { Environment.SetEnvironmentVariable("XSL3_STREAM_STRICT", saved); }
bindings.Append("default: throw new ArgumentException(\"Unknown probe workload\", nameof(workload));\n}\nreturn (input, output, resolver, secondary) => unit.Transform(input, new XsltArgumentList { ResultDocumentHandler = secondary }, output, resolver);\n}\n}\n");
File.WriteAllText(Path.Combine(directory, "StreamingProbe.Native.g.cs"), bindings.ToString());
return 0;
