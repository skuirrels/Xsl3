// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using BenchmarkDotNet.Running;

// dotnet run -c Release --project bench/Xsl3.Benchmarks -f net10.0 -- --filter '*'
// dotnet run -c Release --project bench/Xsl3.Benchmarks -f net10.0 -- --filter '*Transform*' --job short
// dotnet run -c Release --project bench/Xsl3.Benchmarks -f net10.0 -- quick [orders] [stylesheet...]
//   A quick comparison for development (median of interleaved runs); BenchmarkDotNet's numbers are the record.
if (args.Length > 0 && args[0] == "quick")
{
    Xsl3.Benchmarks.Quick.Run(args[1..]);
    return;
}
BenchmarkSwitcher.FromAssembly(typeof(Xsl3.Benchmarks.Corpus).Assembly).Run(args);
