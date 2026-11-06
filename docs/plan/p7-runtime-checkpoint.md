# P7 runtime checkpoint for 1.1.7

On 1 November 2026 the owner requested a stopping point and release of the work completed so far.
This supersedes the earlier instruction to continue optimization until every original P7 gate passes.
It closes a bounded release checkpoint; it does not complete full P7 or change its acceptance targets.

**Closed and published:** PR #27 merged at `b0bf3bb3464e319048c230191577f86c326b5cdb`,
tagged `v1.1.7`. GitHub and all three NuGet packages are public. Reviewed-source CI, post-merge
Linux/Windows/NativeAOT CI, tagged release checks, public-package consumers and 576 saved-style
contracts passed; ordinary performance passed on the PR and was skipped on the main push.
Further P7 work was paused at this checkpoint; bounded [Phase A](p7-phase-a.md) subsequently resumed.
See the [agent handoff](p7-handoff.md) for exact verification links,
remaining gates and evidence availability.

## Release boundary

Use the retained `feature/p7-completion` changes reviewed in PR #27, plus the release corrections
and documentation. Exclude the isolated startup/compact-producer, reader-type, constructor and other
unadopted experiments. Their positive and negative evidence remains in the development record.

Keep published default execution tree-based. `Streamability = Strict` controls static analysis;
forward execution remains an explicit development opt-in. `supports-streaming` remains `no`.
Both public APIs, settings isolation and saved execution retain their contracts.

The retained changes cover accumulator phase/provenance and schema defaults, fork ownership,
cancellation and bounded buffering, temporary-tree/group storage, dynamic-call allocation,
saved-function validation, compiler restrictions and conformance/probe auditing. The Linux
probe's linker requirements apply only to that internal probe, not to consumer packages.

## Completion criteria for this checkpoint

- Review the retained source diff and correct release-blocking issues without adding another optimization phase.
- Pass full .NET 8/10 Release/Debug unit suites, required upstream/differential/ordinary conformance,
  NativeAOT and ordinary performance checks on the reviewed release source.
- Validate the coordinated Xsl3, Xsl3.Build and Xsl3.Cli packages and their saved/native consumers.
- Update repository/package READMEs, shipped API records, changelog, migration notes and release notes.
- Merge PR #27 after required CI passes, tag 1.1.7 on main and publish matching GitHub/NuGet artifacts.
  NuGet's required deployment reviewer remains the owner.

## Deferred full P7 work

The original 16-workload 200 MiB/20 MiB and 2 GiB/200 MiB NativeAOT gates, complete 126-observation
benchmark and seven strict throughput comparisons remain unchanged. Retained hosted evidence still
fails merge, fork and source-document push RSS, with throughput failures recorded separately.
A local diagnostic, correct output, partial pass or green ordinary CI does not qualify full streaming.
The remaining S3–S6 acceptance accounting, final qualification and supported feature claim stay deferred.
See [the full completion record](p7-completion.md) for exact sources, failures and experimental evidence.

The scope is fixed for 1.1.7. Reviewed-source checks are attached to [PR #27](https://github.com/skuirrels/Xsl3/pull/27).
Publication is tracked separately by the [GitHub release](https://github.com/skuirrels/Xsl3/releases/tag/v1.1.7)
and the three NuGet package versions.
