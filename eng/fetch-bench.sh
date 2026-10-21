#!/usr/bin/env bash
# Fetches real 2.0/3.0 stylesheets into external/bench (git-ignored) for the benchmarks and the regression test of
# remaining-scope 1.3. Not committed; each is fetched at a pinned commit, under its own licence:
#   TEI Stylesheets (https://github.com/TEIC/Stylesheets): BSD-2-Clause or CC BY-SA 3.0, LEGAL/LICENCE.
#   DocBook xslTNG (https://github.com/docbook/xslTNG), the 2.7.1 release without Saxon's jars: MIT, LICENSE.
#   eng/fetch-bench.sh                     # the pinned commit below
#   TEI_REF=origin/dev eng/fetch-bench.sh  # latest
TEI_PIN=34da0f4c8f7d956fca3449dc7347181d034d0416
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DIR="$ROOT/external/bench/tei"
mkdir -p "$ROOT/external/bench"
if [ ! -d "$DIR/.git" ]; then
  git clone --quiet --filter=blob:none --no-checkout https://github.com/TEIC/Stylesheets "$DIR"
fi
git -C "$DIR" fetch --quiet origin "${TEI_REF:-$TEI_PIN}"
git -C "$DIR" checkout --quiet FETCH_HEAD
echo "TEI Stylesheets at $(git -C "$DIR" rev-parse HEAD)"

# DocBook xslTNG: the release zip, checked against its SHA-256.
DOCBOOK_VERSION=2.7.1
DOCBOOK_SHA256=1e1f63a0477395f0009c9249a96e19fc97ec7af8aa88a3cc2baced2c7a1b2dc5
DOCBOOK_DIR="$ROOT/external/bench/docbook"
if [ ! -f "$DOCBOOK_DIR/docbook-xslTNG-nosaxon-$DOCBOOK_VERSION/xslt/docbook.xsl" ]; then
  ZIP="$ROOT/external/bench/docbook-xslTNG-nosaxon-$DOCBOOK_VERSION.zip"
  curl -sSL -o "$ZIP" "https://github.com/docbook/xslTNG/releases/download/$DOCBOOK_VERSION/docbook-xslTNG-nosaxon-$DOCBOOK_VERSION.zip"
  ACTUAL=$( (command -v sha256sum >/dev/null && sha256sum "$ZIP" || shasum -a 256 "$ZIP") | cut -d' ' -f1)
  if [ "$ACTUAL" != "$DOCBOOK_SHA256" ]; then
    echo "DocBook xslTNG zip has SHA-256 $ACTUAL, expected $DOCBOOK_SHA256" >&2
    exit 1
  fi
  rm -rf "$DOCBOOK_DIR"
  mkdir -p "$DOCBOOK_DIR"
  unzip -q "$ZIP" -d "$DOCBOOK_DIR"
  rm -f "$ZIP"
fi
echo "DocBook xslTNG $DOCBOOK_VERSION in $DOCBOOK_DIR"
