# Performance baseline

Phase 0 exit criterion (SPEC §22): Xsl3 within ±3% of stock `XslCompiledTransform` on XSLT 1.0. This is the
baseline later phases are measured against (target 1.2–1.5× stock, SPEC §19.6).

Run on 2026-10-18 · .NET 10.0.13 · Windows 11 · BenchmarkDotNet 0.15.8 default job ·
`dotnet run -c Release --project bench/Xsl3.Benchmarks -f net10.0 -- --filter '*Xslt10Transform*' '*Xslt10Compile*'`

## Steady-state transform (pre-parsed input, output to `Stream.Null`)

Ratio is Xsl3 / stock mean time; RatioSD is about 0.1 for every row, so all differences are within noise.
Allocations match to within 2 KB in every case.

| Stylesheet | Orders | Stock | Xsl3 | Ratio |
|---|---:|---:|---:|---:|
| identity | 2,000 | 4.16 ms | 4.14 ms | 1.00 |
| identity | 20,000 | 42.2 ms | 41.4 ms | 0.99 |
| sort | 2,000 | 3.78 ms | 3.77 ms | 1.00 |
| sort | 20,000 | 57.1 ms | 57.4 ms | 1.01 |
| muenchian | 2,000 | 1.22 ms | 1.23 ms | 1.01 |
| muenchian | 20,000 | 19.8 ms | 19.7 ms | 1.00 |
| string-recursion | 2,000 | 1.56 ms | 1.56 ms | 1.01 |
| string-recursion | 20,000 | 16.0 ms | 16.4 ms | 1.03 |
| key-join | 2,000 | 4.17 ms | 4.18 ms | 1.01 |
| key-join | 20,000 | 42.5 ms | 42.1 ms | 1.00 |
| html-report | 2,000 | 66.1 ms | 57.8 ms | 0.92 |
| html-report | 20,000 | 5,500 ms | 5,477 ms | 1.00 |

## Compile (`Load`)

| Stylesheet | Stock | Xsl3 | Ratio |
|---|---:|---:|---:|
| identity | 490 µs | 503 µs | 1.03 |
| muenchian | 702 µs | 684 µs | 0.98 |
| html-report | 666 µs | 662 µs | 1.00 |

## Observations for later phases

- `html-report` is quadratic in both engines: 10× the input costs about 90× the time. Most of that is
  `<xsl:number/>` (level="single"), which counts preceding siblings for every row. The compact tree and its
  indexes (SPEC §14, §19) are the place to fix it.
