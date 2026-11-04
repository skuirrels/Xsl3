# P7 S3 to S6 acceptance-evidence map (released commit b0bf3bb)

Phase A record for [P7 phase A](p7-phase-a.md). Evidence paths under `Xsl3-evidence/` are local copies of hosted CI
artefacts (run 41351971235, retained until 30 January 2027); the run link is the durable reference. Rows reflect
`b0bf3bb` before phase A changes: `merge-039` now runs and passes (see the phase record). After the owner decisions
of 1 November 2026, S5-07, S5-11 and S5-16 are covered by `StreamedStateRetentionTests`, S6-16 is amended to "cancels
the prongs after it; earlier prongs finish", and S6-16/S6-17 are covered by
`StreamedForkTests.AFailingProngCancelsLaterProngsAndEarlierProngsFinish`. On 4 November 2026, S3-17, S3-18, S3-27,
S4-04, S4-05, S4-18, S4-21, S4-22, S4-24, S4-25, S4-29, S4-30, S5-02, S5-14, S6-08 and S6-10 gained the evidence listed under
"Evidence added after the map" in the phase record. The rows below are unchanged.

Sources: `docs/plan/p7-streaming.md` (Stages, Owner decisions, S3 to S6 progress), `docs/plan/p7-completion.md`
(Requirements and evidence, rows S3 to S6), `docs/plan/p7-runtime-contracts.md`, `tests/Xsl3.Tests`, and the hosted
main CI run 41351971235 (ubuntu) evidence bundle at
`/Users/tyomidi/Source/Xsl3-evidence/p7-baseline-b0bf3bb/ci-41351971235/ubuntu/`.

## How "passed" was checked

- Unit tests: the two full Release `Xsl3.Tests` TRX files (`..._21_27_18_net10.0.trx`, 7,339/7,339 passed;
  `..._21_37_24_net8.0.trx`, 6,229/6,229 passed). Every test named below was found in them with outcome `Passed`.
  "yes 8+10" = present and passed on both; "yes 10 only" = saved-assembly cases compiled only for .NET 10.
- W3C strict runs: `streaming-strict.jsonl` (Release, 3.0 API), `streaming-strict-legacy.jsonl` (Release, legacy
  `XslCompiledTransform` API, saved sampling 1-in-10) and `streaming-strict-debug.jsonl` (Debug, verifier active),
  all .NET 10 only, each 2,695 pass / 16 fail / 1 not run of 2,712. Per-set tallies below are from the API run; the
  legacy run has identical per-set outcomes and forwarding counts. "fwd" = cases whose stylesheet stage recorded an
  actual forward (`StreamedNavigator`) source; "init" = forward initial (streamed initial mode) sources.
- W3C harness unit tests (`ExecutionPolicyHarnessTests`, `StreamedInitialSourceHarnessTests`,
  `StrictStreamingCoverageTests`): `..._21_47_35_net10.0.trx`, 97/97 passed, .NET 10 only.

## Strength rules

- DIRECT: the evidence asserts this obligation's property. For "runs streamed" obligations this requires a forward
  assertion (write before reader EOF, physical read boundary, retained-count bound, or a captured actual forward
  source in a passing case), not just output equality.
- PARTIAL: related evidence that does not assert the property; the missing part is stated.
- NONE: no evidence found.
- Output-equality tests (forward vs tree) prove functional correctness only. They do not prove non-tree execution,
  early output or bounded memory. Retained-count/payload assertions are logical bounds, not byte/RSS bounds.

## S3: forward runtime, grounding, source-document, optimizer/verifier, strict mode

| Id | Obligation | Evidence | Passed on b0bf3bb | Strength |
|---|---|---|---|---|
| S3-01 | `StreamedNavigator` forward traversal over `XmlReader` matches tree semantics | `Xsl3.Tests.StreamedNavigatorTests.ForwardTraversalMatchesXPathDocument`; `...StreamedNavigatorTests.UnselectedSubtreesAreSkippedAndIdsKeepDocumentOrder`; `...StreamedNavigatorTests.EntityBoundariesDoNotSplitAdjacentTextNodes` | yes 8+10 | DIRECT |
| S3-02 | Revisiting consumed content raises `stream-position`, never wrong/empty results | `...StreamedNavigatorTests.ClonesShareConsumptionAndRejectRevisitingPassedContent`; `...ValueAfterTraversingChildrenRaisesAnErrorInsteadOfReturningEmpty`; `...ANewDescendantScanMustNotTreatConsumedContentAsEmpty`; `...ConsumingAMatchingNestedDescendantFailsInsteadOfDroppingIt` | yes 8+10 | DIRECT |
| S3-03 | Attribute/namespace/ancestor inspection does not advance the reader | `...StreamedNavigatorTests.AttributesNamespacesAndAncestorsDoNotAdvanceTheReader` | yes 8+10 | DIRECT |
| S3-04 | Navigator retained state bounded by depth, not record count | `...StreamedNavigatorTests.RetainedFramesAreBoundedByDepthRatherThanRecordCount` | yes 8+10 | PARTIAL: asserts internal `RetainedNodeCount` for one shape at two sizes; no byte/RSS bound |
| S3-05 | Reader ownership and cancellation explicit (borrowed readers not closed) | `...StreamedNavigatorTests.CancellationAndReaderOwnershipAreExplicit`; `...ClosedSourceFramesReleaseBorrowedReadersWithoutClosingThem` | yes 8+10 | DIRECT |
| S3-06 | `copy-of` grounds each selected node and leaves the next sibling unread | `...StreamedNavigatorTests.GroundingUsesACompactSubtreeAndLeavesTheNextSiblingUnread`; `...StreamedNavigatorTests.CompiledGroundingRunsOncePerRecord`; `...StreamedSourceDocumentTests.CopyFunctionsGroundEachSelectedRecordBeforeAdvancing` | yes 8+10 | DIRECT (navigator level). Note: `CopyFunctionsGroundEachSelectedRecordBeforeAdvancing` asserts only output and stream closure despite its name |
| S3-07 | `snapshot` semantics on forward input (typed selected nodes, ancestors `xs:anyType`, shallow attribute/namespace parents) | `...StreamedSourceDocumentTests.TypedSnapshotsPreserveSelectedTypesAndGiveAncestorsAnyType`; `...AttributeSnapshotsKeepOtherAttributesButNoParentChildren`; `...SnapshotPreservesAParentlessNamespaceNode`; W3C strict `sf-snapshot` 24/24, 23 fwd | yes 8+10; W3C 10 | DIRECT (functional); no retention bound for snapshot ancestors |
| S3-08 | Overlapping selections are replayed; nested matches never silently dropped; replay buffer released | `...StreamedReplayTests.ExplicitGroundingKeepsNestedMatchesAndFollowingInput`; `...StreamedReplayTests.AbortedGroundingCannotSilentlyLoseMatchingDescendants`; `...StreamedSourceDocumentTests.AbsorbingOverlappingDescendantsNeverSilentlyDropsNestedMatches`; `...StreamedReplayTests.AdvancingPastACopiedSubtreeReleasesItsReplayBuffer` | yes 8+10 | DIRECT |
| S3-09 | Copy options (`copy-namespaces`, `validation`, `copy-accumulators`) independent on forward copies | `...StreamedSourceDocumentTests.CopyOptionsIndependentlyControlAnnotationsAndNamespaces`; `...NamespaceSensitiveCopiesRespectBothOptions`; `...PreservingCopyAnnotationsDoesNotImplicitlyCopyAccumulators`; `...StreamedReplayTests.CopyInstructionsReplayIndependentlyOfNamespaceAndAnnotationOptions` | yes 8+10 | DIRECT |
| S3-10 | Copy provenance registry does not retain unreferenced copies | `...CopyOriginRegistryTests.UnreferencedCopiesAndTheirOriginsAreNotKeptByTheRegistry`; `...CopyOriginRegistryTests.DiscardedStreamedCopiesAndTheirReadersAreNotOwnedByTheRegistry` | yes 8+10 | DIRECT (GC reachability, not bytes) |
| S3-11 | `xsl:source-document streamable="yes"` actually executes on a forward source with early output | `...StreamedSourceDocumentTests.PerRecordResultsAreWrittenBeforeTheReaderReachesTheEnd`; `...StreamingReadBoundaryTests.ForwardExecutionWritesBeforeCrossingThePhysicalReadBoundary` (source-document, api/legacy/saved); `Xsl3.W3C.ExecutionPolicyHarnessTests.StrictSourcesActuallyForwardWhileOrdinarySourcesRemainTrees` | yes 8+10 (saved 10 only); harness 10 | DIRECT |
| S3-12 | Owned source streams close at exit, on parse/body errors, on cancellation, before an outer catch | `...StreamedSourceDocumentTests.OwnedSourcesCloseOnParsingAndBodyErrors`; `...CancellationDuringSkippedContentClosesTheSource`; `...ASourceCaughtByAnOuterTryClosesBeforeItsCatchRuns`; `...ACloseFailureDoesNotHideASourceError` | yes 8+10 | DIRECT |
| S3-13 | Nested source-documents use independent readers | `...StreamedSourceDocumentTests.NestedSourcesHaveSeparateReadersAndCloseAtTheirOwnExit` | yes 8+10 | DIRECT |
| S3-14 | Source-document reuses the ordinary whitespace/DTD reader chain | `...StreamedSourceDocumentTests.SkippingAnUnselectedSubtreeKeepsWhitespaceReaderState`; `...DtdElementContentWhitespaceMatchesOrdinaryInput`; `...SourceExpressionsMatchOrdinaryExecution` | yes 8+10 | DIRECT |
| S3-15 | Forward source validation (strict, lax, `type`) with early results and error/closure semantics | `...StreamedSourceDocumentTests.ForwardValidationAppliesImportedSchemasAndDefaults`; `...ValidationProducesResultsBeforeTheReaderReachesTheEnd`; `...ForwardValidationReportsErrorsAndClosesItsInput`; `...UndeclaredLaxRootsKeepInstanceAttributesWithoutApplyingTheirElementConstraints` | yes 8+10 | DIRECT (identity-constraint state retention not bounded, per S3 notes) |
| S3-16 | Document access settings and read/write URI conflicts enforced for streamed sources | `...StreamedSourceDocumentTests.StreamedReadAndResultDocumentUrisCannotOverlap`; `...InvalidAndDisabledDocumentAccessNeverOpensTheResolver` | yes 8+10 | DIRECT |
| S3-17 | Expression/function operands atomized/consumed item by item before advancing | `...StreamedSourceDocumentTests.AtomizationConsumesEachRecordBeforeAdvancing`; `...StreamedFunctionTests.AbsorbingSequenceArgumentsAreConsumedBeforeAdvancing`; W3C strict `sx-*` 816/817 (794 fwd) | yes 8+10; W3C 10 | PARTIAL: test names claim per-item ordering but assert output only; no read-position/early-output observation |
| S3-18 | Optimizer restricted for forward QIL (no inline/hoist/duplicate/index rewrites) | Indirect: hosted Debug strict run `streaming-strict-debug.jsonl` 2,695/16/1 identical to Release; S3 progress "optimizer verification" | W3C Debug 10 | PARTIAL: no test compiles a stylesheet that ordinary optimisation would hoist/duplicate and asserts the conservative policy kept the read in place |
| S3-19 | Debug verifier rejects a second pass (duplicated read) over a streamed operand | `...StreamedQilVerifierTests.DuplicatingAReadIsRejected` | yes 8+10 | DIRECT (synthetic QIL mutation) |
| S3-20 | Verifier rejects hoisting a read out of a loop or branch | `...StreamedQilVerifierTests.HoistingAReadOutOfALoopIsRejected`; `...HoistingAReadOutOfABranchIsRejected` | yes 8+10 | DIRECT |
| S3-21 | Verifier rejects reordering reads | `...StreamedQilVerifierTests.ReorderingReadsIsRejected` | yes 8+10 | DIRECT |
| S3-22 | Verifier rejects introducing a read, buffer or cache | `...StreamedQilVerifierTests.IntroducingAReadOrBufferIsRejected`; `...AddingACacheBindingIsRejected`; `...ChangingAForBindingIntoACacheIsRejected` | yes 8+10 | DIRECT |
| S3-23 | Verifier rejects changing a read's axis, pull/before-next policy or pruning | `...StreamedQilVerifierTests.ChangingTheAxisOfAnExistingReadIsRejected`; `...ChangingThePullPolicyOfAnOperationIsRejected`; `...ChangingTheBeforeNextPolicyOfAFilterIsRejected`; `...ChangingSubtreePruningOfAnExistingScanIsRejected` | yes 8+10 | DIRECT |
| S3-24 | Verifier allows elimination and unchanged reads | `...StreamedQilVerifierTests.UnchangedReadsAndEliminatedOrderingAreAllowed` | yes 8+10 | DIRECT |
| S3-25 | Verifier runs on real compilations and accepts every strict-suite stylesheet | `streaming-strict-debug.jsonl` (Configuration Debug) 2,695/16/1 | W3C Debug 10 only | PARTIAL: verifier invocation per case is not counted or asserted; Debug unit run in CI covers only `StreamingDeclarationTests` and source-line tests |
| S3-26 | Strict mode supplies streamed inputs as readers and executes them forward | `Xsl3.W3C.ExecutionPolicyHarnessTests.StrictSourcesActuallyForwardWhileOrdinarySourcesRemainTrees`; `Xsl3.W3C.StreamedInitialSourceHarnessTests.StrictInitialSourceIsNotParsedIntoTheGlobalContext`; strict audit: 2,459/2,712 cases with actual forward sources, `Verified: true` | harness 10; W3C 10 | DIRECT |
| S3-27 | Strict mode: a guaranteed-streamable construct that falls back to a tree fails its test | S3-02 (revisit raises), `Xsl3.W3C.StrictStreamingCoverageTests.*` (frozen forwarding minimum); `docs/plan/p7-runtime-contracts.md` "Non-forwarding case review" (253 cases dispositioned) | W3C 10 | PARTIAL: silent materialisation (grounding a whole input) is not detected (the plan's own risk table says strict mode cannot see it); 253 non-forwarded cases are dispositioned, not failed; no negative test injects a fallback and expects failure |
| S3-28 | Strict coverage cannot regress silently (lost passes, new skips, wrong codes) | `Xsl3.W3C.StrictStreamingCoverageTests.LosingAPassingCaseCannotHideBehindAnUnchangedFailureList`; `...ANewSkipCannotReplaceAPassingCase`; `...AnExpectedErrorWithTheWrongCodeCannotBecomeAPass`; `...TheSameNumberOfSkipsCannotHideAChangedReason` | harness 10 | DIRECT |
| S3-29 | Exit: `source-document` in strict mode at baseline | W3C strict `source-document` 45/47, 26 fwd; fails `stream-013`, `non-stream-013` (`supports-streaming` not claimed, S7) | W3C 10 (API+legacy+Debug) | DIRECT (equals the unstreamed 45/2 level); note only 26/47 forwarded |
| S3-30 | Exit: `sf-*` in strict mode at baseline | W3C strict `sf-*` 723/724, 702 fwd; fails `sf-reverse-001` (in original unstreamed failure list) | W3C 10 | DIRECT |
| S3-31 | Exit: `sx-*` in strict mode at baseline | W3C strict `sx-*` 816/817, 794 fwd; fails `sx-union-202` (expects XTSE3430, gets a result) | W3C 10 | DIRECT (at baseline); the XTSE3430 miss is an S2 analysis gap |
| S3-32 | Exit: `si-*` other than fork/merge/iterate/for-each-group in strict mode at baseline | W3C strict 545/546, 526 fwd; fails `si-result-document-024` | W3C 10 | DIRECT |
| S3-33 | Strict results identical through the legacy/saved API path | `streaming-strict-legacy.jsonl` (Api false, saved 1-in-10) 2,695/16/1, per-set identical | W3C 10 | DIRECT |

## S4: streamable modes, both API paths, global context, attribute sets, function categories, schema contracts

| Id | Obligation | Evidence | Passed on b0bf3bb | Strength |
|---|---|---|---|---|
| S4-01 | New `ApplyTemplates(XmlReader, ...)` streams the initial match selection | `...StreamedInitialModeTests.ReaderOutputIsDeliveredDuringTheForwardPass(legacy: False)`; `...StreamingReadBoundaryTests.ForwardExecutionWritesBeforeCrossingThePhysicalReadBoundary` (api); `...StreamingExecutionCaptureTests.BothApisReportTheActualInputRepresentation` | yes 8+10 | DIRECT |
| S4-02 | New `ApplyTemplates(Stream, Uri?, ...)` streams | `...StreamingExecutionCaptureTests.OtherPublicInputPathsKeepTheirActualForwardRepresentation("api-stream")`; `...StreamedInitialModeTests.AllSourceAndDestinationOverloadsShareTheEntryContract` | yes 8+10 | DIRECT (captured forward representation); no early-output check for this overload |
| S4-03 | New `ApplyTemplates(Uri, ...)` streams and owns the resolver's stream | `...StreamingExecutionCaptureTests.OtherPublicInputPathsKeepTheirActualForwardRepresentation("api-uri")`; `...StreamedInitialModeTests.UriResolverReadersAreOwnedIncludingPositionedSubtrees`; `...RequestedStreamedObserversRunForTheInitialSelection` | yes 8+10 | DIRECT |
| S4-04 | All 12 source x destination overload combinations share the entry contract | `...StreamedInitialModeTests.AllSourceAndDestinationOverloadsShareTheEntryContract` | yes 8+10 | PARTIAL: output and ownership only; forward execution not asserted for the 12 combinations |
| S4-05 | Overloads pass mode, parameters and tunnel parameters in streamed execution | `...StreamedInitialModeTests.NamedModeReceivesParametersAndTunnelParameters` | yes 8+10 | PARTIAL: output only (forward and tree runs) |
| S4-06 | `ApplyTemplates(XdmValue)` never streams | `...StreamedInitialModeTests.XdmSelectionKeepsItsTreeAndGlobalContext`; `...InitialSelectionDoesNotAcquireUnrequestedAccumulators("xdm")` | yes 8+10 | PARTIAL: output/error only; no capture asserting a tree representation |
| S4-07 | A non-streamable mode builds a tree through the new overloads | `...StreamedInitialModeTests.NonstreamableModeUsesAnOrdinaryTree` (two passes over the input succeed) | yes 8+10 | DIRECT |
| S4-08 | Legacy `Transform(XmlReader)` streams a streamable default mode | `...StreamedInitialModeTests.ReaderOutputIsDeliveredDuringTheForwardPass(legacy: True)`; `...StreamingReadBoundaryTests.ForwardExecutionWritesBeforeCrossingThePhysicalReadBoundary` (legacy); `...StreamingExecutionCaptureTests.BothApisReportTheActualInputRepresentation(api: False)` | yes 8+10 | DIRECT, but only with `XsltSettings.StreamedExecution = true` (see S4-12) |
| S4-09 | Legacy `Transform(string uri)` also streams | `...StreamingExecutionCaptureTests.OtherPublicInputPathsKeepTheirActualForwardRepresentation("legacy-uri")` | yes 8+10 | DIRECT |
| S4-10 | Saved assembly loaded through the legacy API keeps the forward initial mode | `...StreamedInitialModeTests.SavedInitialModePreservesForwardExecutionAndAbsence`; `...StreamingReadBoundaryTests.ForwardExecutionWritesBeforeCrossingThePhysicalReadBoundary` (saved) | yes 10 only | DIRECT |
| S4-11 | Legacy path keeps tree behaviour for 1.0 stylesheets or when forward execution is off | `...StreamedInitialModeTests.VersionOneReaderEntryKeepsItsExistingTreeBehavior`; `...LegacyReaderKeepsItsGlobalContextWhenForwardExecutionIsOff` | yes 8+10 | PARTIAL: output only; no representation capture |
| S4-12 | Public selection: streaming follows the documented opt-in under Strict/Fallback | `...StreamedInitialModeTests.PublicStaticPolicyKeepsForwardExecutionOptIn` (12 cases, both APIs) | yes 8+10 | DIRECT. Asserts that with default settings neither API streams (forward only if `StreamedExecution` or `XSL3_STREAM_STRICT=1`): decision 5's "legacy Transform streams a streamable default mode" holds only when opted in |
| S4-13 | Global context item is independent of the streamed initial selection | `...StreamedInitialModeTests.GlobalContextIsGroundedAndIndependent`; `...StreamedAccumulatorTests.GlobalContextAndInitialSelectionHaveIndependentAccumulatorValues`; `Xsl3.W3C.StreamedInitialSourceHarnessTests.StrictInitialSourceIsNotParsedIntoTheGlobalContext` | yes 8+10; harness 10 | DIRECT |
| S4-14 | Required global context absent raises XTDE3086 before opening input | `...StreamedInitialModeTests.RequiredGlobalContextFailsBeforeOpeningInitialInput` | yes 8+10 | DIRECT |
| S4-15 | Global context item type is checked (XTTE0590) | `...StreamedInitialModeTests.OptionalGlobalContextChecksItsTypeWhenPresent` | yes 8+10 | DIRECT |
| S4-16 | `position()`/`last()` in global focus raise XPDY0002 with a streamed selection | `...StreamedInitialModeTests.NumericGlobalFocusCannotUseTheInitialSelection` | yes 8+10 | DIRECT |
| S4-17 | Global prerequisites do not buffer the principal result | `...StreamedInitialModeTests.GlobalPrerequisitesDoNotBufferThePrincipalResult` (write before EOF) | yes 8+10 | DIRECT |
| S4-18 | Streamable `xsl:attribute-set` runs in a streamed template | `...StreamedInitialModeTests.StreamableFunctionAndAttributeSetRunInAnInitialMode`; `...StreamedGroupingTests.OrdinaryAttributeSetsInheritTheGroup` | yes 8+10 | PARTIAL: output only; no forward/read-position assertion; no W3C set isolates attribute sets in streamed template rules |
| S4-19 | Attribute-set streamability declaration rules (overrides, references) | `...StreamingDeclarationTests.AttributeSetOverridesPreserveTheStreamingContract`; `...AttributeSetReferenceRuleAppliesEvenWithFallback`; `...AttributeSetDeclarationsRetainTheirOwnStreamingFlags` | yes 8+10 | DIRECT (static) |
| S4-20 | Function category absorbing in a streamed template | `...StreamedFunctionTests.AbsorbingFunctionsCanConstructTheirResultWhileDispatchingInput`; `...StreamedSingletonFunctionTests.TheFirstNodeIsConsumedWithinTheOriginalFunctionCall`; W3C strict `su-absorbing` 35/35, 27 fwd, 4 in streamed initial mode | yes 8+10; W3C 10 | DIRECT (functional, forward-captured W3C initial-mode cases); unit tests are output only; no early-output/bound assertion |
| S4-21 | Function category inspection in a streamed template | W3C strict `su-inspection` 8/8 (4 positive, forwarded via `xsl:source-document` in named templates) | W3C 10 | PARTIAL: no unit runtime test invokes an `inspection` function on a streamed node; W3C cases are not streamable-mode template rules; output only |
| S4-22 | Function category filter in a streamed template | `...StreamedTransmittingFunctionTests.FilterResultsCanBeNavigatedBeforeTheNextInput`; `...StreamedFunctionPathTests.TransmittedFunctionPathsConsumeEachNodeWhileCurrent` (filter rows); W3C `su-filter` 10/10, 6 fwd (source-document) | yes 8+10; W3C 10 | PARTIAL: streamable-mode template, output only; no read-position or early-output assertion |
| S4-23 | Function category shallow-descent in a streamed template | `...StreamedFunctionPathTests.AShortCircuitConsumerEmitsBeforeTheRemainingFunctionPopulationIsRead` (first write within first half); `...StreamedRecursiveFunctionTests.RecursiveOutputStartsBeforeEofWithBoundedSourceHeaders`; `...StreamedTransmittingFunctionTests.ShallowDescentResultsAreConsumedBeforeTheNextChild`; W3C `su-shallow-descent` 12/12 | yes 8+10; W3C 10 | DIRECT |
| S4-24 | Function category deep-descent in a streamed template | `...StreamedFunctionPathTests.TransmittedFunctionPathsConsumeEachNodeWhileCurrent` (one deep-descent row); `...StreamedFunctionPathTests.SavedAssembliesPreserveFunctionPathAndPruningContracts` | yes 8+10 (saved 10 only) | PARTIAL: one output-only case; the W3C suite has no `su-deep-descent` set; no forward/early-output assertion |
| S4-25 | Function category ascent in a streamed template | W3C strict `su-ascent` 6/6: `su-ascent-005`, `-006` forwarded (in `xsl:source-document`), four XTSE3430 negatives | W3C 10 | PARTIAL: no unit runtime test; positive cases are not streamable-mode template rules; output only |
| S4-26 | Function category static rules (declared category, override compatibility, XTSE3155) | `...FunctionStreamabilityTests.AppliesTheDeclaredFunctionCategory`; `...StreamingDeclarationTests.FunctionOverridesPreserveStreamabilityCategory`; `...StreamingDeclarationTests.ZeroArityFunctionCannotDeclareStreamability` | yes 8+10 | DIRECT (static) |
| S4-27 | Schema contracts: typed/validated streamed initial input keeps annotations in streamable modes | `Xsl3.W3C.StreamedInitialSourceHarnessTests.StrictValidatedResolverSourcesRemainReadersThroughXmlAssertions`; W3C `streamable-021`, `-042`, `-043` FAIL (XTTE1512) | harness 10; W3C 10 (3 fail) | PARTIAL: no unit test of a validating/typed initial streamed input through `ApplyTemplates` or legacy `Transform`; three schema cases fail |
| S4-28 | Schema contracts: declared parameter/result types checked incrementally on streamed nodes | `...StreamedTemplateParameterTests.ExplicitWithParamTypesPreserveTheirChecksBeforeTheCalleeConversion`; `...StreamedTransmittingFunctionTests.IncrementalResultChecksRetainTheirTypeErrors`; `...StreamedSequenceContractTests.CardinalityAssertionsRetainCanonicalErrors` | yes 8+10 | DIRECT (error codes in forward mode) |
| S4-29 | `xsl:next-match` in a streamed mode dispatches without advancing | `...StreamedInitialModeTests.NextMatchDispatchesWithoutAdvancingPastTheCurrentNode` | yes 8+10 | PARTIAL: output only |
| S4-30 | Built-in rules (shallow-copy, text-only-copy, shallow-skip) visit forward children/attributes | `...StreamedInitialModeTests.BuiltinRulesVisitForwardChildrenAndAttributes`; `...CopyWithRecursiveTemplatesOnlyReadsTheHeaderBeforeItsChildren` | yes 8+10 | PARTIAL: output only despite the second test's name |
| S4-31 | Exit: `si-apply-templates` strict | W3C 11/11, 11 fwd (7 streamed initial mode) | W3C 10 | DIRECT |
| S4-32 | Exit: `si-next-match` strict | W3C 2/2 (1 forwarded via source-document, 1 XTSE3430) | W3C 10 | DIRECT (small population) |
| S4-33 | Exit: `si-apply-imports` strict | W3C 3/3, 3 fwd | W3C 10 | DIRECT |
| S4-34 | Exit: `su-*` strict | W3C 77/77, 50 fwd | W3C 10 | DIRECT |
| S4-35 | Exit: `streamable` strict | W3C 134/138, 105 fwd; fails `streamable-021`, `-042`, `-043` (XTTE1512), `-147` (wrong `@decimal` output) | W3C 10 | PARTIAL: four failures |

## S5: accumulators, iterate, for-each-group, merge

| Id | Obligation | Evidence | Passed on b0bf3bb | Strength |
|---|---|---|---|---|
| S5-01 | Streamed accumulator observers run on forward initial and source-document input (before/after values) | `...StreamedSourceDocumentTests.DeclaredStreamedAccumulatorObserversRunOnTheReader`; `...StreamedInitialModeTests.RequestedStreamedObserversRunForTheInitialSelection`; `...StreamedAccumulatorTests.BeforeAndAfterValuesFollowStartAndEndRules`; `...StreamingReadBoundaryTests.FinalTotalsRequireTheCompleteInputInEitherMode` (accumulator: captured forward source, exact output) | yes 8+10 | DIRECT |
| S5-02 | Skipped subtrees still contribute to accumulators | `...StreamedAccumulatorTests.SkippedSubtreesStillContributeToTheAccumulator` | yes 8+10 | PARTIAL: forward vs tree output only; skipping is not observed |
| S5-03 | Applicability (`use-accumulators`, XTDE3362, only applicable streamable accumulators run) | `...StreamedSourceDocumentTests.NoAccumulatorAppliesWhenTheSourceOmitsUseAccumulators`; `...StreamedInitialModeTests.InitialSelectionDoesNotAcquireUnrequestedAccumulators`; `...StreamedAccumulatorTests.OnlyApplicableStreamableAccumulatorsRun` | yes 8+10 | DIRECT |
| S5-04 | Rule dependency cycles raise XTDE3400 | `...StreamedAccumulatorTests.CyclicRuleDependenciesReportTheCanonicalError`; `...ADetectedCycleIsNotHiddenByAnEarlierRootValue` | yes 8+10 | DIRECT |
| S5-05 | Accumulator values on copies survive source closure | `...StreamedAccumulatorTests.CopiesKeepDescendantValuesAfterTheirInputCloses`; `...ValidatedCopiesKeepValuesAfterTheirStreamCloses` | yes 8+10 | DIRECT |
| S5-06 | Accumulator state bounded for scalar and rolling-string state | `...StreamedAccumulatorLifetimeTests.ScalarPhaseValuesAndFramesDoNotAccumulateWithProcessedRecords`; `...RollingAtomicValuesFollowTheWindowBoundRatherThanProcessedRecords`; doc `p7-runtime-contracts.md` "Measured retention with explicit payload assumptions" (W/P table) | yes 8+10 | DIRECT (logical counts/characters asserted; not bytes/RSS) |
| S5-07 | Accumulator state bounded for general growing/opaque/node-valued state | none; `p7-runtime-contracts.md` states it needs its own accounting | n/a | NONE |
| S5-08 | Exit: `accumulator` strict | W3C 36/36, 21 fwd (20 streamed initial mode) | W3C 10 | DIRECT |
| S5-09 | `xsl:iterate` executes forward with output before input ends | `...StreamedIterateTests.IterationResultsReachTheWriterBeforeTheInputEnds`; `...SourceResultsReachTheirConsumerWhileCurrent`; `...StreamingReadBoundaryTests.FinalTotalsRequireTheCompleteInputInEitherMode` (iterate: captured forward source) | yes 8+10 | DIRECT |
| S5-10 | Iterate semantics in streamed mode (simultaneous parameter updates, break, on-completion without focus) | `...StreamedIterateTests.ParameterUpdatesAreSimultaneous`; `...BreakDoesNotEvaluateTheNextSelectionItem`; `...CompletionHasNoFocus`; `...ARunningBalanceConsumesEachRowBeforeAdvancing`; W3C `si-iterate` 34/34, 30 fwd | yes 8+10; W3C 10 | DIRECT (functional); `ARunningBalanceConsumesEachRowBeforeAdvancing` asserts output only |
| S5-11 | Iterate retained state bounded | none at S5 level (no lifetime test); only the S7 native matrix iterate workload, not in this bundle | n/a | NONE |
| S5-12 | Exit: `si-iterate` strict | W3C 34/34, 30 fwd | W3C 10 | DIRECT |
| S5-13 | `for-each-group` adjacent/starting/ending streams: first group starts after one row | `...StreamedGroupingTests.TheFirstGroupBodyStartsBeforeItsMembersAreRead` (1 row read at first group); `...StreamingReadBoundaryTests.ForwardExecutionWritesBeforeCrossingThePhysicalReadBoundary` (adjacent, api/legacy/saved) | yes 8+10 (saved 10 only) | DIRECT |
| S5-14 | Members consumed before the population advances; unused members drained; truncated selection keeps the next group | `...StreamedGroupingTests.MembersAreConsumedBeforeThePopulationAdvances` (both APIs); `...UnusedMembersAreDrainedBeforeTheNextGroup`; `...ATruncatedMemberSelectionDoesNotLoseTheNextGroup` | yes 8+10 | PARTIAL: forward vs tree output only; the consumption order is not observed |
| S5-15 | Grouping key rules (single atomic key, error codes) | `...StreamedGroupingTests.NonCompositeKeysRequireExactlyOneAtomicValue`; `...AKeyFailureKeepsItsErrorCode` | yes 8+10 | DIRECT |
| S5-16 | Grouping retains only the current group (bounded) | none at S5 level | n/a | NONE |
| S5-17 | Exit: `si-for-each-group` strict | W3C 114/114, 99 fwd (26 streamed initial mode) | W3C 10 | DIRECT |
| S5-18 | `xsl:merge` k-way over streamed inputs, stable groups, forward execution | `...StreamedMergeTests.SortedInputsAreMergedWithStableGroups`; `...StreamingReadBoundaryTests.ForwardExecutionWritesBeforeCrossingThePhysicalReadBoundary` (merge: output before either source's halfway point, 3 forward sources) | yes 8+10 | DIRECT |
| S5-19 | Merge retention bounded to the current group plus one lookahead per input | `...StreamedMergeLifetimeTests.CompletedGroupsDoNotAccumulateWithProcessedInput`; doc `p7-runtime-contracts.md` "Merge groups, lookahead and explicit retained values" | yes 8+10 | DIRECT (snapshot/key counts; not bytes/RSS) |
| S5-20 | XTDE2220 raised when a streamed merge input is out of order | `...StreamedMergeTests.OutOfOrderInputRaisesItsDefinedError(forward: True)`; W3C `merge-072`, `merge-074` pass forwarded with XTDE2220 | yes 8+10; W3C 10 | DIRECT |
| S5-21 | The 9 original XTDE2220 cases (`merge-070`, `-071`, `-076`, `-078`, `-079`, `-080`, `-081`, `-083`, `-084`) pass in strict mode | W3C strict: 7/9 pass forwarded; `merge-081` FAIL (XTSE3195), `merge-084` FAIL (XPDY0002); both dispositioned in `p7-runtime-contracts.md` "Retained merge fixture dispositions" | W3C 10 (2 fail) | PARTIAL |
| S5-22 | Merge resource ownership, failures and cancellation | `...StreamedMergeTests.OwnedStreamsCloseOnCompletionAndActionFailure`; `...StreamedMergeLifetimeTests.CompletionEarlyExitAndFaultsReleaseUnretainedState` | yes 8+10 | DIRECT |
| S5-23 | Function items do not capture the implicit merge context (XTDE3480/3510) | `...StreamedMergeTests.FunctionItemsDoNotCaptureTheImplicitMergeContext` | yes 8+10 | DIRECT |
| S5-24 | Exit: `si-merge` strict | W3C 6/7, 6 fwd; fails `si-merge-004ns` (XTSE3195) | W3C 10 | PARTIAL |
| S5-25 | Exit: `merge` strict | W3C 22/27, 18 fwd; fails `merge-073`, `-081`, `-082` (XTSE3195), `-084` (XPDY0002); `merge-039` not run (collection environment) | W3C 10 | PARTIAL |

## S6: xsl:fork

| Id | Obligation | Evidence | Passed on b0bf3bb | Strength |
|---|---|---|---|---|
| S6-01 | Tee: every prong sees the same ordered events; the source is read once | `...StreamTeeTests.ReadersReceiveTheSameOrderedEventsAndReadTheSourceOnlyOnce`; `...ForkProngsTests.EveryProngRunsOnItsOwnThreadAndSeesTheSameInput` | yes 8+10 | DIRECT |
| S6-02 | Tee ring bounded by event capacity; fast reader waits for the slow one; partial prefixes released | `...StreamTeeTests.FastReaderWaitsForTheSlowReaderAndTheRingStaysBounded`; `...StreamTeeTests.AliasesReleaseAPartialPrefixBeforeAnyFurtherReadIsRequested`; `...StreamTeeTests.ConsumedEventsAreCollectibleWhileTheTeeIsStillAlive` | yes 8+10 | DIRECT (event counts) |
| S6-03 | Tee memory bound measured with explicit payload assumptions | `...StreamTeePayloadTests.RetainedPayloadBytesFollowTheRingBoundRatherThanTheInputLength` (6 cases; asserts live bytes = (BufferedCount+1)*P and <= (C+1)*P at each checkpoint); recorded table in `p7-runtime-contracts.md` "Measured retention with explicit payload assumptions" | yes 8+10 | DIRECT as an assertion for the synthetic tee with fixed payload P; it is not a compiled-fork or process measurement |
| S6-04 | Compiled-fork frames and variable header payloads bounded and released | `...StreamedForkFrameLifetimeTests.ForkFramesAndHeaderPayloadsStayBoundedAndCollectAfterClosure` (18 cases incl. error/cancel exits) | yes 8+10 | DIRECT for that input shape; replay graphs, parser buffers, schema objects, array overhead and buffered prong output are excluded (per doc) |
| S6-05 | Process-level fork/tee memory within the original budget | Recorded numbers only: `p7-completion.md` push fork 22,597,632 B > 20,971,520 at `722ae27`; nightly 16/16 historically | not in this bundle | PARTIAL: number recorded, failing the push budget, no b0bf3bb measurement and no assertion |
| S6-06 | Each prong runs on its own thread | `...ForkProngsTests.EveryProngRunsOnItsOwnThreadAndSeesTheSameInput` | yes 8+10 | DIRECT (internal `ForkProngs` with synthetic bodies) |
| S6-07 | Compiled `xsl:fork` executes forward (secondary output before half the input; first prong starts before input end) | `...StreamingReadBoundaryTests.ForwardExecutionWritesBeforeCrossingThePhysicalReadBoundary` (fork, api/legacy/saved); `...StreamedForkTests.FirstProngStartsBeforeTheInputEndsOnTheInvokingThread` | yes 8+10 (saved 10 only) | DIRECT |
| S6-08 | Output buffering: prong results appear in declaration order whatever the completion order | `...StreamedForkTests.ProngsConsumeTheSameForwardInputAndConcatenateTheirResults`; `...StreamedForkTests.RawResultApiReceivesPrincipalDocumentContentsInProngOrder`; `...StreamedForkProjectionTests.ProngsKeepDifferentChildNamesAndNamespacesInTheirOriginalResultOrder` | yes 8+10 | PARTIAL: order checked only under natural scheduling; no test forces a later prong to finish first |
| S6-09 | Principal writer stays on the invoking thread; secondary results independent | `...StreamedForkTests.SuppliedPrincipalWriterRunsOnTheOriginalThreadAndRemainsBorrowed`; `...StreamedForkTests.ProngsWriteIndependentSecondaryResultDocuments`; `...ForkCallerTests.InputAndOutputRequestsRunOnTheInvokingThread` | yes 8+10 | DIRECT |
| S6-10 | Principal raw items buffered before the source advances | `...StreamedForkTests.PrincipalRawItemsAreBufferedBeforeTheSourceAdvances` | yes 8+10 | PARTIAL: output only |
| S6-11 | `group-by` inside fork | `...StreamedForkTests.ForkGroupByBuffersItsPopulationBeforeSortingAndMemberAccess`; `...StreamedForkTests.ForkCapturesForwardGroupsBeforeStartingProngs` | yes 8+10 (saved 10 only) | PARTIAL: forward vs tree output only; buffering and its size are not asserted |
| S6-12 | `xsl:sort` inside fork (§19.8.4.19 exemption) | `...StreamedForkTests.ForkSortingByGroupPositionAllowsOneDownwardMemberSelection`; `...ForkGroupByBuffersItsPopulationBeforeSortingAndMemberAccess` | yes 8+10 (saved 10 only) | PARTIAL: output only |
| S6-13 | The error is chosen by prong declaration order | `...ForkProngsTests.DeclarationOrderSelectsTheErrorEvenWhenALaterProngFailsFirst`; `...ForkProngsTests.CancellationCallbackFailuresDoNotReplaceTheOriginalProngFailure` | yes 8+10 | DIRECT (internal runtime) |
| S6-14 | Compiled-fork prong errors reach the containing try with their codes | `...StreamedForkTests.ProngErrorsReturnToTheContainingTry`; `...StreamedForkTests.PrincipalWriterFailuresReturnToTheProngsTry`; `...StreamedForkProjectionTests.ConstructorErrorsKeepTheirCodeAndReleaseFollowingProngs` | yes 8+10 | DIRECT |
| S6-15 | A failing prong cancels the following (later-declared) prongs | `...ForkProngsTests.DeclarationOrderSelectsTheErrorEvenWhenALaterProngFailsFirst`; `...ForkProngsTests.AFailingProngReleasesFollowingReadersBlockedByBackpressure`; `...ForkCallerTests.CallbackFailureCancelsAndJoinsFollowingProngs` | yes 8+10 | DIRECT (internal `ForkProngs`, synthetic bodies) |
| S6-16 | A failing prong cancels earlier-declared prongs too ("cancels the others") | `...ForkProngsTests.ALaterFailureDoesNotCancelAnEarlierProng` asserts the opposite: the earlier prong reads all 1,000 events | yes 8+10 | NONE: by design only following prongs are cancelled (deterministic declaration-order error) |
| S6-17 | Failure cancellation inside a compiled `xsl:fork` stylesheet | `...StreamedForkProjectionTests.ConstructorErrorsKeepTheirCodeAndReleaseFollowingProngs` (asserts FORG0001 only); `...StreamedForkValueTests.FailureAndCancellationInterruptAValueWithoutStrandingOtherProngs` (`StreamedDocument.Fork`, asserts error identity and reader open) | yes 8+10 | PARTIAL: neither asserts that the sibling prong observed cancellation or stopped reading |
| S6-18 | External cancellation stops and joins every prong | `...ForkProngsTests.ParentCancellationStopsAndJoinsEveryProng`; `...ForkRuntimeTests.BothTransformationAndProngCancellationApply`; `...StreamedForkInputTests.OriginalSourceCancellationReachesProngsEvenWithoutFurtherReads` | yes 8+10 | DIRECT |
| S6-19 | Worker and source faults are transferred to the caller after the buffered prefix | `...ForkProngsTests.RuntimeFaultsAreTransferredToTheCallerWithoutEscapingTheWorkerThread`; `...ForkProngsTests.SourceRuntimeFaultsAreTransferredThroughTheTee`; `...StreamTeeTests.SourceFailureFollowsBufferedEventsForEveryReader` | yes 8+10 | DIRECT |
| S6-20 | An early-finishing prong does not truncate the others; no deadlock | `...ForkProngsTests.FinishingAProngEarlyDoesNotTruncateTheOtherProngs`; `...StreamTeeTests.AProngThatStopsEarlyDoesNotHoldBackOtherReaders`; `...StreamTeeTests.NestedTeesShareOneSourceWithoutDeadlocking`; `...StreamTeeTests.RecursiveSourceRequestsFailInsteadOfDeadlocking` | yes 8+10 | DIRECT |
| S6-21 | Ownership: a failed prong closes only its own sources; borrowed readers stay open | `...ForkRuntimeTests.FailedProngClosesOnlyItsOwnForwardSources`; `...ForkRuntimeTests.BorrowedInitialSelectionIsNeverClosedOrReopenedByTheChild`; `...StreamedForkInputTests.MalformedSourceErrorsDoNotCloseTheBorrowedReader` | yes 8+10 | DIRECT |
| S6-22 | Caller ownership: no lost wakeups; active callback buffers kept | `...ForkCallerTests.ProductionRequestsBetweenTheSourceCheckAndWaitAreNotLost`; `...ForkCallerTests.DisposalDoesNotReleaseAnActiveCallbacksArgumentBuffer`; `...ForkCallerTests.CancellationDoesNotDetachAnActiveCallbackFromItsArgumentBuffer` | yes 8+10 | DIRECT |
| S6-23 | Exit: `si-fork` strict | W3C 54/55, 44 fwd (17 streamed initial mode); fails `si-fork-953` (expects XTSE3430, gets a result) | W3C 10 | PARTIAL |

## Both public API paths (summary)

- Streamed execution through the new `ApplyTemplates(XmlReader|Stream|Uri)` overloads and through legacy
  `XslCompiledTransform.Transform(XmlReader|uri)` (including saved assemblies on .NET 10) is directly asserted by
  write-before-EOF, physical read-boundary and captured-representation tests (S4-01 to S4-03, S4-08 to S4-10), and
  by the identical API and legacy W3C strict runs (S3-33).
- Read-boundary coverage of both APIs exists for identity, source-document, merge, adjacent grouping and fork;
  accumulator and iterate are covered for forward representation only (end-of-input totals).
- Gaps: forward execution is opt-in (`StreamedExecution = true` or `XSL3_STREAM_STRICT=1`); with default settings
  neither API streams (S4-12). The 12-way overload matrix (S4-04), mode/parameter passing (S4-05) and the
  `XdmValue` "never streams" rule (S4-06) are not asserted at representation level.

## Tee bound (summary)

- An assertion exists for the synthetic tee with fixed payload size (S6-03) and for compiled-fork frame/header
  payloads of one generated shape (S6-04). The process-level fork memory figure is only a recorded number. It fails
  the 20 MiB push budget in the latest recorded matrix, and this bundle has no b0bf3bb measurement (S6-05).

## Counts

| Stage | DIRECT | PARTIAL | NONE | Total |
|---|---:|---:|---:|---:|
| S3 | 28 | 5 | 0 | 33 |
| S4 | 22 | 13 | 0 | 35 |
| S5 | 17 | 5 | 3 | 25 |
| S6 | 15 | 7 | 1 | 23 |
