#!/usr/bin/env bash
# Builds src/Xsl3 for one TFM and prints a histogram of compiler errors (or the detailed list with -v).
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
TFM="${TFM:-net10.0}"
LOG="${LOG:-$ROOT/artifacts/build-errors.log}"
mkdir -p "$(dirname "$LOG")"
dotnet build "$ROOT/src/Xsl3/Xsl3.csproj" -f "$TFM" -v q -nologo -clp:NoSummary > "$LOG" 2>&1
if [ "$1" = "-v" ]; then
  grep -E "error CS" "$LOG" | sed -E "s/^.*src.Xsl3.//; s/ \[.*$//" | sort -u
else
  grep -oE "error CS[0-9]+: [^[]*" "$LOG" | sort | uniq -c | sort -rn
fi
echo "total: $(grep -E 'error CS' "$LOG" | sed -E 's# \[.*$##' | sort -u | wc -l)"
