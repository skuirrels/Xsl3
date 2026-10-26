# Bounded value replay and merge output, 2026-10-25

The later [relative-value/signature follow-up](streaming-relative-values-signatures-2026-10-25.md)
records subsequent compiler/header fixes and updated validation. The measurements below describe this
bounded-value/merge checkpoint.

This follow-up addresses the tree-merge OOM and overlapping-value costs found in the
[node-set/mode checkpoint](streaming-set-merge-mode-2026-10-25.md). It also completes effective package
mode visibility and validates the `on-multiple-match` enumeration. The full Phase 7 goal remains active.

## Merge actions emit group by group

The old grounded merge path retained the results of every action, including independently constructed
temporary text trees, before emitting any result. The previous 100 MB matrix recorded three tree-merge
OOMs with equal 4 GiB managed-heap guards. Its first OOM stack reached temporary-tree construction
through `MergeCore`.

Group preparation is now shared with a materialized group cursor. The cursor retains the total number
of groups for `last()`, releases consumed groups, and exposes only the current group's items, raw key
and per-source members to the action. Grounded and forward merge compilation share the action loop;
compiled instructions emit their output directly. A declared-streamable source still makes action
context size absent, including when tree input is selected.

The four initial output-observation cases failed before the change: all three action messages had
already run when the first group was emitted. They now observe one, two and three completed messages
at the corresponding group starts. Other coverage preserves node identity, positions, total size,
empty inputs, cancellation failure retention, consumed-row release and saved/debug assemblies.
Existing `X31Merge`/`X31MergeConfigured` entry points remain for older saved consumers. Three additive
runtime helpers expose the new cursor; the saved-data version and function-library fingerprint remain.

A one-pair 100 MB smoke comparison validates both tree and stream outputs under the unchanged 4 GiB
heap guard. Tree completes with peak RSS **1,996,324,864 bytes** instead of OOM. This was a correctness
and heap smoke test overlapping other development checks; its timing is diagnostic. Evidence is
`/tmp/xsl3-p7-materialized-merge-smoke-data/` with copied host/engine artifacts in the adjacent directory.

Grounded merge still materializes inputs and groups. The change removes retained action results; it
does not make the tree path constant-memory.

## Small value windows avoid file I/O

The value-only replay store now starts with two serialized memory streams sharing a **64 KiB logical
cache limit**. Their retained backing-array capacity is bounded by **128 KiB per store**. This is an
input-independent allocation policy, applied to every eligible value population; the generated
benchmark, input, output oracle and CI budgets are unchanged.

If either write would exceed the combined limit, both streams spill. Both private file handles are
acquired and both memory prefixes are copied successfully before either active stream switches. This
preserves binary reader/writer objects, source numbers, links and character offsets. Files keep atomic
owner-only Unix creation, exclusive sharing and delete-on-close ownership. Failures propagate and
release acquired handles.

Small windows reuse their bounded memory arrays and reset logical lengths between generations. Once a
store spills, later windows reuse its file high-water storage. Stale ranges retain the existing 64-bit
generation check. Large captures release the serialized memory arrays and retain only open matching
headers in memory. Scalar node values and final result strings retain their existing size-dependent
allocation behavior.

The new small-overlap regression failed before the change because two files were created. Large,
invalid-input, sequential reuse and cancellation cases still force real spill. Additional coverage
alternates header-heavy and text-heavy windows to detect stale logical lengths. Cancellation and typed
list/union/nil/QName tests exercise both memory and file paths; the large typed fixture also includes
supplementary Unicode characters.

## Effective mode attributes

A package mode could inherit `visibility='public'` in its attribute map while its component declaration
still appeared private. Equal-precedence and imported examples both reproduced XTDE0045. The loader
now updates effective component visibility when that attribute is inherited, preserving the declaration's
package and source provenance. An explicit higher private definition still prevails.

Invalid `on-multiple-match` values previously compiled successfully. The loader now accepts only
`use-last` and `fail`, reporting XTSE0020 for other values. Both defects have regression coverage.

## Validation

Full Debug and Release unit suites pass **3,108 on .NET 10 / 3,012 on .NET 8**. Focused coverage passes
113/108 cases, including both merge paths and both replay stores.

Four new normal/debug saved NativeAOT styles cover grounded group focus/size and large streamed value
spill with Unicode. A separate materialized-merge assembly compiled against the preceding engine also
passes unchanged. Retained value/fork, item/template/focus and immutable version-2 compatibility probes
pass. The macOS arm64 publish completes without trim/AOT warnings.

Evidence prefixes are `/tmp/xsl3-p7-bounded-values-merge-verified-` and
`/tmp/xsl3-p7-bounded-values-merge-native/`; the frozen core/configuration snapshot contains **402 files**.
The Linux source/controller/probe/test snapshot contains **566 files**. All eight full profile failure sets and error-code counts are unchanged. The exact native memory
matrices below are complete. The later ordinary and 100 MB process comparisons start after the owned
functional/native CPU jobs finish.


All eight complete profiles have unchanged failure IDs and expected-error-code counts. No baseline is
changed for this increment. Strict streaming remains **2,681/30/1**, with seven unmatched expected codes.

| Profile | Passed | Failed | Not run | Unmatched expected codes |
|---|---:|---:|---:|---:|
| XSLT 3.0 | 8847 | 21 | 41 | 34 |
| XSLT 2.0 | 4588 | 9 | 11 | 7 |
| Streaming, unstreamed | 2692 | 19 | 1 | 5 |
| Streaming, strict | 2681 | 30 | 1 | 7 |
| Posture/sweep | 790 | 129 | 0 | 0 |
| Schema aware | 843 | 1 | 2 | 6 |
| Schema aware, strict | 842 | 2 | 2 | 6 |
| QT3 | 21884 | 0 | 99 | 67 |

The solution, 46 W3C harness facts, upstream 1,654 per framework, differential 394 per framework and
thirteen controller tests pass. The strict profile retains its known failing audit exit 1; the complete
functional driver exits 0. Profile reports and comparison are frozen in
`/tmp/xsl3-p7-bounded-values-merge-reports/`.

All **402 current/frozen core/configuration hashes** and **566 current/frozen Linux source/controller/
probe/test hashes** match. The native driver exits 0; saved artifacts and their hashes are retained.
The legacy materialized-merge consumer is SHA-256
`ab640f306c0e22a1cc9299795788a517bf7affb3d3a9e6ad15e6146ac04a3a39`, compiled with the preceding engine
`4bc40019476dc31b6087f7f97a890a39d3dbad1f036ab5ad66d8ce48c9769b0f`. The immutable version-2 fork
assembly remains SHA-256 `89ba201ad9e21c7a3a4add5231c114f5aee9a2f9eef4bdd9472a41e680b5caee`.

## Final-source exact native memory matrices

The fresh local Linux arm64 NativeAOT nightly matrix is complete: **48 children, 0 failures** at exact 2 GiB input / 200 MiB absolute RSS. All sixteen measured streams consume exactly 2,147,483,648 bytes and validate their independent output fingerprints. Effective managed allowance is 209,715,200 bytes. Both native-evidence flags are false; normal ICU is enabled. RSS is the greater child/outer wait4 full-lifetime counter, including startup and warmup. Tree baselines are separate and uncapped.

| Workload | Full-lifetime peak RSS bytes | Live managed bytes | Diagnostic elapsed ms | First output ms | Input at first output |
|---|---:|---:|---:|---:|---:|
| identity | 29286400 | 383448 | 54416.986816 | 0.391625 | 8192 |
| adjacent | 27475968 | 363848 | 13572.272297 | 6.660083 | 356352 |
| accumulator | 30384128 | 363680 | 56367.030609 | 56366.994193 | 2147483648 |
| iterate | 28569600 | 419728 | 14967.127591 | 14967.120007 | 2147483648 |
| merge | 31191040 | 407712 | 55941.307693 | 7.544958 | 122880 |
| fork | 30924800 | 368944 | 222733.509310 | 4.769292 | 36864 |
| source-document | 29487104 | 386184 | 52989.377733 | 0.301459 | 8192 |
| climbing-union | 30564352 | 363224 | 44766.747479 | 2.806458 | 53248 |
| climbing-intersection | 30568448 | 363232 | 43350.526978 | 6.533167 | 204800 |
| type-assertion | 26681344 | 360976 | 10355.284380 | 0.246625 | 36864 |
| type-inspection | 26435584 | 361040 | 7386.665171 | 7386.629837 | 2147483648 |
| copy-select | 29515776 | 383488 | 52069.153857 | 0.424625 | 8192 |
| conditional-content | 29523968 | 379944 | 33801.549515 | 0.146041 | 8192 |
| template-parameter | 30744576 | 373296 | 20452.507301 | 1.911333 | 122880 |
| function-parameter | 30547968 | 373168 | 32259.684556 | 7.002375 | 122880 |
| overlap-values | 30617600 | 382016 | 70159.008157 | 1.563750 | 24576 |

These native timings are diagnostic: functional checks overlapped part of the matrix. They are not paired throughput comparisons. Accumulator/iterate/type-inspection results require end-of-input.

The exact 200 MiB-input / 20 MiB RSS push matrix completes all **48 children**, retaining **16 failures**. No startup/warmup or baseline is subtracted.

| Workload | Status | Measured input bytes | Full-lifetime peak RSS bytes |
|---|---|---:|---:|
| identity | budget-exceeded | 209715200 | 25501696 |
| adjacent | budget-exceeded-before-measurement | 0 | 22159360 |
| accumulator | budget-exceeded-before-measurement | 0 | 21245952 |
| iterate | budget-exceeded-before-measurement | 0 | 22482944 |
| merge | budget-exceeded-before-measurement | 0 | 21540864 |
| fork | budget-exceeded-before-measurement | 0 | 21540864 |
| source-document | budget-exceeded | 209715200 | 25030656 |
| climbing-union | budget-exceeded-before-measurement | 0 | 21372928 |
| climbing-intersection | budget-exceeded-before-measurement | 0 | 21422080 |
| type-assertion | budget-exceeded | 209715200 | 25616384 |
| type-inspection | budget-exceeded | 209715200 | 25636864 |
| copy-select | budget-exceeded | 209715200 | 25616384 |
| conditional-content | budget-exceeded | 209715200 | 25870336 |
| template-parameter | budget-exceeded-before-measurement | 0 | 21315584 |
| function-parameter | budget-exceeded-before-measurement | 0 | 21508096 |
| overlap-values | budget-exceeded-before-measurement | 0 | 21409792 |

Evidence is `/tmp/xsl3-p7-bounded-values-merge-linux/`: both complete reports, frozen source/SHA-256 snapshot, `source-check.json`, `run.sh`, publish/run logs, native-intermediate rejection and `measured-summary.json`. The cached official SDK image is `70b5de4a1846`, .NET SDK 10.0.400 / runtime 10.0.11. This is local arm64 evidence; remote linux-x64 CI remains unverified. The nightly pass does not close the exact push budget or the full Phase 7 goal.


## Ordinary performance gate

One gate run exits **0** against `0f2ddcba2c8791f4e8cc6b7b91fe371164ed2021` over exactly three alternating pairs. Timing geometric mean is **0.996520976223**; maximum allocation ratio is **1.047265587827** (grouping cold, 1,597,272/1,672,768 bytes). Cold time is excluded from the 39-row timed mean; cold allocation is included. Cold time ratio is **1.052481283565** (135.522600/142.635000 ms).

The same ordinary harness and corpus runs on both versions with the separate process host excluded equally. All task-owned functional/native CPU jobs were terminal before this comparison. The temporary base worktree is removed. This compares the complete working tree with the base; it does not isolate the causal effect of this increment.

| Workload | Size | Base 0f2ddcb ms | Current branch ms | Head/base | Base allocated bytes | Current allocated bytes | Allocation ratio | Winner |
|---|---|---:|---:|---:|---:|---:|---:|---|
| grouping | cold | 135.522600 | 142.635000 | 1.052481 | 1597272 | 1672768 | 1.047266 | Base observed median |
| grouping | compile | 0.621800 | 0.652400 | 1.049212 | 186804 | 191471 | 1.024983 | No meaningful winner (5% band) |
| sorting | compile | 0.583450 | 0.578500 | 0.991516 | 182433 | 186792 | 1.023894 | No meaningful winner (5% band) |
| keys | compile | 0.512200 | 0.365700 | 0.713979 | 194232 | 199587 | 1.027570 | Current observed median |
| join | compile | 0.096600 | 0.093200 | 0.964803 | 150869 | 154139 | 1.021674 | No meaningful winner (5% band) |
| regex | compile | 0.080900 | 0.082200 | 1.016069 | 165107 | 168234 | 1.018939 | No meaningful winner (5% band) |
| strings | compile | 0.083000 | 0.085100 | 1.025301 | 177987 | 180666 | 1.015052 | No meaningful winner (5% band) |
| maps | compile | 0.159000 | 0.158800 | 0.998742 | 285535 | 291885 | 1.022239 | No meaningful winner (5% band) |
| fold-left | compile | 0.182300 | 0.182400 | 1.000549 | 305117 | 308702 | 1.011750 | No meaningful winner (5% band) |
| iterate | compile | 0.149000 | 0.150700 | 1.011409 | 238285 | 242414 | 1.017328 | No meaningful winner (5% band) |
| identity | compile | 0.044600 | 0.045300 | 1.015695 | 97901 | 99594 | 1.017293 | No meaningful winner (5% band) |
| dispatch | compile | 1.504750 | 1.858400 | 1.235022 | 4004366 | 4117850 | 1.028340 | Base observed median |
| recursion | compile | 0.119100 | 0.122300 | 1.026868 | 237817 | 246674 | 1.037243 | No meaningful winner (5% band) |
| json | compile | 0.067000 | 0.067900 | 1.013433 | 156509 | 158368 | 1.011878 | No meaningful winner (5% band) |
| grouping | 1kb | 0.002500 | 0.002500 | 1.000000 | 13113 | 13183 | 1.005338 | No meaningful winner (5% band) |
| sorting | 1kb | 0.002400 | 0.002200 | 0.916667 | 12205 | 12292 | 1.007128 | Current observed median |
| keys | 1kb | 0.004500 | 0.004600 | 1.022222 | 17468 | 17556 | 1.005038 | No meaningful winner (5% band) |
| join | 1kb | 0.003600 | 0.003700 | 1.027778 | 14120 | 14192 | 1.005099 | No meaningful winner (5% band) |
| regex | 1kb | 0.004700 | 0.005600 | 1.191489 | 15613 | 15689 | 1.004868 | Base observed median |
| strings | 1kb | 0.003300 | 0.003300 | 1.000000 | 12830 | 12903 | 1.005690 | No meaningful winner (5% band) |
| maps | 1kb | 0.006500 | 0.006500 | 1.000000 | 27220 | 27365 | 1.005327 | No meaningful winner (5% band) |
| fold-left | 1kb | 0.004400 | 0.004300 | 0.977273 | 18803 | 18963 | 1.008509 | No meaningful winner (5% band) |
| iterate | 1kb | 0.004200 | 0.004300 | 1.023810 | 23089 | 23203 | 1.004937 | No meaningful winner (5% band) |
| identity | 1kb | 0.002800 | 0.002900 | 1.035714 | 10810 | 10884 | 1.006846 | No meaningful winner (5% band) |
| dispatch | 1kb | 0.007400 | 0.008000 | 1.081081 | 23592 | 23665 | 1.003094 | Base observed median |
| recursion | 1kb | 0.003700 | 0.003500 | 0.945946 | 17570 | 17650 | 1.004553 | Current observed median |
| json | 1kb | 0.005900 | 0.006000 | 1.016949 | 35037 | 35172 | 1.003853 | No meaningful winner (5% band) |
| grouping | 1mb | 0.849850 | 0.849200 | 0.999235 | 1985615 | 1985686 | 1.000036 | No meaningful winner (5% band) |
| sorting | 1mb | 2.080050 | 2.159300 | 1.038100 | 3410835 | 3428508 | 1.005181 | No meaningful winner (5% band) |
| keys | 1mb | 3.688100 | 3.594900 | 0.974730 | 6152454 | 6152541 | 1.000014 | No meaningful winner (5% band) |
| join | 1mb | 2.506400 | 2.357350 | 0.940532 | 4525269 | 4525339 | 1.000015 | Current observed median |
| regex | 1mb | 6.006700 | 5.876300 | 0.978291 | 13071946 | 13072017 | 1.000005 | No meaningful winner (5% band) |
| strings | 1mb | 2.936000 | 2.888800 | 0.983924 | 5462929 | 5462999 | 1.000013 | No meaningful winner (5% band) |
| maps | 1mb | 4.222750 | 4.028800 | 0.954070 | 10134170 | 10152450 | 1.001804 | No meaningful winner (5% band) |
| fold-left | 1mb | 2.933500 | 2.694250 | 0.918442 | 7319896 | 7443143 | 1.016837 | Current observed median |
| iterate | 1mb | 1.247400 | 1.200550 | 0.962442 | 3114702 | 3132445 | 1.005697 | No meaningful winner (5% band) |
| identity | 1mb | 1.900700 | 1.893800 | 0.996370 | 3154312 | 3154400 | 1.000028 | No meaningful winner (5% band) |
| dispatch | 1mb | 1.360900 | 1.352300 | 0.993681 | 2650127 | 2650199 | 1.000027 | No meaningful winner (5% band) |
| recursion | 1mb | 1.225950 | 1.212250 | 0.988825 | 2781875 | 2781928 | 1.000019 | No meaningful winner (5% band) |
| json | 1mb | 9.719600 | 9.216350 | 0.948223 | 18294350 | 18347002 | 1.002878 | Current observed median |

Winner labels use a 5% diagnostic band; they do not replace an uncertainty test. Exact round files and per-row medians/allocations are retained in `/tmp/xsl3-p7-bounded-values-merge-perf-data/`. Only rounds 1–3 are used.

Timed rows above 1.05 remain observed regressions requiring attribution:

- **dispatch compile**: ratio **1.235022428975**, base/current **1.504750/1.858400 ms**. Base rounds `[1.4904, 1.5537, 1.50475]`, current rounds `[2.8287, 1.6395, 1.8584]`. Allocation ratio **1.028340066817**.
- **regex 1kb**: ratio **1.191489361702**, base/current **0.004700/0.005600 ms**. Base rounds `[0.0047, 0.0047, 0.0047]`, current rounds `[0.0055, 0.0056, 0.0056]`. Allocation ratio **1.004867738423**.
- **dispatch 1kb**: ratio **1.081081081081**, base/current **0.007400/0.008000 ms**. Base rounds `[0.0074, 0.0075, 0.0073]`, current rounds `[0.0074, 0.008, 0.008]`. Allocation ratio **1.003094269244**.

No additional performance run was selected to replace these results. Earlier individual outliers are not declared fixed by their absence. A fresh 100 MB process comparison over all sixteen workloads follows this isolated gate. It has its own raw trials and provenance; the earlier full three-size matrix remains evidence for its own snapshot. Throughput/latency exits remain open.


## Isolated 100 MB process comparison

The complete matrix has **96 valid trials, zero failures**: three alternating tree/stream pairs over
all sixteen workloads with equal 4 GiB managed-heap guards. Each output passes its independent
fingerprint check; each child reports exactly 4,294,967,296 available managed bytes. No child times
out. The previously failing tree merge now succeeds in all rounds.

The run starts after all owned functional and native memory/compatibility jobs, followed by the
ordinary three-pair gate. It measures source parsing plus transformation; compilation, warmup and
oracle construction are outside the timed interval. Tiered compilation is disabled equally. RSS is
the full-lifetime wait4 peak. Runtime: .NET 10.0.11, macOS 27.0.1, Arm64.

| Workload, 100 MB | Xsl3 tree ms | Xsl3 stream ms | Stream/tree | Tree MiB/s | Stream MiB/s | Winner |
|---|---:|---:|---:|---:|---:|---|
| identity | 2315.911542 | 2968.463667 | 1.281769 | 41.179220 | 32.126865 | Tree observed median |
| adjacent | 1756.933000 | 1083.593042 | 0.616753 | 54.280631 | 88.010377 | Stream observed median |
| accumulator | 6283.603333 | 4316.893291 | 0.687009 | 15.177188 | 22.091681 | Stream observed median |
| iterate | 1919.582584 | 1424.742417 | 0.742215 | 49.681338 | 66.936613 | Stream observed median |
| merge | 13667.391334 | 3741.847000 | 0.273779 | 6.977735 | 25.486727 | Stream observed median |
| fork | 1915.977500 | 6022.977917 | 3.143554 | 49.774818 | 15.833933 | Tree observed median |
| source-document | 2289.057250 | 3000.906625 | 1.310979 | 41.662318 | 31.779540 | Tree observed median |
| climbing-union | 1988.912833 | 2799.444833 | 1.407525 | 47.949528 | 34.066552 | Tree observed median |
| climbing-intersection | 1817.623542 | 2657.120833 | 1.461865 | 52.468198 | 35.891266 | Tree observed median |
| type-assertion | 1300.960542 | 932.116334 | 0.716483 | 73.305399 | 102.312800 | Stream observed median |
| type-inspection | 1097.817667 | 726.830709 | 0.662069 | 86.870010 | 131.209965 | Stream observed median |
| copy-select | 2260.574500 | 2978.811833 | 1.317723 | 42.187254 | 32.015259 | Tree observed median |
| conditional-content | 1952.465375 | 2058.334375 | 1.054223 | 48.844621 | 46.332332 | Tree observed median |
| template-parameter | 1651.017667 | 1786.422375 | 1.082013 | 57.762817 | 53.384593 | Tree observed median |
| function-parameter | 1595.750458 | 2953.740875 | 1.851004 | 59.763374 | 32.287000 | Tree observed median |
| overlap-values | 1799.533292 | 4274.035333 | 2.375080 | 52.995647 | 22.313206 | Tree observed median |

Winner labels use a descriptive 5% band, not a significance test.

| Workload | Tree first output ms | Stream first output ms | Stream input bytes at first output | Tree full-lifetime median RSS bytes | Stream full-lifetime median RSS bytes |
|---|---:|---:|---:|---:|---:|
| identity | 1050.751625 | 0.198708 | 8192 | 488800256 | 92553216 |
| adjacent | 1434.423792 | 4.066917 | 356352 | 707051520 | 95404032 |
| accumulator | 6283.589625 | 4316.886458 | 100000000 | 1330429952 | 95600640 |
| iterate | 1919.569792 | 1424.724375 | 100000000 | 548929536 | 95551488 |
| merge | 10405.220083 | 5.320000 | 122880 | 1895825408 | 95911936 |
| fork | 1272.241791 | 2.458250 | 36864 | 523075584 | 96468992 |
| source-document | 1022.470125 | 0.274750 | 8192 | 449724416 | 92733440 |
| climbing-union | 1834.780208 | 1.633125 | 53248 | 674791424 | 94715904 |
| climbing-intersection | 1760.730250 | 7.385125 | 204800 | 679624704 | 95633408 |
| type-assertion | 1088.715709 | 0.371584 | 36864 | 550928384 | 92356608 |
| type-inspection | 1097.804500 | 726.824792 | 100000000 | 534118400 | 92160000 |
| copy-select | 1024.532208 | 0.236500 | 8192 | 432930816 | 92798976 |
| conditional-content | 1311.011750 | 0.171416 | 8192 | 658194432 | 92651520 |
| template-parameter | 1120.032917 | 2.257166 | 122880 | 516603904 | 95420416 |
| function-parameter | 1036.379166 | 3.793250 | 122880 | 489684992 | 95928320 |
| overlap-values | 1037.854542 | 0.943375 | 24576 | 445349888 | 95551488 |

The canonical throughput requirement is now fully evaluated and **fails**: identity, fork and
source-document streaming medians exceed their tree medians. Adjacent, accumulator, iterate and merge
are faster. Accumulator, iterate and type-inspection return final values at EOF; their latency is not
an early-emission claim. A current-source three-size/latency sweep remains open.

Evidence is `/tmp/xsl3-p7-bounded-values-merge-process-data/`: complete report, all child stdout/stderr and controller output.
The copied host has **83 unchanged artifact hashes**. Engine SHA-256 is `80578a7ac2f105f29782aa624b406694d1e188e0913471a816e28055533b52ae`;
host is `3674d87971492449c8d4ec97bfe9a40c4359a09a433dd8973d85168629a23155`; controller is `104d46f91bf083e8dca587cc3dbd56d46534b194450eb47e3d0ea67383d8e5ac`. The controller and combined
performance driver terminate with exit **0**. No replacement run was selected.


## Remaining scope

The full P7 conformance, execution, memory, throughput/latency and feature-claim requirements remain
active. Prior checkpoints' memory and performance tables apply to their own frozen sources.
`supports-streaming` stays `no`. Deferred value consumers, remaining strict failures, raw S1 acceptance,
schema-aware saved assemblies and remote Linux x64 evidence remain open.
