# P7 fork name-lookup investigation

## Evidence and next control

[Linux diagnostic 42149213380](https://github.com/skuirrels/Xsl3/actions/runs/42149213380)
profiles unchanged integration source `32c845daf89818aeb4950e7d549a34fa3cef18e9`.
The existing framework-symbol controller is `5db06fbcd023302e70fb06bfb5fc354478c10403`.
ReadyToRun is disabled only for this diagnostic child, to expose otherwise unresolved framework
calls. This is supplemental attribution evidence, not an elapsed-time comparison or an acceptance run.

The original 1,000,000,000-byte fork input completes with 8,695,652 records. All three output
fingerprints match all six corresponding observations from original qualification 42123882563.
The actual managed allowance is 4,294,967,296 bytes; tiering is disabled, the host has four logical
processors, and no processor-count override is set. The artifact digest, four retained engine/consumer
binary hashes and all 409 tracked engine PDB source hashes verify. The other 24 dependency files are
not retained, so their reported hashes are not independently verified.

The trace contains 44,466 kernel CPU samples and zero reported lost events. Reprocessing with the
current, PDB-source-verified region parser preserves every original site count, inclusive count and
event count. Regions contain 24,268 fork-worker samples, 11,344 transform-entry samples, 1,069
output-oracle samples and 7,785 unattributed samples. Regions can include warmup and are not exact
timed-transform boundaries.

| Resolved leaf | Samples | Attribution |
|---|---:|---|
| `NameTable.Add(string)` | 748 | Fork header atomization in the two worker prongs |
| `Marvin.ComputeHash32` called from `ForkHeader.Atomize` | 334 | The same worker atomization path |
| `Marvin.ComputeHash32` called from source-reader paths | 171 | Source parsing; separate from the proposed control |

These are disjoint leaf counts, not an estimate of removable elapsed time. The framework-JIT policy
can change the cost distribution. The previous normal-ReadyToRun fork capture also resolves the
engine's atomization method, but does not supply equivalent framework detail.

The isolated `feature/p7-fork-name-cache` control avoids repeatedly hashing already-seen immutable
source-name references in each prong. It keeps eight input/canonical-reference pairs in a private
`NameTable` subclass. Cache misses use the base name table; eviction drops only a recent reference
mapping. No name table is shared across threads. Preseeded prong references remain authoritative,
and the extra cache retains neither source frames nor attribute values. Its capacity is independent
of document length. No input fixture names or values participate in the implementation.

All **2,298 affected Release/Debug checks** pass across .NET 8/10: 615/534 per configuration. The
new contracts cover string/character-array seeds, repeated references, distinct equal strings,
eviction, empty names and collection of all but at most eight alternate input references. Existing
preseeded element/attribute/namespace, private schema, nested-fork, payload-lifetime, source-demand
and cancellation contracts remain in the selected suites. All four engine/test binary pairs are
frozen; their PDB source hashes match. Before the runtime edit, the initial new contracts and existing
header contracts passed all 22 checks on the original engine.

Review found and corrected one initial defect: null string input threw `NullReferenceException`
instead of the base name table's `ArgumentNullException`. A new test passes against the original
engine, fails against the retained initial-cache binary, and passes on the corrected engine in all
four build/framework combinations. No failed result is discarded.

The initial, pre-correction paired comparison validates all 60 original outputs. Fork's streaming
median is 1,509.809 ms baseline / 1,434.138 ms candidate, but its candidate samples
`[1414.582125, 1434.137667, 1656.566292]` overlap baseline
`[1509.809375, 1509.520833, 1514.284833]`; no reliable gain is established. Other workloads also have
overlapping ranges. These are retained as superseded diagnostic results, not corrected-source
qualification. The corrected-source comparison is running with the unchanged canonical consumer,
normal ReadyToRun, exact 100 MB inputs, three alternating rounds, four effective CPUs and the actual
4 GiB heap guard. Only a useful result warrants a Linux comparison and full final-source qualification.
No cache adoption, original gate pass or release readiness is claimed.

Separately, unchanged integration source `32c845d` passes **14,053 full Debug unit checks**:
7,613 .NET 10 and 6,440 .NET 8, with zero failures/skips. Every TRX result and frozen binary hash
verifies; runtime/test source remained unchanged during the run. Evidence is
`/private/tmp/xsl3-p7-integrated-debug-full-20261106/`. This qualifies the integration baseline,
not the later cache candidate.

## Reproduction and retained evidence

The diagnostic was dispatched without changing the integration runtime:

```sh
gh workflow run streaming-development.yml --repo skuirrels/Xsl3 \
  --ref feature/p7-framework-cpu-symbols \
  -f source_ref=32c845daf89818aeb4950e7d549a34fa3cef18e9 \
  -f cpu_profile=true -f cpu_jit_framework=true -f cpu_workload=fork
```

Artifact **13471722387** expires 4 February 2027. Its ZIP SHA-256 is
`83c0e38ff1833c9206a385250b5e79bf781df1ae7724d376c3502249ef583b6a`;
the raw trace SHA-256 is `30fdfb80a13dc4b36378ec3c4b1ccd3fa81b4d1a417cea133c3678d3f85e1653`.
Local evidence is `/private/tmp/xsl3-p7-fork-framework-20261106/`: the verified artifact,
`audit.py`, `review.json`, both parser PDB inventories, `cpu-regions.json`, the engine PDB inventory
and verified source hashes. The first local parser check detected an older repository build and
stopped before analysis; the successful parse uses the frozen region-tool binary whose PDB matches
the current `eng/StreamingCpuReport/Program.cs` source.

Cache evidence is `/private/tmp/xsl3-p7-fork-name-cache-20261106/`, with the initial source snapshots,
all raw results, executable comparison audits, source manifests and frozen binaries. `corrected/`
contains the repaired source's own validation and paired comparison; `corrected/null-control/`
retains the original-engine positive and initial-cache negative controls using the same new test binary.

Identity, merge, fork and source-document still miss the last complete original throughput gate.
All original final-source memory/throughput gates, supported claims, review/merge and release remain
required. This diagnostic establishes no new performance pass and does not authorize merging PR #40.
