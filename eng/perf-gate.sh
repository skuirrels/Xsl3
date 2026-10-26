#!/usr/bin/env bash
# The performance and allocation gate (remaining-scope 1.7): the XSLT 3.0 micro suite (bench/Xsl3.Benchmarks micro)
# on this checkout's Xsl3 against a base commit's, in the same job, alternating rounds so runner noise hits both.
# The same harness and stylesheets run on both sides: this checkout's bench/ is built against the base's src/.
# Fails when the time's geometric mean grows by more than 5% or a benchmark allocates more than 5% more.
# In CI a pull request labelled perf-accepted (the owner's call) makes a failure advisory.
# Usage: eng/perf-gate.sh [base-ref] [rounds]   (default origin/main, 3 rounds)
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
BASE_REF="${1:-origin/main}"
ROUNDS="${2:-3}"
SIZES="${XSL3_PERF_SIZES:-1kb,1mb}"
MS="${XSL3_PERF_MS:-500}"
WORK="$(mktemp -d)"
trap 'git -C "$ROOT" worktree remove --force "$WORK/base" >/dev/null 2>&1 || true; rm -rf "$WORK"' EXIT

git -C "$ROOT" worktree add --detach "$WORK/base" "$BASE_REF" >/dev/null
rm -rf "$WORK/base/bench"
cp -R "$ROOT/bench" "$WORK/base/bench"
rm -rf "$WORK/base/bench/Xsl3.Benchmarks/bin" "$WORK/base/bench/Xsl3.Benchmarks/obj"
# The ordinary suite must build on commits predating the streamed reader API.
# Exclude the separate process host on both sides; the same micro harness still runs.
build() {
  if dotnet build "$1/bench/Xsl3.Benchmarks" -c Release -f net10.0 -m:1 -p:EnableStreamingProcessBenchmarks=false -o "$2" -v quiet -nologo >"$2.build.log" 2>&1; then
    return 0
  fi
  cat "$2.build.log" >&2
  return 1
}
build "$WORK/base" "$WORK/base-bin"
build "$ROOT" "$WORK/head-bin"

base_files=() head_files=()
for i in $(seq 1 "$ROUNDS"); do
  echo "== round $i: base"
  dotnet "$WORK/base-bin/Xsl3.Benchmarks.dll" micro --sizes "$SIZES" --ms "$MS" --json "$WORK/base-$i.json" >/dev/null
  echo "== round $i: head"
  dotnet "$WORK/head-bin/Xsl3.Benchmarks.dll" micro --sizes "$SIZES" --ms "$MS" --json "$WORK/head-$i.json" >/dev/null
  base_files+=("$WORK/base-$i.json") head_files+=("$WORK/head-$i.json")
done

mkdir -p "$ROOT/artifacts/perf"
cp "$WORK"/*.json "$ROOT/artifacts/perf/"
IFS=,
dotnet "$WORK/head-bin/Xsl3.Benchmarks.dll" micro-compare "${base_files[*]}" "${head_files[*]}" | tee "$ROOT/artifacts/perf/compare.txt"
