# P7 source child-header control

**Rejected, 5 November 2026. Do not merge or adopt this runtime control.**

This isolated control starts from merged main `a5e89ac` on `feature/p7-source-child-header`.
It is independent of the 256/4,096-event buffer candidates and keeps the default at 64.

## Premise and change

The Linux CPU profile of unchanged runtime source equivalent to `a5e89ac` attributes 1,151 inclusive
identity samples and 933 accumulator samples to `StreamedDocument.Child`. These are inclusive CPU
samples, not removable elapsed time. Source inspection found a redundant fork-event test in that path.

All three calls to `Child(XPathNodeType)` occur in `ReadNextCore` after its `ForkEventReader` branch
has returned. Actual fork events create their view frames through `ReadForkEvent` and
`ForkHeader.CreateFresh`; they cannot reach this helper. Reader assignments occur only in constructors
and disposal. Consequently the old `original` local in `Child` is always null, and the new node always
gets its next source number. The control removes the unreachable type-test/provenance arm and leaves
number advancement, overflow behavior, metadata reads, parent links and child flags unchanged.

No pooling, schema assumption, private reader bypass, new configuration, fixture-specific condition,
input consumption or scheduling change is introduced. Existing streaming, fork, identity, replay and
schema regressions provide correctness coverage. A new test that merely mirrored the removed branch
would not add behavioral evidence.

## Qualification

Affected Release tests pass: 3,957 .NET 10 and 3,248 .NET 8, all 7,205 without failures or skips.
The paired local comparison completed all 48 observations with correct independent outputs, exact inputs,
actual heap guards and unchanged frozen binaries. It shows no useful speedup; the control is rejected.
The comparison keeps the original benchmark assembly and all consumer binaries byte-identical, replaces
only the engine DLL/PDB/XML in a frozen candidate directory, and alternates variant order across three
rounds. Identity, accumulator, fork and source-document each run tree and stream modes on 100,000,000
bytes with four effective processors, tiering disabled and the actual 4 GiB heap guard. Every original
output oracle and input/first-output check remains active. These local observations are diagnostics,
not the original hosted full P7 gates.

Evidence is retained under `/private/tmp/xsl3-p7-source-child-header-20261105/`: source patch and hashes,
frozen binaries, raw test results, comparison script and reports. The baseline runtime hash is
`60ef17ce25517b7f32d2a8f50df3a6477f03efea4e3e36123f972b7f054ed049`, built from `78815e8`; its runtime,
benchmark and probe source match `a5e89ac`. Candidate hash:
`b265785e2c98d60e0a0c5c5c31639c1bc999e88eb94cd81461cfe44175332613`.

The full original objective remains active. A passing local control cannot qualify the memory budgets,
all seven throughput targets, final integration, supported-streaming claim or release.

## Paired local results

| Workload | Baseline stream median | Candidate stream median | Candidate / baseline | Winner |
|---|---:|---:|---:|---|
| identity | 2145.122 ms | 2145.363 ms | 1.000113 | No meaningful improvement |
| accumulator | 2072.095 ms | 2076.707 ms | 1.002226 | No meaningful improvement |
| fork | 1842.326 ms | 1857.951 ms | 1.008481 | No meaningful improvement |
| source-document | 2110.240 ms | 2134.750 ms | 1.011615 | No meaningful improvement |

All observations, including overlapping ranges and slower candidates, are retained in `paired-review.json`
and the six raw reports. No Debug, NativeAOT or hosted qualification was started for this rejected control.
The next investigation is independent accumulator state traversal; this change is not its baseline.
