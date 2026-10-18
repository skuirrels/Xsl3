#!/usr/bin/env bash
# Runs only some QT3 test sets (prefixes, comma-separated) and prints their failures.
#   eng/qt3-sets.sh fn-format-date,fn-format-time
set -euo pipefail
cd "$(dirname "$0")/.."
XSL3_QT3_SETS="$1" dotnet test tests/Xsl3.W3C --no-build -f net10.0 --filter "FullyQualifiedName~Qt3RunTests" --logger "console;verbosity=normal" 2>&1 | grep -E "QT3 \(" || true
grep -E '^- `' tests/Xsl3.W3C/bin/Debug/net10.0/w3c-reports/qt3-run.md | cut -c1-${2:-250} || true
