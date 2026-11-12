# P7 integrated acceptance evidence, 6 November 2026

## Added source-stack runtime: full integration qualification pending

The integration candidate now includes the [current-event stack control](p7-current-event-stack.md)
and two reader-retry contracts from runtime/test source `1ef7c32`. Its 15,834 affected checks and both
60-observation paired comparisons are independently audited. Linux identity improves 3.99% with
separated ranges; other ranges overlap, including source-document's 0.52% higher median. Full unit
and reviewed-head CI results for this new integration are not yet established. The complete `a4f7301`
audits below remain explicitly scoped to the predecessor source. No original final-source acceptance
or supported-streaming claim follows from bringing this qualified control into the draft PR.

## Current integration head `a4f7301`: CI and artifact audit complete

All four jobs in [CI 42167378456](https://github.com/skuirrels/Xsl3/actions/runs/42167378456)
passed for `a4f730130728480748112af547aa999426bcfbff`. This verifies the integration source with the
qualified deferred merge-group runtime/compiler and expanded named-closure regression. The separate
attribute-cache control is rejected, and the navigator node-kind control remains unqualified.
Neither is part of this CI result.

| Test artifact | ID | Verified ZIP SHA-256 |
|---|---:|---|
| Windows | 13480908058 | `580207b6f1f8fa18c01a6694455c4a9309a887eb31f536b3a208240a08990e3e` |
| Ubuntu | 13481876865 | `8a347fc7ff8b06a4d44e051b8c87805ca59c877c35375102fbdde91a4aef994a` |

Every full Release row/counter and unique execution ID verifies: **7,637 .NET 10 /6,456 .NET 8**
per host. All **80** expanded named merge-group cases pass across the four hosted unit suites,
as do all **60** exact-dispatch cases across hosted Release and Ubuntu Debug. These counts are
instances across configurations/platforms, not new distinct specifications.

Independent re-audits reproduce all three strict inventories: **2,696 pass /16 accepted failures /
0 skipped /0 unmatched expected codes**. All **4,968** eligible Debug compilations verify among
119,303 recorded compilations. S3–S6 accounting and disposition records match the preceding audit.
The clean local suite and all three hosted audits use pinned revision
`fddf1cf920087e791f13315d68dfbe874d97dc56`; audit controllers/inventory/dispositions are unchanged.

Regeneration reproduces the hosted reviewed manifest and generated fixture hashes. All **66,643**
original expressions remain uniquely mapped from the original **919** cases into the reviewed
**1,000** cases. Reviewed S2 verifies **153** exact static diagnostics and **two** allowed results
in Release and Debug. The reported Release engine hash agrees between strict and unstreamed headers;
the archive does not retain that W3C engine binary for an independent local file-hash check.

The native/saved and ordinary micro audits below also pass on this head. Their smaller workloads
and disabled memory guard do not qualify the original P7 matrices. Full Debug-unit evidence from
`32c845d` remains historical; no full Debug-unit result for `a4f7301` is inferred from it. Final source
still requires every original performance, compatibility and physical-I/O obligation before review,
merge, supported claims and matching publication. PR #40 remains draft and `supports-streaming=no`.

Both archives, raw reports, regenerated fixtures and executable audits are retained under
`/private/tmp/xsl3-p7-integration-head-a4f7301-20261106/` (`review.json`, `review.py`,
`native-perf-review.json`). The run is terminal: do not restart it or treat the earlier live-job notes
as current status.


## Full Debug units on the exact integrated source

After the hosted Release audit, both complete local Debug suites pass on unchanged runtime/test
source `a4f7301`: **7,637 .NET 10 /6,456 .NET 8**, **14,093 total**, with no failures or skips and
warnings treated as errors. All TRX rows and unique execution IDs, four engine/test binaries and
409 runtime /200 test tracked PDB source checksums per framework verify. The 732-file source
manifest matches `a4f7301`. The rejected navigator test/runtime is excluded.

Evidence: `/private/tmp/xsl3-p7-integrated-debug-20261106/`, including `source-manifest.json`,
`validation-review.json`, all raw TRXs/logs, frozen DLLs/PDBs and executable validation/audit scripts.
This closes the current integration's full Debug-unit gap; original throughput and the other final
P7 requirements remain separate. Session 60509 is terminal and successful.

## Changed runtime: merge source groups

The draft integration branch now contains candidate history `f308ab8`: the runtime/compiler source
from `370178a` and the expanded named-closure regression. See the [complete control audit](p7-merge-source-groups.md).
All 7,976 affected contracts pass, as do a fresh 80-case baseline comparison and 80 expanded named-
closure cases on each frozen engine. The expanded cases are reruns with stronger assertions.
All 60 local and 60 Linux observations validate. Merge's 3.93% Linux improvement has separated
ranges and a 0.987767 stream/tree ratio; other ranges overlap. The runtime changes have now passed the current-head CI/artifact audit above. The predecessor
artifacts below remain historical evidence, not substitutes for that new-head audit.
All original final-source gates and release requirements remain active.

## Current integration native and ordinary-performance audit

Run [42167378456](https://github.com/skuirrels/Xsl3/actions/runs/42167378456) is on
`a4f730130728480748112af547aa999426bcfbff`. Its NativeAOT and ordinary performance jobs passed;
Windows and Ubuntu subsequently passed, as recorded above. The following completed artifacts
were downloaded, checked against GitHub's digests, and independently audited:

| Artifact | ID | Verified ZIP SHA-256 |
|---|---:|---|
| Native streaming probe | 13480350324 | `82efe644c4e4816ed03786145f5aa7a947fb6a9ff37be41f9c6ba8ab266df1ea` |
| Ordinary performance | 13479338508 | `0afbb849306b2908344c85509a6b17d3489ad10599a7bcf3f106b5f4099b5142` |

All 48 native smoke observations cover the 16 original workload names with exact 64 KiB baseline
and 2 MiB measured input. Actual NativeAOT metadata, complete outputs, stdout/report agreement,
matching tree/stream baseline fingerprints and whole-lifetime `wait4` RSS fields verify. The memory
limit is zero: this is compatibility evidence, not either original memory-budget matrix. The archive
does not retain the streaming probe executable, so no independent executable-file hash check is claimed.

All 1,728 saved-style checks verify as distinct required scenarios: three fixture/compiler cohorts,
24 styles and 192 scenarios per runtime, each on .NET 8, .NET 10 and NativeAOT. The reader bridge
reports nine passing checks and native execution; both managed-intermediate rejection messages are
retained. Exact artifact file hashes and the executable audit are under
`/private/tmp/xsl3-p7-integration-head-a4f7301-20261106/`.

The three ordinary-performance rounds per variant each contain all 40 rows. Recalculation of the
39 gated row medians yields a time geometric ratio of **1.00127247** and a maximum allocation ratio
of **1.00017326**, passing the unchanged ordinary gate. Cold startup remains reported separately.
These rows do not replace the seven original P7 throughput workloads. The current-head platform artifact review is complete above; original final-source qualification
remains outstanding.

The Windows test artifact subsequently completed: ID **13480908058**, verified ZIP SHA-256
`580207b6f1f8fa18c01a6694455c4a9309a887eb31f536b3a208240a08990e3e`. Independent row/counter and
unique-execution checks pass **7,637 .NET 10 /6,456 .NET 8** full Release units, including all
**24/16** expanded named merge-group cases. This is the new runtime's Windows unit evidence;
the combined platform/inventory audit subsequently passed as recorded above. Raw files and
`windows-unit-review.json` are retained in the same current-head audit directory.

## Preceding integration head `32c845d`

All four jobs in [CI 42148343009](https://github.com/skuirrels/Xsl3/actions/runs/42148343009) passed
for `32c845daf89818aeb4950e7d549a34fa3cef18e9`. This adds only the exact dispatch contract and its
CI inclusion to the runtime reviewed below. It excludes the separate, rejected fork name-cache control.

| `32c845d` test artifact | ID | Verified ZIP SHA-256 |
|---|---:|---|
| Windows | 13473236027 | `05810a1cd3798a2d780bb2b7fd125189e4be71489333dee7755e6604e1590416` |
| Ubuntu | 13472936071 | `17cab5164d2d3320b1fc04bdc1ef605d62868e7315ca0d5d993de6638ad56f2d` |

Every full Release result row and counter verifies: **7,613 .NET 10 /6,440 .NET 8** per platform.
All **60 hosted exact-dispatch cases** pass: 12/8 in each platform's Release suite and 12/8 in
Ubuntu Debug. Independent re-audits reproduce all three hosted strict inventories at 2,696/16/0,
with no unmatched expected codes; all 4,968 eligible Debug compilations verify out of 119,303 records.
Regenerated fixtures and all 66,643 original/reviewed expressions verify, and reviewed S2 retains
153 exact static diagnostics plus two allowed results. Stage outcomes and dispositions equal the
preceding audit. The new reported engine hashes are preserved and checked against the new raw
headers; the archives still do not retain the W3C engine binary for an independent local file check.

Full local Debug units on the same unchanged integration source pass **14,053 checks** (7,613/6,440),
with no failures/skips. Every result row and the frozen binary hashes verify. The current CI audit
and raw archives are under `/private/tmp/xsl3-p7-integration-head-32c845-20261106/`; the full Debug
evidence is under `/private/tmp/xsl3-p7-integrated-debug-full-20261106/`. The original audit attempts
are retained, including the expected engine-hash difference from the earlier build and the audit
tool's refusal to overwrite an existing result. No source, fixture or expected outcome was changed.

PR #40 remains draft under the unchanged full-P7 completion boundary. This is predecessor compatibility
and contract evidence, not final-source original memory/throughput qualification.

## Preceding detailed obligation review

Reviewed source: `6c24a9270e5e1ca589a73e867beb381d9ff8950e`, the integrated public-selection and
corrected-discard candidate in draft [PR #40](https://github.com/skuirrels/Xsl3/pull/40).
All four jobs in [CI 42141508372](https://github.com/skuirrels/Xsl3/actions/runs/42141508372) passed:
Windows, Ubuntu, NativeAOT and ordinary performance. This is compatibility and contract evidence;
it does not pass the separate original streaming memory or throughput gates on this combined source.

## Artifact review

Both test archives were downloaded and verified against GitHub's SHA-256 digests before extraction:

| Artifact | ID | SHA-256 |
|---|---:|---|
| Windows test results | 13470365562 | `f584340d6990e372dca04e7d03635822e551277d7d2c3a98470adc3894fc9273` |
| Ubuntu test results | 13470752032 | `e796201e04c3a1c196d9c5c9eb84308d58e912b9f72dc474a67b128409ef3ddc` |

The full Release unit suites pass **7,601 .NET 10 /6,432 .NET 8** cases on each platform. Every result
row and counter was checked. The [contract reference record](p7-integrated-contract-evidence.json)
retains all 116 historical S3–S6 obligation IDs, their unit references, current passing case counts,
source hashes and artifact provenance. All 171 still-current historical methods pass on both hosts;
the old public opt-in test was deliberately replaced by `PublicSettingsSelectForwardOrTreeExecution`.
Supplementary evidence adds coverage to 29 obligation entries, for 199 distinct referenced methods.
Saved-only methods correctly have no .NET 8 cases. Matching names and passing results do not alone
prove an obligation's semantics: the scope and limitations below remain part of this review.

Of the 33 test files referenced by the historical map, 25 are unchanged from `b0bf3bb`. The eight
changed files were reviewed: additions cover direct observation, public selection, source demand and
projection discard; the forced completion-order test joins the later worker; public read-boundary
tests now use the default Strict policy. Existing expectations were preserved except for the approved
256-event default and the public Strict/Fallback selection change. The historical map remains intact.

The three strict JSONL reports were independently re-audited with `eng/audit-streaming-runtime.py`,
requiring inventory, forwarding, stylesheet-stage forwarding and primary evaluation evidence; Debug
also requires actual verifier completion. Each result exactly matches its hosted audit. CI checked
the engine file hash on the runner. The archives do not retain that W3C engine binary, so this local
review does **not** claim a second independent engine-file hash check.

`eng/stage-exit-report.py` reproduces the hosted stage accounting. The original S1 audit also matches.
Regenerating the reviewed suite produces the exact hosted manifest and generated hashes. All 66,643
reviewed expression records map uniquely to their original case/part/expression, with no omission,
duplication or substitution, and match the reviewed posture/sweep. S2 records were checked for actual
execution, exact static code, source location and reason, or an explicitly allowed successful result.
The first local audit needed two format corrections: `FreeRanging` corresponds to `free-ranging`, and
`square-array-201` permits a successful result as an alternative to XTSE3430. Those failed audit attempts
are retained; no source, fixture, report or expected result was changed.

| Acceptance population | Current result |
|---|---|
| Original S1 | 919 selected cases; raw 790 pass /129 fail retained; 66,643 expressions preserved |
| Reviewed S1 | 1,000 pass /0 fail /0 skipped, Release and Debug |
| Original S2 | 154 pass /1 retained fixture conflict |
| Reviewed S2 | 155 pass in Release and Debug: 153 exact static diagnostics, two allowed results |
| Full strict runtime, modern Release /Debug /legacy with saved sampling | Each 2,696 pass /16 retained failures /0 skipped; no unmatched expected error code |
| Debug verifier | 119,303 compilation records; all 4,968 eligible compilations verified exactly once |

| Stage exit | Strict pass /fail /skip | Same-population unstreamed pass /fail /skip | Regressions |
|---|---|---|---|
| S3 | 2,129 /5 /0 | 2,129 /5 /0 | None |
| S4 | 227 /4 /0 | 226 /5 /0 | None |
| S5 | 213 /5 /0 | 212 /6 /0 | None |
| S6 | 54 /1 /0 | 54 /1 /0 | None |

All nonpassing stage cases have retained dispositions. Seven of the nine named S5 merge cases pass;
`merge-081` and `merge-084` retain the owner's accepted fixture-conflict dispositions. The full strict
conformance rate is 99.41%; this is the conformance portion of S7, not completion of S7.

## Current interpretation of the historical gaps

| Obligations | Current evidence and its limit |
|---|---|
| S3-04 | Isolated live managed-byte measurements plus retained-clone negative controls cover the canonical bounded-record input. They do not bound arbitrary payloads or replace process RSS. |
| S3-17; S4-04/05/18/21/22/24/25/27/30; S5-02 | Forward representation and early sink writes strengthen the earlier output-only checks. All source/destination combinations retain representation checks; raw-result overloads do not claim early sink writes. Function categories, attribute sets, validated readers and skipped-subtree accumulator effects have direct forward controls. |
| S3-18/25 | Real optimizer rewrite controls and per-compilation Debug capture cover restricted transformations and actual verifier invocation. |
| S3-27 | Entry-tree fallback fails the forwarding audit. An injected helper that grounds behind a genuine forward entry fails the separate managed-byte control despite correct output and verifier success. This is not a universal internal-materialization detector; explicit copies and other payload shapes have separate contracts. |
| S4-06/11/12 | Actual tree/forward representation is checked. Strict selects streaming by default when applicable; Fallback selects tree execution. Explicit development overrides and saved compiled selection remain covered. |
| S4-29 | Hosted tests establish output and early streaming. The exact per-dispatch reader-position contract below also passes all 60 hosted instances at `32c845d`, as recorded in the current-head update. It is absent only from the preceding `6c24a92` artifact cohort. |
| S4-35; S5-21/24/25; S6-23 | Current full inventories preserve the recorded fixture/profile/optional-feature dispositions. The former `merge-039` skip is fixed and cannot return. |
| S5-07/11/16 | Managed-retention tests cover bounded node-valued accumulator windows, iterate parameters and adjacent groups, with a deliberately growing-state control. Program-retained unbounded state is not claimed to have constant memory. |
| S5-14 | Direct physical row-start/end observations cover unused, truncated and complete members, draining and boundary lookahead through modern, legacy and saved paths. |
| S6-08/10 | A forced later-prong-first completion preserves declaration order; raw principal items survive actual forward consumption. Principal buffering is permitted and is not an early-output claim. |
| S6-11/12 | Payload observations prove required complete-population buffering for grouping/sorting and collection after release, with retained and rolling-adjacent controls. This is logical payload retention, not a process-memory budget. |
| S6-16/17 | Compiled loaded/saved controls verify the accepted policy: later prongs cancel; earlier prongs finish so error selection follows declaration order. |
| S6-05 | Corrected-discard component `a8d322a` passed both original memory matrices. The final integrated source still requires those exact matrices; ordinary CI's smoke test is insufficient. |

The remaining historical direct rows retain their stated scopes. No blanket "116 obligations complete"
claim follows from a method-reference audit. In particular, bounded-shape measurements, explicit
grounding/output/state storage and final-source process qualification remain distinct.

## S4-29 exact dispatch position contract

`StreamedDispatchReadPositionTests` observes the real `XmlReader.Read` count and current node,
row starts/ends and child starts at three template-rule entries linked by two `xsl:next-match` calls.
Every streamed row must still be on its start tag at all three entries: neither dispatch may read
its child or advance to another row. The lowest-priority rule's attribute predicate inspects only the
header, then its body consumes the value. Complete output, actual source representation and borrowed
reader ownership are checked. Tree controls have the same results but are already at EOF at all entries.
The existing Ubuntu Debug streamability step includes this class, so hosted CI also exercises the
Debug library without adding another test process or a new memory/performance workload.

Modern, legacy and saved execution cover both debug and optimized stylesheet compilation. The new
contract passes **40 cases** across Release/Debug and .NET 8/10 (saved loading on .NET 10). The Debug
run also includes existing initial-mode and forward-execution checks: **257 passed checks in total**
including the 20 new Release cases, with no failures or skips. Runtime source is unchanged. Raw TRXs,
source/test hashes and four frozen binary sets are retained under
`/private/tmp/xsl3-p7-dispatch-positions-20261106/`.

## Remaining full-P7 boundary

1. Resolve the four original throughput misses from the last fully audited component cohort:
   identity, merge, fork and source-document. The small current-mode and fork value-event controls
   are closed without adoption because paired observations did not establish useful speedups.
2. On final reviewed integrated source, pass both complete 16-workload original NativeAOT memory
   matrices and all 126 original throughput observations, with every 100 MB stream median no slower
   than its tree median. Recheck first output and physical input positions at the original sizes.
3. Reconcile this contract evidence with the final runtime diff, complete compatibility/review/CI,
   then enable the truthful supported feature/profile claim and update user documentation.
4. Merge and publish matching NuGet/GitHub releases; the NuGet deployment review remains user-owned.

Local independent audit evidence is under `/private/tmp/xsl3-p7-current-evidence-20261106/`, including
the original ZIPs, extracted reports, regenerated reviewed suite and executable review scripts. The
hosted archives expire on 4 February 2027; rerun the checked-in gate/audit commands if they are unavailable.
`supports-streaming` remains `no`; this evidence does not establish merge or release readiness.
