# P7: review of the five streamability extensions (§19.10)

Measured 3 November 2026 on main at 3ea172a (1.2.0), Release, .NET 10, with a temporary per-extension
switch that was not committed. Spec text: the XSLT 3.0 Recommendation of 8 June 2017 (no published errata).

## The rule

§19.10: a declared-streamable construct that fails the specification's analysis is an XTSE3430 error
"unless the user has indicated that the processor is to handle this situation by processing the stylesheet
without streaming or by making use of processor extensions to the streamability rules where available".
The closing Note allows a processor to use different rules only if everything guaranteed-streamable stays
streamable. Extensions are therefore allowed, but only at user option.

## Findings

All five rewrites behind `ExpressionAnalysisOptions.AllowStreamabilityRewrites` accept constructs that
the specification's analysis rejects. Each one is an extension, and they are on in every mode with no
user option.

| Extension | Code | What the specification says | W3C cases that need it |
|---|---|---|---|
| `E//N` analysed as `E/descendant::N` (child step, no predicates) | `ExpressionAnalysis.cs` `Path_19_8_8_8` | §19.8.8.8 analyses `//` after expanding it to `/descendant-or-self::node()/`. A child step from a crawling posture is roaming (§19.8.8.9 table), and only a scanning expression is rescued. `.//x`, `$p//x` and `a[1]//x` are not scanning expressions: §19.8.10 rejects positional predicates, and the Note to §19.8.8.8 says `.//section/head` "is not guaranteed streamable" | 55 alone, and 39 more that either this or the next one rescues |
| A leading `.` accepted in a scanning expression | `ExpressionAnalysis.cs` `IsScanningPath` | Same Note: a leading dot is not pattern syntax | 2 alone, and the 39 shared |
| Quantified binding over a striding or crawling sequence with a motionless predicate | `ExpressionAnalysis.Expressions.cs` `Bindings_19_8_8` | §19.8.8.2: the `in` expression has usage navigation, so it must be grounded. The Note uses `some $x in child::section satisfies has-children($x)` as the disallowed example: "Some implementations may be able to stream such constructs" | 3 (`streamable-100` to `102`) |
| `S except .` stays striding | `ExpressionAnalysis.Expressions.cs` `Binary_19_8_8` | §19.8.8.4: two striding or crawling operands give crawling. `.` in a striding context is striding, not grounded | 1 (`si-group-055`) |
| `current-group()` over a grounded selection is motionless anywhere | `ExpressionAnalysis.Functions.cs` `SpecialFunction_19_8_9` | §19.8.9.4: the select expression's posture and sweep, and only when no higher-order operand or nested focus lies on the path. Otherwise roaming | 1 (`si-group-051`) |

Total: 101 of the 2,696 strict passes. With all five off, the strict `strm` run is 2,595 / 117 / 0 (95.7%,
still above the S7 threshold of 90%). No case gains, and the S2 static-diagnostic audit is unchanged
(154 / 1, `si-fork-953` the only failure either way). So no expected-XTSE3430 test needs any of the five off.

## The conflict

Unlike `sx-union-202` (a test that expected XTSE3430 and so sided with the specification), these 101
tests expect the extended behaviour. Most were written by the specification's editor, for example
`sx-comma-021` uses `/BOOKLIST/BOOKS/ITEM[1]//text()` and is tagged `positional-predicate` and
`striding-then-crawling`. The two streaming processors in the W3C implementation report also accept them:
Saxon 9.8 passes all 93 of the cases it reports, and Exselt passes 85, with 4 failures and 4 not run.
The test suite and the reference implementations agree with each other and disagree with the letter
of the analysis rules.

## Options

1. **Follow the letter of the specification (as in 1.2.0).** Remove all five. 101 W3C cases fail with
   XTSE3430, and stylesheets using `.//x`, `a[1]//x` or the quantified form stop compiling under the
   default Strict mode. A breaking 1.3.0 that moves Xsl3 away from both reference processors.
2. **Make them a user option, off by default.** For example, a third `XsltStreamabilityMode`
   value that maps onto §19.10's third choice ("process with streaming if it is able to do so"). It
   could also bring back the union extension. Strict stays letter-of-the-specification, which is
   breaking in the same way as option 1. The W3C runner would need the new mode to keep its 2,696
   passes, so the conformance claim becomes "with extensions enabled".
3. **Keep them on by default, but name and document them, with a switch to turn them off.** Strict
   continues to match the test suite and both reference processors. A new setting (for example
   `XsltSettings.StreamabilityExtensions`, default on) gives the user the §19.10 choice, and
   documentation lists the five as Xsl3's extensions. Not breaking. The deviation is that the
   default is the extended rules rather than the specification's, which needs stating in
   `docs/conformance.md`.

**Owner decision (3 November 2026): option 3**, as `XsltSettings.StreamabilityExtensions`, default
`true`. The recommendation was option 3. Each rewrite preserves the result (the 101 cases produce the expected
output under streamed execution), the test suite is the arbiter that other implementations were
judged by, and nothing in the suite needs them off. Option 2 is the strictest reading of §19.10, and
it is the right choice if the owner wants the default to be the specification's analysis exactly.
Either way, the union decision in 1.2.0 stays consistent, because there the test suite asked for the
specification's behaviour.

## Data

The case lists, per-extension results and both S2 reports from this measurement are not committed.
The attribution can be repeated by gating each rewrite on an environment variable and running
`XSL3_XSLT30_PROFILE=strm XSL3_STREAM_STRICT=1` once per extension.
