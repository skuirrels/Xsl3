# P7 navigator node-kind control

## Scope and hypothesis

This isolated control starts from local integration documentation head `bb50e62`, whose runtime,
tests, canonical consumers and controllers are unchanged from `a4f7301`. The rejected attribute-cache
runtime/test are excluded. Integration PR #40 still targets `a4f7301`, whose CI 42167378456 and artifact audit now pass;
the documentation-only integration commit has not been pushed. Original full acceptance
42173435783 is running separately on `a4f7301`; this experimental navigator is excluded.

The retained identity framework-symbol diagnostic on `a8d322a` attributes **426 leaf samples** to
`StreamedNavigator.get_NodeType` among 26,343 CPU samples, with zero lost events. Its PDB's navigator
source checksum (`3419f91516be3052bd41dba3b9605c6b64d4273e0f7e9b78b454c6181771a1e4`) matches
integration `a4f7301` exactly. The diagnostic disables ReadyToRun to resolve framework symbols;
these sample counts identify code to inspect, not a removable share of production elapsed time.
Evidence: `/private/tmp/xsl3-p7-framework-cpu-20261105/`.

A navigator repeatedly computes its node kind from namespace position, attribute position and
its immutable header. The control stores that derived kind when a successful movement changes the
position. `NodeType` becomes a field read. Constructors, cloning, `MoveTo`, attribute/namespace
entry, parent/root movement, header-attribute projection and frozen/live fork binding maintain the
same derived value. Failed movements leave the prior kind unchanged. Advancing within an attribute
or namespace sequence retains that sequence's kind. Source reading, content checks and metadata
properties are unchanged; no name/value, workload or output-specific condition is introduced.

This is separate from the rejected reader-token node-type cache. It introduces a private cursor
field, not a reader wrapper bypass or source buffering. No public/saved helper signature changes.

## Completion criteria

1. Match ordinary tree node kinds while traversing live and explicitly replayed mixed content.
   Cover attribute/namespace entry and iteration, failed movements, cloning, `MoveTo`, parent/root
   movement, direct header projections and retained frozen fork positions. Kind/header queries must
   not advance the physical reader or close a borrowed input.
2. Verify the same contracts against the unchanged baseline. Pass full unit suites in Release/Debug
   on .NET 8/10 with warnings treated as errors; audit individual TRX rows, frozen binaries and PDB
   source checksums. Preserve any initial failures and their attribution.
3. Compare all 60 unchanged canonical observations against the integration baseline including the
   adopted merge improvement. Preserve original output fingerprints, exact input sizes and actual
   resource/compilation settings. Record both absolute timings and stream/tree ratios.
4. A useful local benefit requires an independent Linux comparison before adoption. Otherwise close
   without adoption or repeating unchanged measurements for a better result. All original final-source
   memory/throughput matrices, compatibility, physical-I/O evidence, supported claims, review/merge
   and matching publication remain required for full P7.

## Current evidence

The corrected focused Release contracts pass **eight cases per framework**. Two compare live/replayed
mixed-content traversal to an independently loaded `XPathDocument`, including repeated node-kind
queries, clones, attributes, namespaces, failed movement and physical read counts. Six cover retained
element/attribute/namespace positions with and without replay, frozen binding, same-document clones,
replay-backed rebinding, source disposal and borrowed-reader ownership.

The initial test incorrectly expected live-fork rebinding of a header-only frozen view to succeed.
Both candidate and unchanged baseline instead fail the same three cases on each framework with
`stream-position`; the other five pass. The implementation already requires a fork boundary for that
API. The corrected test explicitly requires that rejection while preserving successful frozen binding
and replay-backed rebinding. No runtime behavior was changed to accommodate this test correction.
Initial TRXs, matching baseline test binaries, reconstructed initial test source and the baseline audit
are retained separately; those failures are not counted as passes.

Full warning-as-error Release/Debug unit validation is running. Baseline controls and the paired
comparison are queued sequentially after it. No elapsed-time benefit or adoption is established.
Source manifests, patches, executable validation/audit scripts and results are retained under
`/private/tmp/xsl3-p7-navigator-node-kind-20261106/`. Recheck the existing process before restarting work.
