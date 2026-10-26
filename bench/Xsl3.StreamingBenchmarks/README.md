# Streaming versus tree execution

A local CPU/parser/serializer benchmark for the **current working copy** of Xsl3. It uses
public `XslCompiledTransform.Load` / `Transform(XmlReader, ..., Stream)` entry points.
It does not change engine code, claim streaming completion, or enforce a CI budget.

## Run

Requires the .NET 10 SDK and Python 3.11+; the phase RSS sampler requires macOS.
No third-party Python packages are needed.

```sh
dotnet build bench/Xsl3.StreamingBenchmarks -c Release --disable-build-servers -p:UseSharedCompilation=false
python3 bench/Xsl3.StreamingBenchmarks/run.py \
  --sizes 1,5,10 --timing-runs 9 --memory-runs 3 \
  --output artifacts/streaming-benchmark-2026-10-23
```

Run from the repository root. Use a fresh output directory for each comparison.
The script fails on a child error, incomplete input consumption, an output mismatch,
or conversion/filtering that does not emit streamed output until the input is exhausted.

## Inputs and workloads

The controller writes deterministic, explicitly synthetic XML files of **exactly
1,000,000, 5,000,000 and 10,000,000 bytes** by default. Trailing whitespace inside the
root fills the remainder after the final complete record. Each record contains a
sequential ID, a generated category code, a generated integer value, and a text label.
These fields are benchmark fixtures, with no production domain/reference-data role.

The same stylesheet declares a streamable default mode for both engines. Separate
child processes use `XSL3_STREAM_STRICT=0` (normal tree execution) or `=1` (development
streaming execution). Compiler streamability policy remains the default Strict.

- **convert:** serialize every record as `id|code|value|label` plus a newline.
- **filter:** serialize the same fields for records where `@id mod 5 = 0`.
- **sum:** serialize `xs:integer(sum(doc/record/@value))`. Values and totals are small
  enough to be exactly representable before the integer formatting conversion.

The synthetic generator derives expected text and sums directly from the generated
record fields. Untimed correctness runs serialize both modes to files and compare
each complete result byte for byte against this independent expected output, and
against each other. SHA-256 and output length are retained in `results.json`.
Measured runs verify output length; they use a counting sink rather than retaining
or hashing all output during the timed interval.

## Measurement

- Release build, .NET 10. One child per observation, run serially. Modes alternate
  order within each repetition. Nine timing pairs and three independent memory
  pairs per size/workload by default; medians summarize each mode.
- `DOTNET_TieredCompilation=0` in every child stabilizes JIT policy for short runs.
  Each child compiles its own stylesheet, runs five transformations over a 65,536-byte
  input with the same wrappers/sink, then requests a full GC before measurement.
- **Execution time:** monotonic `Stopwatch` interval starting before measured reader
  creation and ending after `Transform` returns and disposes the reader. Includes
  input parsing, transformation and serialization. Excludes source compilation,
  warmup, process startup, correctness file writes and report generation.
- **First output:** first nonempty byte write to the result stream, relative to the
  same start. Normal Xsl3 serializer buffering remains active. This measures bytes
  delivered to the caller, not the first XML event or network delivery. Input-byte
  counts at that write confirm progressive output for conversion/filtering.
- **Peak memory:** an external Python thread queries macOS `proc_pidinfo` for the
  child's absolute resident set about every 1 ms during a separately signalled
  execution phase. The parent records pre-execution RSS, sampled peak RSS, sample
  count and maximum observed interval. Timing runs have no memory sampler. This is
  sampled peak process RSS, **not** managed retained heap, allocations, or an
  exact kernel high-water mark; brief peaks between samples may be missed. Loaded
  runtime/compiler/code and committed GC pages remain part of the process RSS.
- Child JSON also retains transform allocation bytes, generation collection counts
  and lifetime process peak working set for diagnosis. Lifetime peak includes
  compilation/warmup and is not the phase peak reported in the comparison.
- Each observation uses a fresh process with small-input warmup; this is not a sustained
service benchmark that repeatedly reuses a heap already sized for the full input.
GC policy and long-running workloads can change the timing trade-off.

Input files are locally generated and filesystem-cache-warm. The measured output
  sink counts/discards serialized bytes; no network delay, output disk speed or
  downstream backpressure is simulated.

`results.json` contains all observations, method definitions, corpus hashes and row
counts, runtime/SDK/platform, Git HEAD/branch, a source-tree fingerprint, and the exact
measured library hash. `git-status.txt` records the working-copy state. Generated
XML and complete correctness outputs remain in the ignored `artifacts/` directory.

Results describe these three synthetic one-pass workloads on one host/runtime.
They cannot establish a universal size cutoff, performance for joins/sorts/groups,
production memory guarantees, or the streaming feature's full release readiness.

## Compare a frozen original with an optimized build

Copy each complete Release output directory before running the comparison. The copied
DLLs stay fixed while tests or other builds use the working tree. Both versions must
contain the same benchmark harness and public-API workload definitions.

```sh
python3 bench/Xsl3.StreamingBenchmarks/compare.py \
  --before-dll artifacts/streaming-investigation-2026-10-23/baseline-bin/Xsl3.StreamingBenchmarks.dll \
  --after-dll artifacts/streaming-investigation-2026-10-23/optimized-bin/Xsl3.StreamingBenchmarks.dll \
  --sizes 1,5,10 --timing-runs 9 --memory-runs 3 \
  --output artifacts/streaming-investigation-2026-10-23/comparison
```

This interleaves four variants (original tree, optimized tree, original streaming,
optimized streaming) and rotates/reverses their order. Every variant is checked
against independently generated expected output. The JSON includes library and
harness hashes, allocation totals, timing ranges, first-output input consumption,
and sampled baseline/peak RSS. Timing and memory still run separately.

For an external EventPipe profiler, `XSL3_BENCH_PROFILE_REPEATS` runs a positive
number of repeated transformations between the `READY`/`GO` and `DONE`/`ACK`
handshakes. This profiling mode is separate from measured comparison runs; leave
that variable unset when benchmarking.

## Average, minimum and maximum

Use `--workloads avg,min,max` with `run.py` or `compare.py` to select the additional
aggregate workloads. The original default workload set remains `convert,filter,sum`.
They evaluate `avg(doc/record/@value)`, `min(doc/record/@value)` and
`max(doc/record/@value)` without truncating or rounding the result.

The generator derives the expected mean from the exact generated integer total and
record count, and tracks the actual minimum/maximum while generating records. It
stores the expected IEEE-754 binary64 bit patterns as well as the expected text.
`compare.py` checks both the complete output bytes and the parsed binary64 result.
All three aggregates require complete input before producing their final result.

```sh
python3 bench/Xsl3.StreamingBenchmarks/compare.py \
  --before-dll artifacts/streaming-avg-min-max-investigation-2026-10-23/before-bin/Xsl3.StreamingBenchmarks.dll \
  --after-dll artifacts/streaming-avg-min-max-investigation-2026-10-23/after-bin/Xsl3.StreamingBenchmarks.dll \
  --sizes 50,100,500 --workloads avg,min,max --timing-runs 9 --memory-runs 3 \
  --output artifacts/streaming-avg-min-max-investigation-2026-10-23/comparison
```
