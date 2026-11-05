# P7 fork capacity qualification

## Smaller-buffer follow-up: 256 events

`feature/p7-fork-capacity-256` starts from `aebb3a2`, retaining its historical 4,096-event control and
diagnostic tooling. Its only additional runtime change is reducing that candidate default to 256.
Relative to merged main `a5e89ac`, the only runtime difference is therefore the default from 64 to 256.
No allocation, producer, wait-policy, fixture, output oracle, heap guard or acceptance limit changes.

The 4,096-event original run [42112345048](https://github.com/skuirrels/Xsl3/actions/runs/42112345048)
fails push memory: fork peaks at 22,941,696 bytes versus the original 20,971,520-byte limit, with all
48 outputs correct and 15/16 workload budgets met. Its nightly job passes all 16 workload budgets
and all 48 outputs: exact 2,147,483,648-byte inputs, actual 209,715,200-byte managed guard, maximum
absolute whole-process RSS 32,321,536 bytes (fork). The throughput job is still running. The larger default is not accepted. Retain both results instead of substituting the later run.

The unchanged-runtime public-setting comparison previously measured a 15.1% lower Linux fork median
at 256 than at 64, with overlapping ranges and a still-failing 1.425856 stream/tree ratio. This motivates
measuring the smaller buffer's memory/throughput tradeoff; it does not establish an acceptance pass.

Default/clone assertions now expect 256. Projection and no-input-without-demand tests add 256 while
retaining 1/7/64/4,096 coverage. Existing saved assemblies keep their compiled capacities. The original
sixteen-workload memory matrices and complete 126-observation benchmark must qualify this exact source
before any adoption; local diagnostics and the earlier 4,096-event results cannot qualify it.

Local Release and Debug each pass 440 .NET 10 / 364 .NET 8 checks, 1,608 total with no failures or
skips. Exact source hashes and raw TRX are retained under
`/private/tmp/xsl3-p7-fork-capacity-256-20261105/`. Hosted original qualification is next.
No candidate has been merged or released.

## Historical 4,096-event candidate

## Candidate and evidence

The [public-setting comparison](p7-fork-buffer-policy.md) at unchanged runtime `a5e89ac` found
4,096 events reduced Linux fork streaming median by 27.9%, with all three observations below all
three 64-event observations and identical independent outputs. Stream/tree still fails at 1.255375.
This is evidence for a candidate, not proof of the original throughput target or memory safety.

`feature/p7-fork-capacity-qualification` starts at `42d2329`. Its only runtime change is the public
`XsltSettings.StreamingBufferCapacity` default: 64 to 4,096. Callers can still specify any positive
capacity; settings clones preserve it and existing saved styles retain their compiled constant.
The compiler already passes this property to fork construction. No canonical fixture, output oracle,
heap limit, RSS limit, timing target, demand contract or scheduling algorithm changes.

The no-read-ahead-without-demand regression now includes 4,096. Existing projection, fallback and
error contracts add 4,096 alongside 1/7/64 through both APIs and saved execution, with Release/Debug
compiler variants. The existing default/clone contract verifies the new default and explicit override.
No fixture supplies runtime configuration beyond the public API.

## Required before adoption

1. Affected Release and Debug fork, tee, cancellation, ownership, projection and physical read-boundary
   contracts pass on .NET 8 and .NET 10, including actual saved loading where supported.
2. The original sixteen-workload NativeAOT push and nightly matrices pass their unchanged exact input,
   absolute whole-process RSS and managed-heap limits. Retain all child reports and native provenance.
3. Re-run the original seven-workload, three-size, three-round throughput matrix. Preserve failing
   targets; a local or per-setting diagnostic does not replace this gate. Assess the candidate against
   the retained baseline without claiming a cross-run ratio alone isolates the default change.
4. Recheck full ordinary CI, strict inventories, saved/native consumers and final source provenance
   before merging. Public streaming support and the full Phase 7 objective remain unfulfilled until
   every original requirement passes, including the separate remaining correctness evidence.

Local Release and Debug each pass 403 .NET 10 / 335 .NET 8 checks (1,476 total), with no
failures or skips. Results and exact source hashes are retained under
`/private/tmp/xsl3-p7-fork-capacity-qualification-20261105/`. The physical-boundary tests compile
the canonical fork with the default settings through API, legacy and saved paths; they independently
check all output fingerprints, borrowed input ownership and first output before the guarded boundary.

Hosted original memory/throughput qualification is pending. The larger default is not adopted into
main or published.


## Audited 4,096-event nightly result

Run `42112345048`, source `aebb3a23aa26df7a9a0226d489100e61833fbd9a`, artifact
`13456366235` (`streaming-memory-nightly`, expires 3 February 2027) reports all 16 budgets and all
48 independent output checks passing. Every child is NativeAOT with dynamic code and trusted platform
assemblies absent. The external `wait4` and internal `getrusage` peaks are both retained and their maximum
is checked. All 32 generated stylesheets match the push artifact byte for byte. Original controller
validation was reapplied to every report and matched each raw child stdout. GitHub's archive digest is
`6dae4a1414db58ca18a6c086617e06d426e0af49720bf833f88735ac6b7b2847`.

The report and review are retained under
`/private/tmp/xsl3-p7-fork-capacity-qualification-20261105/hosted-nightly/` and `nightly-review.json`.
A nightly pass does not override the original tighter push-memory failure or qualify a different default.
