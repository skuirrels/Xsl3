# Streaming process corpus

Generated from `tests/Xsl3.Tests/StreamingProbe.Stylesheets.cs`. The seven canonical P7 paths are identity,
adjacent grouping, accumulators, iterate, merge, fork and source-document. Ancestor-set, type-check
and selected-copy/conditional-content workloads add regression coverage. Generated row fields are synthetic benchmark inputs, with no production
domain-data role. Tests enforce exact agreement with the shared fixture definitions.

Regenerate into a new directory with the `streaming-corpus` benchmark verb, inspect the result and replace
these `.xsl` files. Run `eng/streaming-benchmark.py` against the built benchmark assembly for paired tree
plus transform versus streamed measurements at 1 MB, 100 MB and 1 GB. See `docs/streaming-benchmark.md`.

`function-parameter` passes each live value through an absorbing `item()*` function and a typed absorbing callee. Its independent oracle uses the generated record's ordinal and value.

`overlap-values` atomizes every descendant element within each generated row. The nested `text` and
`em` values exercise overlapping selections and reuse of bounded header/character spill files. The
shared independent oracle derives its expected text from the generated-input fields; execution reads
the XML through the public APIs. The preceding fifteen stylesheet fixtures are unchanged.
