#!/usr/bin/env bash
# Fetches real 2.0/3.0 stylesheets into external/bench (git-ignored) for the benchmarks and the regression test of
# remaining-scope 1.3. Not committed; each is fetched at a pinned commit, under its own licence:
#   TEI Stylesheets (https://github.com/TEIC/Stylesheets): BSD-2-Clause or CC BY-SA 3.0, LEGAL/LICENCE.
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
