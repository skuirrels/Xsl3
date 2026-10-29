# Findings deferred from P7G

## D1: Debug compiler line information for a bare variable sequence

**Resolved by bounded [P7H](p7h-debug-source-line-contracts.md) in 1.1.6** with passing local
Release/Debug, saved/native and regression gates. See its [close-out](../p7h-debug-source-lines-2026-10-29.md).
The original report below remains unchanged; released 1.1.5 still contains this compiler defect.

The initial P7G fixture used `xsl:sequence select="$second"` inside a validated literal result element.
With the library built in Debug and stylesheet debugging enabled, this reaches the existing
`SetLineInfoCheck` assertion, "Attempt to override SourceLineInfo in debug mode". This concerns compiler
debug metadata, independently of DTD identity. Release builds compile the same normal/debug styles.
The initial source and failing logs are retained under `artifacts/p7g-identity/deferred/`,
`artifacts/p7g-identity/baseline/debug-initial*` and `artifacts/p7g-identity/gates-debug-initial/units-debug.log`.

The final contract fixture selects `$second/self::attribute()` instead. `$second` is declared
`attribute()*`, so the selected nodes still enter both explicit construction-validation policies
through implicit copying. All six declarations/copy policies, metadata checks, parentlessness,
identity, names/values and expected results remain unchanged. This is a fixture adaptation to keep
the requested identity scope bounded, not a correction to the Debug compiler or a relaxed assertion.
Retain the original reproduction for a separate compiler-debug phase.
