#!/usr/bin/env bash
# Compile the six shared contracts once, then run saved styles on .NET 8/10 and NativeAOT.
set -euo pipefail
if [[ $# -lt 2 || $# -gt 4 ]]; then echo "Usage: eng/verify-attribute-identity.sh <runtime-identifier> <new-output-directory> [--bare-variable [Release|Debug]]" >&2; exit 64; fi
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
RID="$1"
OUTPUT="$2"
COMPILER_CONFIGURATION="${4:-Release}"
if [[ $# -ge 3 ]]; then
  if [[ "$3" != --bare-variable ]]; then echo "Unknown fixture variant: $3" >&2; exit 64; fi
fi
case "$COMPILER_CONFIGURATION" in Release|Debug) ;; *) echo "Unknown compiler configuration: $COMPILER_CONFIGURATION" >&2; exit 64 ;; esac
if [[ -e "$OUTPUT" || -L "$OUTPUT" ]]; then echo "Output directory must be new: $OUTPUT" >&2; exit 1; fi
mkdir -- "$OUTPUT"
OUTPUT="$(cd "$OUTPUT" && pwd)"
GENERATOR_ARGS=("$OUTPUT/stylesheets")
if [[ $# -ge 3 ]]; then GENERATOR_ARGS+=(--bare-variable); fi
dotnet run --project "$ROOT/tests/Xsl3.AttributeIdentityProbe.Generator" -c "$COMPILER_CONFIGURATION" -- "${GENERATOR_ARGS[@]}" > "$OUTPUT/generator.log" 2>&1
dotnet build "$ROOT/tests/Xsl3.AttributeIdentityProbe" -c Release -m:1 -p:UseSharedCompilation=false \
  -p:IdentityStylesheetDirectory="$OUTPUT/stylesheets" > "$OUTPUT/managed-build.log" 2>&1
for framework in net8.0 net10.0; do
  dotnet "$ROOT/tests/Xsl3.AttributeIdentityProbe/bin/Release/$framework/Xsl3.AttributeIdentityProbe.dll" > "$OUTPUT/$framework.log" 2>&1
done
dotnet publish "$ROOT/tests/Xsl3.AttributeIdentityProbe" -c Release -f net10.0 -r "$RID" -m:1 \
  -p:UseSharedCompilation=false -p:PublishAot=true -p:TreatWarningsAsErrors=true \
  -p:IdentityStylesheetDirectory="$OUTPUT/stylesheets" -o "$OUTPUT/native" > "$OUTPUT/native-publish.log" 2>&1
"$OUTPUT/native/Xsl3.AttributeIdentityProbe" > "$OUTPUT/native.log" 2>&1
echo "Saved identity contracts passed on .NET 8, .NET 10 and NativeAOT. Evidence: $OUTPUT"
