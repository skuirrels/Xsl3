// Licensed under the MIT license. See LICENSE.TXT in the repository root.
using System.Diagnostics;
using System.Text.Json;
using System.Xml;
using Xsl3;

// Each child has one mode, avoiding process-global switch changes around a cached executable.
// The Python controller supplies all corpus sizes, workload names and repetition counts.
if (args.Length != 6) throw new ArgumentException("mode workload input warmup output-or-dash handshake");
string mode = args[0], workload = args[1];
if (mode is not ("tree" or "stream")) throw new ArgumentException("Unknown mode");
Environment.SetEnvironmentVariable("XSL3_STREAM_STRICT", mode == "stream" ? "1" : "0");
string body = workload switch
{
    "convert" => "<xsl:apply-templates select='doc/record'/>",
    "filter" => "<xsl:apply-templates select='doc/record[@id mod 5 = 0]'/>",
    "sum" => "<xsl:value-of select='xs:integer(sum(doc/record/@value))'/>",
    "avg" => "<xsl:value-of select='avg(doc/record/@value)'/>",
    "min" => "<xsl:value-of select='min(doc/record/@value)'/>",
    "max" => "<xsl:value-of select='max(doc/record/@value)'/>",
    _ => throw new ArgumentException("Unknown workload")
};
string sheet = "<xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform' xmlns:xs='http://www.w3.org/2001/XMLSchema'>"
    + "<xsl:mode streamable='yes'/><xsl:output method='text' encoding='utf-8'/>"
    + "<xsl:template match='/'>" + body + "</xsl:template>"
    + "<xsl:template match='record'><xsl:value-of select='@id'/><xsl:text>|</xsl:text>"
    + "<xsl:value-of select='@code'/><xsl:text>|</xsl:text><xsl:value-of select='@value'/>"
    + "<xsl:text>|</xsl:text><xsl:value-of select='label'/><xsl:text>&#10;</xsl:text>"
    + "</xsl:template></xsl:stylesheet>";
var transform = new XslCompiledTransform();
using (var reader = XmlReader.Create(new StringReader(sheet))) transform.Load(reader, new XsltSettings(), null);
// Compilation and warmup are outside measured execution. All children warm only the small corpus.
for (int i = 0; i < 5; i++)
{
    using var warm = new CountedInput(File.OpenRead(args[3]));
    using var warmSink = new TimedSink(warm, null);
    using var reader = XmlReader.Create(warm);
    transform.Transform(reader, null, warmSink);
}
GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
// Profile repeated execution separately from the timing/memory comparison.
if (Environment.GetEnvironmentVariable("XSL3_BENCH_PROFILE_REPEATS") is string repeatText)
{
    int repeats = int.Parse(repeatText, System.Globalization.CultureInfo.InvariantCulture);
    if (repeats <= 0) throw new ArgumentOutOfRangeException(nameof(repeats));
    Console.WriteLine("READY");
    if (Console.ReadLine() != "GO") throw new InvalidOperationException("Missing profile start signal");
    for (int i = 0; i < repeats; i++)
    {
        using var profileInput = new CountedInput(File.OpenRead(args[2]));
        using var profileSink = new TimedSink(profileInput, null);
        using var reader = XmlReader.Create(profileInput);
        transform.Transform(reader, null, profileSink);
    }
    Console.WriteLine("DONE");
    if (Console.ReadLine() != "ACK") throw new InvalidOperationException("Missing profile stop signal");
    return;
}
using var input = new CountedInput(File.OpenRead(args[2]));
using Stream? file = args[4] == "-" ? null : File.Create(args[4]);
using var sink = new TimedSink(input, file);
if (args[5] == "handshake")
{
    Console.WriteLine("READY");
    if (Console.ReadLine() != "GO") throw new InvalidOperationException("Missing start signal");
}
long allocatedBefore = GC.GetTotalAllocatedBytes();
int[] collectionsBefore = [GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2)];
long start = Stopwatch.GetTimestamp();
using (var reader = XmlReader.Create(input, new XmlReaderSettings { CloseInput = false }))
    transform.Transform(reader, null, sink);
long end = Stopwatch.GetTimestamp();
long allocated = GC.GetTotalAllocatedBytes() - allocatedBefore;
int[] collections = [GC.CollectionCount(0) - collectionsBefore[0], GC.CollectionCount(1) - collectionsBefore[1], GC.CollectionCount(2) - collectionsBefore[2]];
file?.Flush();
if (args[5] == "handshake")
{
    Console.WriteLine("DONE");
    if (Console.ReadLine() != "ACK") throw new InvalidOperationException("Missing stop signal");
}
Console.WriteLine(JsonSerializer.Serialize(new {
    mode, workload, inputBytes = input.Bytes, outputBytes = sink.Bytes,
    elapsedMs = (end - start) * 1000.0 / Stopwatch.Frequency,
    firstOutputMs = sink.First == 0 ? (double?)null : (sink.First - start) * 1000.0 / Stopwatch.Frequency,
    inputBytesAtFirstOutput = sink.InputBytesAtFirst,
    allocatedBytes = allocated, collections,
    lifetimePeakRssBytes = Process.GetCurrentProcess().PeakWorkingSet64,
    runtime = Environment.Version.ToString(), processors = Environment.ProcessorCount
}));

sealed class CountedInput(Stream inner) : Stream
{
    public long Bytes { get; private set; }
    public override int Read(byte[] buffer, int offset, int count) { int n = inner.Read(buffer, offset, count); Bytes += n; return n; }
    public override int Read(Span<byte> buffer) { int n = inner.Read(buffer); Bytes += n; return n; }
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => inner.Length;
    public override long Position { get => Bytes; set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
}
sealed class TimedSink(CountedInput input, Stream? destination) : Stream
{
    public long First, Bytes, InputBytesAtFirst;
    private void Observe(int count)
    {
        if (count == 0) return;
        if (First == 0) { First = Stopwatch.GetTimestamp(); InputBytesAtFirst = input.Bytes; }
        Bytes += count;
    }
    public override void Write(byte[] buffer, int offset, int count) { Observe(count); destination?.Write(buffer, offset, count); }
    public override void Write(ReadOnlySpan<byte> buffer) { Observe(buffer.Length); destination?.Write(buffer); }
    public override void Flush() => destination?.Flush();
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => Bytes;
    public override long Position { get => Bytes; set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
