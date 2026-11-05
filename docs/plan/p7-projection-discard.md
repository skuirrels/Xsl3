# P7 incremental discard of excluded fork subtrees

This isolated control starts from `37c60bf` on `feature/p7-projection-discard`. Its baseline runtime is
the 256-event candidate `deed3c1`, which passes both original memory matrices but has not been adopted;
its full throughput run remains independent. No prior rejected control is included.

## Evidence and design

The [256-event CPU profile](https://github.com/skuirrels/Xsl3/actions/runs/42120106652) records 19,860
inclusive samples under source `ReadProjected`. These are overlapping CPU samples, not removable elapsed
time. Source inspection shows that excluded descendants are currently parsed into navigator frames
before projection discards their events.

The control starts an incremental discard only when the compiler-proven projection excludes a nonempty
element and the fork is its sole source consumer. Active observers, replay capture, consumed-header or
match tracking, descendant cursors and nested consumption retain ordinary parsing. No new name/path
assumption or fixture-dependent runtime branch is introduced.

Each discard step calls the existing reader chain once. It resolves explicit entity references, preserves
validation and reader-wrapper state, and performs the ordinary cancellation/disposal check. Every step
counts against the unchanged source-batch scan budget; it does not call unbounded `Skip()` or start source
work without a prong's demand. The omitted element keeps its source frame while descendants need none.

The source document owns the partial-discard state. When every prong finishes early, no extra source reads
are performed merely to restore parser state. A later ordinary navigation demand completes the same
omitted subtree before resuming at the next source event. Disposal releases the pending frame. This
continuation is necessary: storing it only on the fork would leave the source's open-frame stack out of
step with a reader stopped inside nested discarded content.

## Contracts and completion criteria

1. Existing projection, caller-demand, first-output, malformed-content and schema-error contracts remain
   passing with original cases and capacities.
2. An early fork exit at capacities 7/64/256 leaves a 512-child omitted subtree partially read. Later
   navigation on the same source consumes its remainder and reaches the correct outer sibling.
3. Consumed-header observers still see the excluded descendants through the ordinary parser fallback.
4. Explicit entity references inside discarded content resolve correctly, and the following selected
   node preserves its metadata and value.
5. Cancellation triggered inside nested discarded content stops at that reader advance and leaves a
   borrowed reader open.
6. A useful paired throughput improvement, broader Release/Debug fork/replay/identity coverage, saved
   execution and fresh original hosted qualification are required before adoption. Both public APIs,
   buffers, output oracles, memory limits and all seven final throughput targets remain required.

## Initial validation and rejected first candidate

The initial unchanged contract population passes 145 .NET 10 / 105 .NET 8 Release checks. With six new
cases, the targeted population passes 151 / 111 (262 total, no failures/skips). The first test build had
an underscore lambda parameter mistaken for a discard; that compile failure is retained and corrected.
No runtime failure was hidden or test assertion removed.

The completed paired local diagnostic validates all 48 observations. It alternates baseline/candidate order over three rounds for
identity, accumulator, fork and source-document, both modes, exact 100,000,000-byte inputs, four effective
processors, tiering disabled and the actual 4 GiB heap guard. All original consumer binaries stay
byte-identical except the candidate engine DLL/PDB/XML. The 48 observations cannot replace hosted gates.

Fork streaming median falls from 1,703.886875 to 1,486.825500 ms (12.739% lower), with all three
candidate samples below all three baseline samples. First output remains at 36,864 input bytes.
Candidate/baseline streaming ratios for identity, accumulator and source-document are 0.998114,
0.999847 and 1.001977 respectively, with overlapping ranges. The candidate fork stream/tree ratio
is still 1.004302, so this local result is useful evidence for qualification, not a throughput pass.
Broader Release/Debug streaming, fork, replay, identity and read-boundary regression testing follows.

Evidence: `/private/tmp/xsl3-p7-projection-discard-20261105/`, with source/binary manifest, patches,
raw test TRX, frozen engines and comparison scripts. Engine SHA-256:

- Baseline 256: `65ab315c5757321647170c1336150a4110ee1f9d53bb0e75bfe35ab1d2edfa3b`.
- Candidate: `3cc280164165870e56cde7196ae8c1ad9b1f2390e3fea90dcf8c4576fb9da69f`.

The broader Release run exposed three failures in the existing
`ForkContinuationContractsTests.OmittedMixedContentKeepsFollowingPositionIdentity` cases on each framework (.NET 10: 4,728 passed / 3 failed; .NET 8: 3,806 passed / 3 failed):
following row positions changed from `[8, 12]` to `[3, 7]`. The first candidate `fb6d4cd` is therefore
**rejected**, regardless of its measured speedup. Its original hosted qualification
[42122349414](https://github.com/skuirrels/Xsl3/actions/runs/42122349414) was cancelled after this failure
was established; it cannot qualify the corrected source. The original identity assertions remain unchanged.

The correction counts omitted logical positions without allocating their frames. Element, comment and
processing-instruction events advance the position once; adjacent retained text, CDATA and whitespace
across entity boundaries advance it only once, using the ordinary whitespace-retention rule. A new
8-case control compares projected and unprojected positions across explicit text/markup entities,
ordinary/preserved whitespace and capacities 1/7/64/256. The corrected source passes 1,523 .NET 10 / 1,245 .NET 8 affected checks in each of Release and
Debug: **5,536 total**, zero failures/skips. These include all fork, replay, observer, source-boundary,
source-document, namespace, schema-header, disposal, copied-attribute identity and navigator contracts
selected by the retained validation script. The initial broad run's other 8,534 checks remain retained;
its six failures are not relabelled as passes. Full final-source CI remains required.

Fresh paired timing completed with 48 valid observations from frozen corrected binaries and controllers in
`/private/tmp/xsl3-p7-projection-discard-corrected-20261105/`. The corrected engine SHA-256 is
`91f491ffa4676894b2adba8d3932dd4b0047df0d8fe3471dfe18e4d562eb2226`.
The first candidate's 12.7% result does not qualify this correction.

The corrected fork streaming median falls from 1,694.259208 to 1,520.876458 ms (**10.234% lower**).
All three candidate samples (1,505.436917 / 1,520.876458 / 1,540.604208 ms) are below all three baseline
samples (1,691.553709 / 1,694.259208 / 1,726.833709 ms). First output is unchanged at 36,864 input bytes.
The fork stream/tree ratio is 0.954547 in this local cohort. Identity, accumulator and source-document
candidate/baseline streaming ratios are 1.001898 / 1.000816 / 0.999673, with overlapping ranges.
All independent outputs, original measurement validation, actual 4 GiB guards, four effective CPUs,
raw stdout comparisons and frozen hashes pass. Local timings do not establish hosted acceptance.

Corrected source `a8d322ac6bfd094c65a61477a519d0a426059c77` is under fresh original qualification in
[run 42123882563](https://github.com/skuirrels/Xsl3/actions/runs/42123882563): both full memory matrices
and all 126 throughput observations. Results remain pending. A separate existing public-setting
diagnostic compares capacities 64/256/1,024/4,096 on that same immutable runtime in
[run 42124002254](https://github.com/skuirrels/Xsl3/actions/runs/42124002254). It does not change the
candidate default or acceptance limits. Diagnostic run `42123951259` was cancelled because its source
SHA was mistyped; it supplies no measurement evidence.

Full Phase 7, the supported-streaming claim and release remain incomplete.

## Audited corrected push memory

[Run 42123882563](https://github.com/skuirrels/Xsl3/actions/runs/42123882563), immutable source
`a8d322ac6bfd094c65a61477a519d0a426059c77`, passes all **16 original push budgets and 48 outputs**.
Every measured input is exactly 209,715,200 bytes with the actual 20,971,520-byte managed guard.
Every child is NativeAOT; both external whole-lifetime `wait4` and internal `getrusage` peaks are
retained and their maximum checked without subtraction. The largest peak is **17,453,056 bytes**
(fork). Original controller validation, raw stdout comparisons and baseline output equivalence pass.

Artifact `13459529058` (`streaming-memory-push`, expires 3 February 2027) has SHA-256
`8b6fd3d0c29782da952955ed4344d61f16411ccc29aeff0d62b2c17eebbe075f`, independently verified on
the downloaded ZIP as well as reported by GitHub. Report SHA-256:
`ba38299832fd2f39591c2ffc89ae251cff4bcf23a9d598fc3373c04a76145edf`.
All 409 tracked library PDB document hashes match the exact tested source; three generated documents
are excluded from Git comparison. Raw artifacts, executable memory audit and source results are in
`/private/tmp/xsl3-p7-projection-discard-corrected-20261105/{hosted-push,audit-push.py,push-review.json,push-source-review.json}`.
Nightly memory and original throughput subsequently completed as recorded below; full qualification
still fails the throughput target.

## Corrected runtime with explicit capacities

[Run 42124002254](https://github.com/skuirrels/Xsl3/actions/runs/42124002254) completes 24 valid
100 MB observations through the public compiler setting on the same immutable runtime. Source and
consumer hashes match their manifests; every child uses four effective CPUs, disabled tiering and
the actual 4 GiB managed guard. All independent outputs match and every configured capacity is
reported by the child. Medians are milliseconds:

| Capacity | Tree | Streaming | Stream/tree | First output input bytes |
|---:|---:|---:|---:|---:|
| 64 | 3434.313 | 5194.028 | 1.512392 | 36,864 |
| 256 | 3497.644 | 4427.704 | 1.265910 | 36,864 |
| 1,024 | 3423.699 | 4420.987 | 1.291289 | 40,960 |
| 4,096 | 3435.227 | 3858.637 | 1.123255 | 40,960 |

None meets the throughput target in this diagnostic. The 1,024-event samples overlap 256 and have
virtually the same median, so they do not justify another default change. The 4,096-event median is
lower but still fails; its original tighter memory failure remains relevant until any new source is
separately qualified. The current candidate default remains 256.

Artifact `13459997700` (expires 3 February 2027) has GitHub archive digest
`53dfbaf19cd30ef06f5905ee2d45661d2c02c17d56408c61a1704b1cf5d29692`.
Retained engine SHA-256: `48104685f3530ed28ba5f66304e410221e39e1d27019de5ebeecf7a8aba923ea`.
Evidence and raw reports are under `/private/tmp/xsl3-p7-discard-capacity-20261105/`; `review.json`
retains all observations, medians and first-output positions. This public-setting diagnostic does not
replace the original seven-workload gate or qualify a new capacity.

## Completed original qualification and paired Linux follow-up

The same `a8d322a` [qualification run](https://github.com/skuirrels/Xsl3/actions/runs/42123882563)
passes all 16 nightly budgets and 48 original outputs, at exactly 2,147,483,648 input bytes and the
actual 209,715,200-byte heap/absolute RSS limits. Maximum whole-process RSS is 32,309,248 bytes (fork).
Together with push, all 32 budgets and 96 outputs pass. The downloaded nightly artifact `13462241149`
has independently verified SHA-256 `97c7aca87d5bee8ae5fcf0b052c6b8d894928122c398cd6566f4dcf69cd98bcf`.

All 126 original throughput observations validate, including raw output, fingerprints, actual 4 GiB
guards, all sizes/modes/rounds, four CPUs, disabled tiering and binary/controller hashes. All 409 tracked
PDB source hashes match. Only **3/7** 100 MB targets pass:

| Workload | Stream/tree median | Target |
|---|---:|---|
| identity | 1.064619 | Fail |
| adjacent | 0.745620 | Pass |
| accumulator | 0.861578 | Pass |
| iterate | 0.798460 | Pass |
| merge | 1.052924 | Fail |
| fork | 1.447580 | Fail |
| source-document | 1.066196 | Fail |

Artifact `13462492685` expires 3 February 2027; its ZIP digest is independently verified as
`804c2d708cc4ca25c49224dc08f850d1ae80f231741a59ba028c2a23ea3876e9`. The report digest is
`ecefa1b6d7c4b235ababf9bd3a574b164a32bd23b943accee480f39d41ab70ef`. Raw files and executable audits
remain in `/private/tmp/xsl3-p7-projection-discard-corrected-20261105/`. No failed target is waived.

The local paired gain and separate hosted qualification do not establish a causal hosted improvement.
`feature/p7-discard-host-comparison` therefore reuses the already exercised paired revision controller
from `7b24202`, with this exact runtime and baseline `deed3c1b31657e0740d532182cb1e39eeea15aa4` on
one Linux host. The diagnostic explicitly includes identity, accumulator, **merge**, fork and
source-document: 60 observations, three alternating rounds, both modes, exact 100 MB and unchanged
canonical consumer/oracles/4 GiB guard. Only the engine DLL/PDB/XML differ between frozen consumers.
This branch adds no runtime change beyond `a8d322a`; it excludes the rejected linked-stack control.
Original acceptance jobs remain unchanged unless the opt-in revision comparison is requested.

Adoption requires a useful paired benefit without unexplained regressions, then every original gate
on final integrated source. A successful diagnostic job alone does not establish that benefit.

[Paired Linux run 42131056101](https://github.com/skuirrels/Xsl3/actions/runs/42131056101) is complete.
All 60 observations, raw outputs, actual guards, unchanged consumer files, archive digest and 409
tracked PDB source hashes per variant verify. All five fingerprints also match the original full
qualification. Candidate `9fbd8e1` has runtime source identical to corrected discard `a8d322a`.
Streaming medians are milliseconds:

| Workload | Baseline `deed3c1` | Corrected discard `a8d322a` | Candidate/baseline | Winner |
|---|---:|---:|---:|---|
| identity | 3161.721 | 3113.142 | 0.984635 | No meaningful winner; ranges overlap |
| accumulator | 3263.663 | 3296.698 | 1.010122 | No meaningful winner; ranges overlap |
| merge | 5324.816 | 5363.994 | 1.007358 | No meaningful winner; ranges overlap |
| fork | 3218.088 | 2755.886 | 0.856374 | Corrected discard |
| source-document | 3136.589 | 3141.849 | 1.001677 | No meaningful winner; ranges overlap |

All three fork candidate observations (2808.725, 2755.886, 2479.421 ms) are below all three baseline
observations (3218.088, 3326.008, 3209.348 ms). The 14.4% lower median supports keeping corrected
discard as the next candidate's foundation. Its fork stream/tree ratio remains 1.132795 in this
diagnostic, and the separate original qualification's four failed targets remain failed.

Artifact `13463177772` expires 3 February 2027; verified ZIP SHA-256:
`ad84e2a36e14258d858dd01bdbc4f030777e3688f3e78ab06ee6f97f8af1df51`. Raw files, both PDB inventories
and executable `audit.py` are retained at `/private/tmp/xsl3-p7-discard-host-comparison-20261105/`.
This result does not qualify a supported-streaming claim, merge of public integration or release.
