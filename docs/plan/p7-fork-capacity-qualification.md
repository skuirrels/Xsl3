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
absolute whole-process RSS 32,321,536 bytes (fork). The completed throughput job passes four of seven targets (details below). The larger default is not accepted. Retain both results instead of substituting the later run.

The unchanged-runtime public-setting comparison previously measured a 15.1% lower Linux fork median
at 256 than at 64, with overlapping ranges and a still-failing 1.425856 stream/tree ratio. This motivates
measuring the smaller buffer's memory/throughput tradeoff; it does not establish an acceptance pass.

Default/clone assertions now expect 256. Projection and no-input-without-demand tests add 256 while
retaining 1/7/64/4,096 coverage. Existing saved assemblies keep their compiled capacities. The original
sixteen-workload memory matrices and complete 126-observation benchmark must qualify this exact source
before any adoption; local diagnostics and the earlier 4,096-event results cannot qualify it.

Local Release and Debug each pass 440 .NET 10 / 364 .NET 8 checks, 1,608 total with no failures or
skips. Exact source hashes and raw TRX are retained under
`/private/tmp/xsl3-p7-fork-capacity-256-20261105/`. Hosted original qualification is running on `deed3c1b31657e0740d532182cb1e39eeea15aa4`;
both original memory results are audited below. The full throughput job remains pending.
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

## Audited 256-event push result

[Run 42116432334](https://github.com/skuirrels/Xsl3/actions/runs/42116432334), source
`deed3c1b31657e0740d532182cb1e39eeea15aa4`, passes all 16 original push-memory budgets and all
48 independent output checks. Each measured input is exactly 209,715,200 bytes with an actual
20,971,520-byte managed-heap guard. All children report NativeAOT execution (dynamic code and trusted
platform assemblies absent). The maximum of external full-lifetime `wait4` and internal
`getrusage(RUSAGE_SELF)` is checked without subtraction. The largest measured peak is **17,395,712 bytes**
(conditional-content); fork peaks at **17,305,600 bytes**. All baseline tree/stream output fingerprints
match. Original controller validation was reapplied to every report and matched each raw child stdout.

Artifact `13457196442` (`streaming-memory-push`, expires 3 February 2027) has GitHub archive digest
`6aef2ed82cbe86db92104f9117698aa3622c752d43e6de6f3f351b6d770b3282`. Report SHA-256:
`cffad0fd35bdc6e14b5c8f43ac05b17ac92a141844c9fc50ff29eef234146432`.
Local raw files, executable re-audit and per-workload peaks are retained in
`/private/tmp/xsl3-p7-fork-capacity-256-20261105/hosted-push/`, `audit-push.py` and `push-review.json`.

This establishes the original push-memory pass for the named candidate only. The matching nightly
result is recorded below; throughput, final integration and full P7 completion remain unproven.

## Audited 256-event nightly result

The same [run 42116432334](https://github.com/skuirrels/Xsl3/actions/runs/42116432334) and source
`deed3c1b31657e0740d532182cb1e39eeea15aa4` pass all 16 original nightly budgets and all 48 independent
output checks. Measured inputs are exactly 2,147,483,648 bytes with the actual 209,715,200-byte managed
heap guard. All children report NativeAOT execution, and both full-lifetime peak counters pass. The largest
measured whole-process peak is **32,428,032 bytes** (fork). Every raw stdout matches its report, every
baseline tree/stream fingerprint matches, and all 32 generated stylesheets match the push artifact.

Artifact `13456263863` (`streaming-memory-nightly`, expires 3 February 2027) has GitHub archive digest
`12c7262e32bb776665ff75c09fff02154b995b7ac1860cfce8db6726486ef25c`. Report SHA-256:
`a631c71f4640115dc8d267b094ba3aa16bf878072b784990939bd3f828d92f94`.
Raw files, executable re-audit and per-workload peaks are retained under
`/private/tmp/xsl3-p7-fork-capacity-256-20261105/hosted-nightly/`, `audit-nightly.py` and `nightly-review.json`.

Together these qualify all **32 original memory budgets and 96 outputs** for the named 256-event source.
The full 126-observation throughput run is still active; no passing throughput or final P7 claim is made.

## Independent rejected controls

Two later controls started independently from unchanged main `a5e89ac` with capacity 64. Neither is part
of this candidate or a proposed composition:

- `feature/p7-source-child-header`, `9fec679`: removal of an unreachable fork-header arm in source-node
  creation. All 7,205 selected Release tests and 48 paired observations pass, but no useful speedup appears.
- `feature/p7-accumulator-state-traversal`, `2bbc789`: fixed-order array traversal of immutable accumulator
  applicability state. All 2,552 selected Release tests and 24 paired observations pass. Combined streaming
  improvement is only 0.84% with overlapping ranges; the unchanged tree cohort is 3.45% slower. Rejected.

Each branch retains its own review record and source. Their local measurements do not qualify this buffer
candidate, and their runtime changes must not be silently folded into the eventual release.

## Source provenance and next CPU investigation

A Portable PDB source audit matches all **409 tracked library source documents** to the tested commit
`deed3c1b31657e0740d532182cb1e39eeea15aa4`, including `XsltSettings.cs`. The other three PDB documents
are generated assembly attributes, assembly information and regex source, and are not claimed as Git
source matches. Both memory jobs retain the identical library PDB SHA-256
`79090f53c53d53768e5f9185516cb52483ca7d8527d5c9a8c2ad04d48e609b72`.
Their publish logs build the local library and native probe from the workflow checkout. Separately built
native executables and saved-style assemblies are not claimed to be byte-identical. The audit, source
list and executable hashes are retained in `audit-sources.py`, `push-pdb-documents.json` and
`source-review.json` in the 256-event evidence directory. This verifies source checksums and build
provenance, not an independent reconstruction of native machine instructions.

[CPU diagnostic 42120106652](https://github.com/skuirrels/Xsl3/actions/runs/42120106652) uses controller
`a9bdf29` and explicitly selects runtime source `deed3c1`. It profiles the canonical 1 GB fork child
with the actual 4 GiB heap guard and tiering disabled, preserving its output oracle. Its purpose is to
locate remaining costs after changing buffer capacity; instrumented timing cannot qualify throughput.
The original full acceptance run remains active and is not restarted or replaced by this diagnostic.

PRs [#37](https://github.com/skuirrels/Xsl3/pull/37) and
[#38](https://github.com/skuirrels/Xsl3/pull/38) were reviewed without additional findings and marked
ready for review. Their remaining required CI jobs must pass before merging.

## Completed 4,096-event throughput qualification

[Run 42112345048](https://github.com/skuirrels/Xsl3/actions/runs/42112345048), source `aebb3a2`, completed
all 126 canonical observations with correct outputs, matching per-size fingerprints, exact inputs and
actual 4 GiB heap guards. The runtime is .NET 10.0.14 on Ubuntu 24.04.5 x64 with four effective CPUs.
The original controller hash, retained engine/consumer hashes, all raw stdout and 409 tracked library
PDB source hashes were independently checked. No observation was discarded.

| 100 MB workload | Stream / tree median | Original target |
|---|---:|---|
| identity | 1.170416 | fail |
| adjacent | 0.826923 | pass |
| accumulator | 0.991516 | pass |
| iterate | 0.842619 | pass |
| merge | 0.925181 | pass |
| fork | 1.280487 | fail |
| source-document | 1.155759 | fail |

The larger buffer still fails three throughput targets as well as the original push-memory fork budget.
Its 1.280487 fork ratio does not qualify the 256-event source. Cross-run differences are not attributed
solely to capacity, and the five-percent display band waives no target.
Artifact `13457729152` expires 3 February 2027, with GitHub archive digest
`6905608f117111fdebee74c4f8cb2344fabb81efb4eda60ed1e2be5511c00269`. Report SHA-256:
`8f9bc988dcc4e781f805c888c8b439d94ba6608564cdfbc3f60fe3894c2ea411`.
Raw files and executable audit are in the 4,096-event evidence directory under `hosted-throughput/`,
`audit-throughput.py` and `throughput-review.json`.

## Completed 256-event CPU diagnostic

Run `42120106652` completed with correct output on the canonical 1,000,000,000-byte fork input,
actual 4 GiB heap guard, four CPUs and tiering disabled. The retained source revision and engine/consumer
hashes match the report. It recorded 83,789 CPU samples (69,026 with an engine frame), 5,798 symbols and
zero lost events. Overlapping inclusive samples include `ReadNextCore` 32,742, source `ReadProjected`
19,860, `WaitForPublication` 19,737 and `StreamedInputReader.Read` 9,048. Scheduling/spinning remains
visible; these are CPU samples, not off-CPU durations or removable elapsed time.

Source inspection confirms that projection currently drops excluded events only after allocating and
parsing their navigator frames. An incremental discard path is the next isolated control: retain every
underlying reader advance, validation/error/cancellation behavior and the per-batch scan budget while
avoiding descendant frames in compiler-proven excluded subtrees. Replay, consumption tracking and active
observer paths must retain ordinary parsing. Calling unbounded `Skip()` would violate the demand/scan
contract and is not an acceptable implementation.

CPU artifact `13457479106` expires 3 February 2027, GitHub digest
`59b4aecf1149a0242dd0099adf0a0eb0eb94a751812ba29a1f13ca87f04a2b6b`. Raw files and `cpu-review.json`
are in the 256-event evidence directory. Instrumented elapsed time is not throughput qualification.
