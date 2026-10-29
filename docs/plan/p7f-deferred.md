# Findings deferred from P7F

## D1: investigate DTD identity through parentless attribute copies

On both unchanged 1.1.3 runtime and the P7F candidate, copy a DTD-declared ID attribute and IDREF
attribute into an `attribute()*` variable, insert them into a new document, then call `id()` and
`idref()` on that document. Both counts are zero; the initial test expected one. Reproduced in
grounded/forward execution and with preserve/strip policies. `xml:id` lexical normalization still works.

The failed source and all eight baseline/candidate failures are retained under
`artifacts/p7f-identity/deferred/DtdIdentityRoundtripReproduction.cs` and
`artifacts/p7f-identity/focus-initial-*`. They are independent of the optimized untyped-attribute
path; identity-sensitive attributes retain the original builder policy in P7F.

This requires a separate contract investigation of source DTD identity, parentless copies and subsequent
construction, with positive controls at each stage and explicit validation settings. The relevant
[DTD identity rules](https://www.w3.org/TR/xslt-30/#dtd-validation) and
[complex-content construction rules](https://www.w3.org/TR/xslt-30/#constructing-complex-content)
distinguish identity properties from type annotations and require attention to validation policy.
It does not expand the single-hotspot P7F allocation phase. No existing baseline,
fixture or passing assertion was relaxed; the newly added test's broader contract investigation is
retained here instead of being turned into an assertion that the existing loss is correct.
