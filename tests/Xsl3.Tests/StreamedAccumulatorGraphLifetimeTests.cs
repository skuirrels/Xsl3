// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml;
using System.Xml.XPath;
using Xsl3.Runtime;
using Xsl3.Streaming;
using Xsl3.Tree;
using Xsl3.XPath31.Functions;
using Xsl3.Xdm;
using Xunit;
using Xunit.Abstractions;

namespace Xsl3.Tests;

[Collection("Process-wide state")]
public class StreamedAccumulatorGraphLifetimeTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (int delivery in Deliveries())
        foreach (bool debug in new[] { false, true })
        foreach (string copy in new[] { "instruction", "copy-of", "snapshot", "recopy", "strip", "lax", "open-replay", "interrupted-replay" })
        foreach (string kind in new[] { "nodes", "maps", "opaque" })
        foreach (int rows in new[] { 16, 128 })
        foreach (bool retain in new[] { false, true })
            yield return new object[] { delivery, debug, copy, kind, rows, retain };
    }

    private static IEnumerable<int> Deliveries()
    {
        yield return 0;
        yield return 1;
#if NET10_0_OR_GREATER
        yield return 2;
#endif
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void AccumulatorGraphsHaveOnlyTheRequestedOwners(int delivery, bool debug, string copy, string kind, int rows, bool retain)
    {
        using var probe = new Probe(kind, retain, stopAt: copy == "interrupted-replay" ? rows : 0);
        Execute(rows, probe, delivery, debug, copy);
        Assert.Equal(rows, probe.Rows);
        probe.VerifyAndRelease();
        probe.AssertReleased();
        output.WriteLine("kind={0}; rows={1}; retain={2}; samples={3}; maximum payload documents={4}; maximum payload characters={5}; maximum map versions={6}",
            kind, rows, retain, probe.Samples, probe.MaximumPayloads, probe.MaximumCharacters, probe.MaximumMaps);
    }

    public static IEnumerable<object[]> ErrorCases()
    {
        foreach (int delivery in Deliveries())
        foreach (bool debug in new[] { false, true })
        foreach (string ending in new[] { "rule-error", "cycle", "not-applicable", "late-rule-error", "retain-error" })
            yield return new object[] { delivery, debug, ending };
    }

    [Theory]
    [MemberData(nameof(ErrorCases))]
    public void CopiedPhasesPreserveErrorsAfterClosingTheSource(int delivery, bool debug, string ending)
    {
        using var probe = new Probe("maps", true, ending);
        Execute(128, probe, delivery, debug, "instruction");
        Assert.Equal(ending == "not-applicable" ? 128 : ending is "late-rule-error" or "retain-error" ? 127 : 2, probe.Rows);
        probe.VerifyAndRelease();
        probe.AssertReleased();
    }

    private static string Sheet(string copy, string ending)
    {
        string constructor = copy switch {
            "instruction" or "open-replay" or "interrupted-replay" => "<xsl:copy-of select='.' copy-accumulators='yes'/>",
            "copy-of" => "<xsl:sequence select='copy-of(.)'/>",
            "snapshot" => "<xsl:sequence select='snapshot(.)'/>",
            "recopy" => "<xsl:variable name='first' as='element(row)'><xsl:copy-of select='.' copy-accumulators='yes'/></xsl:variable><xsl:sequence select='copy-of($first)'/>",
            "strip" => "<xsl:copy-of select='.' copy-accumulators='yes' validation='strip'/>",
            "lax" => "<xsl:copy-of select='.' copy-accumulators='yes' validation='lax'/>",
            _ => throw new ArgumentOutOfRangeException(nameof(copy)),
        };
        string read = ending == "not-applicable" ? "$node ! accumulator-before('unused')"
            : "[$node ! accumulator-before('n'), $node ! accumulator-after('n')]";
        string open = copy is "open-replay" or "interrupted-replay"
            ? "<xsl:sequence select=\"$probe('open', '" + copy + "', (), (), function($node) { " + read + " })\"/>" : "";
        return $$"""
            <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:xs="http://www.w3.org/2001/XMLSchema" xmlns:err="http://www.w3.org/2005/xqt-errors">
              <xsl:mode streamable="yes" use-accumulators="n"/><xsl:output method="text"/>
              <xsl:param name="probe" as="function(*)" required="yes"/>
              <xsl:accumulator name="n" streamable="yes" initial-value="()">
                <xsl:accumulator-rule match="row" phase="end" select="$probe('next', string(@id), $value, (), ())"/>
              </xsl:accumulator>
              <xsl:accumulator name="unused" streamable="yes" initial-value="0"><xsl:accumulator-rule match="unused" select="$value"/></xsl:accumulator>
              <xsl:template match="/">
                {{open}}
                <xsl:try>
                  <xsl:for-each select="doc/row">
                    <xsl:variable name="copy" as="element(row)">{{constructor}}</xsl:variable>
                    <xsl:sequence select="$probe('copy', string($copy/@id), (), (), $copy)"/>
                    <xsl:sequence select="$probe('observe', string($copy/@id), accumulator-before('n'), accumulator-after('n'), $copy)"/>
                  </xsl:for-each>
                  <xsl:catch><xsl:sequence select="$probe('caught', local-name-from-QName($err:code), (), (), ())"/></xsl:catch>
                </xsl:try>
                <xsl:sequence select="$probe('finish', '', (), (), function($node) { {{read}} })"/>
              </xsl:template>
            </xsl:stylesheet>
            """;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Execute(int rows, Probe probe, int delivery, bool debug, string copy)
    {
        var settings = new XsltSettings { StreamedExecution = true, IncludeDebugInformation = debug, SchemaAware = copy == "lax" };
        string xml = "<doc>" + string.Concat(Enumerable.Range(1, copy == "interrupted-replay" ? rows + 16 : rows).Select(row => "<row id='" + Id(row) + "'/>")) + "</doc>";
        using var input = XmlReader.Create(new StringReader(xml));
        using var sink = new StringWriter(CultureInfo.InvariantCulture);
        var function = new NativeFunction(Enumerable.Repeat(SequenceTypeTest.AnySequence, 5).ToArray(), SequenceTypeTest.AnySequence,
            probe.Invoke, "accumulator graph retention observer");
        using var capture = new StreamingExecutionCapture();
        using (StreamingExecutionCapture.ObserveStage(StreamingExecutionCapture.Stage.Stylesheet))
        {
            if (delivery == 0)
            {
                var transformer = new XsltCompiler { Settings = settings }.Compile(new StringReader(Sheet(copy, probe.Ending))).CreateTransformer();
                transformer.StylesheetParameters[new QName("probe")] = XdmValue.Wrap(new XPathItem[] { function });
                transformer.ApplyTemplates(input, sink);
            }
            else
            {
                var transform = new XslCompiledTransform(debug);
                using var sheet = XmlReader.Create(new StringReader(Sheet(copy, probe.Ending)));
#if NET10_0_OR_GREATER
                if (delivery == 2)
                {
                    using var image = new MemoryStream();
                    transform.CompileToAssembly(sheet, settings, null, "AccumulatorGraph" + Guid.NewGuid().ToString("N"), "Saved.AccumulatorGraph", image);
                    transform.Load(Assembly.Load(image.ToArray()).GetType("Saved.AccumulatorGraph", true)!, settings);
                }
                else
#endif
                    transform.Load(sheet, settings, null);
                var arguments = new XsltArgumentList();
                arguments.AddParam("probe", "", new XPathItem[] { function });
                transform.Transform(input, arguments, sink);
            }
        }
        Assert.NotEqual(ReadState.Closed, input.ReadState);
        Assert.Equal("", sink.ToString());
        var execution = Assert.Single(capture.Snapshot().Executions);
        Assert.True(execution.BodyCompleted);
        var source = Assert.Single(execution.Sources);
        Assert.True(source.RequestedForward);
        Assert.True(source.ActualForward);
        Assert.True(probe.Finished);
    }

    private sealed class Probe(string kind, bool retain, string ending = "complete", int stopAt = 0) : IDisposable
    {
        private const int Width = 7, Characters = 256;
        private readonly List<(WeakReference<CompactDocument> Document, int Characters)> _payloads = new();
        private readonly List<WeakReference<CompactDocument>> _copies = new();
        private readonly List<WeakReference<MapItem>> _maps = new();
        private readonly Dictionary<string, WeakReference<NativeFunction>> _functions = new();
        private readonly List<XPathNavigator> _saved = new();
        private readonly List<int> _savedRows = new();
        private int _copiedRows;
        private readonly List<CopyOriginRegistry.Origin> _origins = new();
        private bool _caught;
        private StreamedNavigator? _replaySource;
        private StreamedDocument.ConsumptionScope? _replay;
        private bool _completeReplay;
        internal string Ending => ending;
        internal int Rows, Samples, MaximumPayloads, MaximumMaps;
        internal long MaximumCharacters;
        internal bool Finished;

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal IList<XPathItem> Invoke(IList<XPathItem>[] arguments)
        {
            string operation = Assert.Single(arguments[0]).Value, id = Assert.Single(arguments[1]).Value;
            if (operation == "open")
            {
                var reader = Assert.IsType<CompiledFunction>(Assert.Single(arguments[4]));
                _replaySource = Assert.IsType<StreamedNavigator>(reader.Owner.ExternalContext.InitialSelection(accumulators: false));
                _completeReplay = id == "open-replay";
                _replay = _replaySource.BeginConsumption(replay: true, accumulators: true);
                return Sequences.Empty;
            }
            if (operation == "next")
            {
                if (id == Id(ending is "late-rule-error" or "retain-error" ? 128 : 3) && ending is "rule-error" or "cycle" or "late-rule-error" or "retain-error")
                    throw new XdmException(ending == "cycle" ? "XTDE3400" : "TEST0001", "Accumulator graph rule failure") {
                        AccumulatorCycle = ending == "cycle", ErrorObject = ending is "late-rule-error" or "retain-error" ? arguments[2] : null,
                    };
                var payload = Payload(id);
                _payloads.Add((new(Assert.IsType<CompactNavigator>(payload).Document), Characters + id.Length));
                if (kind == "maps")
                {
                    MapItem previous = arguments[2].Count == 0 ? MapItem.Empty : Assert.IsType<MapItem>(Assert.Single(arguments[2]));
                    var map = previous.Put(AtomicValue.OfString(id), new XPathItem[] { payload });
                    _maps.Add(new(map));
                    return new XPathItem[] { map };
                }
                XPathItem item = payload;
                if (kind == "opaque")
                {
                    var opaque = Opaque(payload);
                    _functions.Add(id, new(opaque));
                    item = opaque;
                }
                return arguments[2].Skip(Math.Max(0, arguments[2].Count - Width + 1)).Append(item).ToArray();
            }
            if (operation == "caught")
            {
                if (stopAt > 0) { Assert.Equal("TESTSTOP", id); _caught = true; return Sequences.Empty; }
                Assert.Equal(ending == "cycle" ? "XTDE3400" : "TEST0001", id);
                Assert.True(ending is "rule-error" or "cycle" or "late-rule-error" or "retain-error");
                _caught = true;
                return Sequences.Empty;
            }
            if (operation == "finish")
            {
                Assert.Equal(stopAt > 0 || ending is "rule-error" or "cycle" or "late-rule-error" or "retain-error", _caught);
                var reader = Assert.IsType<CompiledFunction>(Assert.Single(arguments[4]));
                if (_replaySource is { } source)
                {
                    if (_completeReplay) source.Document.CompleteSubtree(source.Document.Root);
                    _replay!.Value.Dispose(); _replay = null;
                    Assert.Equal(_completeReplay, StreamedDocument.CanReplay(source.Document.Root));
                    _replaySource = null;
                }
                reader.Owner.ExternalContext.CloseInitialSource();
                for (int i = 0; i < _saved.Count; i++)
                {
                    int row = _savedRows[i];
                    if (ending is "cycle" or "not-applicable" or "retain-error")
                    {
                        var error = Assert.Throws<XdmException>(() => reader.Owner.XsltFunctions.CallFunction(reader, new[] { new XPathItem[] { _saved[i] } }));
                        Assert.Equal(ending == "cycle" ? "XTDE3400" : ending == "retain-error" ? "TEST0001" : "XTDE3362", error.Code);
                        if (ending == "retain-error") Assert.Equal(row - 1, Assert.IsType<MapItem>(Assert.Single(error.ErrorObject!)).Count);
                    }
                    else
                    {
                        var pair = Assert.IsType<ArrayItem>(Assert.Single(reader.Owner.XsltFunctions.CallFunction(reader, new[] { new XPathItem[] { _saved[i] } })));
                        VerifyPhase(pair[0], row - 1); VerifyPhase(pair[1], row);
                    }
                    Assert.True(reader.Owner.Documents.TryGetCopy(_saved[i], out var origin));
                    while (origin.Accumulators == null)
                    {
                        Assert.NotNull(origin.Original);
                        Assert.True(reader.Owner.Documents.TryGetCopy(origin.Original!, out var previous));
                        origin = previous;
                    }
                    Assert.Null(origin.Original);
                    _origins.Add(origin);
                }
                Finished = true;
                return Sequences.Empty;
            }
            if (operation == "copy")
            {
                _copiedRows++;
                Assert.Equal(Id(_copiedRows), id);
                var copy = Assert.IsType<CompactNavigator>(Assert.Single(arguments[4]));
                _copies.Add(new(copy.Document));
                if (retain && (ending == "retain-error" ? _copiedRows == 128 : _copiedRows <= 2))
                { _saved.Add(copy.Clone()); _savedRows.Add(_copiedRows); }
                return Sequences.Empty;
            }
            Assert.Equal("observe", operation);
            Rows++;
            Assert.Equal(Id(Rows), id);
            VerifyPhase(arguments[2], Rows - 1); VerifyPhase(arguments[3], Rows);
            if (Rows % 8 == 0) Sample();
            if (Rows == stopAt) throw new XdmException("TESTSTOP", "Stop the explicit outer replay before EOF");
            return Sequences.Empty;
        }

        private void VerifyPhase(IList<XPathItem> value, int upper)
        {
            if (upper == 0) { Assert.Empty(value); return; }
            if (kind == "maps")
            {
                var map = Assert.IsType<MapItem>(Assert.Single(value)); Assert.Equal(upper, map.Count);
                for (int row = 1; row <= upper; row++) VerifyPayload(Assert.IsAssignableFrom<XPathNavigator>(Assert.Single(map.GetString(Id(row))!)), Id(row));
                return;
            }
            Assert.Equal(Math.Min(upper, Width), value.Count);
            for (int i = 0; i < value.Count; i++)
            {
                string id = Id(Math.Max(1, upper - Width + 1) + i);
                XPathNavigator node;
                if (kind == "opaque")
                {
                    var opaque = Assert.IsType<NativeFunction>(value[i]);
                    Assert.True(_functions[id].TryGetTarget(out var expected)); Assert.Same(expected, opaque);
                    node = Assert.IsAssignableFrom<XPathNavigator>(Assert.Single(opaque.Invoke(Array.Empty<IList<XPathItem>>())));
                }
                else node = Assert.IsAssignableFrom<XPathNavigator>(value[i]);
                VerifyPayload(node, id);
            }
        }

        private static void VerifyPayload(XPathNavigator node, string id)
        {
            Assert.Equal("payload", node.LocalName); Assert.Equal(id, node.GetAttribute("id", ""));
            Assert.Equal(Text(id), node.Value);
        }

        private void Sample()
        {
            Collect();
            var (count, characters) = LivePayloads();
            // An explicitly owned outer grounding legitimately retains the source's earlier phases.
            // Its release is checked separately after the capture ends.
            int bound = _replay != null ? Rows + 1 : kind == "maps" ? Rows : Width + 1 + _saved.Count;
            int maximumCharacters = _payloads.Max(item => item.Characters);
            Assert.InRange(count, 1, bound); Assert.InRange(characters, 0, (long)bound * maximumCharacters);
            int maps = _maps.Count(reference => reference.TryGetTarget(out _));
            Assert.InRange(maps, 0, kind == "maps" ? _replay != null ? Rows + 1 : 2 + _saved.Count : 0);
            Assert.InRange(_copies.Count(reference => reference.TryGetTarget(out _)), 1, 1 + _saved.Count);
            MaximumPayloads = Math.Max(MaximumPayloads, count); MaximumCharacters = Math.Max(MaximumCharacters, characters);
            MaximumMaps = Math.Max(MaximumMaps, maps); Samples++;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal void VerifyAndRelease()
        {
            Collect();
            int expectedPayloads = _savedRows.Count == 0 ? 0 : ending == "retain-error" ? _savedRows.Single() - 1 : _savedRows.Max();
            Assert.Equal(expectedPayloads, LivePayloads().Count);
            Assert.Equal(_saved.Count, _copies.Count(reference => reference.TryGetTarget(out _)));
            for (int i = 0; i < _origins.Count; i++)
            {
                var snapshot = _origins[i].Accumulators!;
                if (ending == "cycle")
                    Assert.Equal("XTDE3400", Assert.Throws<XdmException>(() => snapshot.TryValue(_origins[i].Top, "Q{}n", true, out _)).Code);
                else
                {
                    int row = _savedRows[i];
                    Assert.True(snapshot.TryValue(_origins[i].Top, "Q{}n", false, out var before)); VerifyPhase(before!, row - 1);
                    if (ending == "retain-error")
                    {
                        var error = Assert.Throws<XdmException>(() => snapshot.TryValue(_origins[i].Top, "Q{}n", true, out _));
                        Assert.Equal("TEST0001", error.Code);
                        Assert.Same(Assert.Single(before!), Assert.Single(error.ErrorObject!));
                    }
                    else { Assert.True(snapshot.TryValue(_origins[i].Top, "Q{}n", true, out var after)); VerifyPhase(after!, row); }
                }
            }
            _saved.Clear(); _savedRows.Clear(); _origins.Clear();
            // Local values above may remain live until this verification frame returns.
        }

        internal void AssertReleased()
        {
            Collect(); Assert.Equal(0, LivePayloads().Count);
            Assert.Equal(0, _copies.Count(reference => reference.TryGetTarget(out _)));
            Assert.Equal(0, _maps.Count(reference => reference.TryGetTarget(out _)));
            Assert.All(_functions.Values, reference => Assert.False(reference.TryGetTarget(out _)));
        }

        public void Dispose()
        {
            try { _replay?.Dispose(); }
            finally { _replay = null; _replaySource = null; }
        }

        private static NativeFunction Opaque(XPathNavigator payload) => new(Array.Empty<SequenceTypeTest>(), SequenceTypeTest.AnySequence,
            _ => new XPathItem[] { payload.Clone() }, "opaque retained accumulator node");
        [MethodImpl(MethodImplOptions.NoInlining)]
        private (int Count, long Characters) LivePayloads()
        {
            int count = 0; long characters = 0;
            foreach (var entry in _payloads) if (entry.Document.TryGetTarget(out _)) { count++; characters += entry.Characters; }
            return (count, characters);
        }
        private static string Text(string id) => string.Concat(Enumerable.Repeat(id, (Characters + id.Length - 1) / id.Length))[..Characters];
        private static XPathNavigator Payload(string id)
        {
            var node = XdmNode.Load(new StringReader("<payload id='" + id + "'>" + Text(id) + "</payload>")).Navigator;
            Assert.True(node.MoveToFirstChild()); return node;
        }
    }

    private static string Id(int row) => "r" + row.ToString(CultureInfo.InvariantCulture);
    private static void Collect() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
}
