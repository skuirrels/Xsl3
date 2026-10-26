// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Buffers;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Xml;

namespace Xsl3.Tests;

// Test corpus and independent output oracles for the P7 process-memory gate. None
// of these fixture values are used as data or fallbacks by the transformation engine.
internal static partial class StreamingProbe
{
    internal sealed record Fingerprint(long Bytes, string Sha256);
    internal sealed record Result(string Workload, string Mode, string Status, long RequestedInputBytes, long InputBytes,
        long Records, long LimitBytes, long PeakWorkingSetBytes, string PeakMemorySource, long LiveManagedBytes, double? ElapsedMs,
        double? FirstOutputMs, long? InputBytesAtFirstOutput, bool ValidOutput, Dictionary<string, Fingerprint> Outputs)
    {
        public long AvailableManagedBytes { get; } = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        public bool DynamicCodeSupported { get; } = System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported;
        public bool HasTrustedPlatformAssemblies { get; } = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") != null;
    }

    internal static int RunCommand(string[] args)
    {
        if (args.Length != 4 || !Workloads.Contains(args[0], StringComparer.Ordinal) || args[1] is not ("tree" or "stream")
            || !long.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out long bytes)
            || !long.TryParse(args[3], NumberStyles.None, CultureInfo.InvariantCulture, out long limit)
            || bytes < GeneratedInput.MinimumBytes * 2 || limit < 0)
        {
            Console.Error.WriteLine($"stream-probe <{string.Join("|", Workloads)}> <tree|stream> <input-bytes> <absolute-limit-bytes-or-0>");
            return 64;
        }
        try
        {
            Result result = Run(args[0], args[1] == "stream", bytes, limit);
            Console.WriteLine(JsonSerializer.Serialize(result, StreamingProbeJsonContext.Default.Result));
            return result.Status == "ok" ? 0 : result.Status.StartsWith("budget-exceeded", StringComparison.Ordinal) ? 3 : 2;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    internal static Result Run(string workload, bool forward, long bytes, long limit)
    {
        if (!Workloads.Contains(workload, StringComparer.Ordinal)) throw new ArgumentException("Unknown streaming workload", nameof(workload));
        if (bytes < GeneratedInput.MinimumBytes * 2) throw new ArgumentOutOfRangeException(nameof(bytes));
        ArgumentOutOfRangeException.ThrowIfNegative(limit);
        Invocation invoke = CreateInvocation(workload, forward);
        // Compile the actual entry point and warm runtime code with the same API and
        // small generated shape. Peak RSS still includes startup and this warmup.
        Execute(invoke, workload, GeneratedInput.MinimumBytes * 4);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        long initialPeak = PeakWorkingSet();
        if (limit != 0 && initialPeak > limit)
            return new(workload, forward ? "stream" : "tree", "budget-exceeded-before-measurement", bytes, 0, 0, limit,
                initialPeak, PeakMemorySource, GC.GetTotalMemory(forceFullCollection: true), null, null, null, false, new());
        Execution actual = Execute(invoke, workload, bytes);
        Dictionary<string, Fingerprint> expected = Expected(workload, actual.Records, actual.Padding);
        bool valid = actual.InputBytes == bytes && actual.Outputs.Count == expected.Count
            && expected.All(e => actual.Outputs.TryGetValue(e.Key, out Fingerprint? f) && f == e.Value);
        long peak = PeakWorkingSet();
        string status = !valid ? "output-mismatch" : limit != 0 && peak > limit ? "budget-exceeded" : "ok";
        return new(workload, forward ? "stream" : "tree", status, bytes, actual.InputBytes, actual.Records, limit,
            peak, PeakMemorySource, GC.GetTotalMemory(forceFullCollection: true), actual.ElapsedMs, actual.FirstOutputMs, actual.InputAtFirst, valid, actual.Outputs);
    }

    private sealed record Execution(long InputBytes, long Records, int Padding, double ElapsedMs,
        double? FirstOutputMs, long? InputAtFirst, Dictionary<string, Fingerprint> Outputs);

    private delegate void Invocation(XmlReader input, Stream output, XmlResolver resolver, Func<Uri, TextWriter> secondary);
    private static partial Invocation CreateInvocation(string workload, bool forward);

    private static Execution Execute(Invocation invoke, string workload, long bytes)
    {
        using var resolver = new Inputs(workload, bytes);
        using var principal = workload is "merge" or "source-document" ? null : new GeneratedInput(bytes);
        long ReadBytes() => (principal?.ReadBytes ?? 0) + resolver.Streams.Values.Sum(s => s.ReadBytes);
        long first = 0; long? inputAtFirst = null;
        var emission = new object();
        void Output(int count)
        {
            if (count == 0 || System.Threading.Volatile.Read(ref first) != 0) return;
            lock (emission)
                if (first == 0) { first = Stopwatch.GetTimestamp(); inputAtFirst = ReadBytes(); }
        }
        var sinks = new Dictionary<string, HashSink>(StringComparer.Ordinal) { ["principal"] = new(Output) };
        var writers = new List<TextWriter>();
        int caller = Environment.CurrentManagedThreadId;
        TextWriter Secondary(Uri uri)
        {
            if (Environment.CurrentManagedThreadId != caller) throw new InvalidOperationException("Secondary result opened off the invoking thread");
            string name = uri.AbsoluteUri;
            if (workload != "fork" || name is not (FirstUri or SecondUri) || sinks.ContainsKey(name))
                throw new InvalidOperationException("Unexpected secondary result: " + name);
            var sink = new HashSink(Output); sinks.Add(name, sink);
            var writer = new StreamWriter(sink, new UTF8Encoding(false), 1024, leaveOpen: true);
            writers.Add(writer); return writer;
        }
        using var launch = principal == null ? new MemoryStream(Encoding.UTF8.GetBytes("<launch/>")) : null;
        using var reader = XmlReader.Create((Stream?)principal ?? launch!, new XmlReaderSettings { CloseInput = false });
        long start = Stopwatch.GetTimestamp();
        try
        {
            try { invoke(reader, sinks["principal"], resolver, Secondary); }
            finally { foreach (TextWriter writer in writers) writer.Dispose(); }
            long end = Stopwatch.GetTimestamp();
            long records = principal?.Records ?? resolver.Streams.Values.Sum(s => s.Records);
            int padding = principal?.Padding ?? (workload == "source-document" ? resolver.Streams[SourceUri].Padding : 0);
            var outputs = sinks.ToDictionary(e => e.Key, e => e.Value.Finish(), StringComparer.Ordinal);
            return new(ReadBytes(), records, padding, (end - start) * 1000.0 / Stopwatch.Frequency,
                first == 0 ? null : (first - start) * 1000.0 / Stopwatch.Frequency, inputAtFirst, outputs);
        }
        finally { foreach (HashSink sink in sinks.Values) sink.Dispose(); }
    }

    private sealed class Inputs(string workload, long bytes) : XmlResolver, IDisposable
    {
        internal readonly Dictionary<string, GeneratedInput> Streams = new(StringComparer.Ordinal);
        public override ICredentials? Credentials { set { } }
        public void Dispose() { foreach (GeneratedInput stream in Streams.Values) stream.Dispose(); }
        public override object GetEntity(Uri uri, string? role, Type? type)
        {
            string name = uri.AbsoluteUri;
            GeneratedInput stream = name switch {
                SourceUri when workload == "source-document" => new(bytes),
                LeftUri when workload == "merge" => new(bytes / 2 + bytes % 2, 1, 2),
                RightUri when workload == "merge" => new(bytes / 2, 2, 2),
                _ => throw new XmlException("The probe does not provide " + name),
            };
            if (!Streams.TryAdd(name, stream)) { stream.Dispose(); throw new XmlException("Input opened twice: " + name); }
            return stream;
        }
    }

    private static Dictionary<string, Fingerprint> Expected(string workload, long records, int padding)
    {
        using var primary = new HashSink(); using var first = new HashSink(); using var second = new HashSink();
        byte[] row = GeneratedInput.RecordTemplate(seen: workload != "conditional-content");
        Span<byte> number = stackalloc byte[20];
        if (workload is "identity" or "source-document" or "copy-select" or "conditional-content")
        {
            primary.Write(GeneratedInput.Header);
            if (workload == "conditional-content") primary.Write("<begin/>"u8);
            for (long id = 1; id <= records; id++) { GeneratedInput.Fill(row, id); primary.Write(row); }
            Span<byte> spaces = stackalloc byte[GeneratedInput.RecordLength]; spaces.Fill((byte)' ');
            if (workload == "conditional-content") primary.Write("<end/>"u8);
            else primary.Write(spaces[..padding]);
            primary.Write(GeneratedInput.Footer);
        }
        else if (workload == "type-inspection") primary.Write(records > 0 ? "true"u8 : "false"u8);
        else if (workload is "accumulator" or "iterate")
        {
            long remainder = records % 3;
            Number(primary, checked(records / 3 * 6 + remainder * (remainder + 1) / 2));
        }
        else for (long id = 1; id <= records; id++)
        {
            if (workload == "adjacent")
            {
                bool pair = id < records;
                Byte(primary, (byte)('a' + GeneratedInput.Group(id))); Byte(primary, (byte)'|');
                Number(primary, GeneratedInput.Amount(id) + (pair ? GeneratedInput.Amount(id + 1) : 0)); Byte(primary, (byte)'\n'); if (pair) id++;
            }
            else if (workload == "merge")
            { Number(primary, id); Byte(primary, (byte)'|'); Number(primary, GeneratedInput.Amount(id)); Byte(primary, (byte)'\n'); }
            else if (workload is "template-parameter" or "function-parameter")
            { Number(primary, id); Byte(primary, (byte)'|'); Number(primary, GeneratedInput.Value); Byte(primary, (byte)'\n'); }
            else if (workload == "overlap-values")
            {
                Number(primary, GeneratedInput.Value); Byte(primary, (byte)'|');
                primary.Write(OverlapText); Byte(primary, (byte)'|'); Number(primary, id); Byte(primary, (byte)'\n');
            }
            else if (workload == "type-assertion")
            {
                if (!Utf8Formatter.TryFormat(id, number, out int length, new StandardFormat('D', 20)) || length != 20) throw new InvalidOperationException();
                primary.Write(number); Byte(primary, (byte)'\n');
            }
            else if (workload is "climbing-union" or "climbing-intersection")
            {
                bool left = GeneratedInput.Group(id) == 0, right = GeneratedInput.Amount(id) == 3;
                if (workload == "climbing-union" ? left || right : left && right)
                {
                    if (!Utf8Formatter.TryFormat(id, number, out int length, new StandardFormat('D', 20)) || length != 20) throw new InvalidOperationException();
                    primary.Write(number); Byte(primary, (byte)'\n');
                }
            }
            else if (workload == "fork")
            {
                if (!Utf8Formatter.TryFormat(id, number, out int length, new StandardFormat('D', 20)) || length != 20) throw new InvalidOperationException();
                first.Write(number); Byte(first, (byte)'\n'); Number(second, GeneratedInput.Amount(id) + GeneratedInput.Value); Byte(second, (byte)'\n');
            }
        }
        var expected = new Dictionary<string, Fingerprint>(StringComparer.Ordinal) { ["principal"] = primary.Finish() };
        if (workload == "fork") { expected.Add(FirstUri, first.Finish()); expected.Add(SecondUri, second.Finish()); }
        return expected;
    }

    private static readonly byte[] OverlapText = Encoding.UTF8.GetBytes(GeneratedInput.BeforeText + GeneratedInput.MiddleText + GeneratedInput.AfterText + "|" + GeneratedInput.MiddleText);
    private static void Byte(HashSink sink, byte value) { Span<byte> data = stackalloc byte[1]; data[0] = value; sink.Write(data); }
    private static void Number(HashSink sink, long value)
    {
        Span<byte> data = stackalloc byte[32];
        if (!Utf8Formatter.TryFormat(value, data, out int count)) throw new InvalidOperationException();
        sink.Write(data[..count]);
    }

    internal sealed class HashSink(Action<int>? onWrite = null) : Stream
    {
        private readonly ProbeSha256 _hash = new();
        private Fingerprint? _result;
        private long _bytes;
        private bool _disposed;
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_result != null) throw new InvalidOperationException("The fingerprint has ended");
            onWrite?.Invoke(buffer.Length); _hash.AppendData(buffer); _bytes = checked(_bytes + buffer.Length);
        }
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        internal Fingerprint Finish()
        {
            if (_result != null) return _result;
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _result = new(_bytes, Convert.ToHexString(_hash.GetHashAndReset()));
        }
        protected override void Dispose(bool disposing)
        {
            if (!_disposed && disposing) { _disposed = true; _hash.Dispose(); }
            base.Dispose(disposing);
        }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => !_disposed && _result == null;
        public override long Length => _bytes;
        public override long Position { get => _bytes; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    internal sealed class GeneratedInput : Stream
    {
        internal static readonly byte[] Header = Encoding.UTF8.GetBytes("<doc>"), Footer = Encoding.UTF8.GetBytes("</doc>");
        internal const string BeforeText = "before ", MiddleText = "middle", AfterText = " after";
        private static readonly string Template = "<row id=\"00000000000000000000\" group=\"a\" amount=\"1\"><value>" + Value.ToString(CultureInfo.InvariantCulture) + "</value><text>" + BeforeText + "<em>" + MiddleText + "</em>" + AfterText + "</text></row>";
        internal const int Value = 2;
        private static readonly int IdOffset = Template.IndexOf("00000000000000000000", StringComparison.Ordinal);
        private static readonly int GroupOffset = Template.IndexOf("group=\"", StringComparison.Ordinal) + 7;
        private static readonly int AmountOffset = Template.IndexOf("amount=\"", StringComparison.Ordinal) + 8;
        internal static int RecordLength => Template.Length;
        internal static long MinimumBytes => Header.Length + Footer.Length + RecordLength;
        private readonly byte[] _record = RecordTemplate(false);
        private readonly long _length, _first, _step;
        private long _cached = -1;
        private bool _disposed;
        private long _readBytes;
        internal long ReadBytes => System.Threading.Volatile.Read(ref _readBytes);
        internal long Records { get; }
        internal int Padding { get; }
        internal static int Amount(long id) => (int)((id - 1) % 3) + 1;
        internal static int Group(long id) => (int)((id - 1) / 2 % 2);
        internal static byte[] RecordTemplate(bool seen) => Encoding.UTF8.GetBytes(seen ? Template.Replace("><value>", " seen=\"yes\"><value>", StringComparison.Ordinal) : Template);
        internal static void Fill(byte[] record, long id)
        {
            if (!Utf8Formatter.TryFormat(id, record.AsSpan(IdOffset, 20), out int count, new StandardFormat('D', 20)) || count != 20) throw new InvalidOperationException();
            record[GroupOffset] = (byte)('a' + Group(id)); record[AmountOffset] = (byte)('0' + Amount(id));
        }
        internal GeneratedInput(long bytes, long first = 1, long step = 1)
        {
            if (bytes < Header.Length + Footer.Length) throw new ArgumentOutOfRangeException(nameof(bytes));
            if (first < 1) throw new ArgumentOutOfRangeException(nameof(first));
            if (step < 1) throw new ArgumentOutOfRangeException(nameof(step));
            _length = bytes; _first = first; _step = step;
            Records = (bytes - Header.Length - Footer.Length) / RecordLength;
            Padding = (int)((bytes - Header.Length - Footer.Length) % RecordLength);
            if (Records != 0) _ = checked(first + (Records - 1) * step);
        }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            int written = 0;
            while (written < buffer.Length && ReadBytes < _length)
            {
                long remaining;
                if (ReadBytes < Header.Length)
                {
                    int offset = (int)ReadBytes; int n = Math.Min(buffer.Length - written, Header.Length - offset);
                    Header.AsSpan(offset, n).CopyTo(buffer[written..]); System.Threading.Volatile.Write(ref _readBytes, _readBytes + n); written += n; continue;
                }
                long body = ReadBytes - Header.Length;
                if (body < Records * RecordLength)
                {
                    long index = body / RecordLength; int offset = (int)(body % RecordLength);
                    if (_cached != index) { Fill(_record, checked(_first + index * _step)); _cached = index; }
                    int n = Math.Min(buffer.Length - written, RecordLength - offset);
                    _record.AsSpan(offset, n).CopyTo(buffer[written..]); System.Threading.Volatile.Write(ref _readBytes, _readBytes + n); written += n; continue;
                }
                remaining = _length - Footer.Length - ReadBytes;
                if (remaining > 0)
                {
                    int n = (int)Math.Min(buffer.Length - written, remaining); buffer.Slice(written, n).Fill((byte)' ');
                    System.Threading.Volatile.Write(ref _readBytes, _readBytes + n); written += n; continue;
                }
                int footer = (int)(ReadBytes - (_length - Footer.Length)); int take = Math.Min(buffer.Length - written, Footer.Length - footer);
                Footer.AsSpan(footer, take).CopyTo(buffer[written..]); System.Threading.Volatile.Write(ref _readBytes, _readBytes + take); written += take;
            }
            return written;
        }
        protected override void Dispose(bool disposing) { _disposed = true; base.Dispose(disposing); }
        public override bool CanRead => !_disposed;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _length;
        public override long Position { get => ReadBytes; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
