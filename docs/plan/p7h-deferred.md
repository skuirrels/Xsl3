# Findings deferred from P7H

## D1: stylesheet reader reserialization shifts diagnostic lines

On unchanged 1.1.5 source, `XsltCompiler.Compile(XmlReader)` writes the supplied stylesheet
through `XmlWriter` into text and compiles that text. A multiline stylesheet start tag loses
an attribute newline, shifting later diagnostic lines. The P7H diagnostic fixture reports
`FOAR0001|8|urn:p7h:source-lines` for an instruction on original line 9. The direct legacy
reader API reports original line 9. Both normal/debug stylesheet modes exhibit the shift.

Evidence: `artifacts/p7h-debug-lines/baseline-metadata-corrected.log` and the initial
`focus-debug.log`; the unchanged serialization path is in `src/Xsl3/Xslt/XsltCompiler.cs`.
The initial malformed harness results are retained separately in `baseline-metadata.log`.

P7H's final diagnostic control uses `XsltCompiler.Compile(TextReader, Uri)`, which retains
the input text, and the direct legacy reader API. Both must report the actual source line,
module and error code, independently of debug mode. Source-API identity tests continue to
cover both public APIs. No offset is added to expected lines and no reader path is changed.

Preserving original reader locations across retained/recompiled stylesheet entry points needs
a separate source-mapping contract. It does not expand the shared QIL binding correction.
