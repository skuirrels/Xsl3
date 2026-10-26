# Streaming benchmark evidence, 2026-10-24

The full local process matrix is complete: **126 terminal trials, 111 validated runs and 15 failed tree runs**. All **63 streamed trials** validate exact input consumption and independent output hashes. The 100 MB throughput target remains **incomplete coverage**, because merge has no valid tree observations. Among the six valid 100 MB comparisons, identity, fork and source-document streaming are slower than tree plus transformation. This does not complete S7.

Runtime: **.NET 10.0.11, macOS 27.0.1, Arm64**. Both modes use the same Release artifact, tiered compilation disabled and an explicit 4 GiB managed heap guard; every valid child reports an effective allowance of **4,294,967,296 bytes**. There are three alternating pairs for each workload and exact decimal input size. No other task-owned build, test or benchmark job ran during the matrix. These are managed JIT observations, separate from the saved NativeAOT memory gates.

The timed interval includes generated source reading, XML parsing, transformation, serialization and output hashing. Compilation, a small warmup, forced final collection and the independent oracle are outside it. Full-lifetime RSS is retained separately by wait4. See [method and commands](streaming-benchmark.md).

| Workload | Input MB | Tree median ms | Stream median ms | Stream/tree | Winner |
|---|---:|---:|---:|---:|---|
| identity | 1 | 23.536459 | 47.668791 | 2.025317 | Tree observed median |
| adjacent | 1 | 13.974541 | 11.750750 | 0.840868 | Stream observed median |
| accumulator | 1 | 62.255333 | 43.283166 | 0.695252 | Stream observed median |
| iterate | 1 | 15.706500 | 13.329458 | 0.848659 | Stream observed median |
| merge | 1 | 119.710417 | 44.844416 | 0.374607 | Stream observed median |
| fork | 1 | 17.361416 | 61.234791 | 3.527062 | Tree observed median |
| source-document | 1 | 23.556125 | 50.715333 | 2.152957 | Tree observed median |
| identity | 100 | 2380.745291 | 4764.436000 | 2.001237 | Tree observed median |
| adjacent | 100 | 1760.602958 | 1129.268916 | 0.641410 | Stream observed median |
| accumulator | 100 | 6177.541250 | 4185.497250 | 0.677534 | Stream observed median |
| iterate | 100 | 1644.374542 | 1358.782375 | 0.826322 | Stream observed median |
| merge | 100 | OOM (0/3 valid) | 4546.305750 | — | No meaningful winner |
| fork | 100 | 1767.866750 | 5921.473333 | 3.349502 | Tree observed median |
| source-document | 100 | 2369.459542 | 5011.438875 | 2.115013 | Tree observed median |
| identity | 1000 | 25863.870583 | 49239.938167 | 1.903812 | Tree observed median |
| adjacent | 1000 | OOM (0/3 valid) | 11492.868167 | — | No meaningful winner |
| accumulator | 1000 | OOM (0/3 valid) | 43167.917334 | — | No meaningful winner |
| iterate | 1000 | 19813.655833 | 13223.421083 | 0.667389 | Stream observed median |
| merge | 1000 | OOM (0/3 valid) | 45146.081417 | — | No meaningful winner |
| fork | 1000 | OOM (0/3 valid) | 61351.591709 | — | No meaningful winner |
| source-document | 1000 | 26424.526125 | 51393.275792 | 1.944908 | Tree observed median |

Winner labels describe observed medians with a 5% band, not statistical significance. Failed comparisons have no ratio or winner. The exact throughput requirement uses 1.0, without this descriptive tolerance. All fifteen failed children return code 1 and retain System.OutOfMemoryException in stderr: three merge trials at 100 MB, plus three each for adjacent grouping, accumulators, merge and fork at 1 GB. No streaming winner is inferred from those failures.

Incremental first-output observations are below. Each cell is the median **milliseconds / input bytes consumed**; the input position is independent of total document size for these measured shapes. No quantitative latency threshold was approved or inferred.

| Workload | Stream 1 MB | Stream 100 MB | Stream 1 GB | Tree 100 MB first output |
|---|---:|---:|---:|---:|
| identity | 0.317291 / 8,192 | 0.327500 / 8,192 | 0.352292 / 8,192 | 1140.328333 / 100,000,000 |
| adjacent | 4.242459 / 356,352 | 4.153250 / 356,352 | 4.419167 / 356,352 | 1433.145416 / 100,000,000 |
| merge | 6.722958 / 122,880 | 6.266625 / 122,880 | 6.606625 / 122,880 | OOM (0/3 valid) |
| fork | 2.502833 / 36,864 | 2.276667 / 36,864 | 2.571709 / 36,864 | 1141.466334 / 100,000,000 |
| source-document | 0.395083 / 8,192 | 0.434708 / 8,192 | 0.441292 / 8,192 | 1092.085417 / 100,000,000 |

The accumulator and iterate totals require end-of-input at every size. The streamed child full-lifetime RSS range is **91,013,120–95,780,864 bytes** across this managed matrix. This is not the approved 20 MiB NativeAOT push test, does not change either memory budget and does not replace retained-memory/large-input evidence.

Raw evidence is `/tmp/xsl3-p7-streaming-process-benchmark/`: frozen `bin/` artifacts, `source-state.json`, `shared-fixture-audit.json`, `run.log`, and `measurements/report.json`, `summary.md` plus every child stdout/stderr. The source audit covers 400 production/benchmark source files. The additional audit was captured after launch; all five current shared fixture SHA-256 checksums occur in the frozen portable PDB, and their hashes/configuration hashes are retained explicitly. The runtime, controller, benchmark and engine hashes are in the report. The local command did not request `--require-throughput-target`; it still contains failed trials. The nightly command does require the exact target. No remote CI run or captured controller exit status is claimed by the report.

No engine source, memory limit, default streaming claim or saved-assembly format changed in this increment. The exact push RSS investigation remains [open](streaming-rss-investigation-2026-10-24.md). Remaining strict failures, S1/error-code work, throughput/latency and remote CI evidence continue to prevent Phase 7 completion.


After the matrix, the first ordinary performance-gate attempt stopped before timing: the old base lacks
`Xslt30Transformer.ApplyTemplates(XmlReader, Stream)`. An isolated base archive reproduces its two CS1503
errors with the process host enabled, and builds with zero warnings/errors when it is excluded. The
benchmark project now defaults `EnableStreamingProcessBenchmarks` to true; the ordinary gate explicitly
sets it false on both sides. Build failures print their captured diagnostics. This keeps new streaming APIs
out of an ordinary micro comparison against older engines without changing the default streaming host.

The current default build has **409/409 identical method IL bodies** compared with the frozen measured
benchmark. The current production engine and controller hashes still match. The two differences among the source audit's 400 files are benchmark Program.cs and its project file,
which add the compile guard and property. No production source differs.
`post-compatibility-audit.json` records this distinction. Fresh default builds pass with zero warnings/errors,
and both net10.0 and net8.0 public benchmark smoke matrices pass **22/22 children**. The compatibility build
logs, IL audit and smoke reports are retained under `/tmp/xsl3-p7-streaming-base-compatibility*` and
`/tmp/xsl3-p7-process-benchmark-compat*`; no second full matrix was needed for identical default method bodies.


The subsequent ordinary micro-suite gate passes over three pairs: timed geometric mean **1.036264**,
maximum allocation ratio **1.043896**. Individual regressions remain, including key compilation **1.579705**;
this aggregate pass does not resolve them or the streaming target. Exact observations are recorded in
[the S7 plan evidence](plan/p7-streaming.md#s7-paired-process-benchmark-and-exact-push-rss-investigation).
