# P7 producer allocation investigation

Current branch: `feature/p7-producer-allocation`, based on contract head `78815e8`. An isolated compact
producer candidate is under qualification; it is not yet accepted for the full P7 gates.
The [reclamation-lock control](p7-tee-reclamation-experiment.md) is rejected and
excluded from this branch; its source survives separately at `ce42373` for audit.

## Fresh allocation evidence, 5 November 2026

The preserved baseline benchmark assembly ran the canonical 1,000,000,000-byte streamed fork workload
under `dotnet-trace` 10.0.750501's `gc-verbose` profile, with four effective processors, tiering disabled and
the actual 4,294,967,296-byte managed heap guard. All 8,695,652 records were processed, output byte counts
and hashes matched the independent oracle, and the EventPipe parser reported zero lost events.

| Allocation-tick type name | Sampled bytes | Share |
|---|---:|---:|
| `Node` | 19,541,727,080 | 62.22% |
| `ForkEvent` | 3,614,358,048 | 11.51% |
| `System.Xml.XPath.XPathItem[]` | 1,879,401,400 | 5.98% |
| `SimpleContentState` | 1,872,555,472 | 5.96% |
| `Xsl3.Streaming.StreamedNavigator` | 1,870,982,272 | 5.96% |
| `System.String` | 1,384,230,184 | 4.41% |
| `Attribute[]` | 1,231,139,208 | 3.92% |

Total sampled allocation amounts: 31,405,373,480 bytes. These are allocation-tick estimates attributed to
sampled type names, not exact per-type object counts. They include startup, warmup and oracle work;
the instrumented elapsed time is not a throughput acceptance result. Confirm the short nested type names
and producer/consumer allocation sites before attributing the entire `Node` share to a single path.

Evidence is under `/private/tmp/xsl3-p7-fork-profile-20261105/`: `fork-allocation-baseline.nettrace`,
`fork-allocation-baseline.log`, `allocation-types.json`, and the temporary `trace-reader/` utility source.
The runtime SHA-256 is `60ef17ce25517b7f32d2a8f50df3a6477f03efea4e3e36123f972b7f054ed049`;
the unchanged binaries are in `baseline/`. Temporary artifacts require preservation or rerun if unavailable.
Rebuild ordinary checkout outputs before using them; the preserved `baseline/` is the unchanged control.

The completed stack parser (`allocation-node-sites.json`, zero lost events) resolves the short `Node`
name to `StreamedDocument` frames. `Child → ReadNextCore → ForkSource.ReadProjected` accounts for
12,841,504,448 sampled bytes. Fork consumers account for the remaining 6,700,222,632 through
`ForkHeader.Create → CreateFresh → ReadForkEvent`, split among `Next`, `FirstChild` and `StringValue`.
This supports reducing producer-frame allocations first, while retaining consumer navigation semantics.

## Isolated candidate

Only the compact producer, immutable fork metadata, shared reader-header/text helpers and their tests
were brought forward from `b2316fd`. The bridge addition is solely an exact stock-reader type check;
the historical bridge rewrites, startup work and public API additions are excluded. The source frames
are reusable stack entries; published events own immutable metadata and namespace scopes. Early exit
restores ordinary frames before subsequent source consumption. Custom readers, validation, DTD input,
observation/replay state and NativeAOT retain the ordinary producer. This is an implementation choice,
not a relaxation of any correctness, supported-input, memory or throughput requirement.

The candidate remains unaccepted until affected Release/Debug contracts, paired uninstrumented timings,
and the original hosted qualification establish the benefit and safety. Evidence for this attempt is
under `/private/tmp/xsl3-p7-producer-allocation-20261105/`.

Local correctness evidence: the affected Release suite passed 410 .NET 10 and 370 .NET 8 checks.
After adding failure/cancellation and dynamic header-observation continuation coverage, Debug passed
417/377 checks and the final focused Release suite passed 20/20 checks. The first observer test assumed
finishing the document element always advances to reader EOF; that assertion failed on both frameworks.
The corrected contract explicitly finishes the root before checking EOF, as the existing continuation
tests do. The failed run is retained in `additional/`; final results are in `additional-final/`.

The first candidate (`935196e`, runtime SHA-256
`c955220cf9240451a9784f243f098a07533556f817b1317211f257ea0373b4a3`) ran twelve valid paired local
100 MB fork observations. Baseline tree/stream medians were 1,623.548/2,344.946 ms; candidate medians
were 1,632.658/2,336.340 ms. The streamed improvement was only 0.37%, with overlapping ranges:
**no meaningful throughput benefit established**, and neither variant met the original target.
The complete observations, fingerprints and independent checks are in `paired-review.json` and
the six `baseline-round-*`/`compact-round-*` directories. Other local workloads were present;
these timings are diagnostic and cannot establish hosted acceptance.

A fresh 1 GB allocation trace nevertheless confirms that the candidate was active. Sampled total
allocations fell from 31,405,373,480 to 20,579,868,104 bytes; sampled `Node` allocations fell to
6,844,922,104 bytes, all attributed to consumer `ForkHeader.Create` stacks. No producer `Child`
allocation ticks remained. Both traces report zero lost events and valid canonical output. This is
allocation evidence, not a retained-memory or throughput pass. A subsequent isolated control removes
virtual compact-metadata accessors while preserving the same immutable header ownership.
Its affected Release and Debug suites each passed 417/377 checks (1,588 total). Twelve further valid local observations gave
baseline tree/stream medians of 1,598.086/2,172.507 ms and candidate medians of 1,713.946/2,124.957 ms.
The 2.19% streamed improvement still has overlapping ranges, and the tree control also drifted:
**throughput benefit remains inconclusive**, with the original 100 MB target still failed.
The source patch, its hash, exact binaries and all observations are retained in `direct-comparison/`.
Do not merge either variant based on allocation reduction or these local timings alone.

Next investigate consumer-side frame creation and per-item output construction under profiling. The
first allocation trace places the surviving frame allocations in `ForkHeader.Create`; `SimpleContentState`
and single-item arrays are separate measured contributors. Preserve both candidate binaries so future
controls can distinguish producer-only and composed effects. No candidate PR has been opened; full
hosted acceptance, the supported-streaming claim and release readiness remain unproven.

## Next implementation boundary

Investigate reducing source and fork-view frame allocation while preserving navigator identity, immutable
published metadata, namespace/schema data, bounded retention, observer/replay behavior, cancellation and
reader ownership. The earlier isolated compact producer at `b2316fd` is a reference for review, **not an
approved implementation**. Do not cherry-pick that combined commit: it also includes startup, bridge and
public API changes outside this experiment. Its historical failed memory and incomplete performance
qualification remain in the completion log.

Any candidate must first pass affected correctness/lifetime regressions and demonstrate a meaningful
paired uninstrumented benefit against the preserved baseline. Then qualify exact reviewed source through
the original hosted memory and throughput matrices and full compatibility gates. No new storage budget,
workload-specific result, omitted observation, unsupported API claim or acceptance waiver is authorized.
The other open P7 obligations (internal grounding, fork group/sort retention and final integration/release)
remain part of the active full objective.
