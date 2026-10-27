# P7C: positioned source-reader correctness

**Scope frozen on 2026-10-27. Branch: `feature/p7c-runtime-correctness`.**
This is a bounded runtime follow-up to [P7B](p7b-analysis-closeout.md#p7b-completion),
based on release `v1.1.1` (`11d77dfd0527af588ab88af5c2c01576ff7057aa`).
It closes the input boundary and ownership contract for reader-backed experimental
`xsl:source-document` execution. It does not complete full P7 or enable streaming support.

**P7C implementation closed locally on 2026-10-27.** All five completion criteria below
have local evidence. Implementation scope stops here. PR review and hosted Linux/Windows/native
CI are the integration gates; package publication requires a separately authorized release.
The fixes are unreleased. This document records the local checkpoint; hosted results are tracked
by the P7C pull request checks.

## Reproduced defect

When a resolver supplies an `XmlReader` positioned on an element, an unvalidated
forward source includes subsequent siblings. For example, selecting the first `doc`
in `<outer><doc/><doc><record/></doc></outer>` reports one record instead of zero.
Both the legacy and 3.0 APIs reproduce this on .NET 8 and .NET 10. The existing
initial-source path already limits positioned input to its current subtree; explicit
source validation also establishes a subtree boundary. The ordinary unvalidated
source opener omits that step.

## Fixed scope and completion criteria

1. Retain a fresh full strict-forward baseline and its complete [failure inventory](p7c-runtime-deferred.csv).
   Baseline at entry: **2,695 passed / 16 failed / 1 not run**, zero unmatched error codes.
   Inventory every failure as deferred or a documented fixture/specification conflict;
   do not change raw W3C fixtures or expectations to close P7C.
2. Apply the existing positioned-reader boundary/ownership policy to source-document
   execution through one shared implementation. Empty and nonempty subtrees must
   exclude following siblings. Borrowed readers stay open and stop at their selected
   element's boundary; owned streams/readers retain their existing cleanup policy.
3. Add regression coverage for both APIs, Release/Debug, implicit/preserve/strip and
   explicit validation paths, early exit, body/read errors, and caller continuation.
   Exercise saved normal/debug stylesheets through the supported `Load(Type, settings)`
   API on .NET 8/10 and NativeAOT. Preserve
   namespace context and schema annotations at the boundary.
4. Pass full managed unit tests, upstream and byte-for-byte differential gates;
   retain ordinary and strict-forward conformance failure sets, P7B raw/reviewed
   acceptance, and the ordinary performance gate. Do not add or tolerate regressions.
   Record exact evidence and any platform checks still requiring hosted CI.
5. Review the diff and update the README, changelog, conformance documentation and
   this close-out with the supported behavior, experimental boundary and deferred work.

Once these five items have evidence, stop implementation and close P7C. Newly found
unrelated defects go into the deferred inventory rather than expanding this phase.
Hosted CI and publication remain separate release steps.

## Explicit exclusions

- Reconciliation of all 16 raw strict-forward failures or a new reviewed runtime suite.
- New streaming constructs, public APIs, saved formats or schema features.
- General retained-model bounds, the 200 MiB input / 20 MiB absolute memory target,
  the separate 2 GiB guard, and streaming throughput optimization.
- Changes to protected benchmark inputs, memory limits or upstream expectations.

Normal execution remains tree-based; `supports-streaming` stays `no`. The
[original P7 targets](p7-streaming.md) and experimental CI gates remain unchanged.
P7C completion prepares a reviewable release candidate; it does not itself publish a release.

## Implementation and review

The shared runtime source opener now establishes the positioned input's subtree boundary
before whitespace filtering and validation. Initial inputs and source documents reuse the
existing selected-source view, which already preserves namespace context and owns its scoped
reader. This also fixes lost inherited prefixes used only in QName values: plain `ReadSubtree()`
can omit those bindings. Transparent reader wrappers retain the first capable namespace resolver.

The change is confined to three runtime files and adds no helper ABI or saved-format change.
There are **50 new managed cases on .NET 10 / 48 on .NET 8**, covering both APIs, normal/debug
compilation, empty/nonempty input, preserve/strip/strict policies, unread bodies, body errors,
malformed content inside/outside the selection, typed inputs, namespace context, transparent
wrappers and caller continuation. Existing ownership and validation regressions also pass.

Before changing the runtime, all four corrected two-API/empty-nonempty cases failed per framework
by counting a following sibling. Namespace tests using one valid forward traversal also
demonstrated the lost inherited-prefix error before the selected-source view was reused.

## Exit evidence

| Gate | Current local result |
|---|---|
| Release and Debug managed units | 3,634 .NET 10 / 3,481 .NET 8 in each configuration |
| Upstream regression / byte-for-byte differential | 1,654 / 394 per framework |
| Full W3C harness, with saved round trips | 71 harness tests, including five review guard regressions |
| Ordinary XSLT 3.0 / QT3 | 8,850/18/41 and 21,884/0/99; failure IDs unchanged |
| Full strict-forward streaming development | 2,695/16/1, zero unmatched codes; all 16 entry failures retained |
| Unstreamed streaming development | 2,693/18/1, one existing unmatched dynamic code; unchanged |
| Original S1 / S2 | 790/129/0 and 153/2/0; original baselines unchanged |
| Reviewed S1 / S2, Release and Debug | 1,000/0/0 and 155/0/0; 152 exact static codes and three positive results |
| Saved normal/debug portability | 16 saved styles; 52 checks each on .NET 8 and .NET 10 |
| NativeAOT, macOS ARM64 | Same 52 checks pass; globalization enabled, publish warnings treated as errors |
| Ordinary three-round performance gate against `v1.1.1` | Pass: timing GM 0.995962 over 39 rows, maximum allocation ratio 1.000156 |

The strict-forward baseline added to CI is exactly the complete pre-change 16-case inventory.
It detects additions and stale failures; it does not turn those failed W3C cases into passes.
Review found that a failure-ID comparison alone could miss lost passing cases, new not-run cases
and wrong-code passes. The completed gate therefore also checks all **2,712** selected cases,
the single existing collection skip and zero unmatched expected-error codes. Five Release/Debug
harness regressions exercise those coverage conditions without freezing the number of failures.
The one not-run case and all fixture conflicts remain visible in the raw report. P7B's original
919-case results and reviewed 1,000-case results retain their existing independent gates.

An additional ordinary-profile strict diagnostic reports 8,849/19/41. The exact released 1.1.1
runtime reproduces the identical heading and 19 failure IDs with the same harness. That separate
diagnostic has no new acceptance baseline and does not expand P7C.

Local artifacts are in `artifacts/p7c-runtime/`: entry/red and corrected fixtures, per-framework
test logs, raw/reviewed reports, deferred inventory, source hashes, the saved-style producer and
portable/native consumers. The native consumer references the built runtime assembly, matching
the saved-package deployment path; runtime compilation is not attempted in NativeAOT. Setup
failures are retained separately from the successful verification. No trim/AOT warning is waived.

The API inventories, official suite checkout, existing baselines and protected streaming benchmarks
are unchanged. Hosted Linux/Windows CI and hosted native smoke are tracked separately in the
pull request checks; local macOS evidence does not establish their results.

The [result snapshot](p7c-results.json) records the exact local gates and implementation hashes.
The ordinary performance gate is an aggregate pass: JSON/1 MiB, maps/1 MiB and sorting compilation
have individual time ratios of 1.052830, 1.060333 and 1.157456. All paired rounds remain in the
evidence bundle; no per-row win or new streaming-memory/throughput result is claimed.

## Reproduce

```sh
dotnet build Xsl3.slnx -c Release -m:1
dotnet test tests/Xsl3.Tests -c Release --no-build
dotnet test tests/Xsl3.Tests -c Debug -m:1
dotnet test tests/Xsl3.Upstream.Tests -c Release --no-build --filter 'category!=failing'
dotnet test tests/Xsl3.Differential -c Release --no-build
XSL3_W3C_SAVED=10 dotnet test tests/Xsl3.W3C -c Release --no-build
XSL3_STREAM_STRICT=1 XSL3_XSLT30_PROFILE=strm dotnet test tests/Xsl3.W3C -c Release --no-build --filter FullyQualifiedName~Xslt30Conformance
eng/perf-gate.sh v1.1.1 3
```

P7B raw/reviewed reproduction commands remain in its [close-out](p7b-analysis-closeout.md#reproduce-the-baseline).
Keep `artifacts/p7c-runtime/` with the release evidence to rerun the saved producer and consumers.
