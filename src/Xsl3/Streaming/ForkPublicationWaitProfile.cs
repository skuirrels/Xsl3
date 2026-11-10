// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Xsl3.Streaming;

// Diagnostic branch only. Sampling uses each worker's CPU clock and the monotonic wall clock.
// Keep one bounded set of counters per reader, and emit it after the workers finish.
internal sealed class ForkPublicationWaitProfile(int readers, int stride)
{
    private const int LinuxThreadCpuClock = 3;
    private readonly Totals[] _spin = new Totals[readers];
    private readonly Totals[] _park = new Totals[readers];
    internal long PublishedAt;

    internal static ForkPublicationWaitProfile? Create(int readers) =>
        OperatingSystem.IsLinux() && int.TryParse(Environment.GetEnvironmentVariable("XSL3_FORK_WAIT_PROFILE_STRIDE"), out int stride) && stride > 0
            ? new(readers, stride) : null;

    internal readonly record struct Sample(long Before, long Start, long CpuStart, long Version);

    internal Sample Start(int reader, bool park, long version)
    {
        ref Totals totals = ref (park ? ref _park[reader] : ref _spin[reader]);
        if (totals.Calls++ % stride != 0) return default;
        long before = Stopwatch.GetTimestamp();
        long cpu = ThreadCpu();
        return new(before, Stopwatch.GetTimestamp(), cpu, version);
    }

    internal void Record(int reader, bool park, Sample sample, long version, bool available)
    {
        if (sample.Start == 0) return;
        long end = Stopwatch.GetTimestamp();
        long cpu = ThreadCpu() - sample.CpuStart;
        long afterClock = Stopwatch.GetTimestamp();
        long wall = Nanoseconds(sample.Start, end);
        long offCpu = Math.Max(0, wall - cpu);
        long offCpuUpper = Math.Min(wall, Math.Max(0, Nanoseconds(sample.Before, afterClock) - cpu));
        ref Totals totals = ref (park ? ref _park[reader] : ref _spin[reader]);
        totals.Samples++;
        totals.Wall += wall;
        totals.Cpu += cpu;
        totals.OffCpu += offCpu;
        totals.OffCpuUpper += offCpuUpper;
        if (available && version == sample.Version + 1 && PublishedAt >= sample.Start && PublishedAt <= end)
        {
            // A single publication identifies the boundary. CPU use on either side
            // is unknown, so report bounds for the off-CPU portion after publication.
            long after = Nanoseconds(PublishedAt, end);
            totals.OnePublication++;
            totals.OnePublicationOffCpu += offCpu;
            totals.OnePublicationOffCpuUpper += offCpuUpper;
            totals.AfterLower += Math.Max(0, after - cpu);
            totals.AfterUpper += Math.Min(after, offCpuUpper);
        }
    }

    internal void Write()
    {
        for (int i = 0; i < readers; i++)
        {
            WriteRow(i, "spin", _spin[i]);
            WriteRow(i, "park", _park[i]);
        }
    }

    private void WriteRow(int reader, string phase, Totals t) => Console.Error.WriteLine(FormattableString.Invariant(
        $"p7-publication-offcpu\t{reader}\t{phase}\t{stride}\t{t.Calls}\t{t.Samples}\t{t.Wall}\t{t.Cpu}\t{t.OffCpu}\t{t.OffCpuUpper}\t{t.OnePublication}\t{t.OnePublicationOffCpu}\t{t.OnePublicationOffCpuUpper}\t{t.AfterLower}\t{t.AfterUpper}"));

    private static long Nanoseconds(long start, long end) => Stopwatch.GetElapsedTime(start, end).Ticks * 100;

    private static long ThreadCpu()
    {
        if (ClockGetTime(LinuxThreadCpuClock, out Timespec value) != 0)
            throw new InvalidOperationException("Could not read the Linux thread CPU clock");
        return (long)value.Seconds * 1_000_000_000 + (long)value.Nanoseconds;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Timespec { internal nint Seconds, Nanoseconds; }

    [DllImport("libc", EntryPoint = "clock_gettime")]
    private static extern int ClockGetTime(int clock, out Timespec value);

    private struct Totals
    {
        internal long Calls, Samples, Wall, Cpu, OffCpu, OffCpuUpper, OnePublication, OnePublicationOffCpu,
            OnePublicationOffCpuUpper, AfterLower, AfterUpper;
    }
}
