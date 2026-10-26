# Deferred accumulator phases, 2026-10-25

The [compiled closure follow-up](streaming-closure-captures-2026-10-25.md) extends this checkpoint;
the measurements below describe its preceding frozen core.

This continues the [deferred climbing checkpoint](streaming-deferred-climbing-2026-10-25.md) on
`feature/p7-streaming-analysis`. Deferred ancestor headers now carry applicable accumulator snapshots
instead of rejecting every non-null observer annotation. Full Phase 7 remains active and
`supports-streaming` remains `no`.

## Value representation and lifetime

The existing source-owned header spool registers each observer's metadata codec. The accumulator
codec records nullable before/after phases separately: an absent phase and an empty sequence remain
distinct. Recording never demands an uncomputed phase or invokes an accumulator rule. Rule errors,
cycles, applicability and package ownership continue through the original observer.

`StreamedXdmValueStore` writes scalar values and map/array structure into the same scratch stream.
It preserves the actual atomic type from the compiled type model, QName namespace/local/prefix,
floating-point binary values, sequence-valued entries, empty members, container aliases and cycles.
Traversal is iterative, including 20,000-level containers. Scalar/container writes retain no value
history. Primitive lexical decoding uses the existing casting logic; it introduces no parallel
numeric, calendar, duration or schema calculation rules.

Nodes and opaque function items currently retain their actual identity in a source-owned table.
Nodes use document/position keys and cloned cursors, so moving a provider's navigator cannot change
an earlier recorded position. Callable objects keep their actual captures. **This identity path is
not a bounded spill representation for arbitrary node/closure histories.** Its memory can grow with
those values; releasing the source clears it. General disk storage of identity-bearing values and
its memory proof remain required. Scalar/container measurements cannot close that requirement.

A scratch header read while its source ancestor is still open registers weak phase readers on that
source frame. Later phase publication updates live snapshots before the source closes; discarded
snapshots do not retain a closed-frame history. Reader pruning grows its interval with live readers,
avoiding a scan of the complete live reader list for every input record. Fork views publish through
the original observer, and workers read phases without rerunning rules. A missing end-phase demand
can finish the still-open local source/prong; it never runs rules against a reconstructed header or
advances an original source directly from a fork worker.

The codec lives in the core assembly and reuses the source's existing bounded cache and private
scratch files. Its finite type/observer tables refer to the actual compiled model. The separate
saved-constant format and value-replay layout are unchanged; no public runtime entry point or saved
helper signature changes. Unknown observer metadata still raises `stream-header-metadata` explicitly.

## Reproductions and corrections

The previous frozen runtime reproduces `stream-header-metadata` for a climbing `except` result that
requests `accumulator-before()`. The fixture and output are in
`/tmp/xsl3-p7-accumulator-snapshot-before/`; its second command exits zero only after observing that
specific failure from the previous source.

Review of the first implementation reproduced a second defect on both frameworks: a header read
before its ancestor ended retained a null end phase and raised `ObjectDisposedException` after the
source finished and closed. The corrected publication path updates live snapshots as the original
phase completes. The failing evidence is `/tmp/xsl3-p7-accumulator-snapshot-late-phase-before.log`.
Earlier candidates and their native/timing logs are retained as superseded evidence, not acceptance.

The original strict analysis remains intact. In particular, an `accumulator-after()` call on a
climbing selection can be non-streamable; the phase-lifetime regression is exercised directly through
the runtime rather than weakening that rule. Several existing harness discrepancies also conflict
with the final Recommendation; no suite files or failure baselines are changed to make them pass.
The applicable rules include [axis steps](https://www.w3.org/TR/xslt-30/#streamability-of-axis-steps),
[merge sources](https://www.w3.org/TR/xslt-30/#err-XTSE3195), and
[source-document validation](https://www.w3.org/TR/xslt-30/#source-document-validation).

## Functional and native consumer evidence

The final focused Release checks pass **93 net10.0 / 88 net8.0**. Full Release and Debug each pass
**3,306 net10.0 / 3,199 net8.0** units, and the solution builds. Coverage includes modern and legacy
before-value consumers, normal/debug compilation, saved assemblies, scalar and container values,
QName prefixes, callable captures, grounded nodes, both published phases, open ancestors, fork
ownership, lazy errors, late publication after source close, collection of discarded snapshots,
value-history collection, identity-table cleanup and cancellation.

The final NativeAOT consumer passes eight new saved normal/debug styles plus all retained compatibility
cases. A separate native runtime probe verifies late publication after source close. All **103** retained
saved-DLL hashes are unchanged, including the protected v2 fork consumer. Both native publications
have no warnings. The frozen **413**-file source/configuration snapshot matches the worktree.
The main artifact manifest contains **114** files (**111** saved DLLs plus three publish files); the
scale manifest contains **5** files. Evidence is under `/tmp/xsl3-p7-accumulator-phases-final-native/`
and `/tmp/xsl3-p7-accumulator-phases-final-scale-native/`.

The final 10 MiB native diagnostic exercises a deferred climbing selection and accumulator values.
It consumes all **10,485,760** input bytes and **91,180** rows, validates its output digest, emits after
**57,344** input bytes, and closes its owned source while leaving the borrowed initial reader open.
Peak RSS is **31,457,280** bytes. It is a diagnostic under the existing 200 MiB guard, not evidence for
the separate push RSS target or for arbitrary identity-bearing value histories.

## Conformance and compatibility

Final ordinary W3C **46**, upstream **1,654** and differential **394** per framework, and all **13**
streaming controller tests pass. All eight execution profiles and QT3 parse coverage are freshly
run. Comparing all nine reports with the accepted deferred-climbing checkpoint finds no new or
removed failure IDs. Strict auditing retains exit **1** for its remaining failures. No suite or
failure-baseline files are changed.

| Profile | Passed | Failed | Not run | Unmatched expected codes |
|---|---:|---:|---:|---:|
| XSLT 3.0 | 8849 | 19 | 41 | 34 |
| XSLT 2.0 | 4590 | 8 | 10 | 7 |
| Unstreamed development | 2693 | 18 | 1 | 5 |
| Strict forward inputs | 2695 | 16 | 1 | 7 |
| Posture and sweep | 790 | 129 | 0 | 0 |
| Schema-aware | 843 | 1 | 2 | 6 |
| Schema-aware strict | 842 | 2 | 2 | 6 |
| QT3 | 21884 | 0 | 99 | 67 |

QT3 parse coverage is **22,359/22,359**. Logs are `/tmp/xsl3-p7-accumulator-phases-final-verified-*`;
the consolidated statuses are in `/tmp/xsl3-p7-accumulator-phases-final-consolidated-regressions.json`.
Reports and the failure-ID comparison are in `/tmp/xsl3-p7-accumulator-phases-final-reports/`.

An initial ordinary run failed `unparsed-text-2003` because availability of its live W3C HTTP resource
returned false. Independent processes using both the previous accepted runtime and the current
runtime subsequently read **30,636** characters from that resource. The targeted set then passed
**8/8**, and a fresh complete ordinary run passed **46/46**, without source or baseline changes.
These observations are consistent with transient remote availability; the first HTTP response was
not captured, so no particular server response is asserted. The failed report and targeted recovery
are preserved in `/tmp/xsl3-p7-accumulator-phases-network-failure/`, with the independent probes in
`/tmp/xsl3-p7-unparsed-dependency/`. The full retry log is
`/tmp/xsl3-p7-accumulator-phases-final-verified-w3c-retry.log`. The other final checks ran separately,
and final reports were consolidated only after both command sequences completed.

## Ordinary performance

The final isolated three-pair comparison against `0f2ddcba2c8791f4e8cc6b7b91fe371164ed2021` passes,
exit **0**. Time geometric mean over **39** timed rows is **0.989223741081** and the maximum allocation
ratio is **1.037234042553**. This compares the accumulated branch with the baseline through the same
harness; it is not a per-increment speedup claim. Cold rows are excluded by the existing gate.

Individual timing ratios above 1.05 remain: dispatch compilation **1.072**, regex 1 KB **1.188**, and
maps 1 MB **1.053**. Aggregate success does not remove these outliers. Only rounds **1–3**, the complete
comparison and exact summary are archived in `/tmp/xsl3-p7-accumulator-phases-final-perf-data/`.
All functional, native build and dependency-probe jobs completed before this run. Large native input
children started after it. The earlier timing attempt, which overlapped the newly added lifetime
regression build, remains superseded.

## Exact counter input matrix

Normal and debug saved styles pass at exact **209,715,200** and **2,147,483,648** input bytes. The
source-document uses a streamed integer accumulator whose start rule adds one for each row. Its
deferred selection is:

```xpath
doc/row/value/ancestor::row
except doc/row[number(@id) mod 2 = 0]/text/ancestor::row
```

Each selected row emits its source ID and `accumulator-before('n')`. The controller's expected digest
is independently generated from odd record indexes: the existing generator supplies each row's ID
and the counter must equal that index. No desired fingerprint is substituted. The XML generator is
byte-for-byte unchanged from the accepted preceding harness.

Each child consumes its full source, validates the output digest, emits before EOF, closes its owned
input and leaves the borrowed initial reader open. All four terminal statuses are **0**.

| Saved style | Exact input bytes | First output after input bytes | Child elapsed ms | Lifetime peak RSS bytes |
|---|---:|---:|---:|---:|
| normal | 209715200 | 57344 | 70690.0662 | 31309824 |
| normal | 2147483648 | 57344 | 705048.7006 | 31326208 |
| debug | 209715200 | 57344 | 66895.5263 | 31621120 |
| debug | 2147483648 | 57344 | 678983.7036 | 31571968 |

RSS is the maximum of controller-observed and child-reported full-lifetime peaks, without subtraction.
The **209,715,200-byte** heap/RSS guard, ordinary ICU support and exact input sizes remain unchanged.
These are actual deferred-set and scalar-accumulator runs; they do not prove bounded storage of
arbitrary identity-bearing value histories, the separate **20,971,520-byte** push RSS criterion or
canonical throughput. Child elapsed times are observations, not comparative throughput benchmarks.
Raw output digests, per-child measurements, controller and source provenance are under
`/tmp/xsl3-p7-accumulator-phases-final-scale-native/`.

After the runs, all **413** live/frozen source hashes, native artifact hashes, **103** retained saved-DLL
hashes and the separately protected v2 fork hash match. The final check is recorded in
`/tmp/xsl3-p7-accumulator-phases-final-native/final-check.json`.

## Remaining requirements

Identity-bearing node/closure spill and its memory proof, the remaining semantic/analysis and exact
error-code gates, exact push RSS, canonical throughput, and current-source Linux x64 evidence remain
open. This increment does not complete an S stage or the full Phase 7 plan.
