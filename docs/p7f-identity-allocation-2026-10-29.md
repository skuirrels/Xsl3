# P7F identity allocation reduction, 2026-10-29

P7F closes the [bounded single-hotspot phase](plan/p7f-identity-allocation.md). Ordinary untyped native
attribute copies now use a minimal compact fragment instead of allocating a general tree builder and
unused child buffers. The original annotation/identity-sensitive policy remains in the builder.
No public API, saved format, canonical fixture, input, oracle, globalization setting or budget changes.

This is a **1.1.4 candidate**, pending PR review and separately authorized publication. Version **1.1.3**
remains published. Streaming remains unsupported and `supports-streaming` remains `no`.

## Cause and scope

The baseline managed allocation trace identifies `XsltLibrary.Copies → NodeValidator.Preserve →
CompactDocument.Builder` while copying row attributes. Many sampled allocations are builders, child
arrays and temporary value/stack buffers. A parentless ordinary attribute needs none of those mutable
construction buffers. The new factory uses the existing compact representation and name pool; each copy
still has independent node identity. Typed, DTD identity-sensitive and `xml:id` attributes retain the
existing builder path, including annotation policy and `xml:id` normalization.

The before/after EventPipe traces are attribution evidence, with startup/warmup/oracle included and
sampled allocation weights. The acceptance measurements below use separate NativeAOT images and precise
cumulative allocation counters bracketing the transformation, excluding warmup and the independent oracle.
Matching phase instrumentation adds a small amount of bookkeeping on both sides; it is not in the
uninstrumented throughput measurements. No profiler is attached to those timing runs.

## Paired NativeAOT results

Three fresh processes per variant, size and measurement kind, on each platform: **72 processes**, all
full input and independently validated output. Both runtimes load the same original 1.1.3 saved entrypoints.
The heap guard is **20,971,520 bytes**. RSS rejection is explicitly disabled for this comparison; these are
allocation/throughput measurements, never memory-budget passes. Variant order alternates by round.

Median decimal GB allocated during the transformation; median elapsed milliseconds from the separate
uninstrumented controls. Allocation and time ratios use exact values in the [snapshot](plan/p7f-results.json).

| Platform | Exact input bytes | Baseline allocated GB | Candidate allocated GB | Allocation reduction | Baseline ms | Candidate ms | Time reduction |
|---|---:|---:|---:|---:|---:|---:|---:|
| macOS ARM64 | 10,485,760 | 0.7433 | 0.5683 | 23.55% | 253.79 | 241.36 | 4.90% |
| macOS ARM64 | 209,715,200 | 14.8661 | 11.3648 | 23.55% | 5,070.08 | 4,729.52 | 6.72% |
| macOS ARM64 | 2,147,483,648 | 152.2286 | 116.3750 | 23.55% | 53,610.10 | 49,743.68 | 7.21% |
| Linux x64 | 10,485,760 | 0.7433 | 0.5683 | 23.55% | 618.62 | 588.90 | 4.80% |
| Linux x64 | 209,715,200 | 14.8661 | 11.3648 | 23.55% | 12,244.87 | 11,526.78 | 5.86% |
| Linux x64 | 2,147,483,648 | 152.2286 | 116.3750 | 23.55% | 124,192.14 | 118,569.07 | 4.53% |

All six comparisons meet the frozen **at least 20% allocation reduction** and **no more than 5% slower**
criteria. These results apply to this workload/input family, not every stylesheet. Lower cumulative
allocation is not a claim of a proportionate reduction in retained memory or process RSS.

## Original complete memory gates

Every candidate matrix contains all 48 original children, including 32 passing tree/stream controls.
The [P7E baseline](p7e-memory-diagnosis-2026-10-28.md) remains intact.

| Platform | Input / absolute RSS and heap guard | P7E baseline budget passes | P7F candidate budget passes | Candidate full valid inputs |
|---|---|---:|---:|---:|
| macOS ARM64 | 200 MiB / 20 MiB | 13/16 | **12/16** | 16/16 |
| Linux x64 | 200 MiB / 20 MiB | 0/16 | **0/16** | 4/16; 12 rejected before measurement |
| macOS ARM64 | 2 GiB / 200 MiB | 16/16 | **16/16** | 16/16 |
| Linux x64 | 2 GiB / 200 MiB | 16/16 | **16/16** | 16/16 |

The additional macOS accumulator failure is retained: its internal pre-serialization peak is
**20,676,608 bytes**, but full-lifetime `wait4` reports **21,233,664**, exceeding **20,971,520**.
Three subsequent paired accumulator repeats on the frozen images all pass on both versions, with outer
peaks **20,578,304–20,709,376**. Those controls show that the extra failure did not recur; they do not
replace the original 12/16 result or waive the strict limit. Iterate, merge and fork remain failures.
On Linux all four completed small-budget cases validate output but exceed RSS; startup rejections are
not completed transformations. The 20 MiB acceptance objective remains unmet.

## Regression evidence and review

- Managed Release and Debug: **3,710 / 3,549** unit tests pass on .NET 10 / .NET 8.
- Upstream: **1,654** applicable tests per framework locally; byte-for-byte differential: **394/394**.
- Ordinary conformance retains QT3 **21,884/0/99**, XSLT 2.0 **4,590/8/10** and XSLT 3.0 **8,850/18/41**.
  Original permissive expected-error accounting remains recorded; it is not promoted to an exact-code pass.
- Reviewed S1 **1,000/0/0** and S2 **155/0/0**, Release and Debug. Raw S1 **790/129/0** and raw S2
  **153/2** are preserved separately. Strict-forward remains **2,695/16/1**, with zero unmatched expected
  errors. Unstreamed remains **2,693/18/1**, including its original one unmatched expected error.
- All 32 retained saved entrypoints run through both .NET 8 and .NET 10 consumers. Full native matrices
  cover the original saved workloads; focused tests also compile and load normal/debug saved copies.
- Twelve focused tests pass on both baseline and candidate, checking parentlessness, independent identity,
  names/values after reader closure, `xml:id`, DTD attribute values and saved behavior. Twenty-four Python
  controller/diagnostic/comparison tests pass, including incomplete/non-finite evidence rejection.
- The ordinary three-round performance gate passes: local time geometric mean **1.021** across 39 rows,
  with no allocation regression above its 5% threshold. Individual slower compile rows remain visible
  (`keys` 1.429, `sorting` 1.189); this aggregate gate is not a per-row speedup claim.
- [Implementation CI](https://github.com/skuirrels/Xsl3/actions/runs/40837833886) passes Linux, Windows,
  NativeAOT and ordinary performance checks. [Hosted P7F measurements](https://github.com/skuirrels/Xsl3/actions/runs/40837847561)
  pass the comparison and large memory job; the small memory job correctly fails.

Review preserved one unrelated DTD identity roundtrip finding, reproduced identically before and after;
its broader contract investigation is [deferred](plan/p7f-deferred.md). The original failing test source
and all failures are retained. No existing passing assertion, upstream fixture or baseline was relaxed.
The final manual workflow reuses the original full memory job verbatim instead of maintaining a duplicate;
the retained first-run jobs used exactly the same canonical commands and runtime source.

## Provenance, reproduction and stopping rule

Baseline runtime: release `7bf3dd67a71fe0d77dc2fca1f0264ff8b343c4bf`; merged P7E base:
`59a08e925badf116dfd79a7c1520986e16069935`. Tested implementation:
`57b65933a352d25b2095fa3fa81c6c8090e31a10`. Both platforms use SDK **10.0.403**, runtime/toolchain **10.0.14**.
The snapshot records runtime/fixture hashes, image hashes, all trial metrics, canonical measured rows,
raw report hashes and verified hosted archive digests/expiry. Raw local evidence is under
`artifacts/p7f-identity/`; ignored files are not automatically published. The local unchanged control is
the hashed 1.1.3 P7E image measured afresh. The managed trace host's isolated console handshake is not
linked by the NativeAOT probe.

Reproduce with fresh directories and matching saved entrypoints:

```sh
python3 eng/diagnose-streaming-memory.py prepare --source <baseline-source> --saved <original-saved-entrypoints> --rid <rid> --output <baseline-diagnostic>
python3 eng/diagnose-streaming-memory.py prepare --source <candidate-source> --saved <original-saved-entrypoints> --rid <rid> --output <candidate-diagnostic>
python3 eng/compare-identity-allocation.py --baseline-native <baseline-native> --candidate-native <candidate-native> --baseline-diagnostic <baseline-diagnostic-native> --candidate-diagnostic <candidate-diagnostic-native> --output <new-comparison> --sizes 10485760 209715200 2147483648 --rounds 3 --heap-limit 20971520 --minimum-allocation-reduction 0.2 --maximum-slowdown 1.05
python3 eng/streaming-gate.py --native <candidate-native> --bytes 209715200 --limit 20971520 --timeout-seconds 900 --output <new-push-matrix>
python3 eng/streaming-gate.py --native <candidate-native> --bytes 2147483648 --limit 209715200 --timeout-seconds 900 --output <new-nightly-matrix>
```

Manual workflow: `allocation_comparison=true`, `memory_diagnosis=false`, `source_ref=v1.1.3`.
Scheduled/default acceptance job bodies and limits are unchanged. All bounded P7F criteria have evidence;
stop implementation here. Startup footprint, other allocation hotspots, general streaming support and
the deferred DTD contract are separate work. Release tagging/publication remains separately authorized.
