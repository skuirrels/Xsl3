# P7 S3-25: Debug verifier coverage

The owner resumed full P7 on 5 November 2026. This increment starts from PR #33's merged source
`8582a5b37842fb4a0eafdff90412ff7d8f8a1568` on `feature/p7-verifier-coverage`.
It addresses the per-compilation evidence gap in S3-25; it does not close full P7.

## Contract

- Record each compilation reaching QIL optimization, including its actual `HasStreamedInputs` eligibility,
  harness stage and whether optimization returned. Record eligibility at the code-generation boundary,
  independently of the optimizer's conditional verifier call.
- Record verifier entry and successful return inside `StreamedQilVerifier.Verify` itself. A thrown check
  records an attempt without claiming success. Debug eligible compilations must each have exactly one
  completed verification. Noneligible compilations must have none. Release builds still omit verification.
- Preserve per-case evidence in the full strict JSONL report. A static error before QIL generation has no
  eligible compilation. Preparation and assertion compilations cannot substitute for stylesheet compilation.
  Named initial modes can compile lazily during the stylesheet stage; those actual compilations are included.
- Make the Debug strict CI audit require this evidence; missing, incomplete or failed verification fails
  the audit, even if test outcomes and output fingerprints otherwise match the inventory.
- Keep the existing negative QIL rewrite controls and test actual loaded compilation through both APIs
  on .NET 8/10 in Debug and Release. Verify that captures remain observational and isolated.

## Evidence

Local implementation and review passed; hosted CI remains pending. Original inventories and previously
published evidence are unchanged. Local evidence is retained under `/private/tmp/xsl3-p7-verifier-20261105/`;
this temporary directory is not a durable hosted acceptance artifact. CI uploads the new per-compilation
records within the strict runtime JSONL files and the Debug verifier audit summary.

| Check | Result |
|---|---|
| Complete Debug strict inventory and existing forwarding/evaluation/inventory audits | 2,696 pass / 16 recorded failures / 0 skipped, unchanged across all 2,712 cases |
| Required verifier audit | 119,303 total compilations; all 4,968 eligible compilations verified exactly once |
| Debug focused units (.NET 10 / .NET 8), including bad rewrites, both APIs, saved compilation and capture isolation | 41 / 39 passed |
| Release focused units (.NET 10 / .NET 8), including absence of automatic verifier execution | 41 / 39 passed |
| W3C report contracts plus full strict profile test | 16 passed |
| Python tooling | 89 tests: 88 passed, one existing platform skip; the 32 runtime-audit tests passed again after the lazy-compilation correction |
| Negative mutations of the full captured inventory | Skipped verification, failed verification and unfinished optimization each rejected |

The initial audit correctly exposed a mistaken assumption in the audit implementation: `mode-1406` selects a
named initial mode, which `XsltExecutable.Unit` compiles lazily in the stylesheet stage. Its actual verifier
ran successfully. The audit now accepts compilation in either the compilation or stylesheet stage, with a
negative control proving that preparation/assertion compilation cannot substitute. No fixture, outcome,
verifier behavior or eligibility predicate was changed to make this pass.

Reproduce the acceptance audit after the Debug strict run:

```sh
python3 eng/audit-streaming-runtime.py \
  --report /path/to/streaming-strict-debug.jsonl --suite external/xslt30-test \
  --engine src/Xsl3/bin/Debug/net10.0/Xsl3.dll --manifest docs/plan/p7-runtime-inventory.json \
  --require-forwarding --require-stylesheet-forwarding --require-evaluation --require-verifier \
  --output /new/path/to/streaming-strict-debug-audit.json
```

## Remaining full P7 work

After this contract, address S3-04 retained bytes, S3-27 unintended internal grounding, S5-14 unused/truncated
group members, and S6-11/12 group/sort retention inside fork. The latest reviewed HTTP probe cohort passes
both memory matrices, but five original throughput targets still fail: identity, accumulator, merge, fork and
source-document. Final reviewed-source memory/throughput/first-output qualification, all regression gates,
supported API/saved execution, feature claims, documentation and release remain required. Preserve the
HTTP probe's URI qualification caveat and all historical results; see [PR #33's review](p7-forward-evidence-review.md).
