# Relative value paths and deferred signatures, 2026-10-25

The later [comma/content follow-up](streaming-comma-content-2026-10-25.md) records additional path,
text-construction and debug fixes. The measurements below describe this relative-value checkpoint.

This follow-up continues the [bounded value/merge checkpoint](streaming-bounded-values-merge-2026-10-25.md)
on `feature/p7-streaming-analysis`. Phase 7 remains active and `supports-streaming` remains `no`.

## Relative and compound value populations

Eleven new regression cases initially failed. `sum(.//n)` and `string-join(.//n, '|')` did not receive
value-replay eligibility even though the corresponding explicit descendant axis did. A stable context
item and predicate-free self steps now participate in the same proof. Eligible mixed sequences lower
their individual value populations before combining them with grounded items.

Compound paths such as `.//section/page-count` need a cursor over final selected nodes. Nested child
iteration inherited the section cursor and could not retain nested page-count values when an ancestor
value consumed the subtree. Value-only compilation now scans the final node test once and matches the
predicate-free path backwards against ancestor headers. Disjoint child/self prefixes are hoisted.
No runtime prefix-match facts are assumed to survive serialization. Direct descendant selections keep
their existing lowering.

A second defect became visible once compound paths used historical headers: deserialized local names,
prefixes and namespace URIs were ordinary strings, while `XPathNavigator.NameTable` advertised atomized
names. Generated QName tests use reference equality in that case and incorrectly rejected nested
matches. Replay now atomizes element, attribute and namespace names against the owning document table.
Character data and attribute values remain source values.

Coverage includes relative/absolute/explicit paths, multiple descendant steps, self steps, unrelated
parent names, namespaces, mixed sequence boundaries, both public execution APIs, debug compilation,
normal/debug saved assemblies, and both in-memory and spilled replay. General forward-navigation
position guards remain enabled. Deferred predicates and higher-order continuations are outside this
new lowering.

## Function signature initialization

Checking a saved stylesheet's computed function fingerprint previously parsed every built-in type
signature. The registry now initializes stable IDs, names and arities eagerly, with a thread-safe lazy
signature per definition. Argument coercion and return-type inspection still resolve the canonical
signature through the existing parser and resolver. The fingerprint algorithm and saved format are
unchanged; no fingerprint constant is used by the implementation.

Four regression tests cover metadata-only access, concurrent publication, retained parse errors, and
parallel resolution of every canonical signature, including variadic lookup and an independently
recomputed registry fingerprint. All 236 definitions resolve.

Matched macOS arm64 NativeAOT profiles use normal globalization and identical invariant-culture
instrumentation. These are startup diagnostics, not the process RSS gate. Each source snapshot has
402 core/configuration files. The corrected before profile preserves the preceding implementation;
the deferred profile changes only function signature initialization.

| Observation | Before | Deferred |
|---|---:|---:|
| Absolute RSS before registry access | 12,206,080 | 12,124,160 |
| Absolute RSS after metadata/fingerprint access | 13,565,952 | 12,779,520 |
| Cumulative allocated bytes after metadata access | 912,312 | 397,760 |
| Absolute RSS after resolving all signatures | 13,647,872 | 13,697,024 |
| Cumulative allocated bytes after resolving all signatures | 912,312 | 986,112 |
| Computed fingerprint | -463085345 | -463085345 |
| Parameter/return type parts visited | 587 | 587 |

The benefit is avoiding unused type initialization. Resolving every signature incurs additional lazy
metadata allocations. The original preliminary profile formatted integers with the current culture
between snapshots; the matched profiles correct that instrumentation without disabling ICU. Raw logs
are `/tmp/xsl3-p7-registry-before-corrected-profile/run.log` and
`/tmp/xsl3-p7-registry-deferred-isolated-profile/run.log`. No RSS baseline subtraction is used.

## Functional and saved-binary verification

Full Release and Debug unit suites pass **3,125 on .NET 10 / 3,029 on .NET 8**. The solution builds.
NativeAOT normal/debug saved compound paths pass for memory and disk-spill captures, namespace tests,
mixed sequences and supplementary Unicode. All retained saved probes, old materialized merge code,
and the unchanged version-2 fork consumer also pass. The macOS arm64 publish has no trim/AOT warnings.

Evidence is under `/tmp/xsl3-p7-deferred-relative-verified-` and
`/tmp/xsl3-p7-deferred-relative-native/`. The native source manifest contains 402 files; the Linux
source/controller/probe/test manifest contains 567 files. An initial native driver invocation used the
project filename as its executable name and exited 127; the verified run uses the published assembly
name `Xsl3.Tests`, without rebuilding, and exits 0. Both logs are retained.


All eight complete conformance profiles have no new failure IDs. Strict forward inputs improve from
**2,681/30/1 to 2,683/28/1**, closing `streamable/streamable-037` and `streamable/streamable-039`.
The seven unmatched strict expected-error codes remain. Other profiles and their code counts are
unchanged. No regression baseline is edited. `docs/conformance-sets.md` is regenerated from these reports.

| Profile | Passed | Failed | Not run | Unmatched expected codes |
|---|---:|---:|---:|---:|
| XSLT 3.0 | 8847 | 21 | 41 | 34 |
| XSLT 2.0 | 4588 | 9 | 11 | 7 |
| Streaming unstreamed | 2692 | 19 | 1 | 5 |
| Streaming strict | 2683 | 28 | 1 | 7 |
| S1 posture/sweep | 790 | 129 | 0 | 0 |
| Schema-aware | 843 | 1 | 2 | 6 |
| Schema-aware strict | 842 | 2 | 2 | 6 |
| QT3 | 21884 | 0 | 99 | 67 |

The normal W3C harness passes 46 facts; upstream passes 1,654 and differential passes 394 tests on each
framework. All 13 streaming controller tests pass. The strict audit retains exit 1 because its remaining
failures are still present; the complete regression driver exits 0. Profile comparisons and failure IDs
are retained in `/tmp/xsl3-p7-deferred-relative-reports/profile-comparison.json`.


## Exact native memory gates

The local Linux arm64 NativeAOT nightly matrix passes **48 children / zero failures**. All 16 measured
streams consume exactly **2,147,483,648 input bytes**, validate independent output fingerprints, and use
an effective **209,715,200-byte** managed allowance. The push matrix completes **48 children with 16 RSS
failures** at a requested **209,715,200-byte** input and **20,971,520-byte** allowance. Twelve measured
streams consume and validate the full requested input. Iterate, merge, fork and overlap-values exceed
the budget during startup/warmup, consume zero measured input, and have no measured output fingerprint.
Their early exits are failures, not completed transformations.

Both native evidence flags are false and ICU remains enabled. Full-lifetime RSS is the maximum of
child getrusage and outer wait4, including startup, warmup, execution and reporting. No startup or
baseline subtraction is used. The cached SDK image is
`sha256:70b5de4a18461652574704cee7c7f8797788c54aa45a5de4918ea1cae97cd1e8` (SDK 10.0.400/runtime 10.0.11).
The managed intermediate is still rejected by the native guard. The Docker driver exits 1 for the
retained push failures. This is local Linux arm64 evidence; remote Linux x64 CI remains unverified.

| Workload | Nightly full-lifetime RSS bytes | Push measured input bytes | Push full-lifetime RSS bytes | Push outcome |
|---|---:|---:|---:|---|
| identity | 29126656 | 209715200 | 24469504 | budget-exceeded |
| adjacent | 26681344 | 209715200 | 25829376 | budget-exceeded |
| accumulator | 30285824 | 209715200 | 25632768 | budget-exceeded |
| iterate | 27987968 | 0 | 22413312 | budget-exceeded-before-measurement |
| merge | 30834688 | 0 | 21565440 | budget-exceeded-before-measurement |
| fork | 30670848 | 0 | 21532672 | budget-exceeded-before-measurement |
| source-document | 29097984 | 209715200 | 24702976 | budget-exceeded |
| climbing-union | 30339072 | 209715200 | 25628672 | budget-exceeded |
| climbing-intersection | 30289920 | 209715200 | 25571328 | budget-exceeded |
| type-assertion | 25952256 | 209715200 | 24821760 | budget-exceeded |
| type-inspection | 25784320 | 209715200 | 24719360 | budget-exceeded |
| copy-select | 29192192 | 209715200 | 24510464 | budget-exceeded |
| conditional-content | 29360128 | 209715200 | 24604672 | budget-exceeded |
| template-parameter | 26816512 | 209715200 | 25800704 | budget-exceeded |
| function-parameter | 30535680 | 209715200 | 25870336 | budget-exceeded |
| overlap-values | 30703616 | 0 | 21352448 | budget-exceeded-before-measurement |

Raw reports and measured summaries are under `/tmp/xsl3-p7-deferred-relative-linux/`. Native gate
elapsed times are diagnostic because functional checks overlapped part of the matrix. Ordinary and
paired process timings start after the owned correctness/native jobs finish. The 402 core and 567 Linux
current/frozen source hashes match. The implementation/test increment and before/after hashes are
`/tmp/xsl3-p7-deferred-relative-increment.patch` and `.json`; no commit or push is performed.


## Ordinary performance gate

After the owned correctness/native jobs finish, three alternating pairs against
`0f2ddcba2c8791f4e8cc6b7b91fe371164ed2021` pass the ordinary gate with exit **0**. The same current harness
runs against base and head. The timing geometric mean across 39 non-cold rows is
**1.004693365582**; maximum allocation ratio is
**1.037238560580** (recursion compile).
The 40-row output, three raw pairs and summary are retained in
`/tmp/xsl3-p7-deferred-relative-perf-data/`. Aggregate success does not resolve individual timing outliers:

| Case | Base median ms | Head median ms | Head/base | Allocation ratio |
|---|---:|---:|---:|---:|
| grouping compile | 0.5933000 | 0.6290000 | 1.060171919771 | 1.025037607268 |
| dispatch compile | 1.5575000 | 1.7664000 | 1.134125200642 | 1.028340059740 |
| regex 1kb | 0.0046000 | 0.0056000 | 1.217391304348 | 1.004803689233 |


## Paired 100 MB process comparison

The complete matrix has **96 valid trials / zero failures**, with three alternating tree/stream pairs
per workload, exactly 100,000,000 input bytes and equal 4 GiB managed resource guards. Every child
validates its independent output fingerprints. All 83 copied host artifacts remain unchanged.
Process validation exits **0**. The canonical throughput target is separately **evaluated and failed**:
identity, fork and source-document streaming remain slower than tree plus transform.

| Workload | Tree median ms | Stream median ms | Stream/tree | Winner |
|---|---:|---:|---:|---|
| identity | 2313.896500 | 2977.482125 | 1.286782760162 | Tree observed median |
| adjacent | 1791.226500 | 1104.605208 | 0.616675338378 | Stream observed median |
| accumulator | 6210.043250 | 4350.570083 | 0.700570013421 | Stream observed median |
| iterate | 1957.133667 | 1423.754750 | 0.727469346630 | Stream observed median |
| merge | 13553.808000 | 3790.969875 | 0.279697770176 | Stream observed median |
| fork | 1737.206291 | 6059.781625 | 3.488233755769 | Tree observed median |
| source-document | 2275.479542 | 2964.922042 | 1.302987782256 | Tree observed median |
| climbing-union | 1934.659041 | 2836.491500 | 1.466145424019 | Tree observed median |
| climbing-intersection | 1780.559458 | 2656.326666 | 1.491849460048 | Tree observed median |
| type-assertion | 1351.406250 | 937.138167 | 0.693454072008 | Stream observed median |
| type-inspection | 1096.796167 | 724.809209 | 0.660842215544 | Stream observed median |
| copy-select | 2287.472042 | 2965.355458 | 1.296346098905 | Tree observed median |
| conditional-content | 1968.787500 | 2071.729208 | 1.052286855742 | Tree observed median |
| template-parameter | 1659.098000 | 1772.203042 | 1.068172610660 | Tree observed median |
| function-parameter | 1574.821208 | 2679.075125 | 1.701193196657 | Tree observed median |
| overlap-values | 1809.559125 | 4401.248958 | 2.432221692673 | Tree observed median |

Early output and RSS below use the same trials. These 4 GiB-guarded measurements are not substitutes
for the absolute native RSS gates above.

| Workload | Tree first output ms | Stream first output ms | Tree input at first output | Stream input at first output | Tree median RSS bytes | Stream median RSS bytes |
|---|---:|---:|---:|---:|---:|---:|
| identity | 1055.224375 | 0.238125 | 100000000 | 8192 | 488833024 | 92733440 |
| adjacent | 1461.304000 | 4.195833 | 100000000 | 356352 | 731529216 | 95436800 |
| accumulator | 6210.019250 | 4350.564708 | 100000000 | 100000000 | 1374748672 | 95567872 |
| iterate | 1957.097625 | 1423.739750 | 100000000 | 100000000 | 529301504 | 95551488 |
| merge | 10405.347541 | 5.390084 | 100000000 | 122880 | 1872084992 | 96010240 |
| fork | 1113.502250 | 2.456541 | 100000000 | 36864 | 485146624 | 96468992 |
| source-document | 1035.763834 | 0.232458 | 100000000 | 8192 | 449839104 | 92864512 |
| climbing-union | 1776.745458 | 1.566167 | 100000000 | 53248 | 601899008 | 95600640 |
| climbing-intersection | 1724.251125 | 7.493208 | 100000000 | 204800 | 651837440 | 95666176 |
| type-assertion | 1140.886375 | 0.361166 | 100000000 | 36864 | 551108608 | 92389376 |
| type-inspection | 1096.788000 | 724.803000 | 100000000 | 100000000 | 534134784 | 92438528 |
| copy-select | 1038.879208 | 0.214750 | 100000000 | 8192 | 449806336 | 92913664 |
| conditional-content | 1322.682917 | 0.212500 | 100000000 | 8192 | 641810432 | 92635136 |
| template-parameter | 1119.822125 | 2.208625 | 100000000 | 122880 | 507723776 | 95502336 |
| function-parameter | 1022.681958 | 3.444333 | 100000000 | 122880 | 431292416 | 95846400 |
| overlap-values | 1042.687291 | 1.074125 | 100000000 | 24576 | 449642496 | 95387648 |

The full trials, commands, engine/host hashes, per-round values and child stdout/stderr remain under
`/tmp/xsl3-p7-deferred-relative-process-data/`. The 100 MB matrix does not replace a current three-size
scaling/latency sweep. Accumulator, iterate and type-inspection scalar results naturally require EOF.

## Remaining Phase 7 work

The absolute 20 MiB RSS gate, canonical throughput target and full three-size throughput/latency
requirements remain open. Remaining strict/schema cases include mixed grounded/live path ordering,
non-monotonic ancestor difference, deferred consumers/function continuations, source validation and
typed/serialization behavior. URI-fragment policy and current remote Linux x64 CI evidence remain open.
The completed S1 discrepancy audit and final-Recommendation/suite conflicts remain documented in the
preceding checkpoints. These changes do not establish full streaming conformance or bounded memory
for arbitrarily large atomic/final-string results. `supports-streaming` stays `no`; the full P7 goal
remains active. Changes are uncommitted and unpushed.
