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
var sites = new Dictionary<(int Process, int Thread, string Leaf, string Engine), long>();
var stacks = new Dictionary<CallStackIndex, (string Leaf, string Engine)>();
var examples = new List<object>();
var symbols = new List<object>();
var inclusive = new Dictionary<string, long>();
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
        site = (frames.FirstOrDefault() ?? "<none>", string.Join(" -> ", frames.Where(f => f.Contains("Xsl3.")).Take(4)));
        stacks[index] = site;
    }
    var key = (data.ProcessID, data.ThreadID, site.Leaf, site.Engine);
    sites.TryGetValue(key, out long previous); sites[key] = previous + 1;
    var unique = new HashSet<string>();
    for (var stack = data.CallStack(); stack != null; stack = stack.Caller)
        if (unique.Add(stack.CodeAddress.FullMethodName)) {
            inclusive.TryGetValue(stack.CodeAddress.FullMethodName, out long previousCount);
            inclusive[stack.CodeAddress.FullMethodName] = previousCount + 1;
        }
};
source.Process();
long cpuSamples = counts.GetValueOrDefault("Universal.Events/cpu");
Console.WriteLine(JsonSerializer.Serialize(new {
    Scope = "Kernel Universal.Events/cpu samples only; instrumented diagnostic, not throughput acceptance",
    CpuSamples = cpuSamples,
    CpuSamplesWithEngineFrame = sites.Where(s => s.Key.Engine.Length != 0).Sum(s => s.Value),
    EventsLost = log.EventsLost, EventCounts = counts, Examples = examples, Symbols = symbols,
    Sites = sites.OrderByDescending(s => s.Value).Select(s => new {
        Process = s.Key.Process, Thread = s.Key.Thread, Leaf = s.Key.Leaf, Engine = s.Key.Engine, Samples = s.Value }),
    Inclusive = inclusive.OrderByDescending(s => s.Value).Take(100).Select(s => new { Frame = s.Key, Samples = s.Value })
}, new JsonSerializerOptions { WriteIndented = true }));
return cpuSamples > 0 ? 0 : 1;
