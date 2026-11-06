# P7 integrated qualification candidate

This branch combines public-selection head `5645098` with corrected-discard controller `18fc9a5`.
The latter has the runtime of `a8d322a`: incremental projection discard with logical-position
preservation and a 256-event default. It now also includes the [qualified merge source-group
change](p7-merge-source-groups.md), runtime/compiler `370178a`, through candidate history `f308ab8`.
Merged main `8bdec4c`, including PRs #34–38, is retained.

## Why combine these changes

The public policy implements the original Strict/Fallback API requirement. Corrected discard has
source-verified correctness coverage, passes both original memory matrices (32 budgets /96 outputs),
and shows a useful 14.4% fork improvement in the paired Linux comparison. Its complete original
throughput gate still passes only three of seven targets. Combining them is preparation for continued
qualification, not approval to merge or publish.

Compared with `a8d322a`, the integrated runtime includes `XsltSettings`' public execution
selection and the qualified deferred source-group construction/compiler binding change. Canonical benchmark consumers, stylesheets, generated inputs, output
oracles and original gate controllers are unchanged. The merge adds no runtime from the rejected
short-polling, tail-call, lazy-header, compact-producer, linked-stack or speculative-refill controls.

## Completion criteria

1. Audit the merge against both parents. Preserve public Strict/Fallback and saved-mode contracts,
   explicit buffer settings, logical source numbering, demand, validation, source ownership and
   cancellation. Retain every merged grounding, verifier, retained-state and fork contract.
2. Run combined affected contracts in Release/Debug on .NET 8/10. Final compatibility still requires
   full units, upstream/differential/ordinary and strict W3C inventories, reviewed S1/S2, saved styles,
   NativeAOT consumers and ordinary performance on the eventual final source.
3. Improve remaining throughput from this coherent source with independently audited comparisons.
   Both original memory matrices and all 126 original throughput observations must pass on final source;
   all seven 100 MB medians must meet their targets. Prior component results are not final-source proof.
4. Verify both API paths, actual forwarding, physical input positions and first sink writes. Complete
   the S3–S6 evidence audit against the original plan, including the accepted failure dispositions.
5. Only then finalize the supported feature/profile claim, documentation, review, CI and merge, followed
   by matching NuGet and GitHub publication. NuGet environment approval remains the owner's action.

## Current evidence boundary

The merge preview has no runtime or test-source conflict. Four documentation conflicts combine the
public policy and buffer/discard records while retaining historical qualification failures.
All four jobs in public-selection [CI 42133700130](https://github.com/skuirrels/Xsl3/actions/runs/42133700130)
pass on `5645098`. That predecessor and this combined branch are distinct evidence targets.
Neither a green predecessor run nor the rejected controls' compatibility tests qualify this source.
Combined affected validation passes **5,042 .NET 10 /4,088 .NET 8** checks in each of Release and
Debug: **18,260 total**, with no failures/skips. Every TRX result and 1,339 recorded source-file hashes
were audited against `409068e`; all four engine/test binary sets and PDBs are frozen. Evidence:
`/private/tmp/xsl3-p7-integrated-qualification-20261106/validation-review.json`. Subsequent CPU-report
and documentation changes do not alter runtime/test source. This is affected-contract coverage,
not full unit or hosted final qualification. `supports-streaming` remains `no`.

**Hosted update, 6 November:** all four jobs in
[CI 42141508372](https://github.com/skuirrels/Xsl3/actions/runs/42141508372) passed on `6c24a92`.
The [integrated evidence review](p7-integrated-evidence-review.md) independently rechecks the raw
strict inventories, per-compilation verifier evidence, S1/S2 populations and stage exits, and maps
199 passing test methods to the 116 historical obligation entries. It preserves every scope limitation.
The additional S4-29 physical dispatch-position contract passes 40 new local cases (257 checks with
related Debug coverage); it changes no runtime source and requires CI on the next head.
Neither closed current-mode nor fork value-event performance control is adopted. Original final-source
memory and throughput qualification, full-P7 review, supported claims and release remain outstanding.

See [public selection](p7-public-streaming-selection.md),
[corrected discard and original gates](p7-projection-discard.md), and
[the original completion requirements](p7-completion.md#requirements-and-evidence).


**6 November current-source audit and control decisions:** integration `32c845d` passed all four
jobs in [CI 42148343009](https://github.com/skuirrels/Xsl3/actions/runs/42148343009). The independently
reviewed Windows/Ubuntu artifacts pass full Release units at 7,613 /6,440 per host, all 60 hosted
physical-dispatch instances, reviewed S1/S2 and unchanged strict accounting. Full local Debug units
pass 14,053 checks. The [evidence review](p7-integrated-evidence-review.md) records exact artifacts,
source hashes and limitations.

The fork-name cache, built-in text dispatch and corrected private fork cursor remain excluded.
Their complete records are linked from the [handoff](p7-handoff.md). The text-dispatch local gain did
not reproduce reliably on Linux; the private cursor needed an immediate-cleanup correction and then
regressed locally. No runtime, test, benchmark, controller or gate change accompanies this documentation
update. CI on the documentation head is distinct from the already-audited `32c845d` run. Original
final-source qualification and full-P7 completion remain unproven.

## Merge improvement integrated, 6 November

Candidate history `f308ab8` is now incorporated into the draft integration branch. Only two runtime/
compiler files and one test file differ from predecessor `402cbed`; canonical consumers, controllers,
workloads and original gates are unchanged. The source-group change passes 7,976 affected contracts,
a fresh 80-case baseline comparison and expanded named-closure checks on both exact frozen engines.
The latter are 80 strengthened reruns per engine, not additional distinct cases.

All 60 local and 60 Linux paired observations validate with original output fingerprints and audited
source/binary identities. Merge improves 4.24% locally and 3.93% on Linux with fully separated ranges;
its Linux candidate stream/tree ratio is 0.987767. The other workload ranges overlap, and the higher
fork median remains recorded. This supports carrying the change forward, not full P7 completion.

Integration CI must run on this changed source. Prior `32c845d`/`402cbed` compatibility results do
not prove the new head. The latest full original throughput run still records three of seven passes;
identity, fork and source-document are now the next optimization targets. Both original memory
matrices and all 126 throughput observations remain mandatory on final source. PR #40 stays draft,
`supports-streaming` stays `no`, and no merge to main or release is authorized by these diagnostics.


## Current merge integration CI complete

All four jobs passed on `a4f7301` in [42167378456](https://github.com/skuirrels/Xsl3/actions/runs/42167378456).
The [current-head audit](p7-integrated-evidence-review.md) independently verifies both test archives,
all full Release rows (7,637/6,456 per host), the 80 expanded named-merge and 60 physical-dispatch
instances, unchanged strict inventories and stage accounting, all 4,968 eligible Debug verifications,
and original/reviewed S1/S2 populations. Native/saved and ordinary micro artifacts also verify.
The attribute-cache runtime is excluded after its inconclusive comparison; navigator node-kind work
is isolated and unqualified. No original final-source memory/throughput qualification or full-P7
completion follows from the green CI run. PR #40 remains draft.


## Original acceptance dispatched on the CI-verified integration

[42173435783](https://github.com/skuirrels/Xsl3/actions/runs/42173435783) is the full original
acceptance for immutable `a4f7301`: 209,715,200-byte inputs under 20,971,520-byte absolute/managed
limits and 2,147,483,648-byte inputs under 209,715,200-byte limits, across all 16 NativeAOT workloads;
plus all seven canonical throughput workloads at 1 MB, 100 MB and 1 GB, both modes, three rounds,
with the original 4 GiB guard and all seven 100 MB targets required. Source/controller identity and
the actual three running acceptance jobs are verified. No diagnostic mode or gate change is used.
The changed merge runtime justifies this current-source qualification. Results remain pending and
must be audited before replacing the historical component results. Evidence root:
`/private/tmp/xsl3-p7-integrated-original-20261106/`. Neither isolated cache nor navigator code is included.

## Current full Debug compatibility qualification

The exact integrated runtime/test source `a4f7301` is running the full Debug unit suites on both
.NET 10 and .NET 8, with warnings treated as errors and four configured processors. This fills the
full-Debug gap left by the already-audited hosted Release suites and focused Debug contracts.
The rejected navigator control's full suites do not substitute for this unchanged integration run.
All 732 tracked runtime/test/consumer/controller source files were checked against `a4f7301` before
starting. TRX-row, frozen-binary and PDB-source audits follow sequentially under
`/private/tmp/xsl3-p7-integrated-debug-20261106/`. Session 60509 is live; do not restart it.
