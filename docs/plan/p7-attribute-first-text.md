# P7 attribute-cache first-text control

## Scope and motivation

This isolated control starts from integration `a4f7301` on `feature/p7-attribute-first-text`.
The existing audited identity framework profile attributes 2,955 inclusive samples to `XmlAttributeCache.StartElementContent`
and 2,533 to `FlushAttributes`, within 26,343 CPU samples. Those overlapping inclusive counts identify
work to inspect; they are not removable elapsed-time percentages.

`XmlAttributeCache` stores each name and each value fragment in separate entries. A name entry
already has a text field that it does not use. The control stores the first value fragment there when it is a non-null string, avoiding a second entry and capacity check on the common single-text path. Additional
fragments and typed values retain their existing ordered entries. A new name clears its inline text
so reused storage cannot emit a previous attribute's value. Empty strings remain explicit string
writes; an attribute with no value writes remains empty. No text fragments are concatenated.

Duplicate detection, expanded-name comparisons, last-attribute ordering, namespace timing and writer
replacement callbacks are unchanged. `Count` remains used only to distinguish an empty/nonempty
cache. No public helper, saved format, fixture, canonical consumer or gate is changed, and no
input name/value or workload-specific condition is introduced.

This helper serves both tree and streamed execution. Qualification must therefore inspect both
absolute elapsed time and the original stream/tree ratios; a source-code or allocation reduction
alone cannot establish progress toward the throughput gates.

## Completion criteria

1. Preserve exact fragment ordering and typed-value object identity, duplicate replacement across
   prefixes/namespaces and hash collisions, empty/no-value attributes, capacity growth, late namespaces,
   cache reuse and writer callbacks. Verify focused event contracts against the unchanged baseline.
2. Pass full unit suites in Release/Debug on .NET 8/10. This includes the existing public/saved writer
   replacement, schema, copied-attribute identity, serialization and streamed source contracts.
3. Freeze source/binaries and audit actual test rows and PDB source checksums. Compare all 60 unchanged
   canonical paired observations against the current integration runtime, including the adopted merge
   improvement. Preserve original output fingerprints and actual resource/compilation settings.
4. A reliable local benefit requires a Linux comparison before adoption. Record an inconclusive or
   adverse outcome and close the control without repeatedly measuring unchanged code for a better result.

## Current evidence

The focused Release tests pass four cases on each framework. Each case exercises three cache reuse
cycles, either a short or growing attribute population, both namespace-timing modes, duplicate
expanded names under different prefixes, same-prefix-character hash collisions, first/multipart/empty
text, typed-first and text-before-typed content, and exact removal callbacks. Expected events are
specified independently of the cache's entry representation.

The complete Release and Debug suites pass **28,202 checks**: 7,641 .NET 10 and 6,460 .NET 8
in each configuration, with no failures or skips. All 16 new focused instances also pass against
the unchanged integration engine. These baseline controls establish the existing event contract;
they are not 16 additional distinct test cases.

The full builds exposed CS8604 at the fallback `Init(text)` after the new non-null fast-path
condition. The queued follow-up was stopped before any measurement; the full unit process finished
unchanged. The correction uses `Init(text!)` to preserve the existing asserted non-null contract.
Rebuilt Release/Debug .NET 8/10 engines pass all 16 focused instances with warnings treated as errors.
Their method bodies, signatures, local signatures and exception regions match the corresponding
full-suite engines: 13,602/13,595 methods in Release and 13,637/13,630 in Debug (.NET 10/.NET 8).
This is a compiler-analysis correction; both source states, binary sets and test results are retained.
The full suites were not repeated after this non-executable change.

Both audits independently check every TRX result and unique execution ID, binary hashes and all
409 tracked runtime /201 tracked test source checksums per PDB. The canonical paired comparison
uses the corrected frozen Release .NET 10 engine
`c0a6b5c6f104a311058d1201bf12c37fd314e6e22980f38686ad0eeabb9a6e92`.
The performance decision below closes this control without adoption.

The initial baseline-manifest attempt used Git's quoted filenames for a non-ASCII upstream fixture;
the corrected source inventory uses NUL-delimited paths and covers the engine/consumer/test sources.
No runtime or fixture was changed to accommodate that bookkeeping issue.

The baseline consumer is unchanged. Its engine is the already audited merge candidate
`e0c2e9fa1fefeee5bd02e0ca0964b63b1dc36c53b48a7e1337aebbc1346807bb`, whose runtime source is
identical to integration `a4f7301`. Evidence is retained under
`/private/tmp/xsl3-p7-attribute-first-text-20261106/`.

## Performance decision: closed without adoption

All **60** canonical observations validate: five unchanged workloads in tree/stream modes, three
alternating rounds per engine, exact 100,000,000-byte inputs, an actual 4,294,967,296-byte managed
guard, four reported processors, tiering disabled and normal ReadyToRun policy. Host: .NET 10.0.14,
macOS 27.0.1 ARM64. Every original output fingerprint matches; unchanged consumer/dependency hashes
and all 409 tracked PDB sources per engine verify.

| Workload | Baseline stream median (ms) | Candidate stream median (ms) | Candidate / baseline | Candidate stream / tree | Winner |
|---|---:|---:|---:|---:|---|
| identity | 2126.078042 | 2126.311417 | 1.000110 | 1.070977 | No meaningful winner |
| accumulator | 2076.091625 | 2078.643208 | 1.001229 | 0.854480 | No meaningful winner |
| merge | 3216.819584 | 3227.682667 | 1.003377 | 0.883469 | No meaningful winner |
| fork | 1532.230750 | 1522.340083 | 0.993545 | 1.007613 | No meaningful winner |
| source-document | 2120.850375 | 2119.316375 | 0.999277 | 1.056904 | No meaningful winner |

All baseline/candidate stream ranges overlap. The shared helper does not establish a benefit for
identity or source-document, and the original remaining throughput gates are unresolved. No Linux
rerun, integration adoption or repeat of unchanged code is justified. The comparison also overlapped
a 4.24-second read-only committed-source hash audit; this is retained as a local measurement limitation,
not a reason to discard inconvenient observations or claim a marginal improvement.

The full-test candidate, nullable-analysis correction, corrected frozen binaries, baseline controls,
method-body comparison, all raw observations and independent audits remain under the evidence root.
The corrected subtree additionally records commit `869e12a93731c2770cbd007aaf3f11b7bdcca5bb` and
733 matching committed-source hashes. The earlier stopped follow-up never started a benchmark.

Integration remains at runtime `a4f7301`; this cache runtime and its test are excluded. CI
[42167378456](https://github.com/skuirrels/Xsl3/actions/runs/42167378456) continues independently.
Its completed native/ordinary-performance artifacts are audited in the integration evidence record;
platform suites remain live at this check. Recheck the same run before starting any replacement.
All original final-source memory/throughput gates, compatibility, physical-I/O evidence, supported
claims, review/merge and matching release remain the full P7 completion boundary.
