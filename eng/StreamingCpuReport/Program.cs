// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using System.Text.Json;

if (args.Length != 1 || !File.Exists(args[0]))
{
    Console.Error.WriteLine("Usage: StreamingCpuReport <cpu.nettrace>");
    return 2;
}
string etlx = TraceLog.CreateFromEventPipeDataFile(args[0]);
using var log = new TraceLog(etlx);
using var source = log.Events.GetSource();
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
    if (examples.Count < 5) examples.Add(new { data.ProcessID, data.ThreadID, data.TimeStampRelativeMSec,
        Payload = data.PayloadNames.ToDictionary(name => name, name => data.PayloadByName(name)?.ToString()) });
    var index = data.CallStackIndex();
    if (!stacks.TryGetValue(index, out var site)) {
        var frames = new List<string>();
        for (var stack = data.CallStack(); stack != null; stack = stack.Caller)
            frames.Add(stack.CodeAddress.FullMethodName);
        // Classify from the complete stack, before truncating the display path. These are
        // stack regions, not measured time windows: Execute also includes the small warmup.
        string region = frames.Any(f => f.StartsWith("Xsl3.Tests.StreamingProbe::Expected(", StringComparison.Ordinal)) ? "output-oracle"
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
long cpuSamples = counts.GetValueOrDefault("Universal.Events/cpu");
Console.WriteLine(JsonSerializer.Serialize(new {
    Scope = "Kernel Universal.Events/cpu samples only; instrumented diagnostic, not throughput acceptance",
    CpuSamples = cpuSamples,
    CpuSamplesWithEngineFrame = sites.Where(s => s.Key.Engine.Length != 0).Sum(s => s.Value),
    EventsLost = log.EventsLost, EventCounts = counts, Examples = examples, Symbols = symbols,
    AttributionScope = "Complete-stack regions; warmup may be included; unattributed samples remain visible. Not timed-transform phase boundaries or removable elapsed shares.",
    Regions = regions.OrderByDescending(r => r.Value).Select(r => new { Region = r.Key, Samples = r.Value }),
    Sites = sites.OrderByDescending(s => s.Value).Select(s => new {
        Process = s.Key.Process, Thread = s.Key.Thread, Leaf = s.Key.Leaf, Engine = s.Key.Engine, Region = s.Key.Region, Samples = s.Value }),
    Inclusive = inclusive.OrderByDescending(s => s.Value).Take(100).Select(s => new { Frame = s.Key, Samples = s.Value }),
    InclusiveByRegion = regionalInclusive.GroupBy(s => s.Key.Region).Select(g => new {
        Region = g.Key,
        Frames = g.OrderByDescending(s => s.Value).Take(100).Select(s => new { Frame = s.Key.Frame, Samples = s.Value })
    })
}, new JsonSerializerOptions { WriteIndented = true }));
return cpuSamples > 0 ? 0 : 1;
