# P7 phase A forward-entry evidence review

Bounded continuation requested on 5 November 2026, on `feature/p7-forward-evidence` in
[PR #33](https://github.com/skuirrels/Xsl3/pull/33). Starting source: `c3e68e8`, based on released
1.3.0 (`f9708eb`). Stop after reviewing this PR, closing S4-06/11/27's public-entry evidence gaps,
passing the affected tests and required hosted CI, and merging. No further performance optimization
or release is part of this increment. Full streaming remains unsupported.

## Review corrections and added evidence

- Fix a race in the S6-08 test: enqueue the second callback before signalling the first. The first
  callback also joins the second worker, proving real completion order rather than inferring it from
  the last message. Loaded and saved styles retain declaration-order output with a one-event buffer.
- Remove the accidentally committed `docs/.DS_Store` and ignore Finder metadata.
- S4-06: check a supplied `XdmNode` through all four destination overloads, with forward execution on
  and off, reusing the same selection. Require an actual tree source and non-forward input.
- S4-11: check both public APIs with a 1.0 stylesheet (development execution switch on/off) and a 3.0
  streamable stylesheet with forward execution off. Require a tree representation, late output and
  preserved borrowed-reader ownership.
- S4-27: pass a BCL schema-validating reader into both public APIs. Check integer element annotations
  and the parent's typed default attribute on all 20,000 records; derive the default value from the
  fixture schema. Forward runs must use a `StreamedNavigator` and emit before half the input is read;
  tree controls must remain trees and emit late. Keep the optional configured-schema dispositions.
- Update the plan entry points and distinguish strengthened evidence from complete byte/retention or
  silent-grounding proof. Library code, public settings, inventories and acceptance budgets are unchanged.

## Independent hosted-evidence review

Reviewed the raw artifacts from [run 41905961247](https://github.com/skuirrels/Xsl3/actions/runs/41905961247),
tested source `c8fb33e` on `feature/p7-probe-uris`. The runtime library and probe/controller/corpus sources
match this PR; the differing changes are additional tests and documentation.

| Original gate | Reviewed result |
|---|---|
| Push: 209,715,200 input bytes / 20,971,520-byte RSS limit | 16/16 measured workloads; all 48 children valid; maximum full-lifetime RSS 17,686,528 bytes |
| Nightly: 2,147,483,648 input bytes / 209,715,200-byte RSS limit | 16/16 measured workloads; all 48 children valid; maximum full-lifetime RSS 36,212,736 bytes |
| Paired benchmark: seven workloads, 1/100/1,000 decimal MB, three rounds per mode | All 126 observations valid; exact 100 MB target still fails for identity, accumulator, merge, fork and source-document |

The review checks exact input consumption, configured and effective heap guards, native host identity,
child exits, outer/inner RSS and tree/stream output fingerprints. All 32 small tree/stream baseline
fingerprints match the original 1.1.7 qualification after normalizing only the renamed URI keys.
The benchmark keeps its 4 GiB guard and strict no-slowdown target; the descriptive 5% band waives nothing.
This is a recorded cohort, not a controlled before/after speedup claim.

The memory result applies to the owner-approved HTTP-URI probe. It avoids ICU startup for those URI
parses; it does not reduce library memory for consumers resolving `urn:` resources. Globalization
remains enabled, and existing document/entity tests retain `urn:` coverage.

Artifacts: `streaming-memory-push` (13360387085), `streaming-memory-nightly` (13360978975),
`streaming-benchmark-nightly` (13363133782). Local recovered reports and review logs are under
`/private/tmp/xsl3-pr33-review-20261105/`; use the hosted run while retained or rerun the checked-in
tools if those temporary files are absent.

## Validation and remaining limits

The affected tests pass in both Release and Debug: **215 .NET 10 / 197 .NET 8** in each configuration,
**824 checks** total, with no failures or skips. The initial annotation fixture included an operand
that static analysis rejected; it was replaced by motionless annotation checks, without changing
the analyser or falling back from strict analysis. Python tooling runs 85 tests: 84 pass and one existing
platform-dependent case is skipped. Local documentation targets and whitespace checks pass. Hosted
[PR #33 checks](https://github.com/skuirrels/Xsl3/pull/33/checks) additionally cover the full unit suites,
ordinary conformance, strict inventory, NativeAOT and ordinary performance on the reviewed source.
This does not convert the failing experimental throughput workflow into a pass.

The original baseline map stays historical. Remaining evidence includes S3-04 byte accounting,
S3-25 per-case verifier execution, S3-27 grounding inside a forward run, S5-14 unused/truncated member
read positions and S6-11/12 grouping/sorting retention within fork. The next phase must be bounded
separately; this PR does not close full P7 or enable supported streaming.
