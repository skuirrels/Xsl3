# P7 S3-27: hidden-grounding negative control

This test-only increment starts from merged main `a5e89ac` on
`feature/p7-hidden-grounding-contracts`. No runtime, benchmark fixture, acceptance budget,
public API or supported-streaming claim changes.

## What the control establishes

The existing entry-fallback test turns forward execution off: its correct result is rejected by the
frozen stylesheet-forwarding floor. That cannot detect a helper that first obtains a forward source,
then silently grounds it. `StreamedHiddenGroundingTests` now exercises this second failure mode.

The fixture compiles an ordinary Strict, streamable-mode stylesheet that counts `doc/row`. After the
real static analysis, the test optionally wraps its one `X31InitialSource` call with a test-assembly
helper before normal IL generation. The engine still opens and records the actual `StreamedNavigator`.
The helper either returns that source unchanged or calls the engine's real `StreamedNavigator.Ground()`
and returns the copied tree. Both modes have identical QIL shape. There is no production test hook,
fabricated source report, changed analyzer rule or caller-created replacement tree.

Each child completes its transformation, checks the independent generated record count and exact
input byte count, and reports actual engine capture metadata. Both controls return the same correct
count, complete their forward-source execution record, and pass the optimizer's Debug verifier.
This demonstrates the intended blind spot: graph verification cannot inspect a helper's implementation.

The acceptance check instead measures `GC.GetTotalMemory(forceFullCollection: true)` before and after
execution, at geometrically increasing physical input positions, and at helper return with the returned
navigator explicitly kept alive. The unchanged and transparent-helper controls remain below the original
20,971,520-byte managed bound; the grounding control must exceed it and return budget-failure status.
The test rejects the latter even though output, forward-source evidence and verifier evidence all pass.

## Process isolation and scope

Eight scenarios per framework/configuration cover debug and optimized stylesheets: original,
transparent-helper and grounding executions on 33,554,432 bytes, plus original execution on the original
209,715,200-byte push input. Inputs use the existing canonical generator without materializing the XML
string. Reader instrumentation samples memory without pre-reading or retaining nodes. The output is one
count, not a retained serialization of the source.

Positive children run with the actual 20,971,520-byte managed heap guard. Only the deliberately faulty
control has a larger 671,088,640-byte heap, so it can complete and report the measured violation instead
of crashing. Its acceptance bound remains unchanged. Children drain output/error concurrently, run in
the process-state test collection, and have a three-minute deadline. Raw JSON reports are retained in TRX.

This proves detection of an injected whole-input materialization behind a genuine forward entry and
adds a bounded-memory contract for the compiled count path. It is not a universal materialization
detector or proof for every runtime path. Explicit copies, grouping populations, caller-retained nodes
and arbitrary payload shapes have separate storage contracts. Original per-workload NativeAOT absolute
RSS and throughput gates remain required on the final reviewed source; these JIT managed-byte samples
cannot replace them. The W3C outcome/forwarding audit itself still does not measure heap retention.

## Local validation, 5 November 2026

| Library build / framework | Largest positive live-byte sample | Largest grounding sample | Grounding outputs correct |
|---|---:|---:|---|
| Release / .NET 10 | 1,759,704 | 264,916,720 | yes |
| Release / .NET 8 | 1,861,208 | 255,826,672 | yes |
| Debug / .NET 10 | 1,816,520 | 264,927,152 | yes |
| Debug / .NET 8 | 1,868,968 | 255,199,840 | yes |

All 32 new cases passed. The focused Debug run also passed 30 existing optimizer-policy and verifier
checks, for 62 checks overall. The 32 MiB runs count 291,777 rows; the 200 MiB positive runs count
1,823,610. Every child observed its requested managed heap guard. All Debug compilations recorded
one verifier entry and completion, including the hidden-grounding controls.

Evidence: `/private/tmp/xsl3-p7-hidden-grounding-20261105/{release,debug}/*.trx`, with validation metadata
in that directory. This local temporary path is not durable hosted evidence. Ordinary CI includes the
new tests in its full Release suite and a dedicated Debug step on Linux and Windows. Hosted CI is pending.

The initial prototype tried wrapping `XmlContext`, but this stylesheet uses `X31InitialSource`; the
zero-replacement assertion correctly failed. The final injection targets that actual initial-source
call and asserts exactly one replacement and one helper invocation. No runtime change was needed.
