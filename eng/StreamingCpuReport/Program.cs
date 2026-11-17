// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using System.Text.Json;

int timedPid = 0;
if (args.Length is < 1 or > 2 || !File.Exists(args[0])
    || args.Length == 2 && (!int.TryParse(args[1], out timedPid) || timedPid < 1))
{
    Console.Error.WriteLine("Usage: StreamingCpuReport <cpu.nettrace> [timed-child-pid]");
    return 2;
}
string etlx = TraceLog.CreateFromEventPipeDataFile(args[0]);
using var log = new TraceLog(etlx);
var markers = new List<(string Name, double Milliseconds, int Thread)>();
if (timedPid != 0)
{
    using var markerSource = log.Events.GetSource();
    markerSource.AllEvents += data =>
    {
        if (data.ProcessID == timedPid && data.ProviderName == "Xsl3-StreamingProbe")
            markers.Add((data.EventName, data.TimeStampRelativeMSec, data.ThreadID));
    };
    markerSource.Process();
    if (markers.Count != 2 || markers[0].Name != "TimedBegin" || markers[1].Name != "TimedEnd"
        || markers[0].Thread != markers[1].Thread || markers[0].Milliseconds >= markers[1].Milliseconds)
    {
        Console.Error.WriteLine($"Expected one ordered marker pair from child {timedPid}. Observed: {string.Join(", ", markers)}");
        return 3;
    }
}
using var source = log.Events.GetSource();
long childSamples = 0, before = 0, after = 0, otherProcesses = 0;
long timedCallerSamples = 0;
var counts = new Dictionary<string, long>();
var sites = new Dictionary<(int Process, int Thread, string Leaf, string Engine, string Region), long>();
var stacks = new Dictionary<CallStackIndex, (string Leaf, string Engine, string Region)>();
var examples = new List<object>();
var symbols = new List<object>();
var inclusive = new Dictionary<string, long>();
var regions = new Dictionary<string, long>();
var regionalInclusive = new Dictionary<(string Region, string Frame), long>();
source.AllEvents += data => {
    string kind = data.ProviderName + "/" + data.EventName;
    counts.TryGetValue(kind, out long n); counts[kind] = n + 1;
    if (data.EventName == "ProcessSymbol") symbols.Add(data.PayloadNames.ToDictionary(name => name, name => data.PayloadByName(name)?.ToString()));
    if (data.ProviderName != "Universal.Events" || data.EventName != "cpu") return;
    if (timedPid != 0)
    {
        if (data.ProcessID != timedPid) { otherProcesses++; return; }
        childSamples++;
        if (data.TimeStampRelativeMSec < markers[0].Milliseconds) { before++; return; }
        if (data.TimeStampRelativeMSec >= markers[1].Milliseconds) { after++; return; }
        if (data.ThreadID == markers[0].Thread) timedCallerSamples++;
    }
    if (examples.Count < 5) examples.Add(new { data.ProcessID, data.ThreadID, data.TimeStampRelativeMSec,
        Payload = data.PayloadNames.ToDictionary(name => name, name => data.PayloadByName(name)?.ToString()) });
    var index = data.CallStackIndex();
    if (!stacks.TryGetValue(index, out var site)) {
        var frames = new List<string>();
        for (var stack = data.CallStack(); stack != null; stack = stack.Caller)
            frames.Add(stack.CodeAddress.FullMethodName);
        // Classify from the complete stack, before truncating the display path. These are
        // stack regions, not measured time windows: Execute also includes the small warmup.
        string region = timedPid != 0 ? IdentifyPhase(frames) : frames.Any(f => f.StartsWith("Xsl3.Tests.StreamingProbe::Expected(", StringComparison.Ordinal)) ? "output-oracle"
            : frames.Any(f => f.StartsWith("Xsl3.Tests.StreamingProbe::Execute(", StringComparison.Ordinal)) ? "transform-entry"
            : frames.Any(f => f.StartsWith("Xsl3.Streaming.ForkProngs+", StringComparison.Ordinal) && f.Contains("<RunProngs>")) ? "fork-worker"
            : "unattributed";
        site = (frames.FirstOrDefault() ?? "<none>", string.Join(" -> ", frames.Where(f => f.Contains("Xsl3.")).Take(4)), region);
        stacks[index] = site;
    }
    var key = (data.ProcessID, data.ThreadID, site.Leaf, site.Engine, site.Region);
    sites.TryGetValue(key, out long previous); sites[key] = previous + 1;
    regions.TryGetValue(site.Region, out long regionCount); regions[site.Region] = regionCount + 1;
    var unique = new HashSet<string>();
    for (var stack = data.CallStack(); stack != null; stack = stack.Caller)
        if (unique.Add(stack.CodeAddress.FullMethodName)) {
            inclusive.TryGetValue(stack.CodeAddress.FullMethodName, out long previousCount);
            inclusive[stack.CodeAddress.FullMethodName] = previousCount + 1;
            var regionKey = (site.Region, stack.CodeAddress.FullMethodName);
            regionalInclusive.TryGetValue(regionKey, out long regionalCount);
            regionalInclusive[regionKey] = regionalCount + 1;
        }
};
source.Process();
long captureSamples = counts.GetValueOrDefault("Universal.Events/cpu");
long cpuSamples = timedPid == 0 ? captureSamples : childSamples - before - after;
if (sites.Values.Sum() != cpuSamples || regions.Values.Sum() != cpuSamples
    || timedPid != 0 && childSamples + otherProcesses != captureSamples)
    throw new InvalidDataException("CPU sample totals disagree");
Console.WriteLine(JsonSerializer.Serialize(new {
    Scope = "Kernel Universal.Events/cpu samples only; instrumented diagnostic, not throughput acceptance",
    CpuSamples = cpuSamples,
    TimedWindow = timedPid == 0 ? null : new {
        Pid = timedPid, Thread = markers[0].Thread, BeginMs = markers[0].Milliseconds,
        EndMs = markers[1].Milliseconds, Before = before, After = after, CallerSamples = timedCallerSamples,
        OtherProcessSamples = otherProcesses, CaptureSamples = captureSamples },
    CpuSamplesWithEngineFrame = sites.Where(s => s.Key.Engine.Length != 0).Sum(s => s.Value),
    EventsLost = log.EventsLost, EventCounts = counts, Examples = examples, Symbols = symbols,
    AttributionScope = timedPid != 0 ? "Exclusive categories inside the marker interval. Inclusive counts can overlap." : "Complete-stack regions; warmup may be included; unattributed samples remain visible. Not timed-transform phase boundaries or removable elapsed shares.",
    Regions = regions.OrderByDescending(r => r.Value).Select(r => new { Region = r.Key, Samples = r.Value }),
    Sites = sites.OrderByDescending(s => s.Value).Select(s => new {
        Process = s.Key.Process, Thread = s.Key.Thread, Leaf = s.Key.Leaf, Engine = s.Key.Engine, Region = s.Key.Region, Samples = s.Value }),
    Inclusive = inclusive.OrderByDescending(s => s.Value).Take(100).Select(s => new { Frame = s.Key, Samples = s.Value }),
    InclusiveByRegion = regionalInclusive.GroupBy(s => s.Key.Region).Select(g => new {
        Region = g.Key,
        Frames = g.OrderByDescending(s => s.Value).Take(100).Select(s => new { Frame = s.Key.Frame, Samples = s.Value })
    })
}, new JsonSerializerOptions { WriteIndented = true }));
return cpuSamples > 0 && (timedPid == 0 || log.EventsLost == 0) ? 0 : 1;

static string IdentifyPhase(List<string> frames)
{
    foreach (string frame in frames)
    {
        if (frame.Contains("WKS::gc_heap::") || frame.Contains("SVR::gc_heap::")) return "gc";
        if (frame.Contains("StreamedInputReader::Read(") || frame.Contains("System.Xml.XmlTextReader"))
            return "xml-reader";
        if (frame.Contains("StreamedDocument::ReadNext") || frame.Contains("StreamedDocument::ReadAttributes"))
            return "stream-event";
        if (frame.Contains("XmlQueryContext::BuildTree(") || frame.Contains("CompactDocument"))
            return "tree";
        if (frame.Contains("XmlQueryOutput") || frame.Contains("XmlRawWriter") || frame.Contains("HashSink")
            || frame.Contains("XmlUtf8RawTextWriter"))
            return "output";
        if (frame.Contains("StreamedNavigator") || frame.Contains("StreamedDocument"))
            return "stream-navigation";
    }
    return frames.Any(frame => frame.Contains("Xsl3.")) ? "other-engine" : "unattributed";
}
