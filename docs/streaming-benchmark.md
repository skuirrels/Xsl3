# Streaming process benchmark

The development streaming set lives in `bench/corpus/xslt30/micro/streaming`. Its seven canonical
workloads exercise identity copying with a changed attribute, adjacent grouping, an accumulator total,
an iterate balance, two streamed merge inputs, two fork result documents and source-document processing.
Ancestor union/intersection, type assertion/inspection, selected-element copying and conditional content add six regression workloads. The checked-in
stylesheets are generated from `tests/Xsl3.Tests/StreamingProbe.Stylesheets.cs`; a unit test checks exact
agreement. Synthetic input fields and independent output oracles are test fixtures, never engine data.

Build and run from the repository root:

```sh
dotnet build bench/Xsl3.Benchmarks -c Release -f net10.0 -m:1
python3 eng/streaming-benchmark.py \
  --assembly bench/Xsl3.Benchmarks/bin/Release/net10.0/Xsl3.Benchmarks.dll \
  --sizes 1mb 100mb 1gb --rounds 3 --heap-limit 4294967296 \
  --timeout-seconds 900 --require-throughput-target \
  --output /tmp/xsl3-streaming-benchmark-new
```

The output directory must be new. MB and GB here are decimal: exactly 1,000,000, 100,000,000 and
1,000,000,000 generated input bytes. The memory gates use their separately stated binary MiB/GiB sizes.
`--workloads` selects a subset or any of the thirteen available workloads; `--sizes` also accepts exact
byte counts. A subset cannot satisfy the canonical 100 MB target. The default is all seven canonical
workloads, three pairs per size, a 4 GiB managed heap guard on both modes and a 900-second child timeout.
The heap guard is configurable, and zero explicitly disables it. It is a benchmark resource guard,
not a relaxation of either approved memory gate. Run without competing CPU jobs.

Each child compiles through the public `XsltCompiler` API and executes through `Xslt30Transformer`,
including its public resolver and secondary-result callbacks. `XSL3_STREAM_STRICT` selects tree or stream
for that isolated child's lifetime, including any compilation deferred until execution. There is no test
assembly dependency. The shared input, SHA-256 sinks and independent output oracles are also used by the
[process-memory probe](streaming-probe.md). This keeps the correctness contract identical across hosts.

The controller alternates the first mode between pairs. Compilation, a small warmup, final collection
and oracle generation are outside the measured execution interval. That interval includes generated source
reading, XML parsing, transformation, serialization and hashing of output delivered to the caller. The tree
side therefore includes tree construction. It does not compare a prebuilt tree against streaming input.
Both sides use tiered compilation disabled and the same explicit managed heap guard. The effective allowance
reported by the runtime must agree across modes and repetitions. Every child verifies exact input consumption
and independent expected hashes; the controller also requires matching output fingerprints and record counts.

`report.json` retains every observation, child command, exit code, timeout, stdout/stderr path, measured
execution/first-output timings, first-output input position and full-lifetime RSS. It records runtime and
architecture metadata, configuration, engine/artifact/controller hashes. Atomic checkpoints have
`complete: false` until every planned trial is terminal. A complete matrix may contain failed trials.
`summary.md` contains side-by-side medians, ranges in JSON, MiB/s and a Winner column. The 5% winner band is
descriptive; three observations do not establish statistical significance. Missing, failed, malformed,
inconsistent or timed-out children receive no ratio or winner. Raw logs remain available for diagnosis.

The exact P7 throughput target requires all seven canonical 100 MB comparisons to have at least three
valid observations per mode and a streaming median no greater than the tree-plus-transform median. The
5% descriptive band does not weaken this requirement. `--require-throughput-target` makes incomplete
coverage or a slower result fail. Any failed child always makes the controller exit nonzero. The nightly
workflow runs the full matrix and uploads evidence even on failure; local results do not prove remote CI.

First output is an actual sink write, including secondary outputs. Incremental-emission workloads must be
assessed across sizes using both milliseconds and input bytes consumed before emission. Accumulator and
iterate totals, and the optional type-inspection result, require end-of-input. Their first-output latency
grows with input size by construction. The controller reports these observations; it does not invent an
acceptable latency threshold or claim that the complete P7 latency exit passes.

The controller also accepts `--native <saved-probe-executable>`, using the same saved NativeAOT probe as the
memory gate. Native trials must prove that dynamic code is unsupported and that trusted platform assemblies
are absent. There is no JIT-to-native substitution. Timing comparisons should use one artifact/runtime for
both modes; do not combine managed and NativeAOT observations in one row.

Full-lifetime RSS comes from Unix `wait4`, including compilation, warmup, serialization of the report and
teardown. Linux counters are converted from KiB to bytes; Darwin counters are already bytes. This is not
baseline-subtracted and does not replace the child counter in the memory gate. The controller requires
Linux or macOS. Failed/OOM tree trials remain failures even if streaming completes under the same heap guard.

Controller regression tests run in CI:

```sh
PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s eng/tests -p 'test_streaming_*.py'
```

The ordinary `micro` verb remains the existing in-memory micro suite. `streaming-child` and the paired
controller provide the different input lifecycle required for this streaming set. `eng/perf-gate.sh` sets
`EnableStreamingProcessBenchmarks=false` on both sides, excluding the separate reader-based process host
when building against commits predating that API. Normal builds default to true. Both sides still build
and execute the same current ordinary micro harness. Failed builds print their captured diagnostics.
Regenerate corpus files with `dotnet .../Xsl3.Benchmarks.dll streaming-corpus <new-directory>`, inspect them,
then replace the checked-in `.xsl` files. Existing directories are refused to preserve edits.


The [2026-10-24 local matrix](streaming-benchmark-2026-10-24.md) is complete with 126 trials, all 63
streamed outputs valid and fifteen failed tree trials. It leaves the exact throughput target incomplete;
three valid 100 MB comparisons are slower in streaming. It is evidence of current gaps, not an S7 pass.


The subsequent node-only copy-content / namespace-scope runtime fixes have a fresh
[complete matrix](streaming-copy-content-2026-10-24.md#final-full-paired-process-matrix): 126 trials,
63 validated streams and 15 failed children. The exact throughput target remains incomplete.
The earlier report preserves the earlier artifact's measurements. Neither report establishes a push-budget
or full Phase 7 pass.

The selected-copy increment adds an eighth explicit workload to its [complete local matrix](streaming-copy-select-2026-10-24.md#final-full-paired-process-matrix): **144 trials**, **72 validated streams**, **57 validated tree trials** and **15 tree OOM failures**. Selected-copy emits after 8,192 input bytes at all three sizes but is slower than tree in the 100 MB comparison. The canonical seven-workload throughput target remains incomplete; no latency or exact push-memory pass is inferred.

The subsequent [conditional-content matrix](streaming-conditional-content-2026-10-24.md#final-full-paired-process-matrix)
is complete at **162 trials** across the canonical seven plus selected-copy and conditional-content:
**81 valid streams**, **63 valid tree trials** and **18 tree OOM failures**. The new workload emits after
8,192 input bytes at all three sizes, but its 100 MB streaming median is 1.108109 times tree. Its 1 GB
tree trials fail and receive no ratio/winner. The exact canonical throughput target remains incomplete.

## Node-predicate/prepared-call checkpoint, 2026-10-24

The current-source follow-up measures decimal **100 MB only** across the canonical seven workloads
plus selected-copy and conditional-content, with three fresh alternating pairs and equal 4 GiB heaps.
All **54 trials** terminate: **27 valid streams**, **24 valid trees**, **three tree merge OOM failures**.
The canonical throughput target remains incomplete. A failed comparison has no ratio or winner; the
conditional-content ratio of **1.047953** is within the controller's 5% no-meaningful-winner band.
See [complete comparisons, first-output results and hashes](streaming-node-predicates-2026-10-24.md#isolated-100-mb-process-comparison).
The preceding 162-trial three-size matrix remains evidence for its own frozen source. This follow-up
does not replace that coverage or establish a full latency/throughput exit.

## Template-parameter checkpoint, 2026-10-24

The shared fixture now has fourteen workloads and twenty-eight saved entry points. The added
`template-parameter` workload checks a child node argument, converts it in a context-absent named
template and emits the caller's position. Its oracle is independent of the transform; all thirteen
previous corpus files are unchanged. The canonical set remains seven workloads.

The final-source 100 MB follow-up completes **60 trials**, three alternating pairs over the canonical
seven plus selected-copy, conditional-content and template-parameter. It retains **30 valid streams**,
**27 valid trees** and **three tree merge OOM failures** under equal 4 GiB heaps. The canonical target
remains incomplete. The new workload's stream/tree ratio is **0.898250**, an observed median comparison.
See [all comparisons, first output and hashes](streaming-template-parameters-2026-10-24.md#isolated-100-mb-process-comparison).
No ratio or winner is assigned to merge. Earlier three-size evidence remains tied to its own source;
this follow-up does not close every throughput or latency requirement.

The later arithmetic/focus source has a complete exact fourteen-workload native matrix: **42 nightly
children pass**, and the complete exact 20 MiB push matrix retains **fourteen RSS failures**. All 547
current/frozen source/controller/fixture hashes match. Its one ordinary three-pair gate passes at
geometric mean **0.994790425361** and maximum allocation ratio **1.046024392076**, retaining the 1 KiB
regex timing outlier. No new paired streaming matrix was run for this source; preceding process
matrices remain evidence for their own snapshots. See
[current arithmetic/focus evidence](streaming-arithmetic-focus-2026-10-24.md). Full P7 remains active.

## Generic function-parameter checkpoint, 2026-10-24

The shared fixture adds `function-parameter`, bringing it to fifteen workloads and thirty saved entry
points; the canonical throughput set remains seven workloads. All fourteen preceding corpus
stylesheets are unchanged. The final exact native nightly matrix passes **45 children**; the complete
exact 20 MiB push RSS matrix retains **fifteen failures**. All 548 current/frozen source/controller/
fixture hashes match. These native elapsed/first-output values are diagnostic: no fresh paired
streaming throughput matrix was run for this source. Preceding process matrices remain evidence for
their own snapshots. See [item-parameter evidence](streaming-item-parameters-2026-10-24.md).


## Node-set, mode and overlapping-value comparison, 2026-10-25

The complete current-source 100 MB matrix has **96 trials** over sixteen workloads: **48 valid
streams**, **45 valid trees** and **three tree-merge OOM failures** with equal 4 GiB heap guards.
Copied host/engine artifacts remain unchanged. The matrix follows completed functional/NativeAOT
checks and one isolated ordinary three-pair gate. The earlier matrix was intentionally stopped
after 48 trials for a verified quadratic intersection; its partial results remain separate.
The repaired tree intersection completes all rounds at median **1,809.021667 ms**. Three canonical
stream comparisons are slower, and merge has no valid tree comparison. `overlap-values` has
stream/tree ratio **4.779024348090**. Full throughput and current-source multi-size latency exits
remain open. See [side-by-side results and evidence](streaming-set-merge-mode-2026-10-25.md#isolated-100-mb-process-comparison).


## Bounded cache and tree-merge comparison, 2026-10-25

The current-source matrix completes **96 valid trials with zero failures** over sixteen workloads
at 100 MB. Tree merge succeeds in all three rounds under the unchanged equal 4 GiB managed-heap
guard. The canonical throughput target is now fully evaluated and **fails**: identity, fork and
source-document streaming are slower. Adjacent, accumulator, iterate and merge are faster. Current
overlap-values stream/tree ratio is **2.375079889881**; it remains slower. All copied host/engine
artifacts remain unchanged. See [side-by-side results and evidence](streaming-bounded-values-merge-2026-10-25.md#isolated-100-mb-process-comparison).


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


### Optional-focus checkpoint, 2026-10-26

The corrected optional-focus source passes the original three-pair ordinary gate, and all four exact-size native spill children verify their complete output under the unchanged 200 MiB guard. Individual ordinary timing outliers and the original failed allocation/mixed-result try trials remain recorded. Memory-matrix elapsed times overlap isolated follow-on work and are diagnostic; canonical throughput and exact push RSS remain open. See [the complete checkpoint](streaming-optional-focus-2026-10-26.md).


### XML-to-JSON header-path continuation, 2026-10-26

Eligible filtered descendant/child arguments now preserve continuation while conversion absorbs a selected subtree. Motionless prefix predicates run once, real additional matches produce XPTY0004, and predicate faults retain precedence. Consumed input still requires actual replay. Full Release/Debug pass **3,404 / 3,290**; all nine profile failure-ID sets are unchanged, with strict unmatched codes improving **5 to 4**. Retained/current native saved consumers, the original ordinary gate and four final normal/debug exact-size native JSON cases pass. Unsupported paths, mixed-result try, exact push RSS and full P7 requirements remain open. See [the complete checkpoint](streaming-json-header-continuation-2026-10-26.md).

### Mixed template result streaming, 2026-10-26

The preserved mixed-try failure is now a regression fixture for eligible template pull companions, with early normal/debug native output and unchanged normalization/rollback. The corrected source, failed prototypes, final memory runs and ordinary gate are distinguished in [the checkpoint](streaming-mixed-template-results-2026-10-26.md). Concurrent stress-run elapsed times are diagnostic; canonical throughput and exact push RSS remain open.

### Reference and diagnostic contracts, 2026-10-26

The diagnostic follow-up passes its original ordinary gate at time GM **1.022631015925**, maximum allocation ratio **1.037310200701**. The report preserves individual outliers and adds no new memory-matrix or canonical-throughput claim. Full P7 remains active. See [implementation and evidence](streaming-reference-diagnostics-2026-10-26.md).

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
