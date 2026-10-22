// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using BenchmarkDotNet.Running;

// dotnet run -c Release --project bench/Xsl3.Benchmarks -f net10.0 -- --filter '*'
// dotnet run -c Release --project bench/Xsl3.Benchmarks -f net10.0 -- --filter '*Transform*' --job short
// dotnet run -c Release --project bench/Xsl3.Benchmarks -f net10.0 -- quick [orders] [stylesheet...]
//   A quick comparison for development (median of interleaved runs); BenchmarkDotNet's numbers are the record.
if (args.Length == 3 && args[0] == "corpus")
{
    // dotnet run ... -- corpus <orders> <file>: writes the generated input document.
    System.IO.File.WriteAllText(args[2], Xsl3.Benchmarks.Corpus.Orders(int.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture)));
    return;
}
if (args.Length > 0 && args[0] == "pairs")
{
    // dotnet run ... -- pairs [orders] [name...]: each 1.0 benchmark on stock against its XSLT 3.0 rewrite on Xsl3.
    Xsl3.Benchmarks.Quick.RunPairs(args[1..]);
    return;
}
if (args.Length > 0 && args[0] == "quick30")
{
    // dotnet run ... -- quick30 [orders] [stylesheet]: an XSLT 3.0 stylesheet from bench/corpus/xslt30, Xsl3 alone.
    Xsl3.Benchmarks.Quick.Run30(args[1..]);
    return;
}
if (args.Length > 0 && args[0] == "micro")
{
    // dotnet run ... -- micro [--sizes 1kb,1mb,100mb] [--ms budget] [--json file] [name...]: the XSLT 3.0 micro suite.
    Xsl3.Benchmarks.Micro.Run(args[1..]);
    return;
}
if (args.Length == 3 && args[0] == "micro-compare")
{
    // dotnet run ... -- micro-compare base.json[,…] head.json[,…]: the performance gate (eng/perf-gate.sh).
    System.Environment.Exit(Xsl3.Benchmarks.Micro.Compare(args[1..]));
}
if (args.Length > 0 && args[0] == "tei")
{
    // dotnet run ... -- tei [--ms budget]: the TEI Stylesheets (eng/fetch-bench.sh) on their largest test document.
    Xsl3.Benchmarks.Micro.RunTei(args[1..]);
    return;
}
if (args.Length > 0 && args[0] == "docbook")
{
    // dotnet run ... -- docbook [--ms budget]: DocBook xslTNG (eng/fetch-bench.sh) on a generated 400 KB book.
    Xsl3.Benchmarks.Micro.RunDocBook(args[1..]);
    return;
}
#if SAXON
if (args.Length > 0 && args[0] == "saxon")
{
    // dotnet run ... -f net10.0 -p:Saxon=true -- saxon [--sizes 1kb,1mb] [--ms budget] [--cold-runs n] [--json file] [name...]:
    // the micro suite on Xsl3 and on SaxonCS-HE (remaining-scope 3).
    Xsl3.Benchmarks.SaxonComparison.Run(args[1..]);
    return;
}
if (args.Length == 5 && args[0] == "saxon-child")
{
    Xsl3.Benchmarks.SaxonComparison.RunChild(args[1..]);
    return;
}
#endif
if (args.Length > 0 && args[0] == "quick")
{
    Xsl3.Benchmarks.Quick.Run(args[1..]);
    return;
}
BenchmarkSwitcher.FromAssembly(typeof(Xsl3.Benchmarks.Corpus).Assembly).Run(args);
