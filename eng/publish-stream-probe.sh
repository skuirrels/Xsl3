#!/usr/bin/env bash
# Build the real generated-input probe with statically referenced saved stylesheets.
set -euo pipefail
if [[ $# != 2 ]]; then echo "Usage: eng/publish-stream-probe.sh <runtime-identifier> <new-output-directory>" >&2; exit 64; fi
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
RID="$1"
OUTPUT="$2"
if [[ -e "$OUTPUT" || -L "$OUTPUT" ]]; then echo "Output directory must be new: $OUTPUT" >&2; exit 1; fi
mkdir -- "$OUTPUT"
OUTPUT="$(cd "$OUTPUT" && pwd)"
dotnet run --project "$ROOT/tests/Xsl3.StreamingProbe.Generator" -c Release -- "$OUTPUT/stylesheets"
dotnet publish "$ROOT/tests/Xsl3.StreamingProbe.Native" -c Release -r "$RID" -m:1 \
  -p:ProbeStylesheetDirectory="$OUTPUT/stylesheets" -o "$OUTPUT/bin"
