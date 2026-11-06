# P7 integrated qualification candidate

This branch combines public-selection head `5645098` with corrected-discard controller `18fc9a5`.
The latter has the runtime of `a8d322a`: incremental projection discard with logical-position
preservation and a 256-event default. Merged main `8bdec4c`, including PRs #34–38, is retained.

## Why combine these changes

The public policy implements the original Strict/Fallback API requirement. Corrected discard has
source-verified correctness coverage, passes both original memory matrices (32 budgets /96 outputs),
and shows a useful 14.4% fork improvement in the paired Linux comparison. Its complete original
throughput gate still passes only three of seven targets. Combining them is preparation for continued
qualification, not approval to merge or publish.

Compared with `a8d322a`, the integrated runtime differs only in `XsltSettings`' public execution
selection and its explanation. Canonical benchmark consumers, stylesheets, generated inputs, output
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
Combined validation is pending; `supports-streaming` remains `no`.

See [public selection](p7-public-streaming-selection.md),
[corrected discard and original gates](p7-projection-discard.md), and
[the original completion requirements](p7-completion.md#requirements-and-evidence).
