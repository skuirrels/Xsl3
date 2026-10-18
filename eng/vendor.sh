#!/usr/bin/env bash
# Copies files from a dotnet/runtime checkout into src/Xsl3/Vendored, keeping upstream-relative paths
# so the weekly upstream diff (SPEC §6.4) can map them back.
#   eng/vendor.sh <dotnet-runtime-root> <path under src/libraries> [...]
#   e.g. eng/vendor.sh ~/runtime System.Private.Xml/src/System/Xml/BitStack.cs Common/src/System/Obsoletions.cs
set -euo pipefail
UP="$1"; shift
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
for rel in "$@"; do
  src="$UP/src/libraries/$rel"
  dst_rel="${rel#System.Private.Xml/src/}"
  dst_rel="${dst_rel/#Common\/src\//Common/}"
  dst="$ROOT/src/Xsl3/Vendored/$dst_rel"
  mkdir -p "$(dirname "$dst")"
  cp "$src" "$dst"
  echo "vendored $rel"
done
