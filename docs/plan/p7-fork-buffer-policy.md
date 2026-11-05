# P7 fork buffer configuration investigation

Source production remains demand-driven. The earlier [speculative overlap control](p7-fork-production-overlap.md)
is rejected despite its local elapsed gain: it performs source I/O without a new prong request. The new
three-capacity no-read-ahead regression passes on the unchanged runtime in both .NET 8 and .NET 10,
in Release and Debug (12 checks), and fails all three cases on the rejected control.

This investigation varies the existing public `XsltSettings.StreamingBufferCapacity` through a diagnostic
consumer. It does not alter the runtime, shipped default (64), canonical stylesheets, generated input or
independent output oracles. An explicit diagnostic environment input becomes the public property value;
missing or invalid input fails. Child JSON records the value read back from the compiler settings.

## Local evidence

An unchanged runtime was tested on macOS ARM64 with four effective processors, disabled tiering, the actual
4 GiB heap guard and 100,000,000 input bytes. Three pairs per setting (24 observations) all validated the
same records and three output fingerprints. The setting order reverses/rotates between rounds.

| Configured events | Tree median (ms) | Streaming median (ms) | Stream/tree |
|---:|---:|---:|---:|
| 64 | 1,463.148 | 1,853.241 | 1.266612 |
| 256 | 1,475.888 | 1,704.079 | 1.154613 |
| 1,024 | 1,451.297 | 1,690.787 | 1.165018 |
| 4,096 | 1,461.366 | 1,679.925 | 1.149558 |

256 events reduces the observed streaming median by about 8.0%; larger buffers add little further benefit.
Every configuration still misses the local throughput target. This is neither original hosted acceptance
nor a decision to change the default. Exact whole-process/managed memory, latency and saved-style
configuration compatibility remain required before adopting any configuration policy.

Local source snapshots, source/binary hashes, applied settings, runtime information, comparison driver,
all stdout/stderr and reports are under `/private/tmp/xsl3-p7-buffer-policy-20261105/`.

## Hosted reproduction

`eng/diagnose-streaming-capacity.py` prepares the same consumer from an immutable source checkout, copies
the already-built runtime unchanged, checks the copied binary hash, and runs the original benchmark
controller for all 24 observations. It preserves every copied source's original hash, all diagnostic
insertions, actual configured capacities and cross-setting fingerprint checks. It leaves the runner's
processor allowance unchanged and records it in the original benchmark report.

Dispatch `streaming-development.yml` with `capacity_comparison=true`, all other diagnostic flags false,
and `source_ref=a5e89ac7f6fb630e47872a2bcc297cb010ad36f6`. Default/scheduled runs still execute the original
memory and throughput gates. This explicit diagnostic job cannot establish a full-P7 pass.

Hosted comparison pending. No runtime or default-setting change is adopted on this branch.
