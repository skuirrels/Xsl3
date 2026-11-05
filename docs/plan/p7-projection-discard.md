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

## Current validation

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

The candidate is unaccepted. Full Phase 7, the supported-streaming claim and release remain incomplete.
