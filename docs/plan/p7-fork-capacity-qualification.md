# P7 fork capacity qualification

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
