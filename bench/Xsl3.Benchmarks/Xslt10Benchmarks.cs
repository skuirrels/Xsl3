// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Xml;
using System.Xml.XPath;
using BenchmarkDotNet.Attributes;
using Stock = System.Xml.Xsl;

namespace Xsl3.Benchmarks;

/// <summary>
/// XSLT 1.0 steady-state transforms, stock XslCompiledTransform against Xsl3's legacy 1.0 mode (gate G4:
/// Xsl3 must be at least as fast). The input is parsed once, so this measures transform plus serialization.
/// </summary>
[MemoryDiagnoser]
public class Xslt10Transform
{
    private Stock.XslCompiledTransform _stock = null!;
    private XslCompiledTransform _xsl3 = null!;
    private XPathDocument _input = null!;

    [Params("identity", "sort", "muenchian", "string-recursion", "key-join", "html-report")]
    public string Stylesheet { get; set; } = "";

    [Params(2_000, 20_000)]
    public int Orders { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _input = new XPathDocument(new StringReader(Corpus.Orders(Orders)));
        _stock = new Stock.XslCompiledTransform();
        _stock.Load(Corpus.Stylesheet(Stylesheet));
        _xsl3 = new XslCompiledTransform();
        _xsl3.Load(Corpus.Stylesheet(Stylesheet));

        // Only compare engines that produce the same bytes.
        var expected = new MemoryStream();
        var actual = new MemoryStream();
        _stock.Transform(_input, null, expected);
        _xsl3.Transform(_input, null, actual);
        if (!expected.ToArray().AsSpan().SequenceEqual(actual.ToArray()))
        {
            throw new InvalidOperationException($"{Stylesheet}: Xsl3 output differs from stock.");
        }
    }

    [Benchmark(Baseline = true)]
    public void Stock() => _stock.Transform(_input, null, Stream.Null);

    [Benchmark]
    public void Xsl3() => _xsl3.Transform(_input, null, Stream.Null);
}

/// <summary>Stylesheet compilation (SPEC §19.6: Xsl3's compile time must not exceed stock's).</summary>
[MemoryDiagnoser]
public class Xslt10Compile
{
    private string _path = "";

    [Params("identity", "muenchian", "html-report")]
    public string Stylesheet { get; set; } = "";

    [GlobalSetup]
    public void Setup() => _path = Corpus.Stylesheet(Stylesheet);

    [Benchmark(Baseline = true)]
    public object Stock()
    {
        var xslt = new Stock.XslCompiledTransform();
        xslt.Load(_path);
        return xslt;
    }

    [Benchmark]
    public object Xsl3()
    {
        var xslt = new XslCompiledTransform();
        xslt.Load(_path);
        return xslt;
    }
}

/// <summary>Document loading, which both engines share today: Xsl3 reads input through XPathDocument too.</summary>
[MemoryDiagnoser]
public class InputParse
{
    private string _xml = "";

    [Params(20_000)]
    public int Orders { get; set; }

    [GlobalSetup]
    public void Setup() => _xml = Corpus.Orders(Orders);

    [Benchmark]
    public XPathDocument XPathDocument() => new XPathDocument(XmlReader.Create(new StringReader(_xml)));
}

/// <summary>
/// CPU sampling of Xsl3's transforms (EventPipe; the traces are in the artifacts folder as speedscope files):
/// dotnet run -c Release --project bench/Xsl3.Benchmarks -f net10.0 -- --filter '*Xsl3Profile*' --job short
/// </summary>
[EventPipeProfiler(BenchmarkDotNet.Diagnosers.EventPipeProfile.CpuSampling)]
public class Xsl3Profile
{
    private XslCompiledTransform _xsl3 = null!;
    private XPathDocument _input = null!;

    [Params("identity", "sort", "muenchian", "string-recursion", "key-join")]
    public string Stylesheet { get; set; } = "";

    [GlobalSetup]
    public void Setup()
    {
        _input = new XPathDocument(new StringReader(Corpus.Orders(20_000)));
        _xsl3 = new XslCompiledTransform();
        _xsl3.Load(Corpus.Stylesheet(Stylesheet));
    }

    [Benchmark]
    public void Xsl3() => _xsl3.Transform(_input, null, Stream.Null);
}
