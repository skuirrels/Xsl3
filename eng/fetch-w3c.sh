#!/usr/bin/env bash
# Fetches the W3C conformance suites into external/ (git-ignored). They are not committed: they are large,
# and they are W3C's to distribute. tests/Xsl3.W3C finds them there, or at $XSL3_QT3TESTS.
#   eng/fetch-w3c.sh                      # the pinned commit below
#   QT3_REF=origin/master XSLT30_REF=origin/master eng/fetch-w3c.sh  # latest
# Baselines (tests/Xsl3.W3C/baselines) are recorded against the pinned commit; move the pin and the baselines together.
QT3_PIN=201a6e466940cdfc727f4babfedcde5332b9f578
XSLT30_PIN=fddf1cf920087e791f13315d68dfbe874d97dc56
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
mkdir -p "$ROOT/external"
if [ ! -d "$ROOT/external/qt3tests/.git" ]; then
  git clone --quiet --filter=blob:none https://github.com/w3c/qt3tests "$ROOT/external/qt3tests"
fi
git -C "$ROOT/external/qt3tests" fetch --quiet origin
git -C "$ROOT/external/qt3tests" checkout --quiet "${QT3_REF:-$QT3_PIN}"
echo "qt3tests at $(git -C "$ROOT/external/qt3tests" rev-parse HEAD)"

if [ ! -d "$ROOT/external/xslt30-test/.git" ]; then
  git clone --quiet --filter=blob:none https://github.com/w3c/xslt30-test "$ROOT/external/xslt30-test"
fi
git -C "$ROOT/external/xslt30-test" fetch --quiet origin
git -C "$ROOT/external/xslt30-test" checkout --quiet "${XSLT30_REF:-$XSLT30_PIN}"
echo "xslt30-test at $(git -C "$ROOT/external/xslt30-test" rev-parse HEAD)"
