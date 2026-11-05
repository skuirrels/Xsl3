# P7 S6 fork grouping and sorting retention contracts

## Obligation

S6-11 and S6-12 in the [evidence map](p7-phase-a-evidence-map.md) previously asserted only
forward/tree output equality for `group-by` and sorted grouping inside `xsl:fork`. They did not
observe the required population buffering or its size. These contracts run on unchanged main
runtime `a5e89ac`, independently of the unaccepted performance controls and buffer-default candidate.

## Direct observations

`StreamedForkGroupRetentionTests` runs three valid single-grouping-child forks: one `group-by`
group containing every row; two nonadjacent `group-by` groups sorted in descending key order;
and adjacent pairs sorted in descending key order. Each uses 32 and 512 rows with unique text
payloads, event capacities 1 and 7, normal/debug compilation, both APIs and saved loading on .NET 10.
Expected group order and full output text come from the generated input and independent LINQ grouping.

A reader wrapper observes each actual text string returned to the engine once, retaining only a weak
reference. It returns the original string and never reads ahead, introduces a copy or requests grounding.
An ordinary `xsl:message` at the first group-body entry samples the following after full collections:

- Every source row has reached its closing element, and every text payload has been observed.
- Every observed original payload remains alive: 32 / 512 strings, containing 8,416 / 134,656
  characters. The 16-fold population increase has the corresponding 16-fold retained text size,
  despite an unchanged one- or seven-event tee bound.
- Sorted output visits the expected groups and preserves all member text. The execution capture
  independently records a completed stylesheet body and an actual forward source.
- After execution and disposal, every observed text payload is collectible. Borrowed input remains
  open until its test owner disposes it.

The unsorted adjacent-group control produces its first group before reading/retaining the whole
population, then returns all expected output and releases its payloads. A separate control explicitly
retains every observed source string in the test owner: all 32 remain alive after execution and are
collected only after that owner clears them. Thus the observation distinguishes required population
buffering from a rolling group and detects externally retained data.

This is direct source-payload reachability and character-count evidence, not total managed object bytes,
whole-process RSS, or a claim that buffered grouping uses constant memory. It does not impose a lifetime
on earlier completed groups beyond what semantics require. The original NativeAOT memory matrices and
full P7 acceptance remain independent requirements.

## Validation

Release and Debug each pass 77 .NET 10 / 53 .NET 8 checks: 260 total, no failures or skips. Full unit CI
includes the Release contracts; a dedicated CI step runs the Debug contracts on Linux and Windows.
Hosted CI is pending. Local logs/TRX are retained under
`/private/tmp/xsl3-p7-fork-group-retention-20261105/` and matching `...-release-final.log` /
`...-debug-final.log` files.

The initial diagnostic fixture had an invalid mixed fork-child form and then an arbitrary callback
on `current-group()` rejected by Strict analysis. Those failed attempts remain in the local evidence;
they are test-design failures, not runtime regressions. The final fixture removes that callback and
uses only ordinary grouping/member output and message instructions. No runtime source or static
analysis policy was changed to make these contracts pass.
