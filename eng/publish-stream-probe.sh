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
case "$RID" in
  linux-x64|linux-arm64)
    # Reject an ignored linker option or stale incremental native link before measurement.
    readelf --section-headers --wide "$OUTPUT/bin/Xsl3.StreamingProbe.Native" > "$OUTPUT/elf-sections.txt"
    readelf --version-info "$OUTPUT/bin/Xsl3.StreamingProbe.Native" > "$OUTPUT/elf-versions.txt"
    grep -Fq '.relr.dyn' "$OUTPUT/elf-sections.txt"
    if grep -Fq '.hydrated' "$OUTPUT/elf-sections.txt"; then
      echo 'Probe still contains hydrated metadata; clean its native build outputs and republish.' >&2
      exit 1
    fi
    grep -Fq 'GLIBC_ABI_DT_RELR' "$OUTPUT/elf-versions.txt"
    ;;
esac
