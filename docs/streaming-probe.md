# Streaming process probe

The development `stream-probe` verb generates XML as it is read and hashes output without retaining it.
It is available in the managed test assembly and in a standalone NativeAOT executable. It covers identity copying with a changed attribute, adjacent-group totals,
end-of-document accumulator totals, an iterate balance, two streamed merge inputs, two fork result documents,
source-document processing from a non-streamed principal transformation, and ancestor union/intersection with filtered descendant populations, incremental type assertions, sequence-type inspection, selected-copy cardinality and conditional content with early emission. It calls the public transformer
and resolver/secondary-result APIs. The managed host uses `XsltCompiler` / `Xslt30Transformer`; the native host
loads saved assemblies with `XslCompiledTransform.Load(Type)` and delivers secondary results through
`XsltArgumentList.ResultDocumentHandler`. Both hosts share the generated inputs, stylesheets, output oracles,
measurement code and JSON schema. The [paired process benchmark](streaming-benchmark.md) links these same
fixtures into the existing benchmark project and measures tree-plus-transform versus streaming at the
plan's three sizes. Its resource guard does not replace the memory gate's exact limits.

Build `tests/Xsl3.Tests` in Release, then run the controller from the repository root:

```sh
python3 eng/streaming-gate.py \
  --assembly tests/Xsl3.Tests/bin/Release/net10.0/Xsl3.Tests.dll \
  --bytes 209715200 --limit 20971520 \
  --output /tmp/xsl3-stream-probe-new-run
```

For NativeAOT, publish on a host with the .NET 10 SDK and its NativeAOT prerequisites. Use the host's runtime
identifier (`linux-x64`, `linux-arm64` or `osx-arm64`, for example):

```sh
eng/publish-stream-probe.sh linux-x64 /tmp/xsl3-stream-native-new
python3 eng/streaming-gate.py \
  --native /tmp/xsl3-stream-native-new/bin/Xsl3.StreamingProbe.Native \
  --bytes 209715200 --limit 20971520 \
  --output /tmp/xsl3-stream-native-push-new
```

The publish directory must be new and its parent must exist. The script compiles tree and forward versions of all thirteen fixtures into
saved assemblies, generates direct static references to their entry points, then publishes the consumer with
trim/AOT warnings treated as errors. Normal globalization is preserved. These two development projects are
built by the script rather than the solution because the consumer requires generated inputs. The native
executable rejects CoreCLR-hosted execution, including the managed intermediate produced by the AOT build.
`DynamicCodeSupported == false` alone is insufficient: the SDK puts that feature switch in the managed
intermediate's runtime configuration too. The guard also rejects CoreCLR's trusted-platform-assembly host data;
the controller requires both `DynamicCodeSupported == false` and `HasTrustedPlatformAssemblies == false` for
native reports. CI exercises the real managed intermediate and verifies its explicit rejection.
The development compiler switch is confined to generation; the native consumer uses public load/transform APIs.

For the separate nightly size/budget, use `--bytes 2147483648 --limit 209715200 --timeout-seconds 900`. A zero-limit smoke can use
`--bytes 2097152 --baseline-bytes 65536 --limit 0`. Neither command changes what the push gate requires.

The output directory must be new. The controller retains each child's stdout/stderr and an atomic JSON
checkpoint after each child. `complete: false` identifies an unfinished matrix; only the final report has
`complete: true`. Completion alone does not mean a pass: inspect the failures and each run's `passed` flag.
It first compares tree and streamed output fingerprints on 10 MiB inputs, then runs a separate streamed
child at the requested size and memory limit. The merge input byte count is the combined size of two inputs.
Each source has nesting, attributes, text and mixed content. Source record IDs, values and group boundaries
come from the deterministic test fixture; independent output oracles check counts, ordering and fingerprints
at every measured size. The corpus is never written to disk. The test fixture is not engine reference data.

The limit is **absolute process peak RSS**, including startup, compilation and warmup. The measured child also
receives `DOTNET_GCHeapHardLimit` at that limit. Each report includes `AvailableManagedBytes` from
`GC.GetGCMemoryInfo().TotalAvailableMemoryBytes`; a positive limit is accepted only when the runtime's effective
managed-memory allowance is positive and no larger than the requested cap. A zero limit explicitly disables budget enforcement for
functional or timing diagnostics; it does not establish a memory-budget pass. Baseline children do not inherit
the measured child's heap cap. A process that already exceeds the limit after warmup reports
`budget-exceeded-before-measurement`, zero measured input bytes and no measured timings or output fingerprints.
It fails the controller instead of pretending that the large input ran. Linux/macOS controllers also collect
the full child-lifetime peak with `wait4`, including report serialization and teardown, and enforce the larger
of that counter and the child counter. Missing outer counters fail. Windows child metrics are implemented;
the full-lifetime controller has not yet been implemented for Windows.

Exit codes from an individual child are 0 for a complete, validated run, 1 for an execution failure, 2 for an
output mismatch, 3 for a budget failure, and 64 for malformed arguments. The controller returns nonzero if any
child fails, lacks a valid result or has differing baseline fingerprints. The optional `--timeout-seconds`
wall-time guard defaults to zero (disabled). CI uses 900 seconds per child to stop stalled runs. A timed-out child
is killed and reaped with `wait4`, preserving its lifetime RSS counter; `timedOut: true` always fails, even if it
printed apparently valid JSON before stalling. This is a resource guard, not a streaming throughput threshold.

On 64-bit Unix, peak memory comes from `getrusage(RUSAGE_SELF)`; on Windows it comes from
`Process.PeakWorkingSet64`. Missing or nonpositive counters fail explicitly. Darwin values are bytes; Linux
values are KiB and are converted to bytes. The Darwin result was checked against `/usr/bin/time -l` because
`Process.PeakWorkingSet64` returns zero on this host. The archived Apple manual's units disagree with the
current Darwin observation; Linux documents its units in the
[getrusage manual](https://man7.org/linux/man-pages/man2/getrusage.2.html).

`LiveManagedBytes` uses a forced full collection after execution and oracle validation. It is a diagnostic
retention reading, outside the timed interval, and never replaces the absolute peak. The outer counter also
includes this final collection. Reports from the initial survey predate that change and used an uncollected
managed-byte reading; they must not be compared as retained-memory measurements.

First-output metrics record an actual sink write and the generated input bytes read at that point. Accumulator
and iterate totals need the end of input, so their first-output latency necessarily grows with input size.
Incremental-emission workloads must be assessed separately. No latency threshold is inferred from those totals.

The approved 200 MiB/20 MiB push exit remains unmet. The host's absolute startup footprint must fit the stated
budget; the limit is not reduced to managed live heap or a baseline-subtracted delta. CI is configured to run
the six controller failure-handling tests and both managed and NativeAOT 2 MiB, zero-limit functional smoke
matrices on Linux. Its native job also runs the exact 200 MiB/20 MiB push gate, and `perf-nightly.yml` runs the
2 GiB/200 MiB gate. These gates have no advisory waiver; local failures remain failures. Publish logs and all
controller evidence are retained on failure. The complete 2 GiB/200 MiB matrix now passes locally on Linux arm64;
no remote CI execution, push-budget pass or complete Phase 7 exit is claimed.

The initial 200 MiB-input/200 MiB-limit diagnostic survey failed: the accumulator exceeded peak RSS and the fork
exhausted its managed heap while constructing a secondary-result temporary tree. Five other workloads passed
that diagnostic limit with valid hashes. This is not the approved push gate or the 2 GiB nightly gate. The
approved 200 MiB-input/20 MiB-limit run failed all seven workloads during managed startup/warmup, before measured
input was consumed. These failures remain visible; the smoke test does not replace them.

Non-validated document results with an href now push to secondary serialization writers during forward
execution. XML, HTML, XHTML and text use the existing serialization parameter and URI-ownership logic; text
first discards markup and attributes, then applies character maps and Unicode normalization. A dynamic empty
href keeps principal-result ownership, and an explicit result-tree capture still constructs the requested tree.
Raw sequence methods and validated-result construction retain their existing paths. A secondary writer can
receive partial output before a later body or writer error; it is disposed on completion, errors and cancellation.

Accumulator phases now belong to the source frames instead of a document-wide dependent-handle table.
Retained replay/copy provenance still retains the corresponding phases; discarded frames release their values
with the frame. The 200 MiB accumulator diagnostic after this change validates its hash and stays below the
200 MiB diagnostic RSS limit. That earlier diagnostic alone did not establish an approved size/budget exit.

The native probe's SHA-256 sink uses a small portable implementation confined to the test/probe projects.
It implements [FIPS 180-4](https://nvlpubs.nist.gov/nistpubs/FIPS/NIST.FIPS.180-4.pdf) SHA-256, with standard
vectors and differential checks against the .NET implementation across block/padding/chunk boundaries. This
keeps the probe's hashing dependency from loading OpenSSL solely for measurement. It is not a library
cryptography API or a claim of FIPS validation. The output fingerprint algorithm, bytes, independent oracles
and absolute RSS budgets are unchanged.

The NativeAOT push attempts still fail overall. On this macOS arm64 host the portable-hash probe validates
identity and source-document at 200 MiB under 20 MiB RSS; the other five exceed the limit during warmup and
consume zero measured input. On Linux arm64 all seven fail the absolute limit: four consume the complete
200 MiB input with valid output, while iterate, merge and fork fail during warmup. Raw results are retained
under `/tmp/xsl3-p7-copy-owned-final-push-mac/` and
`/tmp/xsl3-p7-linux-probe/artifacts/push-copy-owned-final/`. Earlier attempts remain preserved separately. A separate Linux `DOTNET_GCConserveMemory=9` diagnostic
also fails; that policy is not configured by the probe or CI. These failures remain part of the evidence.

Fork source reads now cross to the invoking thread in batches that fit free space in the existing tee ring.
Published and staged event payloads together stay within the configured capacity; the staging reference array
is cleared after publication/discard. Accepted prefix events precede a source fault for every reader. Existing
backpressure, detachment, caller-thread I/O, error ordering and cancellation contracts remain in force.

Streamed copy/snapshot provenance now belongs to the compact copy. An invocation token prevents foreign
registries from interpreting it; multiple scopes can annotate a copy without losing each other's values.
Origin payloads are separate from storage links, so borrowing one payload cannot pin another scope's obsolete
source. Copies of streamed copies retain their provenance too. Ordinary grounded copies keep the existing
invocation-owned weak-table lifetime, so a small returned grounded copy does not retain its whole original
input after the invocation is collected. Completed streamed sources drop their reader reference while retaining
headers and accumulator phases; borrowed readers remain open.

The finalized **complete 2 GiB/200 MiB native matrix passes locally**, with all 21 children recorded and no
failures. All seven measured workloads consume exactly 2,147,483,648 input bytes and validate their independent
oracles. Each 10 MiB tree/stream baseline pair also has matching fingerprints. Peak full-lifetime RSS ranges from
27,078,656 to 30,638,080 bytes; no in-execution forced collection or changed limit is used. Native reports have
dynamic code disabled and no CoreCLR trusted-platform-assembly data. Evidence is
`/tmp/xsl3-p7-linux-probe/artifacts/nightly-copy-owned-final/`. This is local Linux arm64 evidence; the configured
GitHub nightly job has not been run against these uncommitted changes.

The original failed matrix remains under `/tmp/xsl3-p7-linux-probe/artifacts/nightly-portable/`, with explicit
termination reasons for its over-budget merge and superseded old fork child. A scratch-only diagnostic compared
merge heap growth, an explicit forced-collection ablation and a provenance-storage ablation. Those diagnostic
builds are not budget-gate artifacts. The final gate uses the repository implementation, original oracles,
normal globalization and the approved absolute limit. The 20 MiB push budget, remaining conformance/runtime
paths and streaming throughput/latency requirements remain open.


After function-path order and scoped descendant pruning, the corrected-source Linux arm64 NativeAOT matrix
again passes all 21 children at **2 GiB / 200 MiB**. The seven measured streams consume their full exact inputs
and validate their oracles; their highest full-lifetime RSS is **30,597,120 bytes (29.180 MiB)**. The source
snapshot matches current production files and both native host-evidence flags are false. The exact
**200 MiB / 20 MiB** rerun remains red for all seven workloads, including two warmup-only failures. Raw reports
are `/tmp/xsl3-p7-function-path-final-linux/artifacts/{nightly,push}/report.json`; the corresponding build log
and explicit managed-intermediate rejection are retained beside them. The earlier superseded pruning build
has an incomplete 17-child checkpoint and is excluded. See the
[function-path and pruning evidence](plan/p7-streaming.md#s4-function-path-order-and-population-scoped-pruning).
No remote CI execution or complete streaming feature claim follows from these local results.


## Ancestor-set workloads and corrected ownership

The default matrix now includes `climbing-union` and `climbing-intersection`: differently filtered row
populations project repeated ancestor attributes. Expected IDs come from the generated row fields, and
both tree and forward runs must match the independent fingerprints. These are benchmark fixtures, with
no runtime data-source role. The shared source generator produces eighteen saved fixtures across nine
workloads; no dynamic stylesheet compilation occurs in the native consumer.

The corrected-source local Linux arm64 NativeAOT **2 GiB / 200 MiB** matrix passes all **27 children**.
All nine large-input streams consume their exact input and peak below **30 MiB RSS**, with the effective
GC cap verified. The two new streams peak at 30,392,320 and 30,523,392 bytes and emit after 53,248 and
204,800 input bytes respectively. Their 10 MiB tree/stream baselines match. These timings were collected
alongside regression processes and do not establish the throughput exit. The fresh report is
`/tmp/xsl3-p7-climbing-owned-final-linux/artifacts/nightly/report.json`; production snapshot hashes match
in `source-check.json`.

An earlier run under `/tmp/xsl3-p7-climbing-final-linux/` was superseded after a foreign grounded-document
retention regression was reproduced. Its 20-child checkpoint is incomplete and is not a gate pass.
The corrected state releases that ordering map on completion/failure; frame facts do not retain a history
of discarded source nodes. See [ancestor-set execution and lifetime evidence](plan/p7-streaming.md#s4-bounded-ancestor-union-and-intersection).

The separate exact **200 MiB / 20 MiB** rerun is complete with **27 children and nine failures**. Identity
and source-document consume their full measured input before failing the absolute RSS limit; the other
seven exceed the limit during warmup. No budget or accounting method was relaxed. The report is
`/tmp/xsl3-p7-climbing-owned-final-linux/artifacts/push/report.json`; its controller and enclosing Docker
command exit 1. The passing nightly matrix does not close this push-budget requirement.


## Incremental type-check workloads

The default matrix now contains **eleven workloads / twenty-two saved fixtures**, including `type-assertion`
and `type-inspection`. The assertion emits each generated row ID through a positive named-node type contract;
inspection checks the complete selected row sequence. Expected IDs and the existence result derive from
the generated fixture records and shape. The first-output check covers assertion; inspection needs the
whole sequence before its final boolean can be known.

The fresh local Linux arm64 NativeAOT **2 GiB / 200 MiB** matrix passes **all 33 children**. All eleven
measured streams consume exact input, validate independent fingerprints and peak below **30 MiB RSS**.
The new assertion/inspection streams peak at 26,427,392 / 26,247,168 bytes. Assertion emits after reading
36,864 bytes; inspection emits at end-of-input. Their 10 MiB tree/stream baselines agree. The effective
measured GC cap and both native flags are verified. Timings were collected alongside regression processes
and do not establish the throughput exit. The report is
`/tmp/xsl3-p7-types-final-linux/artifacts/nightly/report.json`; its production snapshot matches the worktree
in `source-check.json`.

The exact **200 MiB / 20 MiB** type-check increment rerun is complete with **33 children / eleven failures**.
Identity, source-document, type assertion and type inspection consume their full measured inputs before
exceeding RSS. The other seven exceed the limit during warmup. The report is
`/tmp/xsl3-p7-types-final-linux/artifacts/push/report.json`; controller and enclosing Docker exit 1.
The nightly pass does not close this push-budget requirement.

## Selected-copy workload

The current default matrix contains **twelve workloads / twenty-four saved fixtures**. `copy-select`
copies the selected document element and transforms its rows, sharing identity's generated input and
independent output oracle. Its early-output regression detects buffering introduced by selected-copy
cardinality checking or result inference.

The fresh local Linux arm64 NativeAOT **2 GiB / 200 MiB** matrix passes all **36 children**. Selected-copy
consumes the exact input, emits after 8,192 input bytes and peaks at **29,503,488 bytes** full-lifetime RSS.
The exact **200 MiB / 20 MiB** matrix retains all 36 children and has **twelve measured failures**; selected-copy
validates its complete input/output but peaks at **25,395,200 bytes**. Startup and warmup remain included.
Normal ICU and both native-evidence checks remain enabled. Timings are diagnostic because functional
checks ran concurrently. See [the full tables and provenance](streaming-copy-select-2026-10-24.md#exact-native-memory-matrices).


## Conditional-content workload

The current default matrix has **thirteen workloads / twenty-six saved fixtures**. `conditional-content`
emits a conditional header, selected populated source rows and a conditional footer. Its oracle derives
expected row bytes from the generated records, excluding the unselected trailing source padding.

The corrected-source local Linux arm64 NativeAOT **2 GiB / 200 MiB** matrix passes all **39 children**.
Conditional-content validates full input/output, emits after 8,192 input bytes and peaks at **29,777,920
bytes** full-lifetime RSS. The separate exact **200 MiB / 20 MiB** matrix retains 39 children and thirteen
measured failures; conditional-content validates its full input/output but peaks at **26,013,696 bytes**.
Normal ICU, native-evidence checks, startup and warmup are retained. Timings are diagnostic because
functional checks overlap part of the matrix. See [all tables and provenance](streaming-conditional-content-2026-10-24.md#corrected-source-exact-native-memory-matrices).
The exact push-budget and full Phase 7 requirements remain open.

## Typed template-argument workload, 2026-10-24

The shared probe now has fourteen workloads and twenty-eight statically referenced saved entry points.
`template-parameter` selects each generated row's child `value`, checks it as `element(value)`, and passes
it to an `xs:integer` parameter on a context-absent named template. It emits the caller's actual position
and converted value. The independent expected hash derives from generated record IDs and source values;
production code reads the XML through the regular public APIs. Its first-output check passes before
half of the generated source is read. All thirteen prior corpus files are unchanged.

The final source passes **42 exact nightly NativeAOT children** at 2 GiB input / 200 MiB full-lifetime
RSS. Its complete **42-child exact 200 MiB / 20 MiB push matrix retains fourteen RSS failures**. Normal
ICU and both native-evidence checks remain. See [full evidence](streaming-template-parameters-2026-10-24.md).
These local arm64 results do not establish remote Linux x64 CI or the full Phase 7 exit.

Arithmetic/focus validation uses twelve new normal/debug saved macOS NativeAOT fixtures: live numeric
operands, real template numeric capture, local map/array size, scanning predicates, compatibility first
items and empty compatibility operands. Every retained compatibility probe, including the immutable
version-2 fork assembly, also passes. Publish has no trim/AOT warnings; all 396 core/configuration
hashes match. The corrected Linux fourteen-workload matrix passes 42 nightly children and retains
fourteen exact push RSS failures, with 547 matching source/controller/fixture hashes. See
[current evidence](streaming-arithmetic-focus-2026-10-24.md); the full P7 claim remains disabled.

## Generic function-parameter workload, 2026-10-24

The shared probe now has fifteen workloads and thirty saved entry points. `function-parameter` passes
each generated row's child `value` through an `item()*` relay into an absorbing function requiring
`element(value)*`. It emits the actual caller position and converted XML value; the independent oracle
derives expected values from generated source records. All fourteen preceding corpus stylesheets are
byte-identical. Corpus parity and early-output checks pass.

The final-source exact native nightly matrix passes **45 children** at 2 GiB input / 200 MiB RSS. Its
complete exact 200 MiB / 20 MiB push matrix retains **fifteen RSS failures**. Both native-evidence flags
are false, normal ICU remains enabled, and all 548 current/frozen source/controller/fixture hashes
match. Saved/debug item-parameter, fork and group fixtures also pass in macOS NativeAOT; the existing
node-returning helper ABI and immutable version-2 fork assembly remain compatible. See
[current evidence](streaming-item-parameters-2026-10-24.md). Full P7 remains active.

## Overlapping descendant values, 2026-10-25

`overlap-values` adds the sixteenth workload and two saved entry points, bringing the total to
thirty-two. Within each generated row, it atomizes all descendant elements: the `text` value overlaps
its nested `em` value. The oracle derives the output from the generated input fields; execution reads
the XML through the public APIs. The output follows the first completed row. All fifteen earlier
corpus stylesheets remain byte-identical. The runtime reuses private temporary header/character files
between windows; flat values create no files. See [implementation and evidence](streaming-value-replay-2026-10-25.md).

The value-replay source completes both sixteen-workload native matrices: **48 nightly children pass**
at exact 2 GiB input / 200 MiB RSS; the complete **48-child** exact 200 MiB / 20 MiB push matrix
retains **sixteen RSS failures**. All 553 current/frozen source/controller/fixture hashes match.
Normal ICU and full-lifetime counters remain. See [native evidence](streaming-value-replay-2026-10-25.md#final-source-exact-native-memory-matrices).


The bounded-value/merge source completes both sixteen-workload native matrices: **48 nightly
children pass** at exact 2 GiB input / 200 MiB RSS; the complete exact 200 MiB / 20 MiB push matrix
retains **sixteen failures**. All 566 current/frozen source/controller/probe/test hashes match.
Native timings are diagnostic, with normal ICU and full-lifetime counters. The separate fresh
100 MB tree/stream matrix has 96 valid trials and zero failures; throughput still fails for three
canonical workloads. See [current evidence](streaming-bounded-values-merge-2026-10-25.md).


The matching current native nightly matrix passes **48 children / zero failures** at exact 2 GiB input
and 200 MiB absolute RSS. The requested 200 MiB / 20 MiB push matrix retains **16 failures**: twelve full
input/output validations and four startup/warmup exits. The ordinary three-pair gate passes at timing
geometric mean **1.004693365582**, maximum allocation ratio **1.037238560580**. All **96** paired 100 MB
process trials validate under equal 4 GiB guards; canonical throughput still fails on identity, fork
and source-document. These requirements and the full Phase 7 goal remain open. See the
[current complete evidence](streaming-relative-values-signatures-2026-10-25.md).


The final comma/content native nightly matrix passes **48 children / zero failures** at exact 2 GiB
input / 200 MiB absolute RSS. The requested 200 MiB / 20 MiB push matrix retains **16 failures** (twelve
full-input validations and four startup/warmup exits). The ordinary three-pair gate passes at timing
geometric mean **1.024354047451**, maximum allocation ratio **1.037242922079**. All **96** paired 100 MB
trials validate; the canonical throughput target still fails on identity, fork and source-document.
The same-host 1 MB / 1 GB sweep has completed: **84 trials, 72 valid, 12 failed**. All streamed trials
pass; three tree trials each for adjacent, accumulator, merge and fork at 1 GB exhaust the unchanged
4 GiB heap guard. All three planned sizes have been attempted, with those four large-input comparisons
unavailable. Full P7 remains active. See
[current evidence](streaming-comma-content-2026-10-25.md).


The recursive-function follow-up passes normal/debug focused NativeAOT runs at exact 200 MiB and
2 GiB input, with independently checked output digests, first output after 356,352 bytes and lifetime
RSS near 30 MB under a focused 200 MiB guard. This does not satisfy the 20 MiB push requirement. The
full goal and the existing throughput/RSS gates remain open. See
[recursive-function evidence](streaming-recursive-functions-2026-10-25.md).


### Document fragment checkpoint, 2026-10-25

The final isolated ordinary three-pair gate passes: time geometric mean **0.990513413537**, maximum
allocation ratio **1.037238717165**. Focused normal/debug native fragment probes validate exact 200 MiB
and 2 GiB inputs, independent output digests, early output and owned-source closure, with lifetime
RSS **24,576,000–27,377,664 bytes** under the 200 MiB guard. These do not satisfy the separate 20 MiB
push RSS requirement or canonical throughput target. Current source hashes and artifacts match;
full P7 remains active. See [fragment evidence](streaming-document-fragments-2026-10-25.md).


### Navigator ID provenance checkpoint, 2026-10-25

The current isolated ordinary gate passes: time geometric mean **0.977000839111**, maximum allocation ratio
**1.037247283614**. Four focused normal/debug native fragment runs validate exact 200 MiB and 2 GiB inputs,
independent output digests, early output and ownership, at lifetime RSS **24,805,376–27,475,968 bytes** under the
200 MiB guard. This does not satisfy the separate 20 MiB push requirement or canonical throughput target.
The frozen source/config manifest has **407** verified hashes. Full P7 remains active. See
[the current evidence](streaming-id-provenance-2026-10-25.md).


### Deferred header storage checkpoint, 2026-10-25

The final corrected-source ordinary gate passes: time geometric mean **1.005276705514**, maximum allocation
ratio **1.037242922079**. Retained normal/debug native fragment probes pass at exact 200 MiB
and 2 GiB input, with lifetime RSS **24,313,856–27,443,200 bytes** under the 200 MiB guard. The separate 20 MiB
push RSS and canonical throughput requirements remain open. These historical probes do not execute deferred
climbing except. Compiler integration and its memory proof were still required at that checkpoint;
the subsequent deferred climbing checkpoint below supplies those focused results. The native primitive
probe checks 100,000 closed headers, metadata, cache bounds and cleanup. All **411** source/configuration
hashes match. See [current evidence](streaming-deferred-headers-2026-10-25.md).

### Deferred climbing execution checkpoint, 2026-10-25

Actual deferred climbing `except` now passes normal/debug NativeAOT runs at exact **209,715,200**
and **2,147,483,648** input bytes. Every run consumes its full source, validates an independently
computed output digest, emits after **69,632** input bytes and closes its owned source. Lifetime
RSS, measured as the maximum of outer and child peaks without subtraction, ranges from **30048256**
to **30572544** bytes under the unchanged **209,715,200-byte** guard. ICU remains enabled.
This does not satisfy the separate push RSS or canonical throughput requirements.

The ordinary three-pair gate passes with time GM **1.027012847257** over **39** timed rows and maximum
allocation ratio **1.037238717165**. Individual timing outliers remain recorded. Both native publications
have no warnings; the new saved normal/debug cases and retained compatibility consumers pass. All
**412** live/frozen source hashes and **95** retained saved-DLL hashes match. Full P7 remains active.
See [the implementation and complete evidence](streaming-deferred-climbing-2026-10-25.md).

### Deferred accumulator phase checkpoint, 2026-10-25

Source-owned scratch storage now carries accumulator phase snapshots for deferred climbing headers,
including late publication before source close and fork readers. Scalars and map/array structure spill;
node/opaque function identities are retained, so general bounded storage for those histories remains open.

Actual deferred-set/scalar-counter normal/debug native children pass at exact **209,715,200** and
**2,147,483,648** input bytes. Each consumes the full source, independently verifies IDs plus counter
values, emits before EOF and closes its owned source. Lifetime peak RSS ranges from
**31309824** to **31621120** bytes, taking the maximum of outer and child peaks without
subtraction under the unchanged **209,715,200-byte** guard. ICU remains enabled. These measurements do
not satisfy the separate push RSS, canonical throughput or arbitrary identity-history requirements.

The isolated ordinary three-pair gate passes: **39** timed rows, time GM **0.989223741081**, maximum
allocation ratio **1.037234042553**, with individual outliers retained. Both native publications are
warning-free. All **413** frozen/live source hashes and **103** retained saved DLLs match after execution.
Full P7 remains active. See [complete evidence](streaming-accumulator-phases-2026-10-25.md).

### Compiled closure capture checkpoint, 2026-10-25

Actual deferred-set/compiled-closure normal/debug native runs pass exact **209,715,200** and
**2,147,483,648** input bytes. Each closure captures a map containing an array of the numeric source
ID; output verification independently checks each selected ID and invoked closure value. Lifetime
peak RSS ranges from **31,260,672** to **31,653,888** bytes, without subtraction, under the unchanged
**209,715,200-byte** heap/RSS guard with ordinary ICU support. First output follows **57,344** input
bytes in each case. These fixed-owner cases do not prove general opaque/node history spill, exact
push RSS or canonical throughput. Candidate compilation overlaps part of the matrix, so elapsed
times remain diagnostic.

The isolated original ordinary three-pair gate passes over **39** timed rows: time GM
**0.986027868023**, maximum allocation ratio **1.037238403996**. Individual timing outliers remain
recorded. Both native publications have no warnings; all **413** frozen/live source hashes,
**111** retained saved DLLs, protected v2 fork and native artifact hashes match after execution.
Full P7 remains active.
See [the complete checkpoint](streaming-closure-captures-2026-10-25.md).

### Coerced function capture checkpoint, 2026-10-25

Actual declared-function accumulator normal/debug native children pass exact **209,715,200** and
**2,147,483,648** input bytes. The engine creates a coercion wrapper around each map-capturing
compiled function. Each child consumes the full source, independently verifies output, emits at
**57,344** input bytes and closes its owned source. Lifetime peak RSS is **30,212,096–31,834,112** bytes,
without subtraction, under the unchanged **209,715,200-byte** guard with ordinary ICU support.
These fixed-model cases do not close opaque/node history, exact push RSS or canonical throughput.

The first timing gate fails during a change also visible in unchanged-base measurements; its raw
rounds remain preserved. Reversed-order frozen-core attribution does not reproduce the large penalty.
An unchanged original three-pair retry passes over **39** timed rows: GM **1.035572890616**, maximum
allocation ratio **1.037242608875**, with individual timing outliers retained. All native publications
are warning-free; all **413** live/frozen source hashes, **117** retained saved DLLs, protected v2 fork
and artifact hashes match after the matrix. Child times remain diagnostic because isolated follow-on
work overlaps part of the memory matrix. Full P7 remains active.
See [the complete checkpoint](streaming-coerced-captures-2026-10-25.md).

### Library function capture checkpoint, 2026-10-25

Actual computed `function-lookup()` accumulator normal/debug native children pass exact
**209,715,200** and **2,147,483,648** input bytes. The caller supplies the required lookup-name
parameter through the public API; each rule creates a library-function value. All children consume
the full source, independently verify output, emit at **57,344** input bytes and close their owned
source. Lifetime peak RSS is **31,752,192–31,866,880** bytes, without subtraction, under the
unchanged **209,715,200-byte** guard with ordinary ICU. Follow-on prototype work overlaps part of
the matrix, so elapsed times remain diagnostic. General opaque/native/node histories, exact push
RSS and canonical throughput remain open.

The unchanged original three-pair ordinary gate passes on its first attempt over **39** timed rows:
GM **0.994364503290**, maximum allocation ratio **1.037238717165**. Individual timing outliers and
all raw measurements remain in the report. Native publications have no warnings; all **413** source
hashes, **133** retained saved DLLs, the protected v2 fork and artifact/consumer-source manifests
match after the matrix. Full Phase 7 remains active.
See [the complete checkpoint](streaming-library-captures-2026-10-25.md).

### Random-generator capture checkpoint, 2026-10-25

Actual per-row seeded random-generator map accumulators pass normal/debug native runs at exact
**209,715,200** and **2,147,483,648** input bytes. Both callables carry a distinct actual numeric state
for each source ID. Recorded permutation determines the output token order, and an independent
algorithm computes the expected output from source IDs. That verifier also passes against the
preserved preceding core. All children consume the full source, independently verify output, emit
at **57,344** input bytes and close their owned source. Lifetime peak RSS is
**31,260,672–31,801,344** bytes, without subtraction, under the unchanged **209,715,200-byte**
guard with ordinary ICU. These results do not close exact push RSS or canonical throughput.

The original ordinary three-pair gate passes over **39** timed rows: GM
**0.994987004211**, maximum allocation ratio **1.037239186928**.
Raw rounds and individual outliers remain in the report. Native publications have no warnings;
all **413** source hashes, **141** retained and six preceding-core random saved DLLs, protected v2 fork,
generator-source and artifact/consumer-source manifests match after the matrix. Full P7 remains active.
See [the complete checkpoint](streaming-random-captures-2026-10-25.md).


### Optional-focus function values under spill

A separate native fixture creates an accumulator reference with actual absent global focus, spills fresh partial functions per row, and invokes them after deferred climbing selection. Only the prescribed XTDE3350 catch emits source-derived IDs, which an independent hasher verifies. Normal/debug exact 200 MiB and 2 GiB children pass with first output at 57,344 input bytes and unchanged limits. The original mixed-result try variant buffered to EOF and remains a preserved failure; explicit per-record text construction does not close that general gap. See [the complete checkpoint](streaming-optional-focus-2026-10-26.md).


### XML-to-JSON header-path continuation, 2026-10-26

Eligible filtered descendant/child arguments now preserve continuation while conversion absorbs a selected subtree. Motionless prefix predicates run once, real additional matches produce XPTY0004, and predicate faults retain precedence. Consumed input still requires actual replay. Full Release/Debug pass **3,404 / 3,290**; all nine profile failure-ID sets are unchanged, with strict unmatched codes improving **5 to 4**. Retained/current native saved consumers, the original ordinary gate and four final normal/debug exact-size native JSON cases pass. Unsupported paths, mixed-result try, exact push RSS and full P7 requirements remain open. See [the complete checkpoint](streaming-json-header-continuation-2026-10-26.md).

### Mixed template result streaming, 2026-10-26

The original mixed-result try fixture is retained without an outer `value-of` workaround. Its corrected normal/debug native prechecks verify full input, an independent expected-ID digest, early output and source ownership. The corrected cohort freezes **414** core/configuration files and hashes producers, consumers, artifacts and all actual saved-reference paths. See [the exact-size runs and status](streaming-mixed-template-results-2026-10-26.md).

### Reference and diagnostic contracts, 2026-10-26

The current diagnostic cohort verifies **213** retained plus **12** fresh saved styles and compatibility helpers in NativeAOT, with warning-free publications. All **414** live/frozen core hashes, four auxiliary hashes, saved references and artifact/consumer/producer manifests match after the ordinary gate. Earlier exact-size memory evidence remains attached to its own frozen cohort. See [implementation and evidence](streaming-reference-diagnostics-2026-10-26.md).

### Source validation negotiation, 2026-10-26

Explicit streamed validation now negotiates bytes from capable resolvers, preserving reader-only and provider-error contracts. Release/Debug pass **3,449 / 3,331**; strict unmatched codes improve **1 to 0**, with unchanged failure-ID sets. Native runtime validation, **225** retained saved styles and the original ordinary performance gate pass. Imported-schema saved compilation and full P7 gates remain open. See [implementation and evidence](streaming-source-validation-negotiation-2026-10-26.md).

### Annotation and whitespace ordering, 2026-10-26

Streamed whitespace filtering now uses the effective annotation-stripping policy, while preserving explicit validation and xml:space behavior. Release/Debug pass **3,457 / 3,339**; all nine profile headings and failure-ID sets remain unchanged. Native checks pass **12** new normal/debug saved styles plus **225** retained styles. The original ordinary performance gate passes; imported-schema persistence and full P7 gates remain open. See [implementation and evidence](streaming-annotation-whitespace-2026-10-26.md).

### Saved imported-schema validation, 2026-10-26

Resolved schema graphs now travel with supported saved source-validation paths. Release/Debug pass **3,486 / 3,358**, with unchanged profile headings and failure-ID sets. Native checks pass **12** new imported-schema saved styles plus **237** retained styles; the ordinary gate passes. General schema-dependent constant metadata, typed static nodes, .NET 10-to-8 saved-DLL portability and full P7 gates remain open. See [implementation and evidence](streaming-saved-schema-2026-10-26.md).

### Portable saved framework references, 2026-10-26

Saved framework references now use the core project's minimum supported runtime contract. Release/Debug pass **3,490 / 3,358**, with unchanged profile headings and failure-ID sets. Fourteen fresh portable emissions pass on .NET 8 and NativeAOT alongside **249** retained native styles. The **311**-path portable-copy corpus passes on .NET 8 and .NET 10. The ordinary gate passes; general schema metadata, typed static nodes and full P7 gates remain open. See [implementation and evidence](streaming-portable-emitter-2026-10-26.md).

### Saved schema-dependent metadata, 2026-10-26

Saved constants now retain registered schema contexts, named atomic/cast metadata and recursive sequence/function/node tests using table-scoped format 5. Release/Debug pass **3,505 / 3,360**, with unchanged conformance profile headings and failure-ID sets. Fourteen fresh saved styles pass with **263** retained styles under NativeAOT; the **325**-path portable-copy corpus passes on .NET 8 and .NET 10. The ordinary gate passes. Typed static nodes, anonymous atomic metadata and full P7 remain open. See [implementation and evidence](streaming-schema-metadata-2026-10-26.md).

### Typed static-node persistence, 2026-10-26

Annotated static nodes now retain schema types, shared-tree identity, nilled state and node metadata through format 6. Nilled simple-type atomization and optional cardinality checks are corrected. Release/Debug pass **3,519 / 3,372**, with unchanged profile headings and failure-ID sets. Two fresh saved styles pass with **277** retained styles under NativeAOT; the **327**-path portable-copy corpus passes on .NET 8 and .NET 10. The ordinary gate passes. Anonymous atomic constants, unowned schema contexts and full P7 remain open. See [implementation and evidence](streaming-typed-static-nodes-2026-10-26.md).
