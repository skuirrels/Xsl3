# P7 phase A: baseline and S3–S6 acceptance accounting

The owner resumed P7 on 1 November 2026 with a bounded plan: capture a fresh baseline of the released
1.1.7 code, then do phase A, the S3–S6 acceptance accounting and the reassessment of the strict
runtime failures. Memory, throughput and the public streaming claim are later phases (B, C and D in
the [handoff](p7-handoff.md)). Branch: `feature/p7-phase-a`, from the released tree
(`b0bf3bb`, `v1.1.7`) plus the handoff documentation.

## Completion criteria

1. Every original gate has a recorded result for one immutable commit, with the raw evidence kept.
2. Each S3–S6 exit in [the original stage table](p7-streaming.md#stages) is accounted for set by set
   against an unstreamed run of the same population, by a checked-in script rather than prose.
3. Each S3–S6 obligation from the stage exits and the [requirements table](p7-completion.md#requirements-and-evidence)
   is mapped to evidence that passed on the baseline, with every partial or missing item listed.
4. Every strict failure and not-run case is reassessed from its fixture and the normative text.
   Harness and engine defects found this way are fixed with tests. Behaviour changes to the public
   engine, and changes to the owner-approved reviewed suites, go to the owner instead.
5. The strict inventory audit, unit suites and ordinary conformance gates pass on the phase head,
   with no regression from the baseline.

Out of scope: memory and throughput work, `supports-streaming`, and the original gates themselves,
which are unchanged.

## Baseline of 1.1.7 (`b0bf3bb`)

Hosted evidence is kept locally in `Xsl3-evidence/p7-baseline-b0bf3bb/` (outside the repository, since
`artifacts/` is ignored and was lost once). The run links are the durable references.

| Gate | Result on `b0bf3bb` | Evidence |
|---|---|---|
| S1 reviewed (1,000 cases) | 1,000 / 0 / 0 in Release and Debug; raw 790 / 129 / 0 retained | [Main CI 41351971235](https://github.com/skuirrels/Xsl3/actions/runs/41351971235) |
| S2 reviewed (155 cases) | 155 / 0 / 0 in Release and Debug (152 exact codes, 3 allowed results); raw 153 / 2 retained | same run |
| Strict runtime inventory | 2,695 / 16 / 1 in Release, Debug and legacy with saved sampling; inventory verified, zero unmatched codes, 2,459 cases with actual forward sources | same run |
| Unstreamed strm run | 2,693 / 18 / 1 (hosted summary); local per-case export reproduces it exactly | same run, plus local export |
| Push memory (200 MiB / 20 MiB) | 13 / 16 within budget, all 48 outputs valid. Merge 21,987,328, fork 22,487,040, source-document 21,479,424 bytes exceed 20,971,520 | [Streaming run 41358537924](https://github.com/skuirrels/Xsl3/actions/runs/41358537924) |
| Nightly memory (2 GiB / 200 MiB) | 16 / 16 within budget, all 48 outputs valid, exact 2,147,483,648-byte inputs; highest measured whole-process peak 41,312,256 bytes | same run |
| Benchmark (126 observations) | All 126 valid; 4 of 7 100 MB targets pass. Stream/tree medians: identity 1.038963, adjacent 0.686162, accumulator 0.774103, iterate 0.738982, merge 0.995675, fork 1.560587, source-document 1.027513 (Linux x64, 4 processors, .NET 10.0.14) | same run |

## Stage exit accounting

`eng/stage-exit-report.py` compares an audited strict export with an unstreamed export of the same
population, using the exit column of the stage table. CI now runs it after the unstreamed step and
keeps `streaming-stage-exits.json`. Outcomes only: it does not prove forward execution or memory.

| Stage | Exit sets | Strict | Unstreamed | Regressions | Named cases |
|---|---|---|---|---|---|
| S3 | source-document, sf-\*, sx-\*, si-\* except fork, merge, iterate, for-each-group (78 sets) | 2,129 / 5 / 0 | 2,129 / 5 / 0 | none | |
| S4 | si-apply-templates, si-next-match, si-apply-imports, su-\*, streamable (10 sets) | 227 / 4 / 0 | 226 / 5 / 0 | none | |
| S5 | accumulator, si-iterate, si-for-each-group, si-merge, merge | 212 / 5 / 1 | 211 / 6 / 1 | none | 7 of the 9 XTDE2220 cases pass |
| S6 | si-fork | 54 / 1 / 0 | 54 / 1 / 0 | none | |
| S7 | all 2,712 selected cases, 90% required | 99.41% | | | |

The S5 exit names "the 9 XTDE2220 cases" but no list was kept. Rerunning the 22 October baseline
(`0c4fac0` with only a temporary `streaming` feature claim, as the plan describes) recovers exactly nine:
`merge-070`, `071`, `076`, `078`, `079`, `080`, `081`, `083` and `084`. Seven now pass. `merge-081`
and `merge-084` fail for the confirmed fixture conflicts below; the owner accepted those dispositions
on 1 November 2026, so the named-case exit is met.

## Obligation map

[The evidence map](p7-phase-a-evidence-map.md) splits the stage exits and requirement cells into 116
obligations and checks each cited test in the baseline's TRX files.

| Stage | Direct | Partial | None |
|---|---:|---:|---:|
| S3 | 28 | 5 | 0 |
| S4 | 22 | 13 | 0 |
| S5 | 17 | 5 | 3 |
| S6 | 15 | 7 | 1 |

Evidence added after the map, closing its "none" items:

- S5: `StreamedStateRetentionTests` measures the bytes that survive a full compacting collection,
  sampled from `xsl:message` callbacks, across about 146,000 generated rows through both public APIs.
  `xsl:iterate` parameters (a running total and a copy of the last row), an adjacent group and a
  node-valued accumulator window must not grow by more than 1 MiB from the first sample; a control
  whose iteration parameter keeps every row must grow by more than 8 MiB.
- S6: the owner accepted the wording "a failing prong cancels the prongs after it; the prongs before it
  finish, so the error follows declaration order". `StreamedForkTests.AFailingProngCancelsLaterProngsAndEarlierProngsFinish`
  checks it through a compiled `xsl:fork`, loaded and saved: with a buffer of one, the first prong
  sees all 2,000 rows, the third stops within 200 and the second prong's error is raised.

Most partial items check output equality where the obligation is about forward execution, read
position or buffering. Both public API paths do assert streamed execution directly (writes before reader
EOF, physical read boundaries and captured `StreamedNavigator` sources), but only with explicit opt-in.

## Failure reassessment

The 16 strict failures and the one not-run case were re-derived from the fixtures and the XSLT 3.0
and XPath 3.1 Recommendations; the dispositions are in [p7-phase-a-dispositions.csv](p7-phase-a-dispositions.csv).
The pinned suite is upstream's latest commit and has no upstream issues for any of these cases.

- **Confirmed fixture conflicts (8):** `merge-073`, `081`, `082` and `si-merge-004ns` (§15.3 XTSE3195
  forbids `streamable` with `for-each-item`, whatever its value), `merge-084` (§15.7, absent context size),
  `sf-reverse-001` (XPath §3.3.1.1, document order), `si-fork-953` (§19.8.4.19 fork exception) and
  `streamable-147` (§18.1.1, validation overrides `input-type-annotations`).
- **Harness gap fixed:** `merge-039` was skipped because the runner refused every collection environment.
  The engine resolves a collection URI as a directory of `.xml` files in name order; the runner now honours
  a collection only when its declared sources are exactly those files. `merge-039` passes with both log
  files read forward. The strict coverage check now requires zero not-run cases.
- **Fixture/profile conflicts (3):** `stream-013`, `non-stream-013` and `si-result-document-024` expect
  non-schema-aware lax validation (lax treated as strip, §27.2) without declaring `schema_aware` as
  unsatisfied. Schema-awareness is a property of the processor (§27.2), and the runner claims it
  throughout; a schema-aware processor annotates lax results without declarations as `xs:anyType`
  (§25.4.1.1). The suite's reference runner asks for schema-awareness only for tests tagged `schema_aware`,
  so phase A measured that rule: it fixes these three but breaks 51 strm cases, 48 of them fixtures that use
  `xsl:import-schema` without the tag (XTSE1650/1660). The ordinary and `sa` profiles were unchanged. The
  change was reverted and P7D's disposition stands.
- **Engine defect, fixed (1):** `sx-union-202`. Two striding union operands are crawling (§19.8.8.4),
  so the select is not guaranteed streamable and XTSE3430 is required (§19.10). The owner chose to follow
  the spec: the compiler's peer-union rule is removed, so `sx-union-202` passes and the S2 reviewed
  correction for it is withdrawn. `sx-union-315` now fails: its fixture expects a `for-each` over the union of
  the `PRICE` and `QUANTITY` paths with a consuming body to stream, which needs the same extension
  (fixture conflict, accepted). Two unit
  tests that used such a union incidentally were rewritten in spec-streamable form. The frozen runtime
  inventory is edited for these two cases only: `sx-union-315` is recorded as failing and
  `sx-union-202` as passing, and both leave the stylesheet-forwarding minimum (now 2,457), since a
  static XTSE3430 leaves no execution to observe. No other inventory entry changed.
- **Optional behaviour, accepted (3):** `streamable-021`, `042` and `043` validate strictly against
  `loans.xsd`, which only the test environment supplies. Using configured schemas for validation is
  permitted (§25.4.1.1, §2.10) but not required. The owner kept `XsltSettings.Schemas` as an import
  catalogue, so these remain fixture dependencies.
- **Deferred to S7 (1):** `system-property-012` needs `supports-streaming` to be `yes`.

## Owner decisions (1 November 2026)

1. `sx-union-202`: follow the spec. The peer-union rule is removed in every mode, since §19.10 allows an
   extension only when the user asks for one and there is no setting to ask. Breaking for stylesheets
   that relied on it (see the migration guide).
2. `streamable-021/042/043`: keep the import-catalogue meaning of `XsltSettings.Schemas`.
3. S6 exit wording: a failing prong cancels the prongs after it; earlier prongs finish so the error
   follows declaration order.
4. S5 named cases: `merge-081` and `merge-084` accepted as fixture conflicts.
5. The five remaining extensions (3 November 2026): keep them on by default, with
   `XsltSettings.StreamabilityExtensions` to turn them off. See [the extension review](p7-extension-review.md).

The other five compiler extensions behind `AllowStreamabilityRewrites` (the `except` and dot forms, the
descendant-to-child rewrite, scanning patterns, grounded `current-group()` and quantified bindings) are
on in every mode and carry 101 W3C cases. They need the same conformance review before P7 can claim
§19.10 compliance; that review is outside phase A.

## Remaining phase A work

Criteria 1, 2 and 4 are met on the local phase head, and criterion 3's map now has no "none" items.
Local checks after the owner decisions: strict 2,696 / 16 / 0 with the inventory audit passing (sx-union-202
now passes, sx-union-315 now fails), stage exits with every non-passing case accounted for and both S5
named cases met by acceptance, S2 raw 154 / 1 and reviewed 155 / 0 (153 exact codes, 2 allowed results),
all 97 W3C tests and the Python tooling tests. The union change touches the analyser, so the unit suites
were rerun (results in the pull request). Hosted CI on the pushed phase head is still required (criterion 5).

Next:

- Strengthen the partial items that check output equality where the obligation is about forward
  execution, read position or buffering, starting with the optimizer restriction (S3-18), the strict
  fallback detection (S3-27) and the inspection, deep-descent and ascent function categories (S4-21, 24, 25).
- Review the other five `AllowStreamabilityRewrites` extensions against §19.10: done, see
  [the extension review](p7-extension-review.md). All five are extensions that 101 W3C cases depend on;
  they stay on by default behind `XsltSettings.StreamabilityExtensions` (owner decision 5).
