# P7 public streaming selection qualification

This isolated integration branch starts from merged main `f453216` and excludes all unadopted
performance controls. It prepares the original S0/S4 public selection requirement without claiming
supported streaming before full qualification. The historical `af17d4f` selection experiment is a
reference, not inherited test evidence; its earlier implementation was removed at the 1.1.7 release
boundary and is revalidated against the current runtime here.

Default/explicit `Strict` selects forward execution for declared-streamable reader, stream and URI
inputs. `Fallback` selects ordinary tree execution. Existing per-compiler diagnostic overrides and
the explicit `XSL3_STREAM_STRICT=0` tree control remain. The ordinary W3C harness already sets its
execution-only override from the requested strict profile, so it keeps Strict static analysis in
ordinary/unstreamed runs; do not replace that with Fallback and lose required static diagnostics.
The canonical performance and memory consumers select their mode explicitly and remain unchanged.

## Completion criteria

1. Both public APIs select forward/tree behavior from default/Strict/Fallback settings, with actual
   representation, early output and borrowed-reader ownership asserted. Mutating caller settings
   after compile/load cannot change the compiled selection.
2. Saved styles preserve their compiled mode even when loaded with opposite settings. Existing XDM
   inputs remain trees; all source/destination overloads and initial-mode contracts remain valid.
3. Existing physical read-boundary and forwarding cases use public default selection for their forward
   arm, with an explicit internal tree control only for the comparison arm. Original outputs and read
   boundaries remain unchanged, including the EOF-dependent accumulator/iterate exceptions.
4. Full Release/Debug .NET 8/10 compatibility, W3C strict/ordinary/reviewed profiles, saved and NativeAOT
   consumers, original memory and all seven throughput targets pass on final integrated source.
5. Only after all original gates pass: finalize the supported feature/profile claim, review/CI/merge,
   release documentation and publication. This branch alone cannot close P7.

## Evidence

The source-negative .NET 10 Release run has **6 failures /12 passes** across the 18 public/saved
selection cases: four ordinary default/Strict cases and two saved default/Strict cases fail on the
unchanged execution policy. Tree/Fallback controls pass. These failures are retained; no original
oracle or public execution requirement is weakened.

The affected suites pass **257 .NET 10 /226 .NET 8** in each of Release and Debug: **966 checks**,
zero failures or skips. They include all twelve public source/destination combinations, actual
representation and physical read boundaries, schema annotations, XDM tree controls and saved loader
selection. The forward arms use public defaults rather than forcing the internal forward switch.
The negative six failures remain retained separately.

The full Release and Debug unit suites each pass **7,526 .NET 10 /6,373 .NET 8** checks:
**27,798 total**, with zero failures or skips. The independent audit checks every TRX result and its
counters, all eight recorded changed-source hashes against `c889023`, unchanged runtime/test source,
and all four engine hashes in both engine and test output directories. The migration and getting-started
guides distinguish initial selection from the independent global context; this documentation clarification
does not alter the tested runtime.

[Draft PR #39](https://github.com/skuirrels/Xsl3/pull/39) retains the full-P7 merge boundary.
[Hosted CI 42127805496](https://github.com/skuirrels/Xsl3/actions/runs/42127805496) passed all four jobs
on `c889023`: Linux and Windows compatibility, NativeAOT and ordinary performance. The Linux job includes
reviewed analysis, Release/Debug strict inventories, legacy saved sampling and ordinary conformance.
These results do not replace the original streaming memory or throughput requirements.

Evidence is retained under
`/private/tmp/xsl3-p7-public-selection-20261105/`, including source/binary hashes, negative TRX, logs,
`audit-full.py` and `full-review.json`. No final-source qualification, merge or release readiness is claimed.

## Integration with merged grounding contracts

The branch incorporates main `8bdec4c` (merged PR #38) at `9f0fed3`. The handoff conflict is reconciled
without changing runtime or contract source: runtime C# matches the full-tested `c889023`; the
grounding and fork-retention suites, probe entrypoint and CI workflow match main exactly. Both
dedicated Debug contract steps remain.

The combined selected suites pass **357 .NET 10 /302 .NET 8** in each of Release and Debug:
**1,318 checks**, zero failures or skips. The TRX audit confirms all eight hidden-grounding cases
per configuration/framework, all 77/53 fork-retention cases, both optimizer-policy cases and all
13 verifier cases, alongside the public selection/declaration/read-boundary population. Evidence:
`/private/tmp/xsl3-p7-public-selection-20261105/reconciled/`. Hosted CI on this combined head is still
required. The preceding `c889023` run completed successfully before pushing the reconciliation, so
its compatibility evidence is retained rather than cancelled by the new push.
