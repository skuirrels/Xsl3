#!/usr/bin/env bash
# Compile the six shared contracts once, then run saved styles on .NET 8/10 and NativeAOT.
set -euo pipefail
if [[ $# != 2 ]]; then echo "Usage: eng/verify-attribute-identity.sh <runtime-identifier> <new-output-directory>" >&2; exit 64; fi
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
RID="$1"
OUTPUT="$2"
if [[ -e "$OUTPUT" || -L "$OUTPUT" ]]; then echo "Output directory must be new: $OUTPUT" >&2; exit 1; fi
mkdir -- "$OUTPUT"
OUTPUT="$(cd "$OUTPUT" && pwd)"
dotnet run --project "$ROOT/tests/Xsl3.AttributeIdentityProbe.Generator" -c Release -- "$OUTPUT/stylesheets" > "$OUTPUT/generator.log" 2>&1
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
