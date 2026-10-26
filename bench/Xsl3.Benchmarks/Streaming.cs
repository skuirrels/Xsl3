// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using Xsl3.Tests;

namespace Xsl3.Benchmarks;

// The process benchmark shares the memory probe's generated input and independent
// oracles. It compiles and executes through public APIs; no test assembly is loaded.
internal static class Streaming
{
    internal static int WriteRuntimeInfo()
    {
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new {
            Framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            OS = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            Architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
        }));
        return 0;
    }

    internal static int RunChild(string[] args)
    {
        // The development execution switch belongs to this isolated compiler/consumer process.
        if (args.Length == 4 && args[1] is "tree" or "stream")
            Environment.SetEnvironmentVariable("XSL3_STREAM_STRICT", args[1] == "stream" ? "1" : "0");
        return StreamingProbe.RunCommand(args);
    }

    internal static int ExportCorpus(string[] args)
    {
        if (args.Length != 1) { Console.Error.WriteLine("streaming-corpus <new-directory>"); return 64; }
        string directory = Path.GetFullPath(args[0]);
        if (Directory.Exists(directory) || File.Exists(directory)) throw new IOException("Corpus directory must be new: " + directory);
        Directory.CreateDirectory(directory);
        foreach (string workload in StreamingProbe.Workloads)
            File.WriteAllText(Path.Combine(directory, workload + ".xsl"), StreamingProbe.Stylesheet(workload));
        return 0;
    }
}
