# P7 S5-14: group read-position contracts

This increment follows [S3-25 verifier coverage](p7-verifier-coverage.md) on
`feature/p7-group-read-positions`. It strengthens the existing output-equality and early-output evidence
with observations of the actual source reader. No runtime change or acceptance relaxation is needed.

`StreamedGroupingTests.MembersAreReadOrDrainedBeforeTheNextGroupStarts` covers:

- `group-adjacent`, `group-starting-with` and `group-ending-with`;
- unused members, `head(current-group())` atomization, and consumption of every member;
- modern and legacy APIs on .NET 8/10, and saved assembly execution on .NET 10;
- forward execution and the ordinary tree path as a negative timing control;
- groups of one, three and twenty members, with the final group requiring drain through end of input.

Messages at group-body entry and exit sample element-start and element-end counts from a wrapping
`XmlReader`. On forward entry, all preceding members have ended and only the current first member's
header has been read. Unused members remain unread when the body returns; truncated consumption reads
exactly the first member. Full consumption reads every member and permits at most the following group's
header as boundary lookahead. No text or end tag of that next member is consumed before its body starts.
The next group's entry proves that abandoned members were drained before advancing. End-of-input counts
prove that the last abandoned group was drained too.

The tests also assert every group's identity and contents, the actual navigator representation, and borrowed
reader ownership. Tree controls must have consumed the entire source before the first group message, so
output equality alone cannot satisfy the forward read-position assertions. These are reader-position
contracts, not a memory-byte or throughput measurement.

## Validation

Release and Debug grouping regressions each passed: 60 on .NET 10 and 50 on .NET 8 (220 checks total).
Local logs and TRX files are under `/private/tmp/xsl3-p7-verifier-20261105/group-positions-*`; temporary
paths are not durable hosted evidence. PR #34's CI covers the preceding verifier increment, not these
new grouping tests. This increment requires its own reviewed-source CI before merge.

Full P7 still requires S3-04 retained-byte evidence, S3-27 internal-grounding coverage, S6-11/12 fork
grouping/sorting retention, all original performance gates and supported-streaming integration.
