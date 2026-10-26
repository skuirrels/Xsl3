// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Xsl3.Tests;

internal static partial class StreamingProbe
{
    // Process.PeakWorkingSet64 reports zero on some macOS runtimes. A missing
    // measurement must not make an impossible budget pass. Unix getrusage reports
    // bytes on Darwin and KiB on Linux; Windows provides the process counter.
    private static string PeakMemorySource => OperatingSystem.IsWindows() ? "Process.PeakWorkingSet64" : "getrusage(RUSAGE_SELF)";
    private static long PeakWorkingSet()
    {
        long bytes;
        if (OperatingSystem.IsWindows())
        {
            using var process = Process.GetCurrentProcess(); bytes = process.PeakWorkingSet64;
        }
        else if ((OperatingSystem.IsMacOS() || OperatingSystem.IsLinux()) && Environment.Is64BitProcess)
        {
            if (GetResourceUsage(0, out ResourceUsage usage) != 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
            bytes = checked(usage.MaxRss.ToInt64() * (OperatingSystem.IsLinux() ? 1024 : 1));
        }
        else throw new PlatformNotSupportedException("Absolute peak RSS is not implemented for this probe platform");
        return bytes > 0 ? bytes : throw new InvalidOperationException("The operating system did not provide peak RSS");
    }

    [DllImport("libc", EntryPoint = "getrusage", SetLastError = true)]
    private static extern int GetResourceUsage(int who, out ResourceUsage usage);

#pragma warning disable CS0649 // Populated by getrusage, not by managed assignments.
    [StructLayout(LayoutKind.Sequential)]
    private struct ResourceUsage
    {
        public nint UserSeconds, UserMicroseconds, SystemSeconds, SystemMicroseconds;
        public nint MaxRss, Shared, UnsharedData, Stack, MinorFaults, MajorFaults, Swaps;
        public nint BlockInputs, BlockOutputs, MessagesSent, MessagesReceived, Signals, VoluntarySwitches, InvoluntarySwitches;
    }
#pragma warning restore CS0649
}
