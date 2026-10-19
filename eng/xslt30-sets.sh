#!/usr/bin/env bash
# Runs only some xslt30-test sets (prefixes, comma-separated) and prints their failures.
#   eng/xslt30-sets.sh sequence,for-each-group [width]
set -euo pipefail
cd "$(dirname "$0")/.."
XSL3_XSLT30_SETS="$1" dotnet test tests/Xsl3.W3C --no-build -f net10.0 --filter "FullyQualifiedName~Xslt30RunTests" >/dev/null 2>&1 || true
head -1 tests/Xsl3.W3C/bin/Debug/net10.0/w3c-reports/xslt30-run.md
grep -E '^- `' tests/Xsl3.W3C/bin/Debug/net10.0/w3c-reports/xslt30-run.md | cut -c1-${2:-250} || true
