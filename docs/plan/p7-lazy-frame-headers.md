# P7 lazy empty frame headers

This isolated control starts from `18fc9a5` (corrected-discard runtime `a8d322a`). It excludes the
unadopted short-polling and tail-call controls and the separate public-selection integration.
Original qualification inputs, output oracles, budgets and throughput targets are unchanged.

## Measured implementation target

The existing Linux identity CPU capture includes write-barrier work. A separate baseline JIT
diagnostic on macOS ARM64 confirms that every `StreamedDocument.Node` constructor eagerly resolves
the two shared empty header arrays and performs their reference assignments. Empty-string defaults,
in contrast, already use cheap paired stores; no string-initialization change is proposed.

The candidate keeps two array-reference fields but resolves their `Array.Empty<T>()` defaults in
the getters. Assigning a header preserves the exact array, without copying, changing its ownership
or adding an allocation. Leaf frames can keep the null backing fields if their headers are never
inspected. Instance-field types and count stay the same. Public navigator behavior, populated
headers, replay, source demand and fork capacity remain unchanged.

The actual candidate constructor is **92 bytes of ARM64 JIT code versus 224** for the baseline.
The two shared-array lookups and reference assignments are absent; the parent assignment remains.
Both diagnostic children validate the original 1 MB identity output and actual 4 GiB heap guard.
This demonstrates the targeted code change, not a throughput or process-memory improvement.

## Qualification boundary

1. Preserve existing navigator/frame, namespace, schema, copied-attribute identity, fork and
   physical-read contracts. If timing supports further work, complete Release/Debug .NET 8/10
   regression coverage, saved/native consumers and ordinary compatibility.
2. Compare through the unchanged canonical consumer over five workloads, both modes and three
   alternating rounds: 60 observations at exact 100 MB and actual 4 GiB heap guards. Freeze both
   engines and verify output and source identity independently.
3. A useful local result still requires a paired target-platform comparison and all original
   final-source memory, throughput, integration, review, CI and release requirements. Do not adopt
   the control merely because its constructor is smaller.

All **1,080 .NET 10 Release** selected checks pass, with no failures/skips and every individual TRX
result checked against the counters.

## Local paired result

All **60 observations** validate. Independent review checks raw outputs, exact inputs, actual heap
guards, four effective processors, the frozen consumer and binary hashes, and **409 tracked PDB
source hashes per engine**. Original hosted output fingerprints also agree. The measured candidate
source is `6bc43e4`; broader compatibility and target-platform timing are still required.

| Workload | Baseline streaming median (ms) | Candidate (ms) | Candidate / baseline | Winner |
|---|---:|---:|---:|---|
| identity | 2,149.015 | 2,130.465 | 0.991368 | Candidate in this cohort; less than 1% |
| accumulator | 2,100.483 | 2,087.785 | 0.993955 | No meaningful winner; ranges overlap |
| merge | 3,438.433 | 3,445.556 | 1.002072 | No meaningful winner; ranges overlap |
| fork | 1,603.120 | 1,535.016 | 0.957518 | No meaningful winner; ranges overlap |
| source-document | 2,151.399 | 2,127.077 | 0.988695 | No meaningful winner; ranges overlap |

Identity baseline samples are `[2148.943083, 2165.052584, 2149.015459]` ms; candidate samples are
`[2130.464958, 2124.850458, 2146.711417]` ms. Every candidate sample is below every baseline sample,
but the magnitude is small. Fork's larger median reduction has overlapping ranges and a slow first
baseline sample, so it is not claimed as a robust benefit. These results justify one paired Linux
comparison of the same five workloads, not adoption or a full qualification rerun.

Evidence: `/private/tmp/xsl3-p7-lazy-frame-headers-20261106/` contains the source patch/hash, focused
TRX/audit, frozen diagnostic consumer, paired reports/audit and candidate disassembly. Baseline constructor evidence is
in `/private/tmp/xsl3-p7-frame-initialization-20261106/`. No adoption or release readiness is claimed.

## Linux paired result and decision

The [paired Linux run](https://github.com/skuirrels/Xsl3/actions/runs/42136785894) completed on
`a3e284cb1503aa46b095649ff5ae5c484e3661a1`, with the same runtime change as `6bc43e4`.
Independent review validates all **60 observations**, exact 100 MB inputs, actual 4 GiB heap guards,
four effective processors, unchanged consumer binaries, raw output records and original qualification
fingerprints. Both engines match **409 tracked PDB source hashes**. This is a diagnostic comparison,
not the original 126-observation qualification.

| Workload | Baseline streaming median (ms) | Candidate (ms) | Candidate / baseline | Winner |
|---|---:|---:|---:|---|
| identity | 5,186.150 | 5,126.364 | 0.988472 | No meaningful winner; ranges overlap |
| accumulator | 5,110.377 | 5,202.868 | 1.018098 | No meaningful winner; ranges overlap |
| merge | 9,889.539 | 9,599.777 | 0.970700 | No meaningful winner; ranges overlap |
| fork | 4,565.608 | 4,574.326 | 1.001910 | No meaningful winner; ranges overlap |
| source-document | 4,976.465 | 4,948.909 | 0.994463 | No meaningful winner; ranges overlap |

**Decision: close without adoption.** The small local identity benefit is not established on Linux;
all five pairs have overlapping ranges, and the accumulator median is higher. The smaller constructor
alone does not justify integration or a full qualification rerun. Do not repeat this comparison merely
to seek a favorable median. Keep the original gates and the separate public-selection branch unchanged.

The Release and Debug regression suites each pass **4,951 .NET 10 /4,027 .NET 8** checks:
**17,956 total**, with every TRX result audited and no failures/skips. Runtime/test source remained
unchanged throughout validation; all four engine/test binary pairs and PDBs are frozen with hashes.
`full-review.json` and `frozen-validation.json` retain the completed audit. Review confirms that the frame-lifetime test reads the header properties directly and
the managed-memory test uses actual GC measurements; private backing fields do not hide retained payloads.
The compiled fork failure contract passes in both Release frameworks (including saved execution on .NET 10),
observing that earlier prongs finish and later prongs stop before consuming the complete source.

Hosted evidence is retained in `/private/tmp/xsl3-p7-lazy-frame-headers-linux-20261106/`:
artifact **13465798774** (expires 4 February 2027), archive SHA-256
`e6828f752584fe2f958fa1ba186c6f5292cea9e7931b548792959db5e048751b`, raw reports/binaries,
PDB inventories, `audit.py`, `audit.log` and `review.json`. No performance change from this control is
approved for the final qualification source.
