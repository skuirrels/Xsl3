#!/usr/bin/env bash
# After eng/xslt30-sets.sh: the tests of the last run that fail but aren't known failures
# (tests/Xsl3.W3C/baselines/xslt30-run.txt).
set -euo pipefail
cd "$(dirname "$0")/.."
comm -23 \
  <(grep -E '^- `' tests/Xsl3.W3C/bin/Debug/net10.0/w3c-reports/xslt30-run.md | sed -E 's/^- `([^`]*)`.*/\1/' | sort) \
  <(grep -v '^#' tests/Xsl3.W3C/baselines/xslt30-run.txt | sort)
