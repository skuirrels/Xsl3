# Exact push RSS investigation, 2026-10-24

The approved 200 MiB-input / 20 MiB absolute-RSS push gate remains red. The prior complete NativeAOT
matrix has eleven measured failures; the 2 GiB / 200 MiB nightly matrix passes all eleven workloads.
This investigation changes neither limit, globalization, runtime features nor production code.
Its zero-limit diagnostic runs consume the full input but do not establish a memory-budget pass.

Evidence is under `/tmp/xsl3-p7-rss-investigation/`. The unchanged executable is
`/tmp/xsl3-p7-types-final-linux/artifacts/native/bin/Xsl3.StreamingProbe.Native`, built in the cached
official Linux arm64 .NET SDK image with SDK 10.0.400 / runtime 10.0.11. Both native-evidence flags are
false and the ordinary 20 MiB managed cap is retained, except for the explicitly identified smaller-heap
diagnostic. The input is exactly 209,715,200 bytes unless stated otherwise. ICU remains enabled.

| Type-inspection diagnostic | Child-reported peak RSS, bytes | Live managed bytes | Effective managed allowance, bytes |
|---|---:|---:|---:|
| GC conservation 0 | 24,707,072 | 357,824 | 20,971,520 |
| GC conservation 5 | 24,662,016 | 357,824 | 20,971,520 |
| GC conservation 9 | 24,674,304 | 357,824 | 20,971,520 |
| 1 MiB GC regions | 24,666,112 | 357,824 | 20,971,520 |
| 2 MiB GC regions | 24,809,472 | 357,824 | 20,971,520 |
| Foreground GC | 24,576,000 | 357,824 | 20,971,520 |
| Foreground GC plus conservation 9 | 24,735,744 | 357,824 | 20,971,520 |
| 8 MiB heap diagnostic only | 24,346,624 | 357,824 | 8,388,608 |

These diagnostic values come from the child's `getrusage(RUSAGE_SELF)` after execution and oracle
validation. They exclude later JSON serialization and teardown; no outer full-lifetime counter was
collected for these experiments. Each already exceeds the absolute budget before those later phases.
The formal gate additionally collects the full child lifetime with `wait4`.

The [runtime GC configuration documentation](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/garbage-collector)
describes managed-heap controls. They do not cap total process RSS. None of these settings was adopted;
each run remains above 20,971,520 bytes. JSON records are `conserve-{0,5,9}.json`, `region-{100000,200000}.json`,
`foreground.json`, `foreground-conserve.json` and `small-heap-diagnostic.json` in the evidence directory.

An unchanged 2 GiB type-inspection run also captured `/proc/<pid>/smaps` and status after 0.1, 1.1 and
4.1 seconds. The late snapshot has 24,316 KiB resident: 9,844 KiB executable mappings, 8,384 KiB anonymous
mappings, 1,464 KiB libstdc++, 1,424 KiB libicuuc, 1,228 KiB libicui18n, 264 KiB ICU data, 1,300 KiB libc
and smaller mappings. The snapshot's Pss_Anon is 8,828 KiB and Pss_File is 14,612 KiB. Snapshot RSS is not
the full-lifetime maximum. Raw evidence is `late.smaps`, `late.status`, `late.rollup`, the earlier captures,
`capture.sh`, `capture.log` and `mapped-run.json`.

ELF section parsing identifies about 7.43 MB of managed code, 3.54 MB of read-only data and a 3.17 MB
NOBITS `.hydrated` region. The largest anonymous mapping is 3,152 KiB and aligns with that region. This is
evidence of a native image/runtime footprint worth investigating, not attribution of all anonymous RSS
to a managed leak. `symbols.txt` was produced from the debug symbols with Xcode llvm-nm. The attempted
SDK `size`/`readelf` command failed because binutils was absent; `elf-sections.txt` is not successful tool
evidence. The compiler itself is not rooted in the saved consumer; XML/schema and function-library code is.

A separate publish using `IlcDehydrate=false` tested the NativeAOT
[hydrated-data mechanism](https://github.com/dotnet/runtime/blob/main/src/coreclr/tools/Common/Internal/Runtime/DehydratedData.cs).
It preserves normal globalization and reports 27,037,696 bytes peak RSS and 357,872 bytes live managed heap
for the same complete 200 MiB type-inspection input. That is worse than the default. `nohydrate-build.log`,
`nohydrate.sh` and `nohydrate-inspection.json` preserve the experiment; the original artifact was not replaced.

A temporary consumer rooting only the saved identity stylesheet pair also remains above the push budget:
23,842,816 bytes RSS, 367,960 bytes live managed heap, effective cap 20,971,520 and valid full-input output.
`identity-only.json` records the run. The temporary publish succeeded as `identity-native/identity`; the
initial script tried the wrong executable name and exited 127. Its build log has temporary-project warnings
and is not a clean product build. The follow-up run used the actual executable. Splitting the consumer was
therefore not adopted as a solution to the 20 MiB requirement.

These experiments narrow the investigation toward the native image and runtime footprint. They do not
justify excluding startup/warmup, subtracting tree baselines, lowering the effective heap cap, disabling ICU
or declaring a push pass. Native image changes require fresh correctness, saved-assembly and exact memory
matrices. Throughput is measured separately by the [paired process benchmark](streaming-benchmark.md).
