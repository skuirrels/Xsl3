# P7 linked source ancestor stack

This isolated control starts from `074da9c` on `feature/p7-linked-source-stack`. Its baseline runtime
is corrected projection-discard candidate `a8d322a`; it includes the unadopted 256-event default.
No earlier rejected source-dispatch, metadata-bypass or compact-producer control is included.

The audited [supplemental CPU capture](https://github.com/skuirrels/Xsl3/actions/runs/42124527290)
resolves framework parsing and still records 1,517 leaf samples in `ReadNextCore`. This identifies
source bookkeeping as a candidate for measurement, not proof of a speedup. The profiler's changed
framework compilation policy is not used by the paired benchmark.

Every source frame already links to its parent. The document currently also maintains a `List<Node>`
containing the same ancestor chain, adding and removing references for each element. This control
replaces that duplicate list with the top frame. Starting an element sets the top; leaving an ended
element follows its parent. Fork source views, frozen replay views and header-only views initialize
the same chain. Disposal retains only the root, and the existing retained-frame diagnostic derives
the chain length from the stored depth. Reader advancement, numbering, text coalescing, schema and
namespace handling, replay, cancellation and event ordering remain on their existing paths.

## Qualification

- Run existing fork, replay, source-boundary, namespace, schema-header, disposal, identity, navigator,
  recursive-function, ordered-path and actual managed-retention tests in Release/Debug on .NET 8/10.
- Alternate frozen baseline/candidate binaries through three rounds of identity, accumulator, fork
  and source-document, both modes and exact 100 MB inputs. Preserve independent output validation,
  actual 4 GiB guard, four effective processors and disabled tiering; only engine DLL/PDB/XML differ.
- Require convincing improvement before considering adoption. Retain unsuccessful observations and
  reject inconclusive or regressing controls. Local timing cannot replace original hosted gates.
- Full P7 still requires both original memory matrices, all 126 throughput observations, all seven
  100 MB targets, full reviewed-source CI, supported public integration and release.

Evidence directory: `/private/tmp/xsl3-p7-linked-source-stack-20261105/`. The affected suite passes
1,609 .NET 10 and 1,325 .NET 8 checks in each configuration: **5,868 total**, no failures or skips.
This includes the original managed-retention positive and retained-position negative controls.
No test assertions were changed. All 48 paired local observations validate through the original
measurement validator, raw stdout comparison and independent output fingerprints.

| Workload | Candidate/baseline streaming median | Candidate stream/tree |
|---|---:|---:|
| Identity | 0.997754 | 1.071683 |
| Accumulator | 1.001247 | 0.854301 |
| Fork | 1.011363 | 1.048575 |
| Source-document | 0.995712 | 1.057820 |

These small mixed changes do **not** establish a useful local benefit. Baseline engine SHA-256 is
`91f491ffa4676894b2adba8d3932dd4b0047df0d8fe3471dfe18e4d562eb2226`; candidate is
`d3ad64ab7787fdbdc1378165b7217c208fe4808d33d524acaa76ee128d8e10f8`. The source patch, source hashes,
frozen consumer hashes, all samples and TRX files are retained in the evidence directory.

Because the profile identifying this source path came from Linux x64, a paired comparison on that
target remains useful before closing the experiment. The opt-in `revision_comparison` workflow job
builds the immutable `source_ref` baseline consumer and the workflow revision's candidate runtime.
`eng/compare-streaming-revisions.py` freezes two copies of the same consumer and changes only
`Xsl3.dll`/PDB/XML, alternates revisions across three rounds and runs the original benchmark controller.
All 48 observations must validate; raw reports retain timing, first output and input-consumption data.
It rejects inherited framework-compilation overrides. Default/scheduled acceptance remains unchanged.
CLI/YAML checks and a real four-observation 1 MB controller smoke pass. Smoke timing is not evidence
of improvement. Hosted paired results remain pending; no adoption or support claim is made.
