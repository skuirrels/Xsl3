# P7 lazy empty frame headers

This isolated control starts from `18fc9a5` (corrected-discard runtime `a8d322a`). It excludes the
unadopted short-polling and tail-call controls and the separate public-selection integration.
Original qualification inputs, output oracles, budgets and throughput targets are unchanged.

## Measured implementation target

The existing Linux identity CPU capture includes write-barrier work. A separate baseline JIT
diagnostic on macOS ARM64 confirms that every `StreamedDocument.Node` constructor eagerly resolves
the two shared empty header arrays and performs their reference assignments. Empty-string defaults,
in contrast, already use cheap paired stores; no string-initialization change is proposed.

The candidate keeps two array-reference fields but resolves their `Array.Empty<T>()` defaults in
the getters. Assigning a header preserves the exact array, without copying, changing its ownership
or adding an allocation. Leaf frames can keep the null backing fields if their headers are never
inspected. Instance-field types and count stay the same. Public navigator behavior, populated
headers, replay, source demand and fork capacity remain unchanged.

The actual candidate constructor is **92 bytes of ARM64 JIT code versus 224** for the baseline.
The two shared-array lookups and reference assignments are absent; the parent assignment remains.
Both diagnostic children validate the original 1 MB identity output and actual 4 GiB heap guard.
This demonstrates the targeted code change, not a throughput or process-memory improvement.

## Qualification boundary

1. Preserve existing navigator/frame, namespace, schema, copied-attribute identity, fork and
   physical-read contracts. If timing supports further work, complete Release/Debug .NET 8/10
   regression coverage, saved/native consumers and ordinary compatibility.
2. Compare through the unchanged canonical consumer over five workloads, both modes and three
   alternating rounds: 60 observations at exact 100 MB and actual 4 GiB heap guards. Freeze both
   engines and verify output and source identity independently.
3. A useful local result still requires a paired target-platform comparison and all original
   final-source memory, throughput, integration, review, CI and release requirements. Do not adopt
   the control merely because its constructor is smaller.

All **1,080 .NET 10 Release** selected checks pass, with no failures/skips and every individual TRX
result checked against the counters. The local paired comparison is running.

Evidence: `/private/tmp/xsl3-p7-lazy-frame-headers-20261106/` contains the source patch/hash, focused
TRX/audit, frozen diagnostic consumer and candidate disassembly. Baseline constructor evidence is
in `/private/tmp/xsl3-p7-frame-initialization-20261106/`. No adoption or release readiness is claimed.
