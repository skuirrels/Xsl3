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
- S3-18 (4 November 2026): `StreamedOptimizerPolicyTests` runs the real optimizer, in any build configuration, over
  two graphs that the ordinary policy rewrites so that a read moves: a loop-invariant filter condition hoisted into a
  conditional, and a single-use `let` inlined past a later read. The ordinary result fails the verifier's contract,
  which shows each graph is a real rewrite target; the streamed policy's result passes it.
- S3-27 (4 November 2026): `RuntimeCaseReportTests.AnInjectedTreeFallbackPassesItsOutcomeButLosesItsRecordedForwardSource`
  runs a guaranteed-streamable stylesheet with forward execution off. Its output and outcome are unchanged, and
  the record shows only a tree source. `test_an_engine_recorded_tree_fallback_fails_the_stylesheet_floor` feeds
  that record to the strict audit: no per-source rule fires, and the frozen stylesheet-forwarding floor
  fails the run. Grounding inside a forward run (for example `copy-of(.)` at the root) is still not detectable,
  and the 253 non-forwarded W3C cases remain dispositioned rather than failed.
- S4-21, S4-24, S4-25 (4 November 2026): `StreamedFunctionPathTests.FunctionCategoriesRunForwardInStreamableTemplateRules`
  calls an `inspection`, `ascent` and `deep-descent` stylesheet function on the context node of a streamable-mode
  template rule, through both public APIs, over about 20,000 sections. Forward execution writes the first result
  before reading half of the input; the tree-execution control writes only after reading more than half.
- S3-17, S4-04, S4-05, S4-18, S4-22, S4-29, S4-30, S5-02, S5-14 (4 November 2026): `StreamedForwardExecutionTests`
  and a `filter` row in `FunctionCategoriesRunForwardInStreamableTemplateRules` apply the same early-output check
  over 20,000 records, forward and as a tree, through both public APIs where the obligation has both. They cover
  atomization of `data()`, `!` and absorbing-function operands, including inside `xsl:source-document`; all 12
  `ApplyTemplates` source and destination overloads, which also assert a captured `StreamedNavigator` initial
  source (the three raw-result overloads assert only that); a named mode with parameters and tunnel parameters;
  a streamable attribute set; `xsl:next-match`; the `shallow-copy`, `text-only-copy` and `shallow-skip` built-in
  rules; an accumulator over subtrees the template rule never processes; and `group-adjacent` and
  `group-starting-with` members.
- S6-10 (4 November 2026): fork holds each prong's result until the fork completes (S6-08), so early output does not
  apply. `StreamedForwardExecutionTests.ForkPrincipalRawItemsAreCopiedBeforeTheSourceAdvances` serializes all
  20,000 records from a raw-item prong while the captured initial source is a `StreamedNavigator`, which cannot
  revisit a record; the tree control records a tree source and the same output.
- S6-08 (4 November 2026): `StreamedForkTests.ALaterProngThatFinishesFirstStillFollowsTheEarlierOne` holds the first
  prong in its closing `xsl:message` until the second prong has finished, loaded and saved, with a buffer of one.
  The first callback joins the second worker after observing its final message, so the second prong
  always completes first and the result still follows declaration order. Recording the message before
  signalling also prevents a scheduling race in the callback-order assertion.
- S6-05 (4 November 2026): the hosted x64 push matrix passes all 16 budgets for the first time ([41905961247](https://github.com/skuirrels/Xsl3/actions/runs/41905961247)):
  fork peaks at 17,686,528 bytes, merge 16,785,408 and source-document 16,134,144, against 20,971,520. The
  nightly matrix on the same run also passes 16/16. The earlier excess (fork 22,671,360 bytes on the
  [41765424039](https://github.com/skuirrels/Xsl3/actions/runs/41765424039) nightly) was ICU: the probe named its documents with `urn:` URIs, and `System.Uri`
  lower-cases an unrecognised scheme through the globalization stack, which loads ICU and libstdc++, about
  4.4 MB of native code. Identity parses no URI, so only these three workloads paid it. A debugger stop on the
  ICU load and an `smaps` breakdown in a Linux container found the cause; with invariant globalization as a
  diagnostic the three fell to 15.2-16.4 MB with the same outputs. The owner chose to move the probe to
  `http://stream-probe.test/` URIs, which `System.Uri` parses without loading ICU. Budgets, inputs, outputs and
  globalization are unchanged; `urn:` documents stay covered by `DocumentReferenceTests` and
  `EntityResourceTests`. A consumer that resolves `urn:` documents still loads ICU once per process.
- S4-06 (5 November 2026): `StreamedForwardExecutionTests.XdmSelectionKeepsItsTreeRepresentationForEveryDestination`
  reuses a supplied `XdmNode` through raw, stream, text and XML destinations with forward execution on and off.
  All runs report an opened tree source and a non-forward input, with the same complete result.
- S4-11 (5 November 2026): `TreeEntryContractsKeepTheirActualRepresentation` checks both public APIs:
  a 1.0 stylesheet stays on a tree with the development switch on or off; a 3.0 streamable stylesheet stays
  on a tree when the switch is off. Capture, late output and borrowed-reader ownership are asserted.
- S4-27 (5 November 2026): `ValidatedInitialReadersKeepAnnotationsAndForwardExecution` supplies a validating
  `XmlReader` through both public APIs, with forward execution on and off. Every one of 20,000 records keeps
  its integer element annotation and its parent's typed, schema-defaulted attribute. Forward runs report a
  `StreamedNavigator` and write before half the input is read; tree controls report a tree and write late.
  This does not change the accepted import-catalogue dispositions of `streamable-021/042/043`.

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

The five remaining compiler extensions were reviewed in [PR #30](https://github.com/skuirrels/Xsl3/pull/30)
and released in 1.3.0 behind the owner-approved `StreamabilityExtensions` setting. The original concern
about applying them without a user option is resolved; it is not a remaining phase A review task.

## Remaining phase A work

The initial accounting and reassessment shipped in 1.2.0 through [PR #28](https://github.com/skuirrels/Xsl3/pull/28).
Its [hosted CI](https://github.com/skuirrels/Xsl3/actions/runs/41415279933) passed. The subsequent extension
review and optimizer/function evidence shipped in 1.3.0 through PRs #30–32. The bounded follow-up is
[PR #33](https://github.com/skuirrels/Xsl3/pull/33); its [checks](https://github.com/skuirrels/Xsl3/pull/33/checks)
exercise the new entry and fork tests. See the [review record](p7-forward-evidence-review.md) for its exact
scope, independently checked memory/throughput evidence and validation limits.

Recorded checks after the original owner decisions: strict 2,696 / 16 / 0 with the inventory audit passing (sx-union-202
now passes, sx-union-315 now fails), stage exits with every non-passing case accounted for and both S5
named cases met by acceptance, S2 raw 154 / 1 and reviewed 155 / 0 (153 exact codes, 2 allowed results),
all 97 W3C tests and the Python tooling tests. The union change touches the analyser, so the unit suites
were rerun (results in the pull request). These are historical results for that phase head; new changes
still require CI on their own reviewed source.

Next:

- Strengthen the partial items that check output equality where the obligation is about forward
  execution, read position or buffering. The additions above strengthen S3-17, S3-18, S3-27, S4-04, S4-05,
  S4-06, S4-11, S4-18, S4-21, S4-22, S4-24, S4-25, S4-27, S4-29, S4-30, S5-02, S5-14, S6-05, S6-08 and S6-10.
  Preserve the stated limits: S3-27 still cannot detect grounding inside an otherwise forward run, and S5-14's
  new early-output check covers complete group consumption, not read-position evidence for every truncated
  or unused-member path. Other partial items include S3-04 (byte bounds), S3-25 (per-case verifier execution)
  and S6-11/12 (grouping/sorting retention in fork). The original [evidence map](p7-phase-a-evidence-map.md)
  remains a baseline record, not a current completion count.
- Review the other five `AllowStreamabilityRewrites` extensions against §19.10: done, see
  [the extension review](p7-extension-review.md). All five are extensions that 101 W3C cases depend on;
  they stay on by default behind `XsltSettings.StreamabilityExtensions` (owner decision 5).
