# Native initialization diagnostic

Run the **P7 native initialization diagnostic** workflow manually with an immutable 40-character
`source_ref`. Changes to this directory or its workflow also run on pull requests, using the PR head.
The workflow builds that source's NativeAOT probe, records source/toolchain/image hashes, compares
fresh and enlarged parent processes, traces ICU library loading, and retains full-size external
memory mappings for identity and source-document workloads.

`trace.py` requires Linux and the published probe; `--tracer` additionally needs the compiled
`dlopen-trace.so`, adjacent `.dbg` symbols and `addr2line`. Output directories must be new.
Diagnostic interposition is never linked into the product or acceptance image. Reports set
`acceptance` to `false`; parent allocation and traced measurements never subtract from RSS budgets.

The original scheduled/manual acceptance jobs remain in
[streaming-development.yml](../../.github/workflows/streaming-development.yml).
