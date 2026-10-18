// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using BenchmarkDotNet.Running;

// dotnet run -c Release --project bench/Xsl3.Benchmarks -f net10.0 -- --filter '*'
// dotnet run -c Release --project bench/Xsl3.Benchmarks -f net10.0 -- --filter '*Transform*' --job short
BenchmarkSwitcher.FromAssembly(typeof(Xsl3.Benchmarks.Corpus).Assembly).Run(args);
